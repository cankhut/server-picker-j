using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using ServerPickerX.Models;
using ServerPickerX.Services.DependencyInjection;
using ServerPickerX.Services.Games;
using ServerPickerX.Services.Localizations;
using ServerPickerX.Services.Loggers;
using ServerPickerX.Services.MessageBoxes;
using ServerPickerX.Services.Servers;
using ServerPickerX.Services.Themes;
using ServerPickerX.Services.Versions;
using ServerPickerX.Settings;
using ServerPickerX.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ServerPickerX.Views
{
    public partial class MainWindow : Window
    {
        // Singleton instance for accessing the main window on execution lifetime
        public static MainWindow? Instance { get; private set; }

        public static bool IsDebugBuild
        {
            get
            {
                #if DEBUG
                    return true;
                #else
                    return false;
                #endif
            }
        }

        private bool _suppressPresetSelectionChanged;
        private PresetModel? _previousPreset;
        private DispatcherTimer? _autoRefreshTimer;
        private DispatcherTimer? _gameWatcherTimer;
        private TrayIcon? _trayIcon;
        private bool _isExiting;
        private bool _blockedExitPromptAnswered;

        // Game watcher state. The game mode is captured when the watcher starts so a
        // switch mid session restarts it instead of matching one game's process
        // against another game's preset
        private string _gameWatcherGameMode = string.Empty;
        private bool _gameWasRunning;
        private bool _gameWatcherApplied;
        private bool _gameWatcherBusy;

        private readonly ILoggerService _loggerService;
        private readonly JsonSetting _jsonSetting;
        private readonly IMessageBoxService _messageBoxService;
        private readonly IVersionService _versionService;
        private readonly ILocalizationService _localizationService;
        private readonly ServerDefinitionProvider _serverDefinitionProvider;
        private readonly IGameProcessService _gameProcessService;

        // Parameterless constructor, allows design previewer to create its own instance since it doesn't support DI
        public MainWindow()
        {
            InitializeComponent();
            Instance = this;

            _loggerService = ServiceLocator.GetRequiredService<ILoggerService>();
            _jsonSetting = ServiceLocator.GetRequiredService<JsonSetting>();
            _messageBoxService = ServiceLocator.GetRequiredService<IMessageBoxService>();
            _versionService = ServiceLocator.GetRequiredService<IVersionService>();
            _localizationService = ServiceLocator.GetRequiredService<ILocalizationService>();
            _serverDefinitionProvider = ServiceLocator.GetRequiredService<ServerDefinitionProvider>();
            _gameProcessService = ServiceLocator.GetRequiredService<IGameProcessService>();
        }

        // DI constructor, allows inversion of control and unit tests mocking
        public MainWindow(
            ILoggerService loggerService,
            JsonSetting jsonSetting,
            IMessageBoxService messageBoxService,
            IVersionService versionService,
            ILocalizationService localizationService
            )
        {
            InitializeComponent();
            Instance = this;

            _loggerService = loggerService;
            _messageBoxService = messageBoxService;
            _versionService = versionService;
            _jsonSetting = jsonSetting;
            _localizationService = localizationService;
            _serverDefinitionProvider = ServiceLocator.GetRequiredService<ServerDefinitionProvider>();
            _gameProcessService = ServiceLocator.GetRequiredService<IGameProcessService>();
        }

        private async void Window_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await InitializeApp();
        }

        private async void GameModeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            await HandleGameModeChangeAsync();
        }

        private async void PresetComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            PresetModel? previousPreset = e.RemovedItems.Count > 0
                ? e.RemovedItems[0] as PresetModel
                : _previousPreset;

            if (_suppressPresetSelectionChanged)
            {
                return;
            }

            if (PresetComboBox?.SelectedItem is not PresetModel selectedPreset)
            {
                _previousPreset = null;
                return;
            }

            if (!PresetComboBox.IsDropDownOpen)
            {
                _previousPreset = selectedPreset;
                return;
            }

            await HandlePresetChangeAsync(selectedPreset, previousPreset);
        }

        // A click anywhere on a server card toggles its firewall rule
        private async void ServerCard_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            if (sender is not Border { DataContext: ServerModel serverModel })
            {
                return;
            }

            e.Handled = true;

            await vm.ToggleServerBlockAsync(serverModel);
        }

        private void PingServerMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { DataContext: ServerModel serverModel })
            {
                serverModel.PingServer();
            }
        }

        private async void BlockSlowerMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            if (sender is not MenuItem { DataContext: ServerModel serverModel })
            {
                return;
            }

            await vm.BlockSlowerThanAsync(serverModel);
        }

        private void SortBtn_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            if (sender is not Button { Tag: string sortTag })
            {
                return;
            }

            ServerSortField sortField = sortTag switch
            {
                "ping" => ServerSortField.Ping,
                "loss" => ServerSortField.PacketLoss,
                _ => ServerSortField.Location,
            };

            // Clicking the active chip flips direction and re-applies the sort
            if (vm.SortField == sortField)
            {
                vm.SortDescending = !vm.SortDescending;
            }
            else
            {
                vm.SortField = sortField;
                vm.SortDescending = false;
            }

            RefreshSortChips();
        }

        private void RefreshSortChips()
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            SetSortChipState(SortLocationBtn, SortLocationArrow, vm, ServerSortField.Location);
            SetSortChipState(SortPingBtn, SortPingArrow, vm, ServerSortField.Ping);
            SetSortChipState(SortLossBtn, SortLossArrow, vm, ServerSortField.PacketLoss);
        }

        private static void SetSortChipState(
            Button chip,
            TextBlock arrow,
            MainWindowViewModel vm,
            ServerSortField sortField
            )
        {
            bool isActive = vm.SortField == sortField;

            chip.Classes.Set("active", isActive);

            arrow.IsVisible = isActive;
            arrow.Text = vm.SortDescending ? "\u2193" : "\u2191";
        }

        private void TitleBar_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            e.Handled = true;
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            parentWindow?.BeginMoveDrag(e);
        }

        private async void ClusterUnclusterBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm || !vm.ServerModelsInitialized)
            {
                return;
            }

            await vm.ClusterUnclusterServersAsync();
            SyncPresetSelection(vm.SelectedPreset);
            RefreshClusterButtonContent();
        }

        private async void PresetsBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            PresetManagerWindow presetManagementWindow = new(vm)
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            await presetManagementWindow.ShowDialog(this);

            // Reload presets after closing preset manager window
            vm.LoadPresetPickerItems();
            SyncPresetSelection(vm.SelectedPreset);
            RefreshClusterButtonContent();
        }

        public async Task InitializeApp()
        {
            await _jsonSetting.LoadSettingsAsync();

            ThemeService.Apply(_jsonSetting.theme);

            FooterButtons.Instance?.RefreshThemeButton(_jsonSetting.theme);

            await SetLanguage();

            await ConfigureControls();

            var vm = ServiceLocator.GetRequiredService<MainWindowViewModel>();

            await vm.LoadServersAsync();

            DataContext = vm;

            if (vm.ServersLoaded)
            {
                await SyncServersAsync(vm);
                vm.LoadPresetPickerItems();
                await vm.RestoreLastSelectedPresetAsync();
            }

            ConfigurePresetControls(vm);
            RefreshClusterButtonContent();
            RefreshSortChips();

            ConfigureAutoRefresh();
            ConfigureTrayIcon();
            ConfigureGameWatcher();

            await _versionService.CheckVersionAsync();
        }

        // Re-probes on a timer when the user has opted in. Blocked servers are skipped
        // by the sweep itself, so an idle tick costs nothing when everything is blocked
        public void ConfigureAutoRefresh()
        {
            if (_autoRefreshTimer != null)
            {
                _autoRefreshTimer.Stop();
                _autoRefreshTimer.Tick -= AutoRefreshTimer_Tick;
                _autoRefreshTimer = null;
            }

            int minutes = _jsonSetting.auto_refresh_minutes;

            if (minutes <= 0)
            {
                return;
            }

            _autoRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMinutes(minutes)
            };

            _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
            _autoRefreshTimer.Start();
        }

        private void AutoRefreshTimer_Tick(object? sender, EventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            // Skip the tick outright while hidden or mid firewall operation rather
            // than queueing work the user will never see
            if (!IsVisible || WindowState == WindowState.Minimized || !vm.IsOperationAllowed)
            {
                return;
            }

            _ = vm.PingServersAsync(vm.ServerModels);
        }

        // Watches for the current game starting and stopping so the preset the user
        // last picked is applied on launch and taken back off on exit
        public void ConfigureGameWatcher()
        {
            if (_gameWatcherTimer != null)
            {
                _gameWatcherTimer.Stop();
                _gameWatcherTimer.Tick -= GameWatcherTimer_Tick;
                _gameWatcherTimer = null;
            }

            _gameWatcherApplied = false;
            _gameWatcherBusy = false;

            if (!_jsonSetting.auto_apply_on_game_launch)
            {
                return;
            }

            _gameWatcherGameMode = _jsonSetting.game_mode;

            // Seed the state from the current reality, so a game that is already running
            // when the watcher starts is not mistaken for a launch that just happened
            _gameWasRunning = _gameProcessService.IsGameRunning(_gameWatcherGameMode);

            _gameWatcherTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(5)
            };

            _gameWatcherTimer.Tick += GameWatcherTimer_Tick;
            _gameWatcherTimer.Start();
        }

        private void GameWatcherTimer_Tick(object? sender, EventArgs e)
        {
            _ = HandleGameWatcherTickAsync();
        }

        private async Task HandleGameWatcherTickAsync()
        {
            // One tick at a time, a firewall pass easily outlasts the interval
            if (_gameWatcherBusy || DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            if (!_gameWatcherGameMode.Equals(_jsonSetting.game_mode, StringComparison.OrdinalIgnoreCase))
            {
                ConfigureGameWatcher();

                return;
            }

            bool isRunning = _gameProcessService.IsGameRunning(_gameWatcherGameMode);

            if (isRunning == _gameWasRunning)
            {
                return;
            }

            // Wait for the app to go idle rather than queueing firewall work behind a
            // running operation. The transition is still there on the next tick
            if (!vm.ServersLoaded || !vm.IsOperationAllowed)
            {
                return;
            }

            _gameWatcherBusy = true;

            try
            {
                if (isRunning)
                {
                    await ApplyPresetForGameLaunchAsync(vm);
                }
                else if (_gameWatcherApplied)
                {
                    // Only ever undo what this watcher did itself. Blocks the user made
                    // by hand are theirs to remove, the app does not clear them silently
                    _gameWatcherApplied = false;

                    await vm.UnblockAllAsync(shouldClearLastSelectedPreset: false);

                    SyncPresetSelection(vm.SelectedPreset);

                    await _loggerService.LogInfoAsync($"Auto apply unblocked all servers after {_gameWatcherGameMode} closed");
                }

                _gameWasRunning = isRunning;
            }
            finally
            {
                _gameWatcherBusy = false;
            }
        }

        private async Task ApplyPresetForGameLaunchAsync(MainWindowViewModel vm)
        {
            string presetName = _jsonSetting.GetLastSelectedPresetNameByGameMode();

            // Nothing to apply is a normal state, not an error. The user simply has not
            // picked a preset for this game yet
            if (string.IsNullOrWhiteSpace(presetName))
            {
                return;
            }

            PresetModel? preset = vm.GetCurrentGamePreset(presetName);

            if (preset == null)
            {
                return;
            }

            bool presetApplied = await vm.ApplyPresetAsync(preset);

            if (!presetApplied)
            {
                await _loggerService.LogWarningAsync($"Auto apply could not apply preset '{presetName}' on game launch");

                return;
            }

            _gameWatcherApplied = true;

            SyncPresetSelection(vm.SelectedPreset);
            RefreshClusterButtonContent();

            await _loggerService.LogInfoAsync($"Auto apply applied preset '{presetName}' after {_gameWatcherGameMode} started");
        }

        public void ConfigureTrayIcon()
        {
            if (!_jsonSetting.minimize_to_tray)
            {
                DisposeTrayIcon();

                return;
            }

            if (_trayIcon != null)
            {
                return;
            }

            try
            {
                NativeMenuItem showItem = new(_localizationService.GetLocaleValue("TrayShow"));
                showItem.Click += (_, _) => RestoreFromTray();

                NativeMenuItem quitItem = new(_localizationService.GetLocaleValue("TrayQuit"));
                quitItem.Click += (_, _) => ExitApplication();

                NativeMenu trayMenu = new();
                trayMenu.Add(showItem);
                trayMenu.Add(quitItem);

                _trayIcon = new TrayIcon
                {
                    Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://ServerPickerX/Assets/favicon.ico"))),
                    ToolTipText = "Server Picker X",
                    IsVisible = true,
                    Menu = trayMenu
                };

                _trayIcon.Clicked += (_, _) => RestoreFromTray();
            }
            catch (Exception)
            {
                // A desktop without a system tray just keeps the window on close
                _trayIcon = null;
            }
        }

        private void DisposeTrayIcon()
        {
            if (_trayIcon == null)
            {
                return;
            }

            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        private void RestoreFromTray()
        {
            Show();

            WindowState = WindowState.Normal;

            Activate();
        }

        private void ExitApplication()
        {
            _isExiting = true;

            // The tray icon is disposed on the real close path, not here, so a
            // cancelled exit does not leave the app hidden with no way back to it
            Close();
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (!_isExiting && _jsonSetting.minimize_to_tray && _trayIcon != null)
            {
                e.Cancel = true;

                Hide();

                return;
            }

            // Firewall rules outlive the app, so leaving servers blocked is a decision
            // the user should make knowingly rather than discover weeks later
            if (!_blockedExitPromptAnswered
                && DataContext is MainWindowViewModel viewModel
                && viewModel.BlockedServerCount > 0)
            {
                e.Cancel = true;

                // Quitting from the tray leaves the window hidden, and a dialog owned
                // by a hidden window never reaches the user
                RestoreFromTray();

                _ = ConfirmExitWithBlockedServersAsync(viewModel);

                return;
            }

            _autoRefreshTimer?.Stop();
            _gameWatcherTimer?.Stop();

            DisposeTrayIcon();

            base.OnClosing(e);
        }

        private async Task ConfirmExitWithBlockedServersAsync(MainWindowViewModel viewModel)
        {
            string keepBlocked = _localizationService.GetLocaleValue("BlockedOnExitKeep");
            string unblockAll = _localizationService.GetLocaleValue("BlockedOnExitUnblock");
            string cancel = _localizationService.GetLocaleValue("BlockedOnExitCancel");

            string choice = await _messageBoxService.ShowMessageBoxChoiceAsync(
                _localizationService.GetLocaleValue("MessageBoxInfoTitle"),
                string.Format(
                    _localizationService.GetLocaleValue("BlockedOnExitDialogue"),
                    viewModel.BlockedServerCount
                    ),
                [unblockAll, keepBlocked, cancel],
                MsBox.Avalonia.Enums.Icon.Warning
                );

            // Dismissing the dialog leaves the app open rather than guessing, and the
            // pending exit is cancelled so closing the window still minimises to tray
            if (choice != keepBlocked && choice != unblockAll)
            {
                _isExiting = false;

                return;
            }

            if (choice == unblockAll)
            {
                await viewModel.UnblockAllAsync();
            }

            _blockedExitPromptAnswered = true;
            _isExiting = true;

            Close();
        }

        private async Task SetLanguage()
        {
            // Extract language code from enum text
            var language = _jsonSetting.language.Replace(" ", "").Split("|")[1];

            await _localizationService.SetLanguage(language);
        }

        private async Task ConfigureControls()
        {
            try
            {
                IReadOnlyList<string> gameModes = _serverDefinitionProvider.GetGameModes();

                if (gameModes.Count == 0)
                {
                    throw new InvalidOperationException("No server definitions were found.");
                }

                if (!gameModes.Contains(_jsonSetting.game_mode, StringComparer.OrdinalIgnoreCase))
                {
                    await _jsonSetting.SetGameModeAsync(gameModes[0]);
                }

                GameModeComboBox.SelectionChanged -= GameModeComboBox_SelectionChanged;
                GameModeComboBox.ItemsSource = gameModes;
                GameModeComboBox.SelectedItem = _jsonSetting.game_mode;
                GameModeComboBox.SelectionChanged += GameModeComboBox_SelectionChanged;
            }
            catch (InvalidOperationException ex)
            {
                await _loggerService.LogErrorAsync("An error has occured while setting game mode combo box", ex.Message);

                throw;
            }

            RefreshClusterButtonContent();
        }

        private void ConfigurePresetControls(MainWindowViewModel vm)
        {
            SyncPresetSelection(vm.SelectedPreset);
        }

        private async Task SyncServersAsync(MainWindowViewModel vm)
        {
            var localRevision = await _jsonSetting.GetRevisionByGameModeAsync();

            var fetchedRevision = vm.GetServerDataService().GetFetchedRevision();

            string appId = _serverDefinitionProvider.GetAppIdByGameMode(_jsonSetting.game_mode);
            
            IReadOnlyList<string> affectedGameModes = _serverDefinitionProvider.GetGameModesByAppId(appId);
            
            bool hasAffectedPresets = affectedGameModes.Any(
                    gameMode => _jsonSetting.GetPresetsByGameMode(gameMode).Count > 0
                );

            // Store the initial revision without a reset when this game has no saved presets yet.
            if (localRevision == "-1" && !hasAffectedPresets)
            {
                await _jsonSetting.SetRevisionByGameModeAsync(fetchedRevision);
                return;
            }

            // Skip server unblocking and revision sync if local revision is equal to fetched revision
            if (localRevision == fetchedRevision)
            {
                return;
            }

            // This only happens on successful load and sync on startup or game switch
            await _messageBoxService.ShowMessageBoxAsync(
                    _localizationService.GetLocaleValue("MessageBoxInfoTitle"),
                    _localizationService.GetLocaleValue("SyncServersUnblockAllDialogue"),
                    MsBox.Avalonia.Enums.Icon.Setting
                    );

            // Unblock current game rules while preserving last selected preset
            bool unblocked = await vm.UnblockAllAsync(shouldClearLastSelectedPreset: false);

            if (!unblocked)
            {
                return;
            }

            await vm.PruneCurrentGamePresetEntriesAsync();

            if (affectedGameModes.Count > 1)
            {
                if (!await vm.PruneRelatedGamePresetEntriesAsync())
                {
                    return;
                }
            }

            await _jsonSetting.SetRevisionByGameModeAsync(fetchedRevision);
        }

        private async Task HandleGameModeChangeAsync()
        {
            if (DataContext is not MainWindowViewModel vm || GameModeComboBox?.SelectedItem == null)
            {
                return;
            }

            bool result = await _messageBoxService.ShowMessageBoxConfirmationAsync(
                    _localizationService.GetLocaleValue("MessageBoxInfoTitle"),
                    _localizationService.GetLocaleValue("SwapGameModeUnblockAllConflict"),
                    MsBox.Avalonia.Enums.Icon.Setting
                    );

            if (!result)
            {
                // Revert back selection without triggering event handler
                GameModeComboBox.SelectionChanged -= GameModeComboBox_SelectionChanged;
                GameModeComboBox.SelectedItem = _jsonSetting.game_mode;
                GameModeComboBox.SelectionChanged += GameModeComboBox_SelectionChanged;

                return;
            }

            // Clear the currently loaded game rules before changing game mode while preserving last selected preset
            await vm.UnblockAllAsync(shouldClearLastSelectedPreset: false);

            // Update json setting game mode and serialize it
            await _jsonSetting.SetGameModeAsync((string)GameModeComboBox.SelectedItem);

            await InitializeApp();
        }

        private async Task HandlePresetChangeAsync(
            PresetModel selectedPreset,
            PresetModel? previousPreset
            )
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            if (AreSamePresetSelection(selectedPreset, previousPreset))
            {
                return;
            }

            bool presetApplied = await vm.ApplyPresetAsync(selectedPreset);

            if (!presetApplied)
            {
                SyncPresetSelection(previousPreset);
                return;
            }

            SyncPresetSelection(vm.SelectedPreset);
            RefreshClusterButtonContent();
        }

        private void SyncPresetSelection(PresetModel? preset)
        {
            _suppressPresetSelectionChanged = true;
            PresetComboBox.SelectedItem = preset;
            _suppressPresetSelectionChanged = false;
            _previousPreset = preset;
        }

        private static bool AreSamePresetSelection(PresetModel? left, PresetModel? right)
        {
            if (left == null || right == null)
            {
                return left == null && right == null;
            }

            return left.Equals(right);
        }

        private void RefreshClusterButtonContent()
        {
            ClusterUnclusterBtn.Content = _localizationService.GetLocaleValue(
                _jsonSetting.is_clustered ? "UnclusterServers" : "ClusterServers"
                );
        }

    }
}
