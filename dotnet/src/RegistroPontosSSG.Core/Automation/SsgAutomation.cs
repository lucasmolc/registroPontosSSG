using Microsoft.Playwright;
using RegistroPontosSSG.Core.Models;
using RegistroPontosSSG.Core.Security;
using RegistroPontosSSG.Core.Validation;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;

namespace RegistroPontosSSG.Core.Automation;

/// <summary>
/// Automação Playwright do sistema SSG (Sysmap).
/// Porte fiel da implementação Python: login + 2FA TOTP + filtro de mês + registro de pontos.
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
    public HashSet<string> RegisteredDates { get; } = new();
    /// <summary>
    /// Horários (HH:mm) já cadastrados no SSG, agrupados por data DD/MM/YYYY.
    /// Alimentado por <see cref="GetRegisteredDatesAsync"/> e usado para que a regra
    /// de "duplicado em dias próximos" enxergue horários históricos que NÃO estão
    /// no arquivo de entrada (caso típico: 17:26 do dia 19 já salvo no SSG).
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

    /// <summary>
    /// Preenche um campo de hora com máscara HH:MM, tratando casos onde o site formata
    /// automaticamente os dígitos (ex.: digitar "09:16" produzia "91:60" porque o ":"
    /// digitado conflitava com a máscara). Estratégia:
    /// 1) Foca e limpa via teclado (Ctrl+A + Delete) — FillAsync("") não limpa campos mascarados.
    /// 2) Digita apenas os 4 dígitos, deixando a máscara aplicar ":" sozinha.
    /// 3) Lê o valor de volta; se ficou diferente do esperado, tenta novamente
    ///    com FillAsync direto (alguns campos aceitam HH:MM via fill).
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
    /// Preenche um campo de data com máscara DD/MM/YYYY. Mesma lógica do FillTimeFieldAsync:
    /// envia somente os 8 dígitos e deixa a máscara aplicar as "/" automaticamente.
    /// Em campos sem máscara, faz fallback para FillAsync com DD/MM/YYYY.
    /// </summary>
    private async Task<bool> FillDateFieldAsync(ILocator field, string date, string label)
    {
        var formatted = FormatDate(date);
        var digits = new string(formatted.Where(char.IsDigit).ToArray());
        if (digits.Length != 8)
        {
            V($"   {label}: ⚠️ valor '{date}' não possui 8 dígitos (esperado DD/MM/YYYY), abortando");
            return false;
        }
        return await FillMaskedFieldAsync(field, digits, formatted, label);
    }

    /// <summary>
    /// Núcleo do preenchimento de campos mascarados. Recebe os dígitos crus
    /// (sem separadores) e o valor formatado esperado para validação.
    /// </summary>
    private async Task<bool> FillMaskedFieldAsync(ILocator field, string digitsOnly, string expectedFormatted, string label)
    {
        // Tentativa 1: limpar via teclado (triple-click seleciona conteúdo) + digitar só os dígitos.
        // PressSequentially com Delay maior reduz casos onde a máscara perde caracteres.
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

        // Tentativa 3: FillAsync direto com o valor formatado completo
        await field.FocusAsync();
        await field.PressAsync("Control+A");
        await field.PressAsync("Delete");
        await field.FillAsync(expectedFormatted);
        await field.PressAsync("Tab");
        await _page!.WaitForTimeoutAsync(150);
        var v3 = (await field.InputValueAsync() ?? string.Empty).Trim();
        V($"   {label}: tentativa 3 (fill='{expectedFormatted}') → campo='{v3}'");
        if (v3 == expectedFormatted) return true;

        V($"   {label}: ❌ valor final '{v3}' diferente do esperado '{expectedFormatted}'");
        return false;
    }

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

        var port = FindFreePort();
        var args = new List<string>
        {
            $"--remote-debugging-port={port}",
            $"--user-data-dir={userDataDir}",
            "--disable-background-networking", "--disable-client-side-phishing-detection",
            "--disable-default-apps", "--disable-hang-monitor", "--disable-popup-blocking",
            "--disable-prompt-on-repost", "--disable-sync", "--disable-translate",
            "--metrics-recording-only", "--no-first-run", "--safebrowsing-disable-auto-update"
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

        await Task.Delay(3000);

        try
        {
            _browser = await _playwright!.Chromium.ConnectOverCDPAsync($"http://localhost:{port}");
            _context = _browser.Contexts.Count > 0 ? _browser.Contexts[0] : await _browser.NewContextAsync();
            _page = _context.Pages.Count > 0 ? _context.Pages[0] : await _context.NewPageAsync();
            _page.SetDefaultTimeout(_config.Automation.TimeoutMs);
            _log("Conectado ao Chrome do sistema");
        }
        catch (Exception ex)
        {
            _log($"Erro ao conectar ao Chrome: {ex.Message}. Tentando Chromium embutido.");
            try { _chromeProcess?.Kill(); } catch { }
            await StartBundledChromiumAsync();
        }
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

    public async Task<bool> LoginAsync()
    {
        if (_page is null) return false;
        _progress("Acessando SSG...");
        V($"LoginAsync: navegando para {_config.Ssg.BaseUrl}/");
        try
        {
            await _page.GotoAsync(_config.Ssg.BaseUrl + "/");

            _progress("Aguardando Cloudflare...");
            V("LoginAsync: aguardando URL portal.sysmap.com.br ou wp-login (timeout 120s)");
            await _page.WaitForURLAsync(url => url.Contains("portal.sysmap.com.br") || url.Contains("wp-login"), new() { Timeout = 120000 });
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            V($"LoginAsync: URL atual='{_page.Url}'");

            _progress("Preenchendo credenciais...");
            V($"LoginAsync: preenchendo #user_login com '{_config.Credentials.Username}'");
            await _page.WaitForSelectorAsync("#user_login", new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
            await _page.Locator("#user_login").FillAsync(_config.Credentials.Username);
            V($"LoginAsync: preenchendo #user_pass (len={_config.Credentials.Password.Length})");
            await _page.Locator("#user_pass").FillAsync(_config.Credentials.Password);

            // 2FA
            try
            {
                await _page.WaitForSelectorAsync("#googleotp", new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
                var totpField = _page.Locator("#googleotp");
                await totpField.FocusAsync();

                if (!string.IsNullOrWhiteSpace(_config.Credentials.TotpSecret))
                {
                    var code = TotpGenerator.GenerateCode(_config.Credentials.TotpSecret);
                    _progress($"Preenchendo 2FA automaticamente: {code}");
                    await totpField.FillAsync(code);
                    await _page.WaitForTimeoutAsync(500);
                    var submit = _page.Locator("input[type=\"submit\"], button[type=\"submit\"], input[name=\"wp-submit\"]").First;
                    await submit.ClickAsync();
                }
                else
                {
                    _progress("Digite o código 2FA no navegador...");
                }
            }
            catch
            {
                var submit = _page.Locator("input[type=\"submit\"], button[type=\"submit\"], input[name=\"wp-submit\"]").First;
                try { await submit.ClickAsync(); } catch { }
            }

            _progress("Aguardando finalização do login (até 5 min)...");
            await _page.WaitForURLAsync(url =>
                url == "https://portal.sysmap.com.br/" ||
                (url.StartsWith("https://portal.sysmap.com.br") && !url.Contains("wp-login") && !url.Contains("wp-admin")),
                new() { Timeout = 300000 });

            _progress("Navegando para timesheet...");
            await _page.GotoAsync(_config.Ssg.TimesheetUrl);
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            return true;
        }
        catch (Exception ex)
        {
            _log($"Erro no login: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SelectMonthAndFilterAsync(string period)
    {
        if (_page is null || !_config.Automation.SelectCurrentMonth) return true;
        var label = period == "mes_passado" ? "mês passado" : "mês atual";
        V($"SelectMonthAndFilterAsync: period='{period}' (label={label})");
        try
        {
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            var dropdown = _page.Locator("xpath=/html/body/div[3]/div[2]/div[2]/div[3]/div/div/div/a[2]");
            await dropdown.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
            await dropdown.ClickAsync();

            var idx = period == "mes_passado" ? 2 : 1;
            var option = _page.Locator($"xpath=/html/body/div[3]/div[2]/div[2]/div[3]/div/div/div/ul/li[{idx}]/a");
            await option.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
            await option.ClickAsync();

            var search = _page.Locator("#ButtonSearch");
            await search.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
            await search.ClickAsync();
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            return true;
        }
        catch (Exception ex)
        {
            _log($"Erro ao filtrar {label}: {ex.Message}");
            return false;
        }
    }

    public async Task<HashSet<string>> GetRegisteredDatesAsync()
    {
        if (_page is null) return new();
        V("GetRegisteredDatesAsync: lendo #TableTimesheet input.activity-timesheet[date] + horários por data");
        try
        {
            await _page.WaitForSelectorAsync("#TableTimesheet", new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
            var inputs = await _page.Locator("#TableTimesheet input.activity-timesheet[date]").AllAsync();
            V($"   {inputs.Count} input(s) de data encontrados");
            foreach (var inp in inputs)
            {
                var date = await inp.GetAttributeAsync("date");
                if (!string.IsNullOrWhiteSpace(date) && date.Contains('/'))
                    RegisteredDates.Add(date.Trim());
            }
            V($"   datas únicas registradas: {RegisteredDates.Count}");

            // Coleta também os horários (clock-in/out) já preenchidos de cada data.
            // Isto é necessário para que BlockDuplicateTimes/BlockSameMinutes enxerguem
            // horários cadastrados em execuções anteriores e não voltem a usá-los.
            var rows = await _page.Locator("#TableTimesheet tbody tr").AllAsync();
            foreach (var row in rows)
            {
                var dateField = row.Locator("input.activity-timesheet[date]").First;
                if (await dateField.CountAsync() == 0) continue;
                var date = await dateField.GetAttributeAsync("date");
                if (string.IsNullOrWhiteSpace(date)) continue;
                date = date.Trim();

                var clockRows = await row.Locator("table.table-clockInOut tbody tr.dynamicClockInOut").AllAsync();
                if (clockRows.Count == 0) continue;
                if (!RegisteredTimesByDate.TryGetValue(date, out var list))
                {
                    list = new List<string>();
                    RegisteredTimesByDate[date] = list;
                }
                foreach (var cr in clockRows)
                {
                    var inF = cr.Locator("input.textbox-clockin");
                    var outF = cr.Locator("input.textbox-clockout");
                    if (await inF.CountAsync() > 0)
                    {
                        var v = (await inF.InputValueAsync() ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(v)) list.Add(v);
                    }
                    if (await outF.CountAsync() > 0)
                    {
                        var v = (await outF.InputValueAsync() ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(v)) list.Add(v);
                    }
                }
            }
            V($"   horários históricos coletados em {RegisteredTimesByDate.Count} data(s)");

            // Alimenta o validador para que as regras de duplicidade considerem o
            // que já está salvo no SSG (não apenas o que estamos inserindo agora).
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
    /// Lista de datas (DD/MM/YYYY) que foram marcadas para exclusão por
    /// <see cref="MarkPartialDateForDeletionAsync"/> e estão aguardando
    /// <see cref="ExecutePendingDeletionsAsync"/> ser invocado em lote.
    /// </summary>
    public List<string> PendingDeletions { get; } = new();

    /// <summary>
    /// Para uma data já cadastrada no SSG, identifica se faltam pares Entrada-Saída
    /// em relação ao arquivo de origem. Quando faltam, marca os checkboxes corretos
    /// (clock-in/out esquerda + linha do projeto acima de Horas Apontadas) e adiciona
    /// a data em <see cref="PendingDeletions"/>. NÃO clica o X nem confirma — isso
    /// é feito uma única vez em lote por <see cref="ExecutePendingDeletionsAsync"/>.
    /// Retorna true se a data foi marcada para exclusão.
    /// </summary>
    public async Task<bool> MarkPartialDateForDeletionAsync(PunchRecord record)
    {
        if (_page is null) return false;
        if (!RegisteredDates.Contains(record.Date)) return false;

        // Pares desejados a partir do arquivo de origem
        var desired = new List<(string inV, string outV)>();
        if (!string.IsNullOrWhiteSpace(record.Entry) && !string.IsNullOrWhiteSpace(record.LunchOut))
            desired.Add((record.Entry, record.LunchOut));
        if (!string.IsNullOrWhiteSpace(record.LunchReturn) && !string.IsNullOrWhiteSpace(record.Exit))
            desired.Add((record.LunchReturn, record.Exit));
        if (desired.Count == 0 && !string.IsNullOrWhiteSpace(record.Entry) && !string.IsNullOrWhiteSpace(record.Exit))
            desired.Add((record.Entry, record.Exit));
        if (desired.Count == 0) return false;

        V($"CompletePartialDateAsync({record.Date}): pares desejados={desired.Count} → " +
          string.Join(" | ", desired.Select(p => $"{p.inV}-{p.outV}")));

        // Localiza a linha (row) cuja data bate
        var candidateRows = await _page.Locator("#TableTimesheet > tbody > tr:has(input.activity-timesheet[date])").AllAsync();
        if (candidateRows.Count == 0)
            candidateRows = await _page.Locator("#TableTimesheet tbody tr").AllAsync();
        ILocator? targetRow = null;
        foreach (var row in candidateRows)
        {
            var df = row.Locator("input.activity-timesheet[date]").First;
            if (await df.CountAsync() == 0) continue;
            string? d = null;
            try { d = await df.GetAttributeAsync("date", new() { Timeout = 2000 }); }
            catch { continue; }
            if (!string.IsNullOrWhiteSpace(d) && d.Trim() == record.Date)
            {
                targetRow = row;
                break;
            }
        }
        if (targetRow is null)
        {
            V($"   linha da data {record.Date} não encontrada — pulando");
            return false;
        }

        // Conta pares já existentes (lê tanto inputs editáveis quanto células de texto)
        var existingPairs = await ReadExistingPairsAsync(targetRow);
        V($"   pares já no SSG={existingPairs.Count} | desejados={desired.Count}");

        if (existingPairs.Count >= desired.Count)
        {
            V($"   SSG já tem {existingPairs.Count} ≥ {desired.Count} pares — nada a completar");
            return false;
        }

        // 1) Marca SOMENTE o checkbox mais à esquerda de cada linha de clock-in/out
        //    do dia (a coluna "select" da tabela table-clockInOut). NÃO devemos marcar
        //    o checkbox de "Banco de Horas" (coluna à direita) nem checkboxes da
        //    tabela de timesheetrecording — isso causaria exclusão indevida de outros
        //    dias / efeitos colaterais (o que aconteceu nos dias 20 e 21).
        V($"   ⚠️  {record.Date} está incompleto — excluindo todos os apontamentos para recadastrar");
        var clockSelectCells = await targetRow
            .Locator("table.table-clockInOut tbody tr.dynamicClockInOut > td:first-child input[type='checkbox']")
            .AllAsync();
        // Fallback caso a estrutura não tenha tr.dynamicClockInOut explícita
        if (clockSelectCells.Count == 0)
        {
            clockSelectCells = await targetRow
                .Locator("table.table-clockInOut tbody tr > td:first-child input[type='checkbox']")
                .AllAsync();
        }
        V($"   checkboxes de seleção encontrados (col esquerda clockInOut)={clockSelectCells.Count}");
        foreach (var cb in clockSelectCells)
        {
            try
            {
                if (!await cb.IsCheckedAsync()) await cb.CheckAsync(new() { Timeout = 2000 });
            }
            catch { /* invisíveis — ignora */ }
        }

        // 1b) Marca também o checkbox da linha do projeto (acima de "Horas Apontadas"),
        //     que pertence à table-timesheetrecording. Sem ele a exclusão remove apenas
        //     os clock-in/out mas mantém a linha do projeto/horas-apontadas órfã.
        var projectSelectCells = await targetRow
            .Locator("table.table-timesheetrecording tbody tr.dynamicTimesheetrecording > td:first-child input[type='checkbox']")
            .AllAsync();
        if (projectSelectCells.Count == 0)
        {
            projectSelectCells = await targetRow
                .Locator("table.table-timesheetrecording tbody tr > td:first-child input[type='checkbox']")
                .AllAsync();
        }
        V($"   checkboxes da linha do projeto (acima de Horas Apontadas)={projectSelectCells.Count}");
        foreach (var cb in projectSelectCells)
        {
            try
            {
                if (!await cb.IsCheckedAsync()) await cb.CheckAsync(new() { Timeout = 2000 });
            }
            catch { /* invisíveis — ignora */ }
        }

        // Não clica o X aqui — a exclusão é executada em lote por
        // ExecutePendingDeletionsAsync para minimizar a sobrecarga de
        // modais de segurança/confirmação repetidos.
        if (!PendingDeletions.Contains(record.Date))
            PendingDeletions.Add(record.Date);
        V($"   {record.Date} marcado para exclusão em lote (total pendente={PendingDeletions.Count})");
        return true;
    }

    /// <summary>
    /// Executa a exclusão em lote de todas as datas previamente marcadas por
    /// <see cref="MarkPartialDateForDeletionAsync"/>. Estratégia:
    /// 1) Clica UMA ÚNICA vez no botão "X" do header (que age sobre todos os
    ///    checkboxes marcados, somando linhas de múltiplos dias).
    /// 2) Confirma o primeiro modal (modal de segurança "Tem certeza?").
    /// 3) Fecha os modais subsequentes "Perfeito! Registro excluído com sucesso"
    ///    — um para cada data excluída.
    /// 4) Limpa o cache de datas/horários para as datas excluídas e zera a lista
    ///    de pendências.
    /// </summary>
    public async Task<bool> ExecutePendingDeletionsAsync()
    {
        if (_page is null) return false;
        if (PendingDeletions.Count == 0) return true;

        V($"ExecutePendingDeletionsAsync: {PendingDeletions.Count} data(s) pendente(s) → {string.Join(", ", PendingDeletions)}");
        _progress($"Excluindo {PendingDeletions.Count} dia(s) em lote...");

        // 1) Clica o X uma única vez
        var deleted = await ClickHeaderDeleteAsync();
        if (!deleted)
        {
            _log("⚠️  Não foi possível clicar no botão de exclusão (lote)");
            return false;
        }

        // 2) Confirma o modal de segurança ("Tem certeza?")
        await ConfirmDeleteModalAsync();
        await _page.WaitForTimeoutAsync(500);

        // 3) Fecha os modais "Perfeito! Registro excluído com sucesso" — um por data.
        //    O SSG enfileira esses modais; cada OK fecha um e o próximo aparece.
        for (var i = 0; i < PendingDeletions.Count; i++)
        {
            try
            {
                if (!await CloseSuccessModalAsync(timeoutMs: 15000))
                {
                    V($"   modal 'Perfeito!' [{i + 1}/{PendingDeletions.Count}] não detectado — encerrando loop de fechamento");
                    break;
                }
                V($"   modal 'Perfeito!' [{i + 1}/{PendingDeletions.Count}] fechado");
            }
            catch (Exception ex)
            {
                V($"   exceção ao fechar modal 'Perfeito!' [{i + 1}]: {ex.Message}");
                break;
            }
        }
        await _page.WaitForTimeoutAsync(800);

        // 4) Limpa cache para as datas removidas
        foreach (var d in PendingDeletions)
        {
            RegisteredDates.Remove(d);
            RegisteredTimesByDate.Remove(d);
        }
        _log($"🗑️  {PendingDeletions.Count} dia(s) excluído(s) em lote");
        PendingDeletions.Clear();
        return true;
    }

    /// <summary>
    /// Fecha o modal de sucesso "Perfeito! Registro excluído com sucesso" (botão OK).
    /// Tenta vários seletores e aguarda o modal sumir de fato.
    /// </summary>
    private async Task<bool> CloseSuccessModalAsync(int timeoutMs)
    {
        if (_page is null) return false;
        var candidates = new[]
        {
            "div.modal.in .modal-footer button.btn-primary",
            "div.modal.in .modal-footer button",
            "div.modal:visible .modal-footer button.btn-primary",
            ".bootbox.modal.in button.btn-primary",
            "xpath=/html/body/div[9]/div/div/div[2]/button",
            "xpath=//div[contains(@class,'modal') and contains(@class,'in')]//div[contains(@class,'modal-footer')]//button[contains(.,'OK')]"
        };
        foreach (var sel in candidates)
        {
            var loc = _page.Locator(sel).First;
            try
            {
                await loc.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
                await loc.ClickAsync(new() { Timeout = 2000 });
                // Aguarda o modal sumir antes de processar o próximo
                try { await loc.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5000 }); } catch { }
                return true;
            }
            catch { /* tenta próximo */ }
        }
        return false;
    }

    /// <summary>
    /// Wrapper de compatibilidade: marca a data para exclusão e re-cadastra
    /// imediatamente. Mantido para chamadas legadas; o fluxo preferido é
    /// <see cref="MarkPartialDateForDeletionAsync"/> + <see cref="ExecutePendingDeletionsAsync"/>
    /// + <see cref="RegisterPunchAsync"/> chamados em lote pelo orquestrador.
    /// </summary>
    public async Task<bool> CompletePartialDateAsync(PunchRecord record)
    {
        var marked = await MarkPartialDateForDeletionAsync(record);
        if (!marked) return false;
        var ok = await ExecutePendingDeletionsAsync();
        if (!ok) return false;
        V($"   re-cadastrando {record.Date} via RegisterPunchAsync");
        var (success, _) = await RegisterPunchAsync(record);
        if (success)
            _log($"♻️  {record.Date}: excluído e re-cadastrado");
        return success;
    }

    /// <summary>
    /// Lê os pares Entrada/Saída de uma linha do timesheet, suportando tanto
    /// linhas editáveis (input.textbox-clockin/out) quanto linhas já salvas
    /// renderizadas como texto.
    /// </summary>
    private async Task<List<(string inV, string outV)>> ReadExistingPairsAsync(ILocator targetRow)
    {
        var result = new List<(string inV, string outV)>();
        var allClockRows = await targetRow.Locator("table.table-clockInOut tbody tr").AllAsync();
        foreach (var cr in allClockRows)
        {
            string inV = string.Empty, outV = string.Empty;
            var inLoc = cr.Locator("input.textbox-clockin");
            var outLoc = cr.Locator("input.textbox-clockout");
            if (await inLoc.CountAsync() > 0)
            {
                try { inV = (await inLoc.First.InputValueAsync(new() { Timeout = 1500 }) ?? string.Empty).Trim(); }
                catch { }
            }
            if (await outLoc.CountAsync() > 0)
            {
                try { outV = (await outLoc.First.InputValueAsync(new() { Timeout = 1500 }) ?? string.Empty).Trim(); }
                catch { }
            }
            if (string.IsNullOrWhiteSpace(inV) && string.IsNullOrWhiteSpace(outV))
            {
                try
                {
                    var rowText = (await cr.InnerTextAsync(new() { Timeout = 1500 }) ?? string.Empty);
                    var matches = System.Text.RegularExpressions.Regex.Matches(rowText, @"\b([01]?\d|2[0-3]):[0-5]\d\b");
                    if (matches.Count >= 2)
                    {
                        inV = matches[0].Value.PadLeft(5, '0');
                        outV = matches[1].Value.PadLeft(5, '0');
                    }
                }
                catch { }
            }
            if (!string.IsNullOrWhiteSpace(inV) || !string.IsNullOrWhiteSpace(outV))
                result.Add((inV, outV));
        }
        return result;
    }

    /// <summary>
    /// Clica no botão "X" de exclusão do header do grid (topo direito, ao lado dos
    /// botões "+" e "salvar"). Tenta vários seletores para tolerar variações de DOM.
    /// </summary>
    private async Task<bool> ClickHeaderDeleteAsync()
    {
        if (_page is null) return false;
        // Ordem dos ícones no header do grid (h3 > span > i):
        //   i[1] = recarregar/reload (⊙)  ← NUNCA clicar: causa reset/efeitos colaterais
        //   i[2] = adicionar (+)         (usado em RegisterPunchAsync.addBtn)
        //   i[3] = salvar/confirmar (✓)  (usado em ConfirmEntriesAsync.saveBtn)
        //   i[4] = excluir (X)           ← este é o correto para exclusão
        //   i[5] = dropdown (▲)
        var candidates = new[]
        {
            "xpath=/html/body/div[3]/div[3]/div[1]/h3/span/i[4]",
            "h3 span i.fa-trash",
            "h3 span i.fa-times",
            "h3 span i.fa-remove",
            "h3 span i[title*='Excluir' i]",
            "h3 span i[title*='Remover' i]"
        };
        foreach (var sel in candidates)
        {
            var loc = _page.Locator(sel).First;
            try
            {
                if (await loc.CountAsync() == 0) continue;
                await loc.ClickAsync(new() { Timeout = 2000 });
                V($"   X de exclusão clicado via '{sel}'");
                return true;
            }
            catch { /* tenta próximo */ }
        }
        V("   ⚠️ nenhum seletor do X de exclusão funcionou");
        return false;
    }

    /// <summary>
    /// Aguarda e confirma o modal de exclusão (bootbox/bootstrap padrão do SSG).
    /// </summary>
    private async Task ConfirmDeleteModalAsync()
    {
        if (_page is null) return;
        var candidates = new[]
        {
            ".bootbox button.btn-primary",
            ".bootbox button.btn-danger",
            "div.modal.in button.btn-primary",
            "div.modal.in button.btn-danger",
            "div.modal button.confirm",
            "div.modal button[data-bb-handler='confirm']"
        };
        foreach (var sel in candidates)
        {
            var loc = _page.Locator(sel).First;
            try
            {
                if (await loc.CountAsync() == 0) continue;
                await loc.WaitForAsync(new() { Timeout = 3000, State = WaitForSelectorState.Visible });
                await loc.ClickAsync();
                V($"   modal de confirmação confirmado via '{sel}'");
                return;
            }
            catch { }
        }
        // Fallback: pressiona Enter
        try { await _page.Keyboard.PressAsync("Enter"); V("   modal confirmado via Enter"); } catch { }
    }

    /// <summary>
    /// Lê o valor "Horas Registro" calculado e exibido pelo próprio SSG dentro da
    /// linha (geralmente abaixo dos horários Entrada-Saída). É esse valor que deve
    /// ser inserido em "Horas Apontadas" para evitar divergência.
    /// </summary>
    private async Task<string?> ReadHorasRegistroAsync(ILocator row)
    {
        var selectors = new[]
        {
            "table.table-clockInOut tfoot td",
            "table.table-clockInOut tr.totalClockInOut td",
            "table.table-clockInOut tr.total td",
            "table.table-clockInOut .total-hours",
            "table.table-clockInOut .clockInOut-totals",
            "td.total-clockInOut",
            "[class*='totalClockInOut']"
        };
        foreach (var sel in selectors)
        {
            var loc = row.Locator(sel);
            int n;
            try { n = await loc.CountAsync(); } catch { continue; }
            if (n == 0) continue;
            try
            {
                var txt = await loc.First.InnerTextAsync(new() { Timeout = 1500 });
                var m = System.Text.RegularExpressions.Regex.Match(txt ?? string.Empty, @"\b([01]?\d|2[0-3]):[0-5]\d\b");
                if (m.Success)
                {
                    V($"   Horas Registro lida via '{sel}' = '{m.Value}'");
                    return m.Value.PadLeft(5, '0');
                }
            }
            catch { }
        }
        // Fallback: pega o último HH:mm visível na tabela de clock (que é o total).
        try
        {
            var fullText = await row.Locator("table.table-clockInOut").First.InnerTextAsync(new() { Timeout = 1500 });
            var matches = System.Text.RegularExpressions.Regex.Matches(fullText ?? string.Empty, @"\b([01]?\d|2[0-3]):[0-5]\d\b");
            if (matches.Count > 0)
            {
                var v = matches[^1].Value.PadLeft(5, '0');
                V($"   Horas Registro (fallback último HH:mm) = '{v}'");
                return v;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Varre os registros já cadastrados procurando por linhas com indicador de erro
    /// (thumbs-down vermelho — geralmente representado por &lt;i class="fa-thumbs-down"&gt;
    /// ou &lt;span class="...invalid..."&gt;). Para cada uma identificada:
    /// 1) Lê os horários atuais (entrada/saída).
    /// 2) Re-aplica todas as regras de validação (redondo, duplicado, minutos iguais, almoço 1h).
    /// 3) Reescreve os campos com os valores corrigidos.
    /// Os campos do timesheet são editáveis enquanto não confirmados.
    /// </summary>
    public async Task<int> FixFlaggedExistingRecordsAsync()
    {
        if (_page is null) return 0;
        V("FixFlaggedExistingRecordsAsync: procurando registros com indicador de erro");
        var fixedCount = 0;
        try
        {
            await _page.WaitForSelectorAsync("#TableTimesheet", new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
            // Linhas existentes (não as recém-adicionadas .dynamic)
            var rows = await _page.Locator("#TableTimesheet tbody tr").AllAsync();
            V($"   total de linhas no timesheet={rows.Count}");

            foreach (var row in rows)
            {
                // Detecta indicador de erro: thumbs-down, ícone vermelho, classe contendo 'invalid' ou 'error'
                var flagSelectors = new[]
                {
                    "i.fa-thumbs-down",
                    "i.fa-thumbs-o-down",
                    "[class*='thumbs-down']",
                    "[class*='invalid']",
                    "[class*='error']",
                    "i[style*='color: red']",
                    "i[style*='color:#']" // qualquer ícone colorido
                };
                bool flagged = false;
                foreach (var sel in flagSelectors)
                {
                    if (await row.Locator(sel).CountAsync() > 0) { flagged = true; break; }
                }
                if (!flagged) continue;

                // Extrai data da linha
                var dateField = row.Locator("input.activity-timesheet[date], td input[type='text']").First;
                if (await dateField.CountAsync() == 0) continue;
                var dateAttr = await dateField.GetAttributeAsync("date") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(dateAttr))
                    dateAttr = (await dateField.InputValueAsync() ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(dateAttr)) continue;

                V($"   ⚠️  linha com flag detectada para data='{dateAttr}'");

                // Coleta horários atuais (entrada/saída) das clockInOut rows desta data
                var clockRows = await row.Locator("table.table-clockInOut tbody tr.dynamicClockInOut").AllAsync();
                if (clockRows.Count == 0)
                {
                    V("      sem clockRows — pulando");
                    continue;
                }

                var times = new List<(ILocator inField, ILocator outField, string inVal, string outVal)>();
                foreach (var cr in clockRows)
                {
                    var inF = cr.Locator("input.textbox-clockin");
                    var outF = cr.Locator("input.textbox-clockout");
                    if (await inF.CountAsync() == 0 || await outF.CountAsync() == 0) continue;
                    var inV = (await inF.InputValueAsync() ?? string.Empty).Trim();
                    var outV = (await outF.InputValueAsync() ?? string.Empty).Trim();
                    times.Add((inF, outF, inV, outV));
                }
                if (times.Count == 0) continue;

                // Reconstrói entrada/saidaAlmoço/retorno/saída a partir dos pares
                string entry, lunchOut = string.Empty, lunchReturn = string.Empty, exit;
                if (times.Count >= 2)
                {
                    entry = times[0].inVal;
                    lunchOut = times[0].outVal;
                    lunchReturn = times[1].inVal;
                    exit = times[1].outVal;
                }
                else
                {
                    entry = times[0].inVal;
                    exit = times[0].outVal;
                }

                V($"      horários atuais: entry={entry} lo={lunchOut} lr={lunchReturn} exit={exit}");
                var adjusted = _validator.Adjust(dateAttr, entry, lunchOut, lunchReturn, exit);
                V($"      ajustado:        entry={adjusted.Entry} lo={adjusted.LunchOut} lr={adjusted.LunchReturn} exit={adjusted.Exit}");
                if (adjusted.Adjustments.Count == 0)
                {
                    V("      nenhum ajuste aplicável — pulando");
                    continue;
                }

                // Reescreve os campos
                if (times.Count >= 2)
                {
                    await FillTimeFieldAsync(times[0].inField, adjusted.Entry, $"FIX[{dateAttr}].ENTRADA");
                    await FillTimeFieldAsync(times[0].outField, adjusted.LunchOut, $"FIX[{dateAttr}].SAÍDA-ALMOÇO");
                    await FillTimeFieldAsync(times[1].inField, adjusted.LunchReturn, $"FIX[{dateAttr}].RETORNO");
                    await FillTimeFieldAsync(times[1].outField, adjusted.Exit, $"FIX[{dateAttr}].SAÍDA");
                }
                else
                {
                    await FillTimeFieldAsync(times[0].inField, adjusted.Entry, $"FIX[{dateAttr}].ENTRADA");
                    await FillTimeFieldAsync(times[0].outField, adjusted.Exit, $"FIX[{dateAttr}].SAÍDA");
                }

                _log($"🔧 {dateAttr}: corrigido — {string.Join(" ; ", adjusted.Adjustments)}");
                fixedCount++;
            }

            V($"FixFlaggedExistingRecordsAsync: {fixedCount} registro(s) corrigido(s)");
            return fixedCount;
        }
        catch (Exception ex)
        {
            V($"FixFlaggedExistingRecordsAsync EXCEPTION: {ex.Message}");
            _log($"Erro ao corrigir registros existentes: {ex.Message}");
            return fixedCount;
        }
    }

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
            // 1. Adicionar nova linha
            V("🔸 Etapa 1/6: clicando no botão '+' (nova linha)");
            var addBtn = _page.Locator("xpath=/html/body/div[3]/div[3]/div[1]/h3/span/i[2]");
            await addBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
            await addBtn.ClickAsync();
            await _page.WaitForTimeoutAsync(800);

            // 2. Índice da nova linha
            var dynRows = await _page.Locator("#TableTimesheet tbody tr.dynamic[style*=\"display: table-row\"]").CountAsync();
            var rowIdx = dynRows + 1;
            V($"🔸 Etapa 2/6: dynRows visiveis={dynRows} → rowIdx={rowIdx}");

            // 3. Preenche data
            var expectedDate = FormatDate(adjusted.Date);
            V($"🔸 Etapa 3/6: preenchendo DATA campo[row={rowIdx},col=1] valor='{adjusted.Date}' (normalizado='{expectedDate}')");
            var dateInput = _page.Locator($"xpath=//*[@id=\"TableTimesheet\"]/tbody/tr[{rowIdx}]/td[1]/div/input");
            await dateInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
            var dateOk = await FillDateFieldAsync(dateInput, adjusted.Date, $"DATA(row={rowIdx})");
            await dateInput.PressAsync("Tab");
            await _page.WaitForTimeoutAsync(500);
            var dateValue = (await dateInput.InputValueAsync() ?? string.Empty).Trim();
            V($"   data final no campo: '{dateValue}'");
            if (!dateOk || dateValue != expectedDate)
            {
                _log($"❌ Falha ao preencher data {adjusted.Date} (campo ficou '{dateValue}'). Abortando registro.");
                V($"   ❌ data não aceita pelo site — abortando registro para evitar inserir lixo.");
                return (false, adjusted.Adjustments);
            }

            // 4. Monta pares de horários (E-S)
            var pairs = !string.IsNullOrWhiteSpace(adjusted.LunchOut) && !string.IsNullOrWhiteSpace(adjusted.LunchReturn)
                ? new[] { (adjusted.Entry, adjusted.LunchOut), (adjusted.LunchReturn, adjusted.Exit) }
                : new[] { (adjusted.Entry, adjusted.Exit) };
            V($"🔸 Etapa 4/6: {pairs.Length} par(es) de horário para inserir: " +
              string.Join(" | ", pairs.Select(p => $"{p.Item1}→{p.Item2}")));

            var dropdown = _page.Locator($"xpath=//*[@id=\"TableTimesheet\"]/tbody/tr[{rowIdx}]/td[1]/div/div/button");
            var optionEs = _page.Locator($"xpath=//*[@id=\"TableTimesheet\"]/tbody/tr[{rowIdx}]/td[1]/div/div/ul/li[1]/a");

            for (var i = 0; i < pairs.Length; i++)
            {
                var (entryTime, exitTime) = pairs[i];
                if (i > 0)
                {
                    V($"   par[{i}]: clicando no dropdown 'E-S' para adicionar nova linha de clock");
                    await dropdown.ClickAsync();
                    await _page.WaitForTimeoutAsync(200);
                    await optionEs.ClickAsync();
                    await _page.WaitForTimeoutAsync(500);
                }

                var clockRows = await _page.Locator($"#TableTimesheet tbody tr.dynamic:nth-child({rowIdx}) table.table-clockInOut tbody tr.dynamicClockInOut").AllAsync();
                V($"   par[{i}]: clockRows encontradas={clockRows.Count}, usando índice {i}");
                if (clockRows.Count > i)
                {
                    var clockRow = clockRows[i];
                    var typeSelect = clockRow.Locator("select.ddl-access-type");
                    var typeCount = await typeSelect.CountAsync();
                    V($"   par[{i}]: ddl-access-type encontrado={typeCount}");
                    if (typeCount > 0)
                    {
                        await typeSelect.SelectOptionAsync(new[] { "ATIVIDADE EXTERNA" });
                        await _page.WaitForTimeoutAsync(200);
                        V($"   par[{i}]: selecionado 'ATIVIDADE EXTERNA'");
                    }

                    var inField = clockRow.Locator("input.textbox-clockin");
                    if (await inField.CountAsync() > 0)
                    {
                        await FillTimeFieldAsync(inField, entryTime, $"par[{i}].ENTRADA");
                    }
                    else
                    {
                        V($"   par[{i}]: ⚠️  campo input.textbox-clockin NÃO encontrado");
                    }

                    var outField = clockRow.Locator("input.textbox-clockout");
                    if (await outField.CountAsync() > 0)
                    {
                        await FillTimeFieldAsync(outField, exitTime, $"par[{i}].SAÍDA");
                    }
                    else
                    {
                        V($"   par[{i}]: ⚠️  campo input.textbox-clockout NÃO encontrado");
                    }
                }
                else
                {
                    V($"   par[{i}]: ⚠️  clockRow {i} indisponível (count={clockRows.Count})");
                }
            }

            // 5. Selecionar OSI/Projeto (antes das horas, pois o campo de horas pertence à linha do projeto)
            V("🔸 Etapa 5/6: selecionando OSI/Projeto");
            var tsRows = await _page.Locator($"#TableTimesheet tbody tr.dynamic:nth-child({rowIdx}) table.table-timesheetrecording tbody tr.dynamicTimesheetrecording").AllAsync();
            V($"   timesheetrecording rows encontradas={tsRows.Count}");
            ILocator? projectRow = null;
            if (tsRows.Count > 0)
            {
                projectRow = tsRows[^1];
                var osiBtn = projectRow.Locator("span.input-group-btn button.button-show-items");
                if (await osiBtn.CountAsync() > 0)
                {
                    V("   clicando botão 'show-items' do OSI");
                    await osiBtn.ClickAsync();
                    await _page.WaitForTimeoutAsync(500);
                    var projectBtn = _page.Locator("xpath=/html/body/div[7]/div/div/div[2]/table/tbody/tr[2]/td[1]/button/i");
                    await projectBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
                    V("   clicando botão do projeto (1ª linha do modal OSI)");
                    await projectBtn.ClickAsync();
                    await _page.WaitForTimeoutAsync(800);
                }
                else
                {
                    V("   ⚠️  botão show-items do OSI não encontrado");
                }
            }

            // 6. Horas apontadas — deve ser EXATAMENTE o valor que o SSG calcula
            //    abaixo dos horários (campo "Horas Registro"). Tentamos lê-lo da
            //    linha recém-criada; se não conseguir, caímos no cálculo local.
            string workedHours;
            var newRowLoc = _page.Locator($"#TableTimesheet tbody tr.dynamic:nth-child({rowIdx})").First;
            var read = await ReadHorasRegistroAsync(newRowLoc);
            if (!string.IsNullOrWhiteSpace(read))
            {
                workedHours = read!;
                V($"🔸 Etapa 6/6: horas apontadas (lidas do SSG)='{workedHours}'");
            }
            else
            {
                workedHours = CalcWorkedHours(adjusted.Entry, adjusted.LunchOut, adjusted.LunchReturn, adjusted.Exit);
                V($"🔸 Etapa 6/6: horas apontadas (calculadas localmente)='{workedHours}'");
            }
            if (projectRow is not null)
            {
                // Tenta vários seletores em ordem para encontrar o input de horas dentro da linha do projeto
                var hoursCandidates = new[]
                {
                    "input.textbox-hours",
                    "input[placeholder='Horas']",
                    "input.hours",
                    "td input[type='text']:not([placeholder='Observação']):not([placeholder*='OSI'])"
                };
                ILocator? hoursField = null;
                foreach (var sel in hoursCandidates)
                {
                    var loc = projectRow.Locator(sel).First;
                    if (await loc.CountAsync() > 0)
                    {
                        hoursField = loc;
                        V($"   campo de horas localizado via '{sel}'");
                        break;
                    }
                }

                // Fallback: pega o primeiro <input type=text> da linha do projeto (placeholder "Horas")
                if (hoursField is null)
                {
                    var inputs = projectRow.Locator("input[type='text']");
                    var n = await inputs.CountAsync();
                    V($"   fallback: inputs[text] na linha do projeto={n}");
                    if (n > 0) hoursField = inputs.First;
                }

                if (hoursField is not null)
                {
                    await FillTimeFieldAsync(hoursField, workedHours, "HORAS APONTADAS");
                    await _page.WaitForTimeoutAsync(200);
                }
                else
                {
                    V("   ⚠️  campo de horas apontadas NÃO encontrado na linha do projeto");
                    _log($"⚠️  Não foi possível preencher 'Horas Apontadas' para {record.Date}");
                }
            }

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

    public async Task<bool> ConfirmEntriesAsync()
    {
        if (_page is null) return false;
        V("ConfirmEntriesAsync: clicando botão salvar");
        try
        {
            var saveBtn = _page.Locator("xpath=/html/body/div[3]/div[3]/div[1]/h3/span/i[3]");
            await saveBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
            await saveBtn.ClickAsync();
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await _page.WaitForTimeoutAsync(1000);
            V("ConfirmEntriesAsync: OK");
            return true;
        }
        catch (Exception ex)
        {
            _log($"Erro ao confirmar: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> CloseConfirmationModalAsync()
    {
        if (_page is null) return false;
        try
        {
            var okBtn = _page.Locator("xpath=/html/body/div[9]/div/div/div[2]/button");
            await okBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
            await okBtn.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 300000 });
            await _page.WaitForTimeoutAsync(500);
            return true;
        }
        catch (Exception ex)
        {
            _log($"Modal de confirmação não detectado: {ex.Message}");
            return false;
        }
    }

    private static string FormatTime(string time)
    {
        if (string.IsNullOrWhiteSpace(time)) return time;
        var parts = time.Split(':');
        if (parts.Length != 2) return time;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return time;
        return $"{h:D2}:{m:D2}";
    }

    /// <summary>
    /// Normaliza datas para DD/MM/YYYY. Aceita entradas como '20/5/2025', '20-05-2025'
    /// ou '2025-05-20' e devolve sempre com dígitos completos.
    /// </summary>
    private static string FormatDate(string date)
    {
        if (string.IsNullOrWhiteSpace(date)) return date;
        var s = date.Trim().Replace('-', '/').Replace('.', '/');
        var parts = s.Split('/');
        if (parts.Length != 3) return date;

        int d, m, y;
        // Caso ISO yyyy/MM/dd
        if (parts[0].Length == 4 && int.TryParse(parts[0], out var yIso)
            && int.TryParse(parts[1], out var mIso) && int.TryParse(parts[2], out var dIso))
        {
            y = yIso; m = mIso; d = dIso;
        }
        else if (int.TryParse(parts[0], out d) && int.TryParse(parts[1], out m) && int.TryParse(parts[2], out y))
        {
            if (y < 100) y += 2000; // ano com 2 dígitos
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
        // Intencional: NÃO fechamos o navegador nem o contexto ao final do processo.
        // O usuário pediu para manter o Chrome aberto após a execução, permitindo
        // revisar/conferir os apontamentos antes de fechar manualmente.
        // Apenas liberamos os recursos do Playwright (cliente) sem encerrar o browser.
        await Task.CompletedTask;
        _playwright?.Dispose();
    }
}
