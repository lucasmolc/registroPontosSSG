using Microsoft.Playwright;
using RegistroPontosSSG.Core.Models;
using RegistroPontosSSG.Core.Security;
using RegistroPontosSSG.Core.Validation;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace RegistroPontosSSG.Core.Automation;

/// <summary>
/// Automação Playwright do sistema SSG (Sysmap).
///
/// Fluxo: login + 2FA TOTP → rota <c>#/access-entry/get-list</c> → filtro de período →
/// preenchimento dos cards de dia (Registro de E-S + Apontamento) → "Salvar dias alterados".
///
/// A interface antiga (<c>timesheetrecording.asp</c> com <c>#TableTimesheet</c>) não existe mais:
/// o SSG migrou para uma SPA AngularJS onde os cards de todos os dias do período já vêm
/// renderizados — não é preciso criar linha nem digitar a data. Ver <see cref="SsgSelectors"/>.
/// </summary>
public sealed class SsgAutomation : IAsyncDisposable
{
    private readonly AppConfig _config;
    private readonly TimeValidator _validator;
    private readonly Action<string> _log;
    private readonly Action<string> _progress;
    private readonly Action<string> _verbose;

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;
    private Process? _chromeProcess;

    /// <summary>
    /// Texto de um OSI/Projeto/Atividade já usado pelo profissional, capturado dos dias
    /// que já possuem apontamento. Serve de fallback para o autocomplete quando o modal
    /// de "Listagem de Itens" não abre.
    /// </summary>
    private string? _knownProjectText;

    public HashSet<string> RegisteredDates { get; } = new();

    /// <summary>
    /// Horários (HH:mm) já cadastrados no SSG, agrupados por data DD/MM/YYYY.
    /// Alimentado por <see cref="GetRegisteredDatesAsync"/> e usado para que a regra de
    /// "duplicado em dias próximos" enxergue horários que NÃO estão no arquivo de entrada.
    /// </summary>
    public Dictionary<string, List<string>> RegisteredTimesByDate { get; } = new();

    public SsgAutomation(AppConfig config, Action<string>? log = null, Action<string>? progress = null, Action<string>? verbose = null)
    {
        _config = config;
        _validator = new TimeValidator(config.Validation);
        _log = log ?? (_ => { });
        _progress = progress ?? (_ => { });
        _verbose = verbose ?? (_ => { });
    }

    private void V(string message) => _verbose($"[{DateTime.Now:HH:mm:ss.fff}] {message}");

    // ------------------------------------------------------------------
    // Preenchimento de campos mascarados
    // ------------------------------------------------------------------

    /// <summary>
    /// Preenche um campo de hora com máscara HH:MM. Os campos <c>.mask-time</c> da SPA
    /// ignoram <c>FillAsync</c> (o valor fica vazio ou "__:__"), por isso enviamos apenas
    /// os 4 dígitos e deixamos a máscara aplicar o ":" sozinha.
    /// </summary>
    private async Task<bool> FillTimeFieldAsync(ILocator field, string time, string label)
    {
        var formatted = FormatTime(time);
        var digits = new string(formatted.Where(char.IsDigit).ToArray());
        if (digits.Length != 4)
        {
            V($"   {label}: ⚠️ valor '{time}' não possui 4 dígitos, abortando preenchimento");
            return false;
        }
        return await FillMaskedFieldAsync(field, digits, formatted, label);
    }

    /// <summary>
    /// Núcleo do preenchimento de campos mascarados: recebe os dígitos crus (sem
    /// separadores) e o valor formatado esperado, usado para validar o resultado.
    /// </summary>
    private async Task<bool> FillMaskedFieldAsync(ILocator field, string digitsOnly, string expectedFormatted, string label)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await field.ClickAsync(new() { ClickCount = 3 });
            await field.PressAsync("Delete");
            await _page!.WaitForTimeoutAsync(50);
            await field.PressSequentiallyAsync(digitsOnly, new() { Delay = 80 });
            await _page.WaitForTimeoutAsync(150);
            var v = (await field.InputValueAsync() ?? string.Empty).Trim();
            V($"   {label}: tentativa {attempt} (digits='{digitsOnly}') → campo='{v}'");
            if (v == expectedFormatted) return true;
        }

        // Último recurso: alguns campos sem máscara aceitam o valor completo via fill.
        await field.FocusAsync();
        await field.PressAsync("Control+A");
        await field.PressAsync("Delete");
        await field.FillAsync(expectedFormatted);
        await field.PressAsync("Tab");
        await _page!.WaitForTimeoutAsync(150);
        var final = (await field.InputValueAsync() ?? string.Empty).Trim();
        V($"   {label}: tentativa 3 (fill='{expectedFormatted}') → campo='{final}'");
        if (final == expectedFormatted) return true;

        V($"   {label}: ❌ valor final '{final}' diferente do esperado '{expectedFormatted}'");
        return false;
    }

    // ------------------------------------------------------------------
    // Navegador
    // ------------------------------------------------------------------

    public async Task StartAsync()
    {
        _progress("Iniciando navegador...");
        _playwright = await Playwright.CreateAsync();

        if (_config.Automation.UseSystemChrome)
            await StartSystemChromeAsync();
        else
            await StartBundledChromiumAsync();
    }

    private async Task StartSystemChromeAsync()
    {
        var chromePath = !string.IsNullOrWhiteSpace(_config.Automation.ChromePath)
            ? _config.Automation.ChromePath
            : FindChrome();

        if (string.IsNullOrEmpty(chromePath) || !File.Exists(chromePath))
        {
            _log("Chrome do sistema não encontrado, usando Chromium embutido");
            await StartBundledChromiumAsync();
            return;
        }

        var userDataDir = _config.Automation.UseChromeProfile
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data")
            : Configuration.ConfigService.BrowserDataDirectory;
        Directory.CreateDirectory(userDataDir);

        // 1) Reaproveita um Chrome de debug já aberto (outra execução ou outro script
        //    podem estar usando o mesmo perfil; subir um segundo Chrome sobre o mesmo
        //    --user-data-dir faz o processo delegar para a instância existente e a porta
        //    de debug nunca abre).
        var existingPort = await FindRunningDebugPortAsync(userDataDir);
        if (existingPort is int reusePort && await TryConnectAsync(reusePort, reused: true))
            return;

        // 2) Sobe uma instância nova
        var port = FindFreePort();
        var devToolsFile = Path.Combine(userDataDir, "DevToolsActivePort");
        try { if (File.Exists(devToolsFile)) File.Delete(devToolsFile); } catch { }

        var args = new List<string>
        {
            $"--remote-debugging-port={port}",
            $"--user-data-dir={userDataDir}",
            "--disable-background-networking", "--disable-client-side-phishing-detection",
            "--disable-default-apps", "--disable-hang-monitor", "--disable-popup-blocking",
            "--disable-prompt-on-repost", "--disable-sync", "--disable-translate",
            "--metrics-recording-only", "--no-first-run", "--no-default-browser-check",
            "--safebrowsing-disable-auto-update"
        };
        if (!_config.Automation.UseChromeProfile)
            args.Add("--profile-directory=Default");

        _log($"Iniciando Chrome do sistema (porta {port})...");
        _chromeProcess = Process.Start(new ProcessStartInfo
        {
            FileName = chromePath,
            Arguments = string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        // Aguarda o endpoint responder de fato (em vez de um Task.Delay fixo).
        var ready = await WaitForDebugEndpointAsync(port, TimeSpan.FromSeconds(30));
        if (!ready)
        {
            // O Chrome pode ter escolhido outra porta e registrado em DevToolsActivePort.
            var advertised = ReadDevToolsPort(userDataDir);
            if (advertised is int p2 && await TryConnectAsync(p2, reused: false)) return;

            _log("Chrome não abriu a porta de debug — usando Chromium embutido");
            try { _chromeProcess?.Kill(); } catch { }
            await StartBundledChromiumAsync();
            return;
        }

        if (!await TryConnectAsync(port, reused: false))
        {
            _log("Falha ao conectar via CDP — usando Chromium embutido");
            try { _chromeProcess?.Kill(); } catch { }
            await StartBundledChromiumAsync();
        }
    }

    /// <summary>
    /// Conecta via CDP. Usa sempre <c>127.0.0.1</c>: com "localhost" o Windows resolve
    /// <c>::1</c> primeiro e o Chrome, que escuta apenas em IPv4, recusa a conexão
    /// (<c>ECONNREFUSED ::1:porta</c>).
    /// </summary>
    private async Task<bool> TryConnectAsync(int port, bool reused)
    {
        try
        {
            _browser = await _playwright!.Chromium.ConnectOverCDPAsync($"http://127.0.0.1:{port}");
            _context = _browser.Contexts.Count > 0 ? _browser.Contexts[0] : await _browser.NewContextAsync();
            _page = _context.Pages.Count > 0 ? _context.Pages[0] : await _context.NewPageAsync();
            _page.SetDefaultTimeout(_config.Automation.TimeoutMs);
            _log(reused
                ? $"Reutilizando Chrome de debug já aberto (porta {port})"
                : $"Conectado ao Chrome do sistema (porta {port})");
            return true;
        }
        catch (Exception ex)
        {
            V($"TryConnectAsync(porta={port}) falhou: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Procura um Chrome de debug ativo: primeiro pela porta anunciada no
    /// <c>DevToolsActivePort</c> do perfil, depois pelas portas presentes nas linhas de
    /// comando dos processos chrome.exe em execução.
    /// </summary>
    private async Task<int?> FindRunningDebugPortAsync(string userDataDir)
    {
        var candidates = new List<int>();

        if (ReadDevToolsPort(userDataDir) is int fromFile)
            candidates.Add(fromFile);

        foreach (var port in ReadDebugPortsFromProcesses())
            if (!candidates.Contains(port)) candidates.Add(port);

        foreach (var port in candidates)
        {
            if (await IsDebugEndpointAliveAsync(port))
            {
                V($"FindRunningDebugPortAsync: porta {port} respondendo");
                return port;
            }
        }
        V($"FindRunningDebugPortAsync: nenhuma porta de debug ativa (candidatas: {string.Join(", ", candidates)})");
        return null;
    }

    private static int? ReadDevToolsPort(string userDataDir)
    {
        try
        {
            var file = Path.Combine(userDataDir, "DevToolsActivePort");
            if (!File.Exists(file)) return null;
            var first = File.ReadLines(file).FirstOrDefault();
            return int.TryParse(first?.Trim(), out var port) ? port : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Extrai as portas de <c>--remote-debugging-port</c> das linhas de comando dos
    /// processos chrome.exe. Cobre o caso de outro script já ter subido o Chrome com um
    /// perfil diferente do nosso.
    /// </summary>
    private static IEnumerable<int> ReadDebugPortsFromProcesses()
    {
        var ports = new List<int>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -Command \"Get-CimInstance Win32_Process -Filter \\\"Name='chrome.exe'\\\" | Select-Object -ExpandProperty CommandLine\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return ports;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(15000);

            foreach (Match m in Regex.Matches(output, @"--remote-debugging-port=(\d+)"))
            {
                if (int.TryParse(m.Groups[1].Value, out var port) && !ports.Contains(port))
                    ports.Add(port);
            }
        }
        catch { /* sem WMI/PowerShell: seguimos apenas com o DevToolsActivePort */ }
        return ports;
    }

    private static async Task<bool> IsDebugEndpointAliveAsync(int port)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var resp = await http.GetAsync($"http://127.0.0.1:{port}/json/version");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static async Task<bool> WaitForDebugEndpointAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await IsDebugEndpointAliveAsync(port)) return true;
            await Task.Delay(500);
        }
        return false;
    }

    private async Task StartBundledChromiumAsync()
    {
        var userDataDir = Configuration.ConfigService.BrowserDataDirectory;
        Directory.CreateDirectory(userDataDir);

        _context = await _playwright!.Chromium.LaunchPersistentContextAsync(userDataDir, new BrowserTypeLaunchPersistentContextOptions
        {
            Headless = _config.Automation.Headless,
            SlowMo = _config.Automation.SlowMoMs,
            ViewportSize = new ViewportSize { Width = 1366, Height = 768 },
            Locale = "pt-BR",
            TimezoneId = "America/Sao_Paulo",
            Args = new[]
            {
                "--disable-blink-features=AutomationControlled", "--disable-infobars",
                "--disable-extensions", "--disable-gpu", "--disable-dev-shm-usage", "--log-level=3"
            },
            IgnoreDefaultArgs = new[] { "--enable-automation" }
        });

        _page = _context.Pages.Count > 0 ? _context.Pages[0] : await _context.NewPageAsync();
        _page.SetDefaultTimeout(_config.Automation.TimeoutMs);
        _log("Chromium embutido iniciado");
    }

    // ------------------------------------------------------------------
    // Login
    // ------------------------------------------------------------------

    public async Task<bool> LoginAsync()
    {
        if (_page is null) return false;
        _progress("Acessando SSG...");
        V($"LoginAsync: navegando para {_config.Ssg.BaseUrl}/");
        try
        {
            await _page.GotoAsync(_config.Ssg.BaseUrl + "/");

            _progress("Aguardando Cloudflare...");
            await _page.WaitForURLAsync(
                url => url.Contains("portal.sysmap.com.br") || url.Contains("wp-login") || url.Contains("ssg.sysmap.com.br/index.html"),
                new() { Timeout = 120000 });
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            V($"LoginAsync: URL atual='{_page.Url}'");

            var alreadyAuthenticated = _page.Url.Contains("ssg.sysmap.com.br")
                                       || (_page.Url.Contains("portal.sysmap.com.br") && !_page.Url.Contains("wp-login"));

            if (alreadyAuthenticated)
            {
                V("LoginAsync: sessão já autenticada — pulando formulário");
            }
            else
            {
                _progress("Preenchendo credenciais...");
                await _page.WaitForSelectorAsync(SsgSelectors.LoginUser, new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
                await _page.Locator(SsgSelectors.LoginUser).FillAsync(_config.Credentials.Username);
                await _page.Locator(SsgSelectors.LoginPassword).FillAsync(_config.Credentials.Password);

                try
                {
                    await _page.WaitForSelectorAsync(SsgSelectors.LoginTotp, new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
                    var totpField = _page.Locator(SsgSelectors.LoginTotp);
                    await totpField.FocusAsync();

                    if (!string.IsNullOrWhiteSpace(_config.Credentials.TotpSecret))
                    {
                        var code = TotpGenerator.GenerateCode(_config.Credentials.TotpSecret);
                        _progress("Preenchendo 2FA automaticamente...");
                        await totpField.FillAsync(code);
                        await _page.WaitForTimeoutAsync(500);
                        await _page.Locator(SsgSelectors.LoginSubmit).First.ClickAsync();
                    }
                    else
                    {
                        _progress("Digite o código 2FA no navegador...");
                    }
                }
                catch
                {
                    try { await _page.Locator(SsgSelectors.LoginSubmit).First.ClickAsync(); } catch { }
                }

                _progress("Aguardando finalização do login (até 5 min)...");
                await _page.WaitForURLAsync(url => !url.Contains("wp-login") && !url.Contains("wp-admin"),
                    new() { Timeout = 300000 });
            }

            _progress("Abrindo Registros de Entrada/Saída...");
            await _page.GotoAsync(_config.Ssg.AccessEntryUrl);
            await _page.WaitForSelectorAsync(SsgSelectors.DateRangeComponent,
                new() { State = WaitForSelectorState.Visible, Timeout = 60000 });
            await DismissModalsAsync();
            V($"LoginAsync: tela de registros carregada ({_page.Url})");
            return true;
        }
        catch (Exception ex)
        {
            _log($"Erro no login: {ex.Message}");
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Filtro de período
    // ------------------------------------------------------------------

    /// <summary>
    /// Seleciona o período pelo preset do dropdown ("Mês Atual" / "Mês Anterior") e filtra.
    /// Digitar as datas manualmente não funciona: os campos são mascarados e o Angular
    /// rejeita o filtro com "O campo Período é de preenchimento obrigatório".
    /// </summary>
    public async Task<bool> SelectMonthAndFilterAsync(string period)
    {
        if (_page is null) return false;
        var label = period == "mes_passado" ? "mês anterior" : "mês atual";
        V($"SelectMonthAndFilterAsync: period='{period}' (label={label})");
        try
        {
            await DismissModalsAsync();
            await _page.WaitForSelectorAsync(SsgSelectors.DateRangeToggle,
                new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

            await _page.Locator(SsgSelectors.DateRangeToggle).First.ClickAsync();
            await _page.WaitForTimeoutAsync(400);

            var presetSelector = period == "mes_passado"
                ? SsgSelectors.DateRangePreviousMonth
                : SsgSelectors.DateRangeCurrentMonth;
            await _page.Locator(presetSelector).First.ClickAsync();
            await _page.WaitForTimeoutAsync(600);

            var start = (await _page.Locator(SsgSelectors.StartDate).First.InputValueAsync() ?? string.Empty).Trim();
            var end = (await _page.Locator(SsgSelectors.EndDate).First.InputValueAsync() ?? string.Empty).Trim();
            V($"   período selecionado: {start} até {end}");
            if (string.IsNullOrWhiteSpace(start) || string.IsNullOrWhiteSpace(end))
            {
                _log($"⚠️  Preset '{label}' não preencheu as datas do filtro");
                return false;
            }

            _progress($"Filtrando {label}...");
            await _page.Locator(SsgSelectors.FilterButton).First.ClickAsync();

            await _page.WaitForSelectorAsync(SsgSelectors.DayCard,
                new() { State = WaitForSelectorState.Attached, Timeout = 90000 });
            await _page.WaitForTimeoutAsync(1500);
            await DismissModalsAsync();

            var days = await _page.Locator(SsgSelectors.DayCard).CountAsync();
            V($"   {days} card(s) de dia carregado(s)");
            return days > 0;
        }
        catch (Exception ex)
        {
            _log($"Erro ao filtrar {label}: {ex.Message}");
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Leitura do que já está cadastrado
    // ------------------------------------------------------------------

    /// <summary>
    /// Varre os cards de dia e coleta as datas que já possuem registros de E/S, os
    /// horários já salvos (para as regras de duplicidade) e um OSI/Projeto/Atividade
    /// conhecido (fallback do autocomplete).
    /// </summary>
    public async Task<HashSet<string>> GetRegisteredDatesAsync()
    {
        if (_page is null) return RegisteredDates;
        V("GetRegisteredDatesAsync: varrendo cards .access-entry-day");
        try
        {
            var cards = await _page.Locator(SsgSelectors.DayCard).AllAsync();
            V($"   {cards.Count} card(s) encontrados");

            foreach (var card in cards)
            {
                var date = (await card.GetAttributeAsync(SsgSelectors.AttrDate) ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(date)) continue;

                var times = new List<string>();
                var rows = await card.Locator(SsgSelectors.AccessRows).AllAsync();
                foreach (var row in rows)
                {
                    var inField = row.Locator(SsgSelectors.ClockIn);
                    var outField = row.Locator(SsgSelectors.ClockOut);
                    if (await inField.CountAsync() > 0)
                    {
                        var v = (await inField.First.InputValueAsync() ?? string.Empty).Trim();
                        if (IsTime(v)) times.Add(v);
                    }
                    if (await outField.CountAsync() > 0)
                    {
                        var v = (await outField.First.InputValueAsync() ?? string.Empty).Trim();
                        if (IsTime(v)) times.Add(v);
                    }
                }

                if (times.Count > 0)
                {
                    RegisteredDates.Add(date);
                    RegisteredTimesByDate[date] = times;
                }

                if (_knownProjectText is null)
                {
                    var projects = await card.Locator(SsgSelectors.ProjectActivity).AllAsync();
                    foreach (var p in projects)
                    {
                        var v = (await p.InputValueAsync() ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(v) && v != "-")
                        {
                            _knownProjectText = v;
                            V($"   OSI/Projeto conhecido capturado: '{v}'");
                            break;
                        }
                    }
                }
            }

            V($"   datas com registro: {RegisteredDates.Count} | horários coletados em {RegisteredTimesByDate.Count} data(s)");
            _validator.LoadExistingTimes(RegisteredTimesByDate);
            return RegisteredDates;
        }
        catch (Exception ex)
        {
            _log($"Erro ao obter datas cadastradas: {ex.Message}");
            return RegisteredDates;
        }
    }

    public bool IsDateRegistered(string date) => RegisteredDates.Contains(date);

    /// <summary>
    /// Datas presentes no arquivo que não têm card correspondente no período filtrado
    /// (dia fora do período, bloqueado para lançamento ou data inválida).
    /// </summary>
    public async Task<List<string>> GetUnavailableDatesAsync(IEnumerable<string> dates)
    {
        var missing = new List<string>();
        if (_page is null) return missing;
        foreach (var date in dates)
        {
            var card = await FindDayCardAsync(date);
            if (card is null) { missing.Add(date); continue; }
            var allowed = (await card.GetAttributeAsync(SsgSelectors.AttrAccessAllowed) ?? "Y").Trim();
            var valid = (await card.GetAttributeAsync(SsgSelectors.AttrValidDate) ?? "Y").Trim();
            if (!allowed.Equals("Y", StringComparison.OrdinalIgnoreCase) ||
                !valid.Equals("Y", StringComparison.OrdinalIgnoreCase))
                missing.Add(date);
        }
        return missing;
    }

    // ------------------------------------------------------------------
    // Registro do ponto
    // ------------------------------------------------------------------

    public async Task<(bool success, List<string> adjustments)> RegisterPunchAsync(PunchRecord record)
    {
        if (_page is null) return (false, new());
        if (_config.Automation.IgnoreExistingDates && IsDateRegistered(record.Date))
        {
            V($"⏭️  RegisterPunchAsync({record.Date}) ignorado (já cadastrado)");
            return (true, new());
        }

        var adjusted = _validator.Adjust(record.Date, record.Entry, record.LunchOut, record.LunchReturn, record.Exit);
        V($"▶️  RegisterPunchAsync IN  raw=[{record.Date} | {record.Entry} | {record.LunchOut} | {record.LunchReturn} | {record.Exit}]");
        V($"   ajustado=[{adjusted.Date} | {adjusted.Entry} | {adjusted.LunchOut} | {adjusted.LunchReturn} | {adjusted.Exit}]");
        if (adjusted.Adjustments.Count > 0)
            V($"   ajustes aplicados: {string.Join(" ; ", adjusted.Adjustments)}");

        try
        {
            // 1. Localiza o card do dia (já renderizado pelo filtro — não se cria linha nem se digita data)
            var card = await FindDayCardAsync(adjusted.Date);
            if (card is null)
            {
                _log($"❌ {adjusted.Date}: card do dia não encontrado no período filtrado");
                return (false, adjusted.Adjustments);
            }

            var allowed = (await card.GetAttributeAsync(SsgSelectors.AttrAccessAllowed) ?? "Y").Trim();
            var validDate = (await card.GetAttributeAsync(SsgSelectors.AttrValidDate) ?? "Y").Trim();
            V($"🔸 Etapa 1/5: card localizado (access-allowed={allowed}, valid-date={validDate})");
            if (!allowed.Equals("Y", StringComparison.OrdinalIgnoreCase) ||
                !validDate.Equals("Y", StringComparison.OrdinalIgnoreCase))
            {
                _log($"⛔ {adjusted.Date}: dia não permite lançamento (allowed={allowed}, valid={validDate})");
                return (false, adjusted.Adjustments);
            }

            // 2. Expande o card
            V("🔸 Etapa 2/5: expandindo o card do dia");
            await EnsureDayExpandedAsync(card);

            // 3. Pares Entrada/Saída
            var pairs = !string.IsNullOrWhiteSpace(adjusted.LunchOut) && !string.IsNullOrWhiteSpace(adjusted.LunchReturn)
                ? new[] { (adjusted.Entry, adjusted.LunchOut), (adjusted.LunchReturn, adjusted.Exit) }
                : new[] { (adjusted.Entry, adjusted.Exit) };
            V($"🔸 Etapa 3/5: inserindo {pairs.Length} par(es): " +
              string.Join(" | ", pairs.Select(p => $"{p.Item1}→{p.Item2}")));

            foreach (var (entryTime, exitTime) in pairs)
            {
                var before = await card.Locator(SsgSelectors.AccessRows).CountAsync();
                await card.Locator(SsgSelectors.AddAccessRow).First.ClickAsync();
                await WaitForRowCountAsync(card, SsgSelectors.AccessRows, before + 1);

                var row = card.Locator(SsgSelectors.AccessRows).Last;
                var okIn = await FillTimeFieldAsync(row.Locator(SsgSelectors.ClockIn).First, entryTime, $"E/S[{entryTime}].ENTRADA");
                var okOut = await FillTimeFieldAsync(row.Locator(SsgSelectors.ClockOut).First, exitTime, $"E/S[{exitTime}].SAÍDA");
                if (!okIn || !okOut)
                {
                    _log($"❌ {adjusted.Date}: falha ao preencher {entryTime}-{exitTime}");
                    return (false, adjusted.Adjustments);
                }
            }

            // 4. Apontamento: horas + OSI/Projeto/Atividade
            V("🔸 Etapa 4/5: adicionando linha de apontamento");
            var apBefore = await card.Locator(SsgSelectors.AppointmentRows).CountAsync();
            await card.Locator(SsgSelectors.AddAppointmentRow).First.ClickAsync();
            await WaitForRowCountAsync(card, SsgSelectors.AppointmentRows, apBefore + 1);
            var appointmentRow = card.Locator(SsgSelectors.AppointmentRows).Last;

            // Prefere o total que o próprio SSG calculou para o dia (evita divergência
            // entre "Horas Totais" e "Horas Apontadas").
            var hours = await ReadDayTotalHoursAsync(card)
                        ?? CalcWorkedHours(adjusted.Entry, adjusted.LunchOut, adjusted.LunchReturn, adjusted.Exit);
            V($"   horas apontadas='{hours}'");
            await FillTimeFieldAsync(appointmentRow.Locator(SsgSelectors.AppointedHours).First, hours, "HORAS APONTADAS");

            V("🔸 Etapa 5/5: selecionando OSI/Projeto/Atividade");
            if (!await SelectProjectAsync(appointmentRow))
                _log($"⚠️  {adjusted.Date}: OSI/Projeto/Atividade não selecionado — confira no navegador antes de salvar");

            _validator.RegisterUsedTimes(adjusted.Date, new[]
            {
                adjusted.Entry, adjusted.LunchOut, adjusted.LunchReturn, adjusted.Exit
            });
            RegisteredDates.Add(record.Date);
            V($"✅ RegisterPunchAsync OUT success=true {record.Date}");
            return (true, adjusted.Adjustments);
        }
        catch (Exception ex)
        {
            V($"❌ RegisterPunchAsync EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            V($"   StackTrace: {ex.StackTrace}");
            _log($"Erro ao registrar {record.Date}: {ex.Message}");
            return (false, adjusted.Adjustments);
        }
    }

    private async Task<ILocator?> FindDayCardAsync(string date)
    {
        if (_page is null) return null;
        var formatted = FormatDate(date);
        var card = _page.Locator($"{SsgSelectors.DayCard}[{SsgSelectors.AttrDate}=\"{formatted}\"]").First;
        return await card.CountAsync() > 0 ? card : null;
    }

    private async Task EnsureDayExpandedAsync(ILocator card)
    {
        await card.ScrollIntoViewIfNeededAsync();
        var body = card.Locator(SsgSelectors.DayBody).First;
        if (await body.CountAsync() == 0) return;
        if (await body.IsVisibleAsync()) return;

        await card.Locator(SsgSelectors.DayToggle).First.ClickAsync();
        try
        {
            await body.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        }
        catch
        {
            V("   ⚠️ card não expandiu no tempo esperado");
        }
        await _page!.WaitForTimeoutAsync(300);
    }

    private async Task WaitForRowCountAsync(ILocator card, string rowsSelector, int expected)
    {
        var rows = card.Locator(rowsSelector);
        for (var i = 0; i < 40; i++)
        {
            if (await rows.CountAsync() >= expected) return;
            await _page!.WaitForTimeoutAsync(100);
        }
        V($"   ⚠️ esperado {expected} linha(s) em '{rowsSelector}', encontrado {await rows.CountAsync()}");
    }

    /// <summary>Lê "Horas Totais" calculado pelo SSG no card do dia.</summary>
    private async Task<string?> ReadDayTotalHoursAsync(ILocator card)
    {
        try
        {
            var span = card.Locator(SsgSelectors.AccessTotalHours).First;
            if (await span.CountAsync() == 0) return null;
            var text = (await span.InnerTextAsync() ?? string.Empty).Trim();
            var match = Regex.Match(text, @"\b([01]?\d|2[0-3]):[0-5]\d\b");
            if (!match.Success) return null;
            var value = match.Value.PadLeft(5, '0');
            if (value == "00:00") return null;
            V($"   Horas Totais lidas do SSG='{value}'");
            return value;
        }
        catch { return null; }
    }

    /// <summary>
    /// Seleciona o OSI/Projeto/Atividade da linha de apontamento. Caminho principal:
    /// botão "?" (<c>.button-show-items</c>) que abre a "Listagem de Itens" e escolhe o
    /// item via <c>.button-select</c>. Fallback: autocomplete, digitando o início de um
    /// projeto já usado pelo profissional em outro dia.
    /// </summary>
    private async Task<bool> SelectProjectAsync(ILocator appointmentRow)
    {
        if (_page is null) return false;
        var field = appointmentRow.Locator(SsgSelectors.ProjectActivity).First;

        // Caminho 1: modal "Listagem de Itens"
        try
        {
            var showItems = appointmentRow.Locator(SsgSelectors.ShowItemsButton).First;
            if (await showItems.CountAsync() > 0)
            {
                await showItems.ClickAsync();
                var modal = _page.Locator(SsgSelectors.ItemsModal).First;
                await modal.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });

                var rows = await _page.Locator(SsgSelectors.ItemsModalRows).AllAsync();
                V($"   modal de itens: {rows.Count} linha(s)");
                foreach (var row in rows)
                {
                    var text = (await row.InnerTextAsync() ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(text) || text == "-") continue;

                    var select = row.Locator(SsgSelectors.ItemsModalSelect).First;
                    if (await select.CountAsync() == 0) continue;

                    await select.ClickAsync();
                    try { await modal.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10000 }); } catch { }
                    await _page.WaitForTimeoutAsync(400);

                    var value = (await field.InputValueAsync() ?? string.Empty).Trim();
                    V($"   item selecionado no modal → campo='{value}'");
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        _knownProjectText ??= value;
                        return true;
                    }
                    break;
                }

                // Fecha o modal se ainda estiver aberto
                try
                {
                    if (await modal.IsVisibleAsync())
                    {
                        await _page.Locator(SsgSelectors.ItemsModalClose).First.ClickAsync();
                        await _page.WaitForTimeoutAsync(300);
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            V($"   modal de itens falhou: {ex.Message}");
        }

        // Caminho 2: autocomplete com um projeto já conhecido
        if (string.IsNullOrWhiteSpace(_knownProjectText))
        {
            V("   ⚠️ sem projeto conhecido para o autocomplete");
            return false;
        }

        try
        {
            var term = _knownProjectText!.Length > 12 ? _knownProjectText[..12] : _knownProjectText;
            await field.ClickAsync();
            await field.PressAsync("Control+A");
            await field.PressAsync("Delete");
            await field.PressSequentiallyAsync(term, new() { Delay = 120 });

            var suggestion = _page.Locator(SsgSelectors.TypeaheadItems).First;
            await suggestion.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await suggestion.ClickAsync();
            await _page.WaitForTimeoutAsync(400);

            var value = (await field.InputValueAsync() ?? string.Empty).Trim();
            V($"   autocomplete (term='{term}') → campo='{value}'");
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            V($"   autocomplete falhou: {ex.Message}");
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Salvar
    // ------------------------------------------------------------------

    /// <summary>Clica em "Salvar dias alterados" e confirma o diálogo, se houver.</summary>
    public async Task<bool> ConfirmEntriesAsync()
    {
        if (_page is null) return false;
        V("ConfirmEntriesAsync: clicando 'Salvar dias alterados'");
        try
        {
            var save = _page.Locator(SsgSelectors.SaveButton).First;
            await save.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await save.ScrollIntoViewIfNeededAsync();
            await save.ClickAsync();
            await _page.WaitForTimeoutAsync(1500);

            // Diálogo de confirmação ("Confirma o salvamento?"), quando exibido
            var confirm = _page.Locator(SsgSelectors.BootboxPrimary).First;
            if (await confirm.CountAsync() > 0 && await confirm.IsVisibleAsync())
            {
                V("   confirmando diálogo de salvamento");
                await confirm.ClickAsync();
                await _page.WaitForTimeoutAsync(1500);
            }

            V("ConfirmEntriesAsync: OK");
            return true;
        }
        catch (Exception ex)
        {
            _log($"Erro ao salvar: {ex.Message}");
            return false;
        }
    }

    /// <summary>Fecha o modal de resultado exibido após o salvamento.</summary>
    public async Task<bool> CloseConfirmationModalAsync()
    {
        if (_page is null) return false;
        try
        {
            var button = _page.Locator(SsgSelectors.BootboxPrimary).First;
            await button.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
            var message = await ReadModalMessageAsync();
            if (!string.IsNullOrWhiteSpace(message)) _log($"💬 SSG: {message}");
            await button.ClickAsync();
            await _page.WaitForTimeoutAsync(500);
            return true;
        }
        catch (Exception ex)
        {
            V($"CloseConfirmationModalAsync: {ex.Message}");
            return false;
        }
    }

    private async Task<string?> ReadModalMessageAsync()
    {
        if (_page is null) return null;
        try
        {
            var body = _page.Locator(".bootbox .modal-body, div.modal.in .modal-body").First;
            if (await body.CountAsync() == 0) return null;
            var text = (await body.InnerTextAsync() ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text.Replace('\n', ' ');
        }
        catch { return null; }
    }

    /// <summary>
    /// Fecha qualquer modal aberto (avisos de validação, "Listagem de Itens", sucesso).
    /// Necessário porque um modal esquecido mantém o <c>.modal-backdrop</c> ativo e todos
    /// os cliques seguintes falham por timeout.
    /// </summary>
    private async Task DismissModalsAsync()
    {
        if (_page is null) return;
        for (var i = 0; i < 6; i++)
        {
            try
            {
                var modal = _page.Locator(SsgSelectors.AnyVisibleModal).First;
                if (await modal.CountAsync() == 0 || !await modal.IsVisibleAsync()) return;

                var message = await ReadModalMessageAsync();
                if (!string.IsNullOrWhiteSpace(message))
                    V($"   fechando modal: {message[..Math.Min(120, message.Length)]}");

                var button = modal.Locator(SsgSelectors.ModalCloseButtons).First;
                if (await button.CountAsync() > 0)
                    await button.ClickAsync(new() { Timeout = 5000 });
                else
                    await _page.Keyboard.PressAsync("Escape");

                await modal.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5000 });
            }
            catch { return; }
        }
    }

    // ------------------------------------------------------------------
    // Utilidades
    // ------------------------------------------------------------------

    private static bool IsTime(string value)
        => !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, @"^([01]?\d|2[0-3]):[0-5]\d$");

    private static string FormatTime(string time)
    {
        if (string.IsNullOrWhiteSpace(time)) return time;
        var parts = time.Split(':');
        if (parts.Length != 2) return time;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return time;
        return $"{h:D2}:{m:D2}";
    }

    /// <summary>
    /// Normaliza datas para DD/MM/YYYY. Aceita '20/5/2025', '20-05-2025' ou '2025-05-20'.
    /// </summary>
    private static string FormatDate(string date)
    {
        if (string.IsNullOrWhiteSpace(date)) return date;
        var s = date.Trim().Replace('-', '/').Replace('.', '/');
        var parts = s.Split('/');
        if (parts.Length != 3) return date;

        int d, m, y;
        if (parts[0].Length == 4 && int.TryParse(parts[0], out var yIso)
            && int.TryParse(parts[1], out var mIso) && int.TryParse(parts[2], out var dIso))
        {
            y = yIso; m = mIso; d = dIso;
        }
        else if (int.TryParse(parts[0], out d) && int.TryParse(parts[1], out m) && int.TryParse(parts[2], out y))
        {
            if (y < 100) y += 2000;
        }
        else
        {
            return date;
        }
        return $"{d:D2}/{m:D2}/{y:D4}";
    }

    private static string CalcWorkedHours(string entry, string lunchOut, string lunchReturn, string exit)
    {
        int ToMinutes(string t)
        {
            var p = t.Split(':');
            return int.Parse(p[0], CultureInfo.InvariantCulture) * 60 + int.Parse(p[1], CultureInfo.InvariantCulture);
        }
        try
        {
            int total;
            if (!string.IsNullOrWhiteSpace(lunchOut) && !string.IsNullOrWhiteSpace(lunchReturn))
                total = (ToMinutes(lunchOut) - ToMinutes(entry)) + (ToMinutes(exit) - ToMinutes(lunchReturn));
            else
                total = ToMinutes(exit) - ToMinutes(entry);
            return $"{total / 60:D2}:{total % 60:D2}";
        }
        catch { return "08:00"; }
    }

    private static string FindChrome()
    {
        var candidates = new[]
        {
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Google\Chrome\Application\chrome.exe"),
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"),
            Environment.ExpandEnvironmentVariables(@"%LocalAppData%\Google\Chrome\Application\chrome.exe")
        };
        return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        // Intencional: o navegador NÃO é fechado ao final da execução, permitindo que o
        // usuário revise os apontamentos. Também não encerramos um Chrome reutilizado.
        await Task.CompletedTask;
        _playwright?.Dispose();
    }
}
