using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using ServerPickerX.Constants;
using ServerPickerX.Helpers;
using ServerPickerX.Services.DependencyInjection;
using ServerPickerX.Services.Localizations;
using ServerPickerX.Services.MessageBoxes;
using ServerPickerX.Services.Themes;
using ServerPickerX.Settings;
using ServerPickerX.ViewModels;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace ServerPickerX;

public partial class SettingsWindow : Window
{
    private readonly JsonSetting _jsonSetting;
    private readonly ILocalizationService _localizationService;
    private readonly IMessageBoxService _messageBoxService;

    // Parameterless constructor, allows design previewer to create its own instance since it doesn't support DI
    public SettingsWindow()
    {
        InitializeComponent();

        _jsonSetting = ServiceLocator.GetRequiredService<JsonSetting>();
        _localizationService = ServiceLocator.GetRequiredService<ILocalizationService>();
        _messageBoxService = ServiceLocator.GetRequiredService<IMessageBoxService>();
    }

    // DI constructor, allows inversion of control and unit tests mocking
    public SettingsWindow(
        JsonSetting jsonSetting,
        ILocalizationService localizationService
        )
    {
        InitializeComponent();

        _jsonSetting = jsonSetting;
        _localizationService = localizationService;
        _messageBoxService = ServiceLocator.GetRequiredService<IMessageBoxService>();
    }

    private async void Window_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Set data context and configure UI controls
        await _jsonSetting.LoadSettingsAsync();

        DataContext = ServiceLocator.GetRequiredService<SettingsWindowViewModel>();

        VersionTextBlock.Text = "Version: " + Assembly.GetEntryAssembly()!.GetName().Version!.ToString(3);

        LanguageComboBox.SelectionChanged -= LanguageComboBox_SelectionChanged;
        LanguageComboBox.SelectedValue = _jsonSetting.language;
        LanguageComboBox.SelectionChanged += LanguageComboBox_SelectionChanged;

        ThemeComboBox.SelectionChanged -= ThemeComboBox_SelectionChanged;
        ThemeComboBox.SelectedIndex = GetThemeIndex(_jsonSetting.theme);
        ThemeComboBox.SelectionChanged += ThemeComboBox_SelectionChanged;

        AutoRefreshComboBox.SelectionChanged -= AutoRefreshComboBox_SelectionChanged;
        AutoRefreshComboBox.SelectedIndex = GetAutoRefreshIndex(_jsonSetting.auto_refresh_minutes);
        AutoRefreshComboBox.SelectionChanged += AutoRefreshComboBox_SelectionChanged;

        MinimizeToTraySetting.IsChecked = _jsonSetting.minimize_to_tray;

        RenderModeComboBox.ItemsSource = ResolveRenderModeValues();

        RenderModeComboBox.SelectionChanged -= RenderModeComboBox_SelectionChanged;
        RenderModeComboBox.SelectedValue = _jsonSetting.render_mode;
        RenderModeComboBox.SelectionChanged += RenderModeComboBox_SelectionChanged;
    }

    // The pipeline is chosen before Avalonia starts, so a change needs a restart
    private async void RenderModeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (RenderModeComboBox?.SelectedItem is not string selectedRenderMode) return;

        if (selectedRenderMode == _jsonSetting.render_mode) return;

        await _jsonSetting.SetRenderModeAsync(selectedRenderMode);

        await _messageBoxService.ShowMessageBoxAsync(
            _localizationService.GetLocaleValue("MessageBoxInfoTitle"),
            _localizationService.GetLocaleValue("RenderModeDialogue"),
            MsBox.Avalonia.Enums.Icon.Setting
            );
    }

    private static IReadOnlyList<string> ResolveRenderModeValues()
    {
        return OperatingSystem.IsWindows()
            ? RenderModes.AllWindows
            : RenderModes.AllLinux;
    }

    private async void AutoRefreshComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (AutoRefreshComboBox is null) return;

        int minutes = AutoRefreshComboBox.SelectedIndex switch
        {
            1 => 5,
            2 => 15,
            3 => 30,
            _ => 0,
        };

        await _jsonSetting.SetAutoRefreshMinutesAsync(minutes);

        Views.MainWindow.Instance?.ConfigureAutoRefresh();
    }

    private static int GetAutoRefreshIndex(int minutes)
    {
        return minutes switch
        {
            5 => 1,
            15 => 2,
            30 => 3,
            _ => 0,
        };
    }

    private async void MinimizeToTraySetting_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        bool enabled = MinimizeToTraySetting.IsChecked == true;

        await _jsonSetting.SetMinimizeToTrayAsync(enabled);

        Views.MainWindow.Instance?.ConfigureTrayIcon();
    }

    private async void ThemeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox is null) return;

        // Items are ordered System, Light, Dark so the labels stay translatable
        string selectedTheme = ThemeComboBox.SelectedIndex switch
        {
            1 => ThemeService.LightTheme,
            2 => ThemeService.DarkTheme,
            _ => ThemeService.SystemTheme,
        };

        ThemeService.Apply(selectedTheme);

        FooterButtons.Instance?.RefreshThemeButton(selectedTheme);

        await _jsonSetting.SetThemeAsync(selectedTheme);
    }

    private static int GetThemeIndex(string? theme)
    {
        if (ThemeService.LightTheme.Equals(theme, StringComparison.OrdinalIgnoreCase)) return 1;

        if (ThemeService.DarkTheme.Equals(theme, StringComparison.OrdinalIgnoreCase)) return 2;

        return 0;
    }

    private void TitleBar_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        // Prevent other mouse event listeners from being triggered
        e.Handled = true;

        var parentWindow = TopLevel.GetTopLevel(this) as Window;

        parentWindow?.BeginMoveDrag(e);
    }

    private async void LanguageComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LanguageComboBox is null || LanguageComboBox.SelectedItem is null) return;

        // Set language using combo box selection, this will trigger UI updates immediately
        var selectedLanguage = (string)LanguageComboBox.SelectedItem;
        var language = selectedLanguage.Replace(" ", "").Split("|")[1];

        await _jsonSetting.SetLanguageAsync(selectedLanguage);

        await _localizationService.SetLanguage(language);
    }
}