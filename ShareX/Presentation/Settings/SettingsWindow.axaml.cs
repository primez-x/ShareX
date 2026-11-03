#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

#nullable enable

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using ShareX.AvaloniaUI.Controls;
using ShareX.AvaloniaUI.Theming;
using ShareX.HelpersLib;
using ShareX.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using static ShareX.AvaloniaUI.Controls.SettingsControls;

namespace ShareX;

/// <summary>
/// The single settings entry point: the common options on simple pages, and buttons that open the detailed settings windows.
/// </summary>
public partial class SettingsWindow : Window
{
    private const string SystemTheme = "System";

    private readonly ApplicationSettingsViewModel _application = new();
    private readonly HotkeySettingsViewModel? _hotkeys = HotkeySettingsIntegration.AcquireViewModel();
    private readonly Dictionary<string, Control> _pages = [];
    private readonly IReadOnlyList<EnumOption<string>> _themeOptions;
    private readonly BoundValue<EnumOption<string>> _theme;

    private static TaskSettings DefaultTaskSettings => ApplicationState.DefaultTaskSettings;

    public SettingsWindow()
    {
        InitializeComponent();
        RequestedThemeVariant = ThemeManager.GetCurrentTheme();

        _themeOptions =
        [
            .. _application.ThemeOptions.OrderByDescending(x => x.Value == "Light"),
            new(SystemTheme, Strings.SettingsWindow_UseSystemTheme)
        ];
        _theme = new BoundValue<EnumOption<string>>(GetThemeOption(), SetThemeOption);

        bool uploadsEnabled = !SystemOptions.DisableUpload;
        List<SettingsNavigationItem> navigationItems =
        [
            AddPage("general", Strings.SettingsWindow_General, LucideIcons.settings, BuildGeneralPage),
            AddPage("capture", Strings.SettingsWindow_Capture, LucideIcons.camera, BuildCapturePage),
            AddPage("shortcuts", Strings.SettingsWindow_Shortcuts, LucideIcons.keyboard, BuildShortcutsPage)
        ];

        if (uploadsEnabled)
        {
            navigationItems.Add(AddPage("sharing", Strings.SettingsWindow_Sharing, LucideIcons.share, BuildSharingPage));
        }

        navigationItems.Add(AddPage("advanced", Strings.SettingsWindow_Advanced, LucideIcons.sliders_horizontal,
            (id, title, icon) => BuildAdvancedPage(id, title, icon, uploadsEnabled)));

        Navigation.Items = navigationItems;
        Navigation.SearchRoot = SettingsPages;
        Navigation.PropertyChanged += OnNavigationPropertyChanged;
        Navigation.SelectedItem = navigationItems[0];

        Opened += (_, _) =>
        {
            Activate();
            Navigation.RefreshFilter();
        };
        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += OnClosed;
    }

    private SettingsNavigationItem AddPage(string id, string title, string icon, Func<string, string, string, Control> build)
    {
        Control page = build(id, title, icon);
        _pages.Add(id, page);
        SettingsPages.Children.Add(page);
        return new SettingsNavigationItem(id, title, icon);
    }

    private Control BuildGeneralPage(string id, string title, string icon)
    {
        ComboBox theme = new() { ItemsSource = _themeOptions };
        theme.Classes.Add("form-control");
        theme.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(BoundValue<EnumOption<string>>.Value))
        {
            Source = _theme,
            Mode = BindingMode.TwoWay
        });

        ComboBox language = new() { ItemsSource = _application.LanguageOptions };
        language.Classes.Add("form-control");
        language.Bind(SelectingItemsControl.SelectedItemProperty, ApplicationBinding(nameof(ApplicationSettingsViewModel.SelectedLanguage)));

        StackPanel restartNotice = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = Strings.ApplicationSettingsWindow_RestartShareXToApplyThisChange,
                    VerticalAlignment = VerticalAlignment.Center
                },
                ActionButton(Strings.ApplicationSettingsWindow_RestartNow, _application.Restart)
            }
        };
        restartNotice.Bind(IsVisibleProperty, ApplicationBinding(nameof(ApplicationSettingsViewModel.RestartRequired)));

        CheckBox startWithWindows = ApplicationCheck(nameof(ApplicationSettingsViewModel.StartWithWindows));
        startWithWindows.Bind(ContentProperty, ApplicationBinding(nameof(ApplicationSettingsViewModel.StartWithWindowsText)));
        startWithWindows.Bind(IsEnabledProperty, ApplicationBinding(nameof(ApplicationSettingsViewModel.StartWithWindowsEnabled)));

        CheckBox showTray = ApplicationCheck(nameof(ApplicationSettingsViewModel.ShowTray));
        showTray.Content = Strings.SettingsWindow_ShowTrayIcon;

        return Page(id, title, icon,
            Card(Strings.SettingsWindow_Appearance,
                Row(Strings.ApplicationSettingsWindow_ThemeLabel, theme),
                Row(Strings.ApplicationSettingsWindow_LanguageLabel, language),
                restartNotice),
            Card(Strings.SettingsWindow_Startup,
                startWithWindows,
                Check(Strings.SettingsWindow_ShowCapturePanelAtStartup,
                    () => ApplicationState.Settings.CapturePanelRunAtStartup,
                    value => ApplicationState.Settings.CapturePanelRunAtStartup = value),
                showTray,
                Hint(Strings.SettingsWindow_ShowTrayIconHint)));
    }

    private Control BuildCapturePage(string id, string title, string icon)
    {
        CheckBox useCustomFolder = ApplicationCheck(nameof(ApplicationSettingsViewModel.UseCustomScreenshotsPath));
        useCustomFolder.Content = Strings.SettingsWindow_UseCustomScreenshotsFolder;

        TextBox folder = new();
        folder.Classes.Add("form-control");
        AutomationProperties.SetName(folder, Strings.SettingsWindow_CustomScreenshotsFolder);
        folder.Bind(TextBox.TextProperty, new Binding(nameof(ApplicationSettingsViewModel.CustomScreenshotsPath))
        {
            Source = _application,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        folder.Bind(IsEnabledProperty, ApplicationBinding(nameof(ApplicationSettingsViewModel.UseCustomScreenshotsPath)));

        Grid folderRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        Button browse = ActionButton(Strings.ApplicationSettingsWindow_Browse, BrowseScreenshotsFolder);
        Grid.SetColumn(browse, 1);
        folderRow.Children.Add(folder);
        folderRow.Children.Add(browse);

        TextBlock preview = Hint(string.Empty);
        preview.VerticalAlignment = VerticalAlignment.Center;
        preview.Bind(TextBlock.TextProperty, ApplicationBinding(nameof(ApplicationSettingsViewModel.ScreenshotsFolderPreview)));
        Grid previewRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        Button openFolder = ActionButton(Strings.SettingsWindow_OpenFolder, TaskHelpers.OpenScreenshotsFolder);
        Grid.SetColumn(openFolder, 1);
        previewRow.Children.Add(preview);
        previewRow.Children.Add(openFolder);

        TaskSettingsImage image = DefaultTaskSettings.ImageSettings;
        EnumOption<EImageFormat>[] formats = Enum.GetValues<EImageFormat>()
            .Select(x => new EnumOption<EImageFormat>(x, x.GetDescription().ToUpperInvariant()))
            .ToArray();
        BoundValue<EnumOption<EImageFormat>> format = new(
            formats.FirstOrDefault(x => x.Value == image.ImageFormat) ?? formats[0],
            value => image.ImageFormat = value.Value);
        ComboBox formatCombo = new() { ItemsSource = formats };
        formatCombo.Classes.Add("form-control");
        formatCombo.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(BoundValue<EnumOption<EImageFormat>>.Value))
        {
            Source = format,
            Mode = BindingMode.TwoWay
        });

        return Page(id, title, icon,
            Card(Strings.SettingsWindow_AfterCapture,
                Check(Strings.SettingsWindow_OpenEditorAfterCapture,
                    () => DefaultTaskSettings.OpenEditorAfterCapture,
                    value => DefaultTaskSettings.OpenEditorAfterCapture = value),
                AfterCaptureCheck(Strings.SettingsWindow_CopyImageToClipboard, AfterCaptureTasks.CopyImageToClipboard),
                AfterCaptureCheck(Strings.SettingsWindow_SaveImageToFile, AfterCaptureTasks.SaveImageToFile),
                Check(Strings.SettingsWindow_PlaySoundAfterCapture,
                    () => DefaultTaskSettings.GeneralSettings.PlaySoundAfterCapture,
                    value => DefaultTaskSettings.GeneralSettings.PlaySoundAfterCapture = value)),
            Card(Strings.SettingsWindow_ScreenshotsFolder,
                useCustomFolder,
                folderRow,
                previewRow,
                Hint(Strings.SettingsWindow_ScreenshotsFolderHint)),
            Card(Strings.SettingsWindow_ImageFile,
                Row(Strings.SettingsWindow_ImageFileFormatLabel, formatCombo),
                Hint(Strings.SettingsWindow_ImageFileFormatHint),
                Check(Strings.SettingsWindow_ShowCursor,
                    () => DefaultTaskSettings.CaptureSettings.ShowCursor,
                    value => DefaultTaskSettings.CaptureSettings.ShowCursor = value)),
            Card(Strings.SettingsWindow_RegionSelection,
                Check(Strings.SettingsWindow_ActiveMonitorOnly,
                    () => DefaultTaskSettings.CaptureSettings.RegionCaptureOptions.ActiveMonitorOnly,
                    value => DefaultTaskSettings.CaptureSettings.RegionCaptureOptions.ActiveMonitorOnly = value),
                Hint(Strings.SettingsWindow_ActiveMonitorOnlyHint)));
    }

    private Control BuildShortcutsPage(string id, string title, string icon)
    {
        StackPanel header = new()
        {
            Margin = new Thickness(28, 24, 28, 0),
            Children = { PageTitle(title, icon), Hint(Strings.SettingsWindow_ShortcutsHint) }
        };

        // While Windows opens Snipping Tool on Print Screen, the Print Screen shortcuts below never run.
        if (IntegrationHelpers.IsPrintScreenKeyUsedBySnippingTool())
        {
            StackPanel notice = new() { Spacing = 6, Margin = new Thickness(0, 10, 0, 0) };
            notice.Children.Add(Hint(Strings.SettingsWindow_PrintScreenKeyUsedBySnippingTool));
            notice.Children.Add(ActionButton(Strings.SettingsWindow_UsePrintScreenKey, () =>
            {
                IntegrationHelpers.UsePrintScreenKeyForShareX();
                notice.IsVisible = false;
            }));
            header.Children.Add(notice);
        }

        Grid page = new() { RowDefinitions = new RowDefinitions("Auto,*"), IsVisible = false };
        SettingsSearch.SetPageId(page, id);
        page.Children.Add(header);

        if (_hotkeys != null)
        {
            HotkeySettingsView editor = new() { DataContext = _hotkeys, Margin = new Thickness(18, 8, 18, 18) };
            Grid.SetRow(editor, 1);
            page.Children.Add(editor);
        }

        return page;
    }

    private Control BuildSharingPage(string id, string title, string icon) =>
        Page(id, title, icon,
            Card(Strings.SettingsWindow_Upload,
                AfterCaptureCheck(Strings.SettingsWindow_UploadAfterCapture, AfterCaptureTasks.UploadImageToHost),
                Hint(Strings.SettingsWindow_UploadHint),
                ActionButton(Strings.SettingsWindow_SetUpUploads, () => TaskHelpers.OpenUploadersConfigWindow())));

    private Control BuildAdvancedPage(string id, string title, string icon, bool uploadsEnabled)
    {
        StackPanel buttons = new() { Spacing = 6 };
        buttons.Children.Add(ActionButton(Strings.SettingsWindow_AllApplicationSettings, ApplicationSettingsIntegration.Show));
        buttons.Children.Add(ActionButton(Strings.SettingsWindow_AllTaskSettings,
            () => MainWindowIntegration.ExecuteCommand(MainFormCommand.TaskSettings)));
        buttons.Children.Add(ActionButton(Strings.SettingsWindow_AllShortcutSettings, HotkeySettingsIntegration.Show));

        if (uploadsEnabled)
        {
            buttons.Children.Add(ActionButton(Strings.SettingsWindow_DestinationSettings, () => TaskHelpers.OpenUploadersConfigWindow()));
        }

        return Page(id, title, icon,
            Card(Strings.SettingsWindow_DetailedSettings,
                Hint(Strings.SettingsWindow_AdvancedHint),
                buttons));
    }

    private Binding ApplicationBinding(string property) => new(property) { Source = _application, Mode = BindingMode.TwoWay };

    /// <summary>
    /// Binds straight to the application settings view model, because the view model can reject or adjust a new value.
    /// </summary>
    private CheckBox ApplicationCheck(string property)
    {
        CheckBox checkBox = new();
        checkBox.Classes.Add("setting");
        checkBox.Bind(ToggleButton.IsCheckedProperty, ApplicationBinding(property));
        return checkBox;
    }

    private static CheckBox AfterCaptureCheck(string text, AfterCaptureTasks task) =>
        Check(text,
            () => DefaultTaskSettings.AfterCaptureJob.HasFlag(task),
            value => DefaultTaskSettings.AfterCaptureJob = value
                ? DefaultTaskSettings.AfterCaptureJob.Add(task)
                : DefaultTaskSettings.AfterCaptureJob.Remove(task));

    private static Button ActionButton(string text, Action action)
    {
        Button button = new() { Content = text, HorizontalAlignment = HorizontalAlignment.Left };
        button.Classes.Add("compact");
        button.Click += (_, _) => action();
        return button;
    }

    private async void BrowseScreenshotsFolder()
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.ApplicationSettingsWindow_ChooseScreenshotsFolderPath,
            AllowMultiple = false
        });

        string? path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            _application.CustomScreenshotsPath = path;
            _application.UseCustomScreenshotsPath = true;
        }
    }

    private EnumOption<string> GetThemeOption()
    {
        if (_application.UseSystemTheme)
        {
            return _themeOptions[^1];
        }

        return _themeOptions.FirstOrDefault(x => x.Value == _application.SelectedTheme?.Value) ?? _themeOptions[0];
    }

    private void SetThemeOption(EnumOption<string> option)
    {
        if (option.Value == SystemTheme)
        {
            _application.UseSystemTheme = true;
            return;
        }

        _application.UseSystemTheme = false;
        _application.SelectedTheme = option;
    }

    private void OnNavigationPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != SettingsNavigation.SelectedItemProperty)
        {
            return;
        }

        string? selectedId = Navigation.SelectedItem?.Id;
        foreach ((string id, Control page) in _pages)
        {
            page.IsVisible = id == selectedId;
        }
    }

    private void OnThemeChanged(object? sender, ThemeVariant theme) =>
        Dispatcher.UIThread.Post(() =>
        {
            RequestedThemeVariant = theme;
            _theme.Value = GetThemeOption();
        });

    private void OnClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        Navigation.PropertyChanged -= OnNavigationPropertyChanged;

        if (_hotkeys != null)
        {
            HotkeySettingsIntegration.ReleaseViewModel();
        }

        _application.Dispose();
    }
}
