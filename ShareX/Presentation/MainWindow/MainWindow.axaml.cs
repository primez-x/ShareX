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
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ShareX.AvaloniaUI.Theming;
using ShareX.HelpersLib;
using ShareX.Localization;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using DrawingBitmap = SkiaSharp.SKBitmap;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;
using MessageBox = ShareX.AvaloniaUI.MessageBox;
using MessageBoxButtons = ShareX.AvaloniaUI.MessageBoxButtons;
using MessageBoxResult = ShareX.AvaloniaUI.DialogResult;

namespace ShareX;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    // Card margin (4 + 4), padding (6 + 6) and border (1 + 1).
    private const double CardChrome = 22;
    private const int PreviewWidth = 600;
    private const int PreviewHeight = 440;

    private readonly ITrayIconService _trayIconService;
    private readonly MainMenuPresenter _menuPresenter;
    private readonly MainMenuBuilder _menuBuilder = new();
    private readonly CaptureLibrary _library = new();
    private readonly DispatcherTimer _searchTimer;
    private readonly DispatcherTimer _relativeTimeTimer;
    private CaptureLibraryFilter _filter = CaptureLibraryFilter.All;
    private DrawingSize _thumbnailSize;
    private ScrollViewer? _scrollViewer;
    private CaptureLibraryItem? _selected;
    private CancellationTokenSource? _previewCancellation;
    private AvaloniaBitmap? _previewBitmap;
    private PointerPressedEventArgs? _dragTrigger;
    private CaptureLibraryItem? _dragItem;
    private Point _dragStart;
    private bool _dragStarted;
    private bool _libraryStarted;
    private bool _allowClose;
    private bool _disposed;

    public AvaloniaBitmap? TitleBarIcon { get; private set; }
    public bool IsWindowMaximized => WindowState == Avalonia.Controls.WindowState.Maximized;
    public bool IsDarkTheme => ThemeManager.IsDarkTheme;
    public string ThemeToggleText => IsDarkTheme
        ? Strings.ApplicationSettingsWindow_Dark
        : Strings.ApplicationSettingsWindow_Light;
    public string NewCaptureTip => ActionsToolbarWindowIntegration.GetActionTip(HotkeyType.RectangleRegion);

    public MainWindow() : this(MainWindowIntegration.TrayIconService, MainWindowIntegration.MenuPresenter)
    {
    }

    internal MainWindow(ITrayIconService trayIconService, MainMenuPresenter menuPresenter)
    {
        _trayIconService = trayIconService;
        _menuPresenter = menuPresenter;

        InitializeComponent();
        RequestedThemeVariant = ThemeManager.GetCurrentTheme();
        Title = ApplicationInfo.Title;
        UpdateTitleBarIcon();
        DataContext = this;
        RestoreWindowBounds();

        CaptureRows.ItemsSource = _library.Gallery.Rows;
        CaptureRows.AddHandler(PointerPressedEvent, OnRowsPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        CaptureRows.AddHandler(PointerMovedEvent, OnRowsPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        CaptureRows.AddHandler(PointerReleasedEvent, OnRowsPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        CaptureRows.AddHandler(KeyDownEvent, OnRowsKeyDown, RoutingStrategies.Tunnel);
        _library.ItemsChanged += OnLibraryItemsChanged;

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            ReloadLibrary();
        };

        _relativeTimeTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _relativeTimeTimer.Tick += (_, _) =>
        {
            foreach (CaptureLibraryItem item in _library.Items) item.RefreshTime();
        };

        ThemeManager.ThemeChanged += OnThemeChanged;
        Opened += OnOpened;
        Closing += OnClosing;
        PositionChanged += (_, _) => SaveWindowBounds();
        Resized += (_, _) => SaveWindowBounds();
        KeyDown += OnWindowKeyDown;
        UpdatePreview();
    }

    public void ShowAndActivate()
    {
        if (_menuPresenter.IsTrayMenuOpen)
        {
            _menuPresenter.CloseAllMenus();
            Dispatcher.UIThread.Post(ShowAndActivate);
            return;
        }

        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == Avalonia.Controls.WindowState.Minimized)
        {
            WindowState = Avalonia.Controls.WindowState.Normal;
        }

        Activate();
        MainWindowIntegration.ReportVisibility(true);
    }

    public void HideToTray()
    {
        SaveWindowBounds();
        Hide();
        MainWindowIntegration.ReportVisibility(false);
    }

    public void SetTitle(string title)
    {
        Title = title;
    }

    /// <summary>
    /// Runs after menu commands and setting changes. Updates the toolbar, the empty state and the preview actions.
    /// The library data updates through history and task events, so the library loads again only when the thumbnail size changed.
    /// </summary>
    public void RefreshMenus()
    {
        RequestedThemeVariant = ThemeManager.GetCurrentTheme();
        OnPropertyChanged(nameof(NewCaptureTip));
        UpdateEmptyState();
        UpdatePreviewActions();

        if (_libraryStarted && GetThumbnailSize() != _thumbnailSize)
        {
            ReloadLibrary();
        }
    }

    public void CloseFromHost()
    {
        if (_disposed)
        {
            return;
        }

        _allowClose = true;
        Close();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _scrollViewer = CaptureRows.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (_scrollViewer != null)
            {
                _scrollViewer.ScrollChanged += OnScrollChanged;
            }
            UpdateColumns();
        }, DispatcherPriority.Loaded);

        _libraryStarted = true;
        _relativeTimeTimer.Start();
        ReloadLibrary();
    }

    #region Title bar

    private void OnThemeToggleClick(object? sender, RoutedEventArgs e)
    {
        string theme = ThemeManager.IsDarkTheme ? "Light" : "Dark";
        ApplicationThemeOptions options = ApplicationState.Settings.ThemeOptions;
        options.Theme = theme;
        options.UseSystemTheme = false;
        SettingManager.SaveApplicationConfigAsync();
    }

    private void OnTopmostClick(object? sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        TaskHelpers.PlayNotificationSoundAsync(NotificationSound.ActionCompleted);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = Avalonia.Controls.WindowState.Minimized;
    }

    private void OnMaximizeRestoreClick(object? sender, RoutedEventArgs e)
    {
        WindowState = IsWindowMaximized
            ? Avalonia.Controls.WindowState.Normal
            : Avalonia.Controls.WindowState.Maximized;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    #endregion Title bar

    #region Toolbar

    private void OnNewCaptureClick(object? sender, RoutedEventArgs e) => RunCaptureAction(HotkeyType.RectangleRegion);

    private static void RunCaptureAction(HotkeyType action) => _ = TaskHelpers.ExecuteJob(action, autoHideMainWindow: true);

    private void OnNewCaptureMenuClick(object? sender, RoutedEventArgs e)
    {
        List<MainMenuEntry> entries = [];

        foreach (HotkeyType action in ApplicationState.Settings.CapturePanelActions ?? [])
        {
            if (action == HotkeyType.None)
            {
                if (entries.Count > 0 && !entries[^1].IsSeparator) entries.Add(MainMenuEntry.Separator());
                continue;
            }

            HotkeyType selectedAction = action;
            entries.Add(new MainMenuEntry(ActionsToolbarWindowIntegration.GetActionLabel(selectedAction),
                TaskHelpers.FindMenuLucideIcon(selectedAction), () => RunCaptureAction(selectedAction)));
        }

        if (entries.Count > 0 && !entries[^1].IsSeparator) entries.Add(MainMenuEntry.Separator());
        entries.Add(new MainMenuEntry(Strings.MainWindow_ShowCapturePanel, LucideIcons.panel_top, ActionsToolbarWindowIntegration.Show));

        _menuPresenter.TryOpenContextMenu(MainMenuPresenter.BuildContextMenu(entries), NewCaptureMenuButton,
            PlacementMode.BottomEdgeAlignedLeft);
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_libraryStarted) return;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void OnFilterClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: CaptureLibraryFilter filter })
        {
            return;
        }

        foreach (ToggleButton chip in FilterPanel.Children.OfType<ToggleButton>())
        {
            chip.IsChecked = Equals(chip.Tag, filter);
        }

        if (_filter != filter)
        {
            _filter = filter;
            ReloadLibrary();
        }
    }

    private void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        _menuPresenter.TryOpenContextMenu(MainMenuPresenter.BuildContextMenu(_menuBuilder.BuildMoreMenu()), MoreButton,
            PlacementMode.BottomEdgeAlignedRight);
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e) => SettingsIntegration.Show();

    #endregion Toolbar

    #region Library

    private static DrawingSize GetThumbnailSize()
    {
        DrawingSize size = ApplicationState.Settings.ThumbnailSize;
        return new DrawingSize(Math.Max(96, size.Width), Math.Max(72, size.Height));
    }

    internal void ReloadLibrary()
    {
        if (_disposed || !_libraryStarted) return;

        Select(null);
        _thumbnailSize = GetThumbnailSize();
        UpdateColumns();
        if (_scrollViewer != null) _scrollViewer.Offset = default;
        _ = _library.ReloadAsync(SearchTextBox.Text ?? string.Empty, _filter, _thumbnailSize.Width, _thumbnailSize.Height);
    }

    private void OnLibraryItemsChanged()
    {
        if (_selected != null && _library.Gallery.IndexOf(_selected) < 0)
        {
            Select(null);
        }
        else if (_selected?.IsTask == true)
        {
            UpdatePreviewText(_selected);
            UpdatePreviewActions();
        }

        UpdateEmptyState();
        Dispatcher.UIThread.Post(LoadMoreIfViewportNotFilled, DispatcherPriority.Background);
    }

    private void UpdateEmptyState()
    {
        bool empty = _library.IsLoaded && _library.Items.Count == 0;
        EmptyState.IsVisible = empty;
        if (!empty) return;

        bool filtered = _library.HasSearchOrFilter;
        EmptyStateTitle.Text = filtered ? Strings.MainWindow_NoMatchingCaptures : Strings.MainWindow_NoCapturesYet;
        EmptyStateHint.IsVisible = !filtered;
        EmptyStateCaptureButton.IsVisible = !filtered;

        string? hotkey = ActionsToolbarWindowIntegration.GetHotkeyText(HotkeyType.RectangleRegion);
        EmptyStateHint.Text = hotkey == null
            ? Strings.MainWindow_CaptureHint
            : string.Format(Strings.MainWindow_CaptureHintHotkey, hotkey);
    }

    private void OnCaptureRowsSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateColumns();

    private void UpdateColumns()
    {
        // The list padding (6 + 6) and the vertical scroll bar take part of the width.
        double availableWidth = CaptureRows.Bounds.Width - 12 - 14;
        double cardWidth = GetThumbnailSize().Width + CardChrome;
        _library.Gallery.UpdateColumns(availableWidth, cardWidth);

        // Center the columns, so the space left over after the last column does not collect on the right side.
        double leftover = Math.Max(0, availableWidth - _library.Gallery.Columns * cardWidth);
        CaptureRows.Padding = new Thickness(6 + Math.Floor(leftover / 2), 6, 6, 6);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scrollViewer == null) return;
        double remaining = _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height - _scrollViewer.Offset.Y;
        if (remaining <= Math.Max(300, _thumbnailSize.Height * 2)) _ = _library.LoadMoreAsync();
    }

    private void LoadMoreIfViewportNotFilled()
    {
        if (_scrollViewer == null || !_library.HasMore) return;
        if (_scrollViewer.Extent.Height <= _scrollViewer.Viewport.Height + 1) _ = _library.LoadMoreAsync();
    }

    private async void OnCardAttached(object? sender, VisualTreeAttachmentEventArgs e) =>
        await _library.Gallery.AttachAsync(sender as Control);

    private void OnCardDetached(object? sender, VisualTreeAttachmentEventArgs e) => _library.Gallery.Detach(sender as Control);

    private async void OnCardDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is Control control && control.IsAttachedToVisualTree())
        {
            _library.Gallery.Detach(control);
            await _library.Gallery.AttachAsync(control);
        }
    }

    private void OnCardClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CaptureLibraryItem item })
        {
            Select(item);
        }
    }

    private static CaptureLibraryItem? GetItem(object? source) =>
        (source as Visual)?.FindAncestorOfType<Button>(includeSelf: true)?.DataContext as CaptureLibraryItem;

    private void OnRowsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<ScrollBar>(includeSelf: true) != null)
        {
            return;
        }

        CaptureLibraryItem? item = GetItem(e.Source);
        PointerPoint point = e.GetCurrentPoint(CaptureRows);

        if (point.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            _menuPresenter.CloseActiveContextMenu();
            if (item != null) Select(item);
            return;
        }

        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }

        ResetDrag();
        Select(item);

        if (item == null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            Dispatcher.UIThread.Post(() => RunDefaultAction(item));
            return;
        }

        _dragTrigger = e;
        _dragItem = item;
        _dragStart = point.Position;
    }

    private async void OnRowsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragTrigger == null || _dragItem == null || _dragStarted ||
            !e.GetCurrentPoint(CaptureRows).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Point position = e.GetPosition(CaptureRows);
        Size dragSize = Application.Current?.PlatformSettings?.GetTapSize(PointerType.Mouse) ?? new Size(4, 4);
        if (Math.Abs(position.X - _dragStart.X) < dragSize.Width / 2d &&
            Math.Abs(position.Y - _dragStart.Y) < dragSize.Height / 2d)
        {
            return;
        }

        if (!TryGetExistingFile(_dragItem, out string filePath))
        {
            ResetDrag();
            return;
        }

        _dragStarted = true;
        PointerPressedEventArgs trigger = _dragTrigger;
        e.Pointer.Capture(null);

        try
        {
            IStorageFile? file = await StorageProvider.TryGetFileFromPathAsync(filePath);
            if (file != null)
            {
                DataTransfer data = new();
                data.Add(DataTransferItem.CreateFile(file));
                await DragDrop.DoDragDropAsync(trigger, data, DragDropEffects.Copy);
            }
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex);
        }
        finally
        {
            ResetDrag();
        }
    }

    private void OnRowsPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        PointerUpdateKind kind = e.GetCurrentPoint(CaptureRows).Properties.PointerUpdateKind;

        if (kind == PointerUpdateKind.LeftButtonReleased && !_dragStarted)
        {
            ResetDrag();
        }
        else if (kind == PointerUpdateKind.RightButtonReleased && GetItem(e.Source) is { } item &&
            (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is Button card)
        {
            _menuPresenter.TryOpenContextMenu(MainMenuPresenter.BuildContextMenu(BuildItemMenu(item)), card, PlacementMode.Pointer);
            e.Handled = true;
        }
    }

    private void ResetDrag()
    {
        _dragTrigger = null;
        _dragItem = null;
        _dragStarted = false;
    }

    private void OnRowsKeyDown(object? sender, KeyEventArgs e)
    {
        CaptureLibraryItem? item = _selected;
        int count = _library.Items.Count;

        if (count > 0 && e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)
        {
            int current = item == null ? -1 : _library.Gallery.IndexOf(item);
            int columns = _library.Gallery.Columns;
            int next = e.Key switch
            {
                Key.Left => current - 1,
                Key.Right => current + 1,
                Key.Up => current - columns,
                Key.Down => current + columns,
                Key.Home => 0,
                _ => count - 1
            };
            SelectAndFocus(_library.Items[Math.Clamp(current < 0 ? 0 : next, 0, count - 1)]);
        }
        else if (item != null && e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None) RunDefaultAction(item);
        else if (item != null && e.Key == Key.Delete) DeleteItem(item);
        else if (item != null && e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control) CopyItem(item);
        else if (item != null && e.Key == Key.C && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)) CopyFilePath(item);
        else if (item != null && e.Key == Key.E && e.KeyModifiers == KeyModifiers.Control) EditItem(item);
        else return;

        e.Handled = true;
    }

    private void SelectAndFocus(CaptureLibraryItem item)
    {
        Select(item);
        int index = _library.Gallery.IndexOf(item);
        CaptureRows.ScrollIntoView(index / _library.Gallery.Columns);

        Dispatcher.UIThread.Post(() =>
        {
            CaptureRows.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(x => ReferenceEquals(x.DataContext, item))?
                .Focus(NavigationMethod.Directional);
        }, DispatcherPriority.Loaded);
    }

    private void Select(CaptureLibraryItem? item)
    {
        if (ReferenceEquals(_selected, item)) return;

        if (_selected != null) _selected.IsSelected = false;
        _selected = item;
        if (item != null) item.IsSelected = true;
        UpdatePreview();
    }

    #endregion Library

    #region Preview and actions

    private void UpdatePreview()
    {
        CaptureLibraryItem? item = _selected;
        _previewCancellation?.Cancel();
        _previewCancellation = null;
        SetPreviewBitmap(null);

        PreviewHint.IsVisible = item == null;
        PreviewPanel.IsVisible = item != null;
        if (item == null) return;

        PreviewPlaceholder.Text = item.PlaceholderIcon;
        PreviewDimensionsLabel.IsVisible = false;
        PreviewDimensions.IsVisible = false;
        UpdatePreviewText(item);
        UpdatePreviewActions();

        if (TryGetExistingFile(item, out string filePath))
        {
            CancellationTokenSource cancellation = new();
            _previewCancellation = cancellation;
            _ = LoadPreviewAsync(item, filePath, cancellation.Token);
        }
    }

    private void UpdatePreviewText(CaptureLibraryItem item)
    {
        string? filePath = item.FilePath;
        bool fileExists = TryGetExistingFile(item, out _);

        PreviewName.Text = item.FileName;
        PreviewStatus.Text = item.Subtitle;
        PreviewStatus.IsVisible = item.IsTask;
        PreviewDate.Text = item.DateTime == DateTime.MinValue ? string.Empty : item.DateTime.ToString("G");
        PreviewFolder.Text = string.IsNullOrEmpty(filePath) ? string.Empty : Path.GetDirectoryName(filePath);
        PreviewSize.Text = fileExists ? FileHelpers.GetFileSizeReadable(filePath!, ApplicationState.Settings.BinaryUnits) : string.Empty;
        PreviewSizeLabel.IsVisible = PreviewSize.IsVisible = !string.IsNullOrEmpty(PreviewSize.Text);
    }

    private static bool HasErrors(CaptureLibraryItem? item) =>
        item?.Task?.Status == TaskStatus.Failed && item.Task.Info?.Result?.IsError == true;

    private void OnShowErrorsClick(object? sender, RoutedEventArgs e) => _selected?.Task?.ShowErrorWindow();

    private void UpdatePreviewActions()
    {
        CaptureLibraryItem? item = _selected;
        bool fileExists = item != null && TryGetExistingFile(item, out _);
        bool isWorking = item?.Task?.IsWorking == true;

        // A failed upload shows the way to the error details first, so the abnormal state is obvious.
        ShowErrorsButton.IsVisible = HasErrors(item);
        EditButton.IsVisible = fileExists && item!.IsImage && !isWorking;
        OpenButton.IsEnabled = fileExists;
        CopyButton.IsEnabled = fileExists;
        CopyFilePathButton.IsEnabled = !string.IsNullOrEmpty(item?.FilePath);
        ShowInFolderButton.IsEnabled = fileExists;
        ShareButton.IsVisible = fileExists && !isWorking && CanShare(item!.FilePath!);
        DeleteButton.IsEnabled = fileExists && !isWorking;
    }

    private async Task LoadPreviewAsync(CaptureLibraryItem item, string filePath, CancellationToken token)
    {
        try
        {
            AvaloniaBitmap? bitmap = await _library.Gallery.Loader.LoadAsync(filePath, PreviewWidth, PreviewHeight, token);
            if (token.IsCancellationRequested || !ReferenceEquals(_selected, item))
            {
                bitmap?.Dispose();
                return;
            }

            SetPreviewBitmap(bitmap);

            if (item.IsImage)
            {
                DrawingSize size = await Task.Run(() => SkiaImageHelpers.GetImageFileDimensions(filePath), token);
                if (!token.IsCancellationRequested && !size.IsEmpty)
                {
                    PreviewDimensions.Text = $"{size.Width} × {size.Height}";
                    PreviewDimensionsLabel.IsVisible = PreviewDimensions.IsVisible = true;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A new selection cancels the preview of the previous selection.
        }
        catch (Exception e)
        {
            DebugHelper.WriteException(e);
        }
    }

    private void SetPreviewBitmap(AvaloniaBitmap? bitmap)
    {
        AvaloniaBitmap? previous = _previewBitmap;
        _previewBitmap = bitmap;
        PreviewImage.Source = bitmap;
        PreviewPlaceholder.IsVisible = bitmap == null;
        previous?.Dispose();
    }

    private static bool TryGetExistingFile(CaptureLibraryItem item, out string filePath)
    {
        filePath = item.FilePath ?? string.Empty;
        return filePath.Length > 0 && File.Exists(filePath);
    }

    private static bool CanShare(string filePath)
    {
        TaskSettings taskSettings = ApplicationState.DefaultTaskSettings;
        return TaskHelpers.IsShareAvailable(taskSettings.GetFileDestinationByDataType(TaskHelpers.FindDataType(filePath, taskSettings)));
    }

    private IReadOnlyList<MainMenuEntry> BuildItemMenu(CaptureLibraryItem item)
    {
        bool fileExists = TryGetExistingFile(item, out string filePath);
        WorkerTask? task = item.Task;
        bool isWorking = task?.IsWorking == true;

        return
        [
            new(Strings.MainWindow_Edit, LucideIcons.pencil, () => EditItem(item),
                isVisible: fileExists && item.IsImage && !isWorking, inputGesture: new KeyGesture(Key.E, KeyModifiers.Control)),
            new(Strings.MainWindow_Open, LucideIcons.external_link, () => OpenItem(item), isVisible: fileExists),
            new(Strings.MainWindow_Copy, LucideIcons.copy, () => CopyItem(item), isVisible: fileExists,
                inputGesture: new KeyGesture(Key.C, KeyModifiers.Control)),
            new(Strings.MainWindow_CopyFilePath, LucideIcons.route, () => CopyFilePath(item), isVisible: filePath.Length > 0,
                inputGesture: new KeyGesture(Key.C, KeyModifiers.Control | KeyModifiers.Shift)),
            new(Strings.MainWindow_ShowInFolder, LucideIcons.folder_open, () => ShowInFolder(item), isVisible: fileExists),
            new(Strings.MainWindow_Share, LucideIcons.share_2, () => ShareItem(item),
                isVisible: fileExists && !isWorking && CanShare(filePath)),
            MainMenuEntry.Separator(),
            new(Strings.MainWindow_StopUpload, LucideIcons.circle_stop, () => task?.Stop(), isVisible: isWorking),
            new(Strings.MainWindow_ShowErrors, LucideIcons.triangle_alert, () => task?.ShowErrorWindow(),
                isVisible: HasErrors(item)),
            new(Strings.MainWindow_RemoveFromList, LucideIcons.list_x, () => TaskManager.Remove(task),
                isVisible: task != null && !isWorking),
            MainMenuEntry.Separator(),
            new(Strings.MainWindow_Delete, LucideIcons.trash_2, () => DeleteItem(item), isVisible: fileExists && !isWorking,
                inputGesture: new KeyGesture(Key.Delete))
        ];
    }

    private void RunDefaultAction(CaptureLibraryItem item)
    {
        if (item.IsImage && item.Task?.IsWorking != true) EditItem(item);
        else OpenItem(item);
    }

    private void OnEditClick(object? sender, RoutedEventArgs e) => WithSelected(EditItem);
    private void OnOpenClick(object? sender, RoutedEventArgs e) => WithSelected(OpenItem);
    private void OnCopyClick(object? sender, RoutedEventArgs e) => WithSelected(CopyItem);
    private void OnCopyFilePathClick(object? sender, RoutedEventArgs e) => WithSelected(CopyFilePath);
    private void OnShowInFolderClick(object? sender, RoutedEventArgs e) => WithSelected(ShowInFolder);
    private void OnShareClick(object? sender, RoutedEventArgs e) => WithSelected(ShareItem);
    private void OnDeleteClick(object? sender, RoutedEventArgs e) => WithSelected(DeleteItem);

    private void WithSelected(Action<CaptureLibraryItem> action)
    {
        if (_selected != null) action(_selected);
    }

    private static void EditItem(CaptureLibraryItem item)
    {
        if (TryGetExistingFile(item, out string filePath) && FileHelpers.IsImageFile(filePath))
        {
            TaskHelpers.AnnotateImageFromFile(filePath);
        }
    }

    private static void OpenItem(CaptureLibraryItem item)
    {
        if (TryGetExistingFile(item, out string filePath)) FileHelpers.OpenFile(filePath);
    }

    private static void CopyItem(CaptureLibraryItem item)
    {
        if (!TryGetExistingFile(item, out string filePath)) return;

        bool copied = FileHelpers.IsImageFile(filePath)
            ? ClipboardHelpers.CopyImageFromFile(filePath)
            : ClipboardHelpers.CopyFile(filePath);

        if (copied) TaskHelpers.PlayNotificationSoundAsync(NotificationSound.ActionCompleted);
    }

    private static void CopyFilePath(CaptureLibraryItem item)
    {
        if (!string.IsNullOrEmpty(item.FilePath)) ClipboardHelpers.CopyText(item.FilePath);
    }

    private static void ShowInFolder(CaptureLibraryItem item)
    {
        if (TryGetExistingFile(item, out string filePath)) FileHelpers.OpenFolderWithFile(filePath);
    }

    private static void ShareItem(CaptureLibraryItem item)
    {
        if (TryGetExistingFile(item, out string filePath) && CanShare(filePath)) UploadManager.UploadFile(filePath);
    }

    private void DeleteItem(CaptureLibraryItem item)
    {
        if (!TryGetExistingFile(item, out string filePath) || item.Task?.IsWorking == true ||
            MessageBox.Show(Strings.MainForm_tsmiDeleteSelectedFile_Click_Do_you_really_want_to_delete_this_file_,
                "ShareX - " + Strings.MainForm_tsmiDeleteSelectedFile_Click_File_delete_confirmation,
                MessageBoxButtons.YesNo) != MessageBoxResult.Yes)
        {
            return;
        }

        FileHelpers.DeleteFile(filePath, true);

        // Only the file is deleted. The history item stays, so upload links and deletion links are kept.
        if (item.History != null)
        {
            _library.RemoveFile(filePath);
        }
        else
        {
            TaskManager.Remove(item.Task);
        }
    }

    #endregion Preview and actions

    #region Window

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SearchTextBox.IsFocused && !string.IsNullOrEmpty(SearchTextBox.Text))
        {
            SearchTextBox.Text = string.Empty;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
        }
        else if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control)
        {
            UploadManager.ClipboardUploadMainWindow();
        }
        else
        {
            return;
        }

        e.Handled = true;
    }

    private void OnDragEnter(object? sender, DragEventArgs e) => UpdateDropOverlay(e);

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        e.Handled = true;
    }

    private void OnDragOver(object? sender, DragEventArgs e) => UpdateDropOverlay(e);

    private void UpdateDropOverlay(DragEventArgs e)
    {
        // A card dragged out of the library must not upload when the card drops back on the window.
        if (_dragStarted)
        {
            DropOverlay.IsVisible = false;
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        bool hasFiles = e.DataTransfer.TryGetFiles()?.Any() == true;
        bool hasText = !string.IsNullOrEmpty(e.DataTransfer.TryGetText());
        bool supported = hasFiles || hasText;

        e.DragEffects = supported ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.IsVisible = supported;

        if (hasFiles)
        {
            DropOverlayTitle.Text = Strings.MainWindow_ReleaseToUploadFiles;
            DropOverlayDescription.Text = Strings.MainWindow_FilesDroppedUploadDescription;
        }
        else if (hasText)
        {
            DropOverlayTitle.Text = Strings.MainWindow_ReleaseToUploadText;
            DropOverlayDescription.Text = Strings.MainWindow_TextDroppedUploadDescription;
        }

        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        e.Handled = true;

        if (_dragStarted)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        UploadManager.DragDropUpload(e.DataTransfer);
    }

    private void OnThemeChanged(object? sender, Avalonia.Styling.ThemeVariant theme) =>
        Dispatcher.UIThread.Post(() =>
        {
            RequestedThemeVariant = theme;
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(ThemeToggleText));
        });

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_allowClose && ApplicationState.Settings.ShowTray)
        {
            e.Cancel = true;
            HideToTray();
            SettingManager.SaveAllSettingsAsync();

            if (ApplicationState.Settings.FirstTimeMinimizeToTray)
            {
                TaskHelpers.ShowNotificationTip(Strings.ShareXIsMinimizedToTheSystemTray, "ShareX", 8000);
                ApplicationState.Settings.FirstTimeMinimizeToTray = false;
            }

            return;
        }

        if (!_allowClose)
        {
            e.Cancel = true;
            Dispatcher.UIThread.Post(ApplicationLifecycle.ForceClose);
            return;
        }

        DisposeWindowResources();
    }

    private void DisposeWindowResources()
    {
        if (_disposed)
        {
            return;
        }

        SaveWindowBounds();
        _disposed = true;
        _searchTimer.Stop();
        _relativeTimeTimer.Stop();
        ThemeManager.ThemeChanged -= OnThemeChanged;
        if (_scrollViewer != null) _scrollViewer.ScrollChanged -= OnScrollChanged;
        _library.ItemsChanged -= OnLibraryItemsChanged;
        _previewCancellation?.Cancel();
        SetPreviewBitmap(null);
        _library.Dispose();
        ResetDrag();
        _menuPresenter.CloseAllMenus();
        _trayIconService.Visible = false;
        TitleBarIcon?.Dispose();
    }

    private void RestoreWindowBounds()
    {
        Screen? screen = Screens.Primary;

        DrawingPoint savedPosition = ApplicationState.Settings.MainFormPosition;
        PixelPoint? position = null;
        if (ApplicationState.Settings.RememberMainFormPosition && !savedPosition.IsEmpty)
        {
            PixelPoint point = new(savedPosition.X, savedPosition.Y);
            if (Screens.ScreenFromPoint(point) is Screen savedScreen)
            {
                screen = savedScreen;
                position = point;
            }
        }

        DrawingSize savedSize = ApplicationState.Settings.MainFormSize;
        if (ApplicationState.Settings.RememberMainFormSize && !savedSize.IsEmpty)
        {
            Width = savedSize.Width;
            Height = savedSize.Height;
        }

        if (screen != null)
        {
            // The default size and a saved size from a larger monitor must fit the work area.
            Rect workArea = screen.WorkingArea.ToRect(screen.Scaling);
            Width = Math.Max(MinWidth, Math.Min(Width, workArea.Width));
            Height = Math.Max(MinHeight, Math.Min(Height, workArea.Height));
        }

        if (position != null)
        {
            Position = position.Value;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
    }

    private void SaveWindowBounds()
    {
        if (_disposed || WindowState != Avalonia.Controls.WindowState.Normal)
        {
            return;
        }

        ApplicationState.Settings.MainFormPosition = new DrawingPoint(Position.X, Position.Y);
        ApplicationState.Settings.MainFormSize = new DrawingSize((int)Math.Round(ClientSize.Width), (int)Math.Round(ClientSize.Height));
    }

    private void UpdateTitleBarIcon()
    {
        using DrawingBitmap bitmap = SkiaImageHelpers.ByteArrayToBitmap(ShareXResources.IconBytes);
        using MemoryStream stream = new();
        bitmap.Save(stream, SkiaSharp.SKEncodedImageFormat.Png);
        stream.Position = 0;

        AvaloniaBitmap? previous = TitleBarIcon;
        TitleBarIcon = new AvaloniaBitmap(stream);
        OnPropertyChanged(nameof(TitleBarIcon));
        previous?.Dispose();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            OnPropertyChanged(nameof(IsWindowMaximized));
        }
        else if (change.Property == IconProperty)
        {
            UpdateTitleBarIcon();
        }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    #endregion Window
}
