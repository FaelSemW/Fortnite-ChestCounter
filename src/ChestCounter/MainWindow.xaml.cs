using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ChestCounter;

public sealed class TeammateDisplayItem
{
    public string PlayerName { get; set; } = "";
    public bool IsLocalPlayer { get; set; }
    public string AvatarIcon => IsLocalPlayer ? "⭐" : "👤";
    public string RoleBadge => IsLocalPlayer ? "VOCÊ" : "ALIADO";
    public Brush RoleBg => IsLocalPlayer ? (Brush)new BrushConverter().ConvertFrom("#261E0A")! : (Brush)new BrushConverter().ConvertFrom("#162032")!;
    public Brush RoleBorder => IsLocalPlayer ? (Brush)new BrushConverter().ConvertFrom("#F59E0B")! : (Brush)new BrushConverter().ConvertFrom("#22304A")!;
    public Brush RoleColor => IsLocalPlayer ? (Brush)new BrushConverter().ConvertFrom("#FBBF24")! : (Brush)new BrushConverter().ConvertFrom("#94A3B8")!;
    public Brush CardBorder => IsLocalPlayer ? (Brush)new BrushConverter().ConvertFrom("#F59E0B")! : (Brush)new BrushConverter().ConvertFrom("#22304A")!;
    public Brush NameColor => IsLocalPlayer ? (Brush)new BrushConverter().ConvertFrom("#FDE68A")! : (Brush)new BrushConverter().ConvertFrom("#F8FAFC")!;
    public int ChestsCount { get; set; }
    public int AmmoBoxesCount { get; set; }
    public int OtherContainersCount { get; set; }
    public string SharePercentText { get; set; } = "";
    public string ExtraContainersText => $"{AmmoBoxesCount} munição • {OtherContainersCount} outros";
}

public sealed class MatchDisplayItem : INotifyPropertyChanged
{
    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
                OnPropertyChanged(nameof(DetailsVisibility));
                OnPropertyChanged(nameof(ExpandArrow));
            }
        }
    }

    public Visibility DetailsVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;
    public string ExpandArrow => IsExpanded ? "▲" : "▼";

    public string DateFormatted { get; set; } = "";
    public string ModeFormatted { get; set; } = "";
    public string PlacementFormatted { get; set; } = "";
    public Brush PlacementColor { get; set; } = Brushes.White;
    public Brush PlacementBg { get; set; } = Brushes.Transparent;
    public Brush PlacementBorder { get; set; } = Brushes.Transparent;
    public FontWeight PlacementWeight { get; set; } = FontWeights.Normal;
    public int ChestsCount { get; set; }
    public string TreasureText { get; set; } = "";
    public string ReplayFile { get; set; } = "";
    public bool IsWin { get; set; }
    public MatchRecord RawMatch { get; set; } = null!;

    public ObservableCollection<TeammateDisplayItem> SquadDisplayMembers { get; } = new();
    public string SquadSummaryText { get; set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class MainWindow : Window
{
    private readonly Store _store;
    private readonly Config _config;
    private readonly TrackerData _data;
    private readonly Tracker _tracker;
    private readonly CloudSync _cloud;
    private readonly DispatcherTimer _pollTimer;
    private FileSystemWatcher? _watcher;
    private readonly ObservableCollection<MatchDisplayItem> _allMatches = new();
    private readonly ObservableCollection<MatchDisplayItem> _displayMatches = new();
    private string _currentFilter = "All";
    private string _searchQuery = "";
    private bool _isFortniteRunning = false;
    private bool _scanBusy = false;
    private DateTime _lastAutoSyncUtc = DateTime.MinValue;

    public MainWindow(Store store, Config config, TrackerData data, Tracker tracker)
    {
        InitializeComponent();
        _store = store;
        _config = config;
        _data = data;
        _tracker = tracker;
        _cloud = new CloudSync(_store, _config, _data);

        ListMatches.ItemsSource = _displayMatches;
        TxtDemosFolder.Text = _config.DemosFolder;

        LoadData();
        UpdateOverlayButtons();
        UpdateCloudButton();
        SetupWatcher();

        // High-frequency polling timer (every 2 seconds) for seamless auto-sync
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();

        Loaded += async (s, e) =>
        {
            ErrorLog.Write("WindowLoaded", "Window Loaded Successfully!");
            UpdateStatusPill();
            UpdateFortniteProcessState();
            UpdateCloudButton();
            if (_cloud.IsLoggedIn)
            {
                await SyncCloudAsync(silent: false);
            }
        };

        Closing += (s, e) => ErrorLog.Write("WindowClosing", "Closing triggered. Cancel=" + e.Cancel);
        Closed += (s, e) => ErrorLog.Write("WindowClosed", "Window Closed!");
    }

    private void SetupWatcher()
    {
        try
        {
            if (Directory.Exists(_config.DemosFolder))
            {
                _watcher = new FileSystemWatcher(_config.DemosFolder, "*.replay")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                _watcher.Created += (s, e) => Dispatcher.InvokeAsync(() => RunBackgroundScan(isManual: false));
                _watcher.Changed += (s, e) => Dispatcher.InvokeAsync(() => RunBackgroundScan(isManual: false));
                _watcher.Renamed += (s, e) => Dispatcher.InvokeAsync(() => RunBackgroundScan(isManual: false));
            }
        }
        catch
        {
            _watcher = null;
        }
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        RunBackgroundScan(isManual: false);
        UpdateFortniteProcessState();
    }

    private void RunBackgroundScan(bool isManual)
    {
        if (_scanBusy || _cloud.Busy) return;
        _scanBusy = true;
        try
        {
            var countBefore = _data.Matches.Count;
            var newCount = _tracker.Scan(ignoreQuietPeriod: isManual);
            _tracker.UpdateLive();

            UpdateOverlayView();
            UpdateStatusPill();

            if (newCount > 0 || _data.Matches.Count != countBefore)
            {
                LoadData();
                ShowFeedback($"🎉 {newCount} nova partida adicionada!");
            }

            if ((newCount > 0 || _cloud.Dirty) && _cloud.IsLoggedIn && DateTime.UtcNow - _lastAutoSyncUtc > TimeSpan.FromSeconds(5))
            {
                _ = SyncCloudAsync(silent: true);
            }
        }
        catch
        {
            // Ignore scan errors during poll
        }
        finally
        {
            _scanBusy = false;
        }
    }

    private void UpdateOverlayView()
    {
        if (OperatingSystem.IsWindows() && NativeOverlay.IsRunning)
        {
            var view = OverlayModel.Build(Profiles.View(_data, _config), _tracker.LiveMatch, _tracker.IsLive);
            ReportWriter.WriteOverlay(_store, view);
            NativeOverlay.Update(view);
        }
    }

    private void UpdateFortniteProcessState()
    {
        try
        {
            _isFortniteRunning = Process.GetProcessesByName("FortniteClient-Win64-Shipping").Length > 0;
            if (_isFortniteRunning)
            {
                TxtFortniteProcessStatus.Text = "● Fortnite em execução • Pronto";
                TxtFortniteProcessStatus.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            }
            else
            {
                TxtFortniteProcessStatus.Text = "○ Fortnite fechado (Histórico salvo)";
                TxtFortniteProcessStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }
        }
        catch
        {
            // Ignore process query issues
        }
    }

    private void UpdateStatusPill()
    {
        if (_tracker.IsLive)
        {
            PillStatus.Background = new SolidColorBrush(Color.FromRgb(38, 30, 10));
            PillStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            DotStatus.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            TxtLiveStatus.Text = "● Em partida (gravação ativa)";
            TxtLiveStatus.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
        }
        else if (_tracker.IsFinishing)
        {
            PillStatus.Background = new SolidColorBrush(Color.FromRgb(14, 42, 58));
            PillStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(6, 182, 212));
            DotStatus.Fill = new SolidColorBrush(Color.FromRgb(6, 182, 212));
            TxtLiveStatus.Text = "● Analisando replay...";
            TxtLiveStatus.Foreground = new SolidColorBrush(Color.FromRgb(6, 182, 212));
        }
        else if (_isFortniteRunning)
        {
            PillStatus.Background = new SolidColorBrush(Color.FromRgb(10, 42, 30));
            PillStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            DotStatus.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            TxtLiveStatus.Text = "● Monitorando em tempo real";
            TxtLiveStatus.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
        }
        else
        {
            PillStatus.Background = new SolidColorBrush(Color.FromRgb(22, 32, 50));
            PillStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85));
            DotStatus.Fill = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            TxtLiveStatus.Text = "● Monitorando replays";
            TxtLiveStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
        }
    }

    private void ShowFeedback(string msg)
    {
        TxtRefreshFeedback.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
        TxtRefreshFeedback.Text = msg;
    }

    public void LoadData()
    {
        var view = Profiles.View(_data, _config);
        var matches = view.Matches.OrderByDescending(When).ToList();

        var player = matches.Select(m => m.PlayerName).FirstOrDefault(n => !string.IsNullOrEmpty(n));
        TxtPlayerName.Text = string.IsNullOrEmpty(player) ? "Jogador" : player;

        // Compute Hero Stats
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var totalChests = matches.Sum(m => m.ChestsOpened);
        var sessionMatches = matches.Where(ReportWriter.IsThisSession).ToList();
        var sessionChests = sessionMatches.Sum(m => m.ChestsOpened);
        var totalMatches = matches.Count;
        var avg = totalMatches > 0 ? (double)totalChests / totalMatches : 0.0;
        var best = totalMatches > 0 ? matches.Max(m => m.ChestsOpened) : 0;
        var treasures = matches.Sum(m => m.TreasureFinds.Count);

        StatTotalChests.Text = totalChests.ToString("N0", pt);
        StatSessionChests.Text = sessionChests.ToString("N0", pt);
        StatSessionSub.Text = sessionMatches.Count == 1
            ? "1 partida nesta sessão"
            : $"{sessionMatches.Count:N0} partidas nesta sessão";
        StatTotalMatches.Text = totalMatches.ToString("N0", pt);
        StatAvgChests.Text = avg.ToString("0.0", pt);
        StatBestMatch.Text = best.ToString("N0", pt);
        StatTreasures.Text = treasures.ToString("N0", pt);

        // Format each match item
        _allMatches.Clear();
        foreach (var m in matches)
        {
            var isWin = m.Placement == 1;
            var when = When(m);
            var dateStr = when.ToString("dd/MM HH:mm", CultureInfo.GetCultureInfo("pt-BR"));
            var modeStr = Pretty.Playlist(m.Playlist);
            var placeStr = isWin ? "🏆 Vitória Royale!" : (m.Placement is int p && p > 0 ? $"{p}º / {m.TotalPlayers}" : "—");

            var trList = m.TreasureFinds.Select(f => "★ " + f.DisplayName).ToList();
            var trStr = trList.Count > 0 ? string.Join(", ", trList) : "—";

            var item = new MatchDisplayItem
            {
                DateFormatted = dateStr,
                ModeFormatted = modeStr,
                PlacementFormatted = placeStr,
                IsWin = isWin,
                ChestsCount = m.ChestsOpened,
                TreasureText = trStr,
                ReplayFile = m.ReplayFile,
                RawMatch = m
            };

            if (isWin)
            {
                item.PlacementColor = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                item.PlacementBg = new SolidColorBrush(Color.FromRgb(38, 30, 10));
                item.PlacementBorder = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                item.PlacementWeight = FontWeights.Bold;
            }
            else
            {
                item.PlacementColor = new SolidColorBrush(Color.FromRgb(226, 232, 240));
                item.PlacementBg = new SolidColorBrush(Color.FromRgb(22, 32, 50));
                item.PlacementBorder = new SolidColorBrush(Color.FromRgb(34, 48, 74));
                item.PlacementWeight = FontWeights.SemiBold;
            }

            // Populate squad breakdown
            var squad = m.SquadMembers != null && m.SquadMembers.Count > 0
                ? m.SquadMembers
                : new List<SquadMemberChests>
                {
                    new SquadMemberChests
                    {
                        PlayerName = m.PlayerName ?? "Você",
                        IsLocalPlayer = true,
                        ChestsOpened = m.ChestsOpened,
                        AmmoBoxesOpened = m.AmmoBoxesOpened,
                        OtherContainersOpened = m.OtherContainersOpened
                    }
                };

            var totalSquadChests = squad.Sum(s => s.ChestsOpened);
            item.SquadSummaryText = squad.Count > 1 
                ? $"Total da Equipe: {totalSquadChests} baús ({squad.Count} jogadores)" 
                : $"Total: {totalSquadChests} baús (Partida Solo)";

            foreach (var member in squad)
            {
                var pct = totalSquadChests > 0 ? (double)member.ChestsOpened / totalSquadChests * 100 : 0.0;
                var pctText = squad.Count > 1 ? $"({pct:0}%)" : "";

                item.SquadDisplayMembers.Add(new TeammateDisplayItem
                {
                    PlayerName = member.PlayerName,
                    IsLocalPlayer = member.IsLocalPlayer,
                    ChestsCount = member.ChestsOpened,
                    AmmoBoxesCount = member.AmmoBoxesOpened,
                    OtherContainersCount = member.OtherContainersOpened,
                    SharePercentText = pctText
                });
            }

            _allMatches.Add(item);
        }

        ApplyFilter();
    }

    private static DateTime When(MatchRecord m) => (m.MatchStartUtc ?? m.AnalyzedAtUtc).ToLocalTime();

    private void ApplyFilter()
    {
        _displayMatches.Clear();
        var query = _searchQuery.Trim().ToLowerInvariant();

        foreach (var item in _allMatches)
        {
            // Mode filter
            if (_currentFilter == "NoBuild" && !item.ModeFormatted.Contains("Construção Zero", StringComparison.OrdinalIgnoreCase))
                continue;
            if (_currentFilter == "Squads" && !item.ModeFormatted.Contains("Esquadrões", StringComparison.OrdinalIgnoreCase))
                continue;
            if (_currentFilter == "Solo" && !item.ModeFormatted.Contains("Solo", StringComparison.OrdinalIgnoreCase))
                continue;
            if (_currentFilter == "Wins" && !item.IsWin)
                continue;

            // Search filter
            if (query.Length > 0)
            {
                var match = item.DateFormatted.ToLowerInvariant().Contains(query)
                            || item.ModeFormatted.ToLowerInvariant().Contains(query)
                            || item.PlacementFormatted.ToLowerInvariant().Contains(query)
                            || item.TreasureText.ToLowerInvariant().Contains(query)
                            || item.ReplayFile.ToLowerInvariant().Contains(query);
                if (!match) continue;
            }

            _displayMatches.Add(item);
        }

        TxtMatchesCountStatus.Text = $"{_displayMatches.Count} partidas";
    }

    private void FilterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;

        _currentFilter = tag;

        // Reset filter button styles
        BtnFilterAll.Style = (Style)FindResource("FilterBtn");
        BtnFilterNoBuild.Style = (Style)FindResource("FilterBtn");
        BtnFilterSquads.Style = (Style)FindResource("FilterBtn");
        BtnFilterSolo.Style = (Style)FindResource("FilterBtn");
        BtnFilterWins.Style = (Style)FindResource("FilterBtn");

        btn.Style = (Style)FindResource("FilterBtnActive");
        ApplyFilter();
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchQuery = TxtSearch.Text ?? "";
        TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(_searchQuery) ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_scanBusy || _cloud.Busy)
        {
            ShowFeedback("⏳ Aguarde a sincronização atual terminar...");
            return;
        }

        _scanBusy = true;
        BtnRefresh.IsEnabled = false;
        TxtRefreshLabel.Text = "Verificando...";
        IconRefresh.Text = "⏳";
        TxtRefreshFeedback.Text = "Buscando novas partidas na pasta de demos...";
        TxtRefreshFeedback.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));

        try
        {
            var newCount = 0;
            await System.Threading.Tasks.Task.Run(() =>
            {
                newCount = _tracker.Scan(ignoreQuietPeriod: true);
                _tracker.UpdateLive();
            });

            LoadData();
            UpdateOverlayView();
            UpdateFortniteProcessState();
            UpdateStatusPill();

            TxtRefreshFeedback.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            TxtRefreshFeedback.Text = newCount > 0 
                ? $"🎉 {newCount} nova(s) partida(s) adicionada(s)!" 
                : $"✅ Histórico local atualizado às {DateTime.Now:HH:mm:ss} ({_data.Matches.Count} partidas)";
        }
        catch (Exception ex)
        {
            TxtRefreshFeedback.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TxtRefreshFeedback.Text = $"Erro: {ex.Message}";
        }
        finally
        {
            _scanBusy = false;
            await System.Threading.Tasks.Task.Delay(300);
            BtnRefresh.IsEnabled = true;
            TxtRefreshLabel.Text = "Atualizar Partidas";
            IconRefresh.Text = "🔄";
        }

        if (_cloud.IsLoggedIn)
        {
            await SyncCloudAsync(silent: false);
        }
    }

    private void UpdateCloudButton()
    {
        if (_cloud.IsLoggedIn)
        {
            IconCloud.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            TxtCloudStatus.Text = _cloud.Busy ? "Nuvem: Sincronizando..." : $"Nuvem: {_cloud.User}";
            TxtCloudStatus.Foreground = new SolidColorBrush(Color.FromRgb(110, 231, 183));
        }
        else
        {
            IconCloud.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
            TxtCloudStatus.Text = "Save na Nuvem: Entrar";
            TxtCloudStatus.Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240));
        }
    }

    private async Task SyncCloudAsync(bool silent)
    {
        if (_scanBusy || _cloud.Busy || !_cloud.IsLoggedIn) return;
        _lastAutoSyncUtc = DateTime.UtcNow;

        TxtCloudStatus.Text = "Nuvem: Sincronizando...";
        if (!silent)
        {
            TxtRefreshFeedback.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
            TxtRefreshFeedback.Text = "☁ Sincronizando com a nuvem...";
        }

        var res = await _cloud.SyncAsync();
        LoadData();
        UpdateOverlayView();
        ReportWriter.WriteAll(_store, _config, _data);
        UpdateCloudButton();

        if (res.Ok)
        {
            if (!silent || res.Message.Contains("+") && !res.Message.Contains("+0 baixadas"))
            {
                ShowFeedback(res.Message);
            }
        }
        else if (!silent)
        {
            TxtRefreshFeedback.Foreground = new SolidColorBrush(Color.FromRgb(252, 165, 165));
            TxtRefreshFeedback.Text = res.Message;
        }
    }

    private void BtnCloud_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CloudWindow(_cloud, _config, _data) { Owner = this };
        dlg.ShowDialog();
        UpdateCloudButton();
        if (dlg.DataChanged)
        {
            LoadData();
            UpdateOverlayView();
            ReportWriter.WriteAll(_store, _config, _data);
            if (_cloud.IsLoggedIn)
            {
                ShowFeedback($"☁ Conectado como {_cloud.User} ({_data.Matches.Sum(m => m.ChestsOpened):N0} baús)");
            }
        }
    }

    private async void BtnResetSession_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Deseja zerar apenas o contador da sessão atual?\n\nSeu total histórico e partidas salvas na nuvem continuam intactos.",
            "Zerar Sessão Atual",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        ReportWriter.ResetSession(_data);
        _store.SaveData(_data);
        LoadData();
        UpdateOverlayView();
        ReportWriter.WriteAll(_store, _config, _data);
        ShowFeedback("⏱ Nova sessão iniciada!");

        if (_cloud.IsLoggedIn)
        {
            await SyncCloudAsync(silent: true);
        }
    }

    private void BtnToggleOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (!NativeOverlay.IsRunning)
        {
            NativeOverlay.Start(_config.OverlayX, _config.OverlayY, _config.OverlayLocked, 
                (x, y) => { _config.OverlayX = x; _config.OverlayY = y; _store.SaveConfig(_config); },
                locked => { _config.OverlayLocked = locked; _store.SaveConfig(_config); UpdateOverlayButtons(); });
            _config.ShowOverlay = true;
        }
        else
        {
            NativeOverlay.Toggle();
            _config.ShowOverlay = NativeOverlay.IsVisible;
        }
        _store.SaveConfig(_config);
        UpdateOverlayButtons();
    }

    private void BtnLockOverlay_Click(object sender, RoutedEventArgs e)
    {
        NativeOverlay.ToggleLock();
        _config.OverlayLocked = NativeOverlay.IsLocked;
        _store.SaveConfig(_config);
        UpdateOverlayButtons();
    }

    private void BtnStyleOverlay_Click(object sender, RoutedEventArgs e)
    {
        var styles = NativeOverlay.Styles;
        _config.OverlayStyle = styles[(Array.IndexOf(styles, _config.OverlayStyle) + 1) % styles.Length];
        _store.SaveConfig(_config);
        NativeOverlay.SetStyle(_config.OverlayStyle);
        UpdateOverlayButtons();
    }

    private void UpdateOverlayButtons()
    {
        var isVisible = NativeOverlay.IsRunning ? NativeOverlay.IsVisible : _config.ShowOverlay;
        var isLocked = NativeOverlay.IsRunning ? NativeOverlay.IsLocked : _config.OverlayLocked;

        TxtOverlayToggle.Text = isVisible ? "Overlay em Jogo: ATIVADO" : "Overlay em Jogo: DESATIVADO";
        TxtLockToggle.Text = isLocked ? "Overlay: TRAVADO (Clique passa)" : "Overlay: DESTRAVADO (Arrastar)";
        TxtStyleToggle.Text = _config.OverlayStyle == "hud" ? "Estilo: HUD Minimapa" : "Estilo: Cartão Clássico";
    }

    private void BtnExpandMatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MatchDisplayItem item)
        {
            item.IsExpanded = !item.IsExpanded;
            e.Handled = true;
        }
    }

    private void ListMatches_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListMatches.SelectedItem is MatchDisplayItem item)
        {
            item.IsExpanded = !item.IsExpanded;
            ListMatches.SelectedItem = null;
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    public void BringToFront()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _pollTimer.Stop();
        _watcher?.Dispose();
        _watcher = null;
        base.OnClosing(e);
    }
}
