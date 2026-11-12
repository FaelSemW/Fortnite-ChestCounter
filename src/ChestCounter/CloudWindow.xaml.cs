using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ChestCounter;

public partial class CloudWindow : Window
{
    private readonly CloudSync _cloud;
    private readonly Config _config;
    private readonly TrackerData _data;

    public bool DataChanged { get; private set; }

    public CloudWindow(CloudSync cloud, Config config, TrackerData data)
    {
        InitializeComponent();
        _cloud = cloud;
        _config = config;
        _data = data;

        TxtUrl.Text = _config.CloudUrl ?? "";
        TxtUser.Text = _config.CloudUser ?? "";
        RefreshView();
    }

    private void RefreshView()
    {
        var logged = _cloud.IsLoggedIn;
        PanelLogin.Visibility = logged ? Visibility.Collapsed : Visibility.Visible;
        PanelConnected.Visibility = logged ? Visibility.Visible : Visibility.Collapsed;

        if (logged)
        {
            TxtAccountUser.Text = $"Conectado como {_config.CloudUser}";
            TxtAccountServer.Text = $"Servidor: {_config.CloudUrl}";
            var sessionChests = _data.Matches.Where(ReportWriter.IsThisSession).Sum(m => m.ChestsOpened);
            var totalChests = _data.Matches.Sum(m => m.ChestsOpened);
            TxtLocalSummary.Text = $"Total: {totalChests:N0} baús ({_data.Matches.Count:N0} partidas) • Sessão atual: {sessionChests:N0}";
            TxtLastSync.Text = _cloud.LastSyncLocal.HasValue
                ? $"Última sincronização: {_cloud.LastSyncLocal.Value:HH:mm:ss}"
                : "Ainda não sincronizado nesta janela.";
        }
    }

    private void SetBusy(bool busy, string? status = null, string colorHex = "#94A3B8")
    {
        BtnLogin.IsEnabled = !busy;
        BtnRegister.IsEnabled = !busy;
        BtnSync.IsEnabled = !busy;
        BtnLogout.IsEnabled = !busy;
        TxtUrl.IsEnabled = !busy;
        TxtUser.IsEnabled = !busy;
        TxtPassword.IsEnabled = !busy;

        if (!string.IsNullOrEmpty(status))
        {
            BannerStatus.Visibility = Visibility.Visible;
            TxtStatus.Text = status;
            TxtStatus.Foreground = (Brush)new BrushConverter().ConvertFromString(colorHex)!;
        }
    }

    private async Task AuthenticateAsync(bool register)
    {
        var url = TxtUrl.Text.Trim();
        var user = TxtUser.Text.Trim();
        var pass = TxtPassword.Password;

        if (string.IsNullOrWhiteSpace(url))
        {
            SetBusy(false, "Cole o link do seu servidor no Render.", "#FCA5A5");
            return;
        }
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(pass))
        {
            SetBusy(false, "Informe usuário e senha.", "#FCA5A5");
            return;
        }

        SetBusy(true, register ? "Criando conta e acordando o servidor..." : "Entrando e acordando o servidor...", "#38BDF8");
        var auth = await _cloud.AuthAsync(url, user, pass, register);
        if (!auth.Ok)
        {
            SetBusy(false, auth.Message, "#FCA5A5");
            return;
        }

        TxtPassword.Password = "";
        SetBusy(true, "Sincronizando baús, histórico e sessão com a nuvem...", "#38BDF8");
        var sync = await _cloud.SyncAsync();
        DataChanged = true;
        RefreshView();
        SetBusy(false, sync.Ok ? sync.Message : $"Conectado, mas a sincronização falhou: {sync.Message}", sync.Ok ? "#6EE7B7" : "#FCD34D");
    }

    private async void BtnLogin_Click(object sender, RoutedEventArgs e) => await AuthenticateAsync(false);

    private async void BtnRegister_Click(object sender, RoutedEventArgs e) => await AuthenticateAsync(true);

    private async void TxtPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && BtnLogin.IsEnabled)
        {
            await AuthenticateAsync(false);
        }
    }

    private async void BtnSync_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Sincronizando com a nuvem...", "#38BDF8");
        var sync = await _cloud.SyncAsync();
        DataChanged = true;
        RefreshView();
        SetBusy(false, sync.Message, sync.Ok ? "#6EE7B7" : "#FCA5A5");
    }

    private void BtnLogout_Click(object sender, RoutedEventArgs e)
    {
        _cloud.Logout();
        DataChanged = true;
        RefreshView();
        SetBusy(false, "Desconectado. Seus dados locais continuam salvos neste PC.", "#94A3B8");
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
