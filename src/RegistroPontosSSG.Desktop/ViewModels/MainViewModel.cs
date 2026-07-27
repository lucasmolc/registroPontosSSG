using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RegistroPontosSSG.Core.Automation;
using RegistroPontosSSG.Core.Configuration;
using RegistroPontosSSG.Core.Models;
using RegistroPontosSSG.Core.Reading;
using RegistroPontosSSG.Core.Security;
using RegistroPontosSSG.Core.Update;

namespace RegistroPontosSSG.Desktop.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ConfigService _configService = new();
    public AppConfig Config { get; private set; }

    public ObservableCollection<PunchRecord> Records { get; } = new();
    public ObservableCollection<LogEntry> LogEntries { get; } = new();
    public ObservableCollection<string> VerboseLogEntries { get; } = new();

    private string? _verboseLogFilePath;
    private readonly object _verboseFileLock = new();

    [ObservableProperty] private string _status = "Pronto";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string _totpStatus = "Não configurado";

    public MainViewModel()
    {
        Config = _configService.Load();
        // O arquivo de pontos não é persistido entre execuções — sempre inicia vazio.
        FilePath = string.Empty;
        Config.PunchFile.FilePath = string.Empty;
        UpdateTotpStatus();
    }

    public string Username
    {
        get => Config.Credentials.Username;
        set { Config.Credentials.Username = value; OnPropertyChanged(); }
    }

    public string Password
    {
        get => Config.Credentials.Password;
        set { Config.Credentials.Password = value; OnPropertyChanged(); }
    }

    public bool BlockRoundTimes
    {
        get => Config.Validation.BlockRoundTimes;
        set { Config.Validation.BlockRoundTimes = value; OnPropertyChanged(); }
    }
    public bool BlockDuplicates
    {
        get => Config.Validation.BlockDuplicateTimes;
        set { Config.Validation.BlockDuplicateTimes = value; OnPropertyChanged(); }
    }
    public bool BlockExactOneHourLunch
    {
        get => Config.Validation.BlockExactOneHourLunch;
        set { Config.Validation.BlockExactOneHourLunch = value; OnPropertyChanged(); }
    }
    public bool BlockSameMinutes
    {
        get => Config.Validation.BlockSameMinutes;
        set { Config.Validation.BlockSameMinutes = value; OnPropertyChanged(); }
    }
    public int DaysToCheckDuplicates
    {
        get => Config.Validation.DaysToCheckDuplicates;
        set { Config.Validation.DaysToCheckDuplicates = value; OnPropertyChanged(); }
    }
    public bool UseSystemChrome
    {
        get => Config.Automation.UseSystemChrome;
        set { Config.Automation.UseSystemChrome = value; OnPropertyChanged(); }
    }
    public bool IgnoreExistingDates
    {
        get => Config.Automation.IgnoreExistingDates;
        set { Config.Automation.IgnoreExistingDates = value; OnPropertyChanged(); }
    }

    [RelayCommand]
    private void BrowseFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Planilhas (*.xlsx;*.csv)|*.xlsx;*.csv|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv",
            Title = "Selecione o arquivo de pontos"
        };
        if (dialog.ShowDialog() == true)
        {
            FilePath = dialog.FileName;
            Config.PunchFile.FilePath = FilePath;
            TryLoadRecords();
        }
    }

    private void TryLoadRecords()
    {
        try
        {
            Records.Clear();
            var reader = new PunchFileReader();
            foreach (var rec in reader.Read(FilePath))
                Records.Add(rec);
            Status = $"{Records.Count} registro(s) carregado(s)";

            // Um arquivo em formato inesperado produzia zero registros sem qualquer
            // aviso, e a execução seguia como se não houvesse nada a lançar.
            if (Records.Count == 0)
            {
                MessageBox.Show(
                    "Nenhum registro de ponto foi reconhecido neste arquivo.\n\n" +
                    "Formatos aceitos:\n" +
                    "  • Relatório exportado do SSG (Time Sheet Report)\n" +
                    "  • Planilha com as colunas: data, entrada, saida_almoco, " +
                    "retorno_almoco, saida\n\n" +
                    "Verifique se a primeira aba do arquivo contém os dados.",
                    "Nenhum registro reconhecido",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            Status = $"Erro ao ler arquivo: {ex.Message}";
            MessageBox.Show(ex.Message, "Erro ao ler arquivo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void SaveConfig()
    {
        try
        {
            _configService.Save(Config);
            Status = "Configurações salvas com sucesso";
            MessageBox.Show("Configurações salvas com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao salvar: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void OpenTotpWizard()
    {
        var wizard = new Views.TotpWizardWindow
        {
            Owner = Application.Current.MainWindow,
            InitialSecret = Config.Credentials.TotpSecret
        };
        if (wizard.ShowDialog() == true && !string.IsNullOrWhiteSpace(wizard.ResultSecret))
        {
            Config.Credentials.TotpSecret = wizard.ResultSecret;
            UpdateTotpStatus();
            _configService.Save(Config);
            MessageBox.Show("2FA configurado e salvo!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private void ClearTotp()
    {
        if (MessageBox.Show("Remover a configuração de 2FA automático?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Config.Credentials.TotpSecret = string.Empty;
        UpdateTotpStatus();
        _configService.Save(Config);
    }

    private void UpdateTotpStatus()
    {
        TotpStatus = string.IsNullOrWhiteSpace(Config.Credentials.TotpSecret)
            ? "Não configurado — você digitará o código 2FA no navegador"
            : "✓ Configurado — preenchimento automático ativo";
    }


    // ═════════════════════════ Atualização ═════════════════════════

    [ObservableProperty] private bool _isUpdateAvailable;
    [ObservableProperty] private string _updateMessage = string.Empty;
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private int _updateProgress;

    private UpdateInfo? _pendingUpdate;

    /// <summary>Versão em execução, exibida no rodapé.</summary>
    public string AppVersionLabel => $"v{UpdateService.CurrentApplicationVersion.ToString(3)}";

    public bool CheckUpdatesOnStartup
    {
        get => Config.Update.CheckOnStartup;
        set
        {
            if (Config.Update.CheckOnStartup == value) return;
            Config.Update.CheckOnStartup = value;
            _configService.Save(Config);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Executada quando a janela carrega: mostra as novidades se o app acabou de ser
    /// atualizado e, se configurado, procura por uma versão mais recente.
    /// </summary>
    public async Task InitializeAsync()
    {
        ShowWhatsNewIfUpdated();

        if (Config.Update.CheckOnStartup)
            await CheckForUpdatesAsync(silencioso: true);
    }

    /// <summary>
    /// Na primeira execução após uma atualização, exibe o resumo do CHANGELOG.
    /// A versão que rodou por último fica no config, então o aviso aparece uma única vez.
    /// </summary>
    private void ShowWhatsNewIfUpdated()
    {
        var atual = UpdateService.CurrentApplicationVersion;
        var anterior = Version.TryParse(Config.LastRunVersion, out var lida) ? lida : null;

        try
        {
            var secoes = anterior is null
                ? (ChangelogReader.ReadForVersion(atual) is { } unica
                    ? new List<ChangelogSection> { unica }
                    : new List<ChangelogSection>())
                : atual > anterior
                    ? ChangelogReader.ReadBetween(anterior, atual).ToList()
                    : new List<ChangelogSection>();

            if (secoes.Count > 0)
            {
                var janela = new Views.WhatsNewWindow(secoes, atual, anterior, Config.Update.Repository)
                {
                    Owner = Application.Current.MainWindow
                };
                janela.ShowDialog();
            }
        }
        catch (Exception ex)
        {
            AddVerbose($"falha ao exibir novidades: {ex.Message}");
        }

        if (Config.LastRunVersion != atual.ToString())
        {
            Config.LastRunVersion = atual.ToString();
            _configService.Save(Config);
        }
    }

    [RelayCommand]
    private async Task CheckForUpdates() => await CheckForUpdatesAsync(silencioso: false);

    private async Task CheckForUpdatesAsync(bool silencioso)
    {
        try
        {
            using var servico = new UpdateService(Config.Update.Repository);

            if (servico.IsDevelopmentBuild)
            {
                if (!silencioso)
                    MessageBox.Show(
                        "Esta é uma compilação local, sem número de versão — a verificação " +
                        "de atualizações só funciona no executável publicado.",
                        "Atualizações", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var info = await servico.CheckForUpdateAsync();
            _pendingUpdate = info;
            IsUpdateAvailable = info is not null;

            if (info is not null)
            {
                UpdateMessage = $"Versão {info.Version.ToString(3)} disponível " +
                                $"({info.SizeLabel}) — você está na {servico.CurrentVersion.ToString(3)}.";
                AddLog($"🆕 {UpdateMessage}");
            }
            else if (!silencioso)
            {
                MessageBox.Show(
                    $"Você já está na versão mais recente ({servico.CurrentVersion.ToString(3)}).",
                    "Atualizações", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            AddVerbose($"falha ao verificar atualizações: {ex.Message}");
            if (!silencioso)
                MessageBox.Show($"Não foi possível verificar atualizações:\n\n{ex.Message}",
                    "Atualizações", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Baixa a nova versão e agenda a troca do executável. As configurações ficam em
    /// %APPDATA% e não são afetadas; ainda assim uma cópia de segurança é feita antes.
    /// </summary>
    [RelayCommand]
    private async Task InstallUpdate()
    {
        if (_pendingUpdate is null || IsUpdating) return;

        var resposta = MessageBox.Show(
            $"Baixar e instalar a versão {_pendingUpdate.Version.ToString(3)} ({_pendingUpdate.SizeLabel})?\n\n" +
            "O aplicativo será fechado e reaberto automaticamente ao final.\n" +
            "Suas configurações são preservadas: ficam em %APPDATA%\\RegistroPontosSSG, " +
            "fora da pasta do executável.",
            "Atualizar aplicativo", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (resposta != MessageBoxResult.Yes) return;

        try
        {
            IsUpdating = true;
            UpdateProgress = 0;
            Status = "Baixando atualização...";

            var backup = ConfigService.BackupConfig();
            AddVerbose(backup is null
                ? "sem config salvo para copiar"
                : $"cópia de segurança do config: {backup}");

            using var servico = new UpdateService(Config.Update.Repository);
            var progresso = new Progress<int>(p => UpdateProgress = p);
            var caminho = await servico.DownloadAsync(_pendingUpdate, progresso);

            AddLog("✅ Download concluído — substituindo o executável e reabrindo...");
            UpdateService.ApplyAndRestart(caminho);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            IsUpdating = false;
            Status = "Falha ao atualizar";
            AddLog($"❌ Falha ao atualizar: {ex.Message}");
            MessageBox.Show(
                $"Não foi possível concluir a atualização:\n\n{ex.Message}\n\n" +
                "Você pode baixar a nova versão manualmente pela página de releases.",
                "Atualização", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        var url = _pendingUpdate?.ReleaseUrl is { Length: > 0 } releaseUrl
            ? releaseUrl
            : $"https://github.com/{Config.Update.Repository}/releases";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AddVerbose($"falha ao abrir o navegador: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecute))]
    private async Task RunAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            MessageBox.Show("Preencha usuário e senha primeiro.", "Configuração incompleta",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (Records.Count == 0)
        {
            MessageBox.Show("Selecione um arquivo de pontos com registros válidos.", "Sem registros",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsRunning = true;
            LogEntries.Clear();
            VerboseLogEntries.Clear();
            StartNewVerboseLogFile();
            _configService.Save(Config);

            await EnsurePlaywrightBrowsersAsync();

            var records = Records.ToList();
            var period = PunchFileReader.DetectMonth(records);
            AddLog($"📅 {records.Count} registro(s) ({(period == "mes_passado" ? "mês passado" : "mês atual")})");
            AddVerbose($"=== Início da execução {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            AddVerbose($"registros={records.Count} period={period}");
            AddVerbose($"config: UseSystemChrome={Config.Automation.UseSystemChrome} Headless={Config.Automation.Headless} SlowMo={Config.Automation.SlowMoMs} IgnoreExistingDates={Config.Automation.IgnoreExistingDates}");

            await using var automation = new SsgAutomation(Config,
                msg => Application.Current.Dispatcher.Invoke(() => AddLog(msg)),
                msg => Application.Current.Dispatcher.Invoke(() => Status = msg),
                msg => Application.Current.Dispatcher.Invoke(() => AddVerbose(msg)));

            await automation.StartAsync();

            AddLog("🔐 Realizando login...");
            if (!await automation.LoginAsync())
            {
                AddLog("❌ Falha no login");
                return;
            }
            AddLog("✅ Login OK");

            if (!await automation.SelectMonthAndFilterAsync(period))
            {
                AddLog("❌ Falha ao filtrar o período no SSG");
                return;
            }

            await automation.GetRegisteredDatesAsync();
            AddLog($"🔍 {automation.RegisteredDates.Count} data(s) já cadastrada(s)");

            // Datas do arquivo que não têm card disponível no período (fora do filtro,
            // bloqueadas ou inválidas) — avisamos antes de tentar registrar.
            var unavailable = await automation.GetUnavailableDatesAsync(records.Select(r => r.Date));
            foreach (var date in unavailable)
                AddLog($"⛔ {date}: dia indisponível para lançamento no SSG");

            int ok = 0, skip = 0, fail = 0;

            for (var i = 0; i < records.Count; i++)
            {
                var rec = records[i];
                Status = $"[{i + 1}/{records.Count}] {rec.Date}";

                if (Config.Automation.IgnoreExistingDates && automation.IsDateRegistered(rec.Date))
                {
                    AddLog($"⏭️  {rec.Date} já cadastrado");
                    skip++;
                    continue;
                }

                var (success, adjustments) = await automation.RegisterPunchAsync(rec);
                if (success)
                {
                    AddLog($"✅ {rec.Date}");
                    foreach (var a in adjustments) AddLog($"   ↳ {a}");
                    ok++;
                }
                else
                {
                    AddLog($"❌ {rec.Date}");
                    fail++;
                }
            }

            if (ok > 0)
            {
                AddLog("💾 Salvando dias alterados...");
                if (await automation.ConfirmEntriesAsync())
                {
                    await automation.CloseConfirmationModalAsync();
                    AddLog("✅ Apontamentos salvos!");
                }
                else
                {
                    AddLog("❌ Falha ao salvar — revise os dados no navegador");
                }
            }

            AddLog($"📊 Resultado: ✅ {ok}  ⏭️ {skip}  ❌ {fail}");
            Status = "Concluído";
        }
        catch (Exception ex)
        {
            AddLog($"❌ Erro: {ex.Message}");
            MessageBox.Show(ex.ToString(), "Erro na execução", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool CanExecute() => !IsRunning;

    private async Task EnsurePlaywrightBrowsersAsync()
    {
        Status = "Verificando navegador Playwright (1ª execução pode demorar)...";
        await Task.Run(() =>
        {
            try
            {
                var exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
                if (exitCode != 0) AddLogThreadSafe($"⚠️ Playwright install retornou {exitCode}");
            }
            catch (Exception ex)
            {
                AddLogThreadSafe($"⚠️ Falha ao instalar browser: {ex.Message}");
            }
        });
    }

    private void AddLogThreadSafe(string msg)
        => Application.Current.Dispatcher.Invoke(() => AddLog(msg));

    private void AddLog(string message)
    {
        LogEntries.Add(new LogEntry(DateTime.Now, message));
        // Mantém apenas as últimas 500 entradas para não crescer indefinidamente
        while (LogEntries.Count > 500) LogEntries.RemoveAt(0);
        // Espelha no verbose também para correlacionar com os detalhes
        AddVerbose(message);
    }

    private void AddVerbose(string message)
    {
        VerboseLogEntries.Add(message);
        while (VerboseLogEntries.Count > 5000) VerboseLogEntries.RemoveAt(0);
        WriteVerboseToFile(message);
    }

    private void StartNewVerboseLogFile()
    {
        try
        {
            Directory.CreateDirectory(ConfigService.LogsDirectory);
            _verboseLogFilePath = Path.Combine(ConfigService.LogsDirectory, $"run-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        }
        catch { _verboseLogFilePath = null; }
    }

    private void WriteVerboseToFile(string message)
    {
        if (string.IsNullOrEmpty(_verboseLogFilePath)) return;
        try
        {
            lock (_verboseFileLock)
            {
                File.AppendAllText(_verboseLogFilePath, message + Environment.NewLine);
            }
        }
        catch { /* ignora falhas de IO no log */ }
    }

    [RelayCommand]
    private void OpenVerboseLog()
    {
        var win = new Views.VerboseLogWindow(VerboseLogEntries)
        {
            Owner = Application.Current.MainWindow
        };
        win.Show();
    }
}

public sealed record LogEntry(DateTime Timestamp, string Message)
{
    public string Display => $"[{Timestamp:HH:mm:ss}] {Message}";
}
