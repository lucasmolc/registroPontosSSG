using System.Windows;
using System.Windows.Controls;
using RegistroPontosSSG.Core.Security;

namespace RegistroPontosSSG.Desktop.Views;

public partial class TotpWizardWindow : Window
{
    public string ResultSecret { get; private set; } = string.Empty;
    public string InitialSecret { get; set; } = string.Empty;

    private bool _secretVisible;
    private bool _syncingSecret;

    public TotpWizardWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Loaded += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(InitialSecret))
            {
                _syncingSecret = true;
                SecretPasswordBox.Password = InitialSecret;
                SecretBox.Text = InitialSecret;
                _syncingSecret = false;
                EvaluateSecret(InitialSecret);
            }
        };
    }

    private void BrowseQr_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Imagens (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            Title = "Selecione a imagem do QR code"
        };
        if (dialog.ShowDialog() != true) return;

        QrResultPanel.Visibility = Visibility.Visible;
        try
        {
            var info = QrCodeReader.ReadOtpAuthFromImage(dialog.FileName);
            if (info is null)
            {
                QrResultLabel.Text = "❌ Não foi possível ler um QR code TOTP nessa imagem.";
                OkButton.IsEnabled = false;
                return;
            }

            var preview = TotpGenerator.GenerateCode(info.Secret);
            var maskedSecret = MaskSecret(info.Secret);
            QrResultLabel.Text = $"✅ Secret extraída com sucesso!\n\nIssuer: {info.Issuer}\nLabel: {info.Label}\nSecret: {maskedSecret}\nCódigo atual: {preview}\n\nClique em Salvar para confirmar.";
            ResultSecret = info.Secret;
            OkButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            QrResultLabel.Text = $"❌ Erro: {ex.Message}";
            OkButton.IsEnabled = false;
        }
    }

    private void SecretPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingSecret) return;
        _syncingSecret = true;
        SecretBox.Text = SecretPasswordBox.Password;
        _syncingSecret = false;
        EvaluateSecret(SecretPasswordBox.Password);
    }

    private void SecretBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingSecret) return;
        _syncingSecret = true;
        SecretPasswordBox.Password = SecretBox.Text;
        _syncingSecret = false;
        EvaluateSecret(SecretBox.Text);
    }

    private void ToggleSecret_Click(object sender, RoutedEventArgs e)
    {
        _secretVisible = !_secretVisible;
        if (_secretVisible)
        {
            SecretBox.Visibility = Visibility.Visible;
            SecretPasswordBox.Visibility = Visibility.Collapsed;
            ToggleSecretButton.Content = "🙈";
        }
        else
        {
            SecretBox.Visibility = Visibility.Collapsed;
            SecretPasswordBox.Visibility = Visibility.Visible;
            ToggleSecretButton.Content = "👁";
        }
    }

    private void EvaluateSecret(string raw)
    {
        var secret = (raw ?? string.Empty).Trim().Replace(" ", "");
        if (string.IsNullOrEmpty(secret))
        {
            ManualResultPanel.Visibility = Visibility.Collapsed;
            OkButton.IsEnabled = false;
            return;
        }

        ManualResultPanel.Visibility = Visibility.Visible;
        if (!TotpGenerator.IsValidSecret(secret))
        {
            ManualResultLabel.Text = "❌ Secret inválida (deve ser Base32).";
            OkButton.IsEnabled = false;
            return;
        }
        try
        {
            var code = TotpGenerator.GenerateCode(secret);
            ManualResultLabel.Text = $"✅ Código atual: {code}\n\nCompare com seu Authenticator. Se bater, clique em Salvar.";
            ResultSecret = secret;
            OkButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ManualResultLabel.Text = $"❌ Erro: {ex.Message}";
            OkButton.IsEnabled = false;
        }
    }

    private static string MaskSecret(string secret)
    {
        if (string.IsNullOrEmpty(secret)) return string.Empty;
        if (secret.Length <= 4) return new string('•', secret.Length);
        var visible = Math.Min(4, secret.Length / 4);
        return secret.Substring(0, visible) + new string('•', secret.Length - visible);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
