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
using System.Linq;
using DrawingPoint = System.Drawing.Point;

namespace ShareX;

public partial class ActionsToolbarWindow : Window
{
    private const int DefaultTopMargin = 8;

    private bool _positionReady;
    private bool _adjustingPosition;
    private bool _closing;

    public ActionsToolbarWindow()
    {
        InitializeComponent();
        RequestedThemeVariant = ThemeManager.GetCurrentTheme();
        Topmost = ApplicationState.Settings.ActionsToolbarStayTopMost;
        ApplicationState.Settings.CapturePanelActions ??= [];

        ToolTip.SetTip(Grip, Strings.ActionsToolbarWindow_Tip);
        ToolbarFrame.ContextMenu = CreateToolbarMenu();
        UpdateGripCursor();
        RefreshToolbar();

        Opened += OnOpened;
        PositionChanged += OnPositionChanged;
        Closed += (_, _) => _closing = true;
    }

    internal void RefreshToolbar()
    {
        ActionItems.Children.Clear();
        bool primaryAssigned = false;

        foreach (HotkeyType action in ApplicationState.Settings.CapturePanelActions)
        {
            if (action == HotkeyType.None)
            {
                Border separator = new();
                separator.Classes.Add("panel-separator");
                ActionItems.Children.Add(separator);
                continue;
            }

            TextBlock icon = new() { Text = TaskHelpers.FindMenuLucideIcon(action) };
            icon.Classes.Add("icon");
            icon.Classes.Add("panel-icon");

            string label = GetShortLabel(action);
            TextBlock text = new() { Text = label };
            text.Classes.Add("panel-label");

            Button button = new()
            {
                Content = new StackPanel { Spacing = 2, Children = { icon, text } },
                Tag = action
            };
            button.Classes.Add("panel-button");
            if (!primaryAssigned)
            {
                button.Classes.Add("primary");
                primaryAssigned = true;
            }

            AutomationProperties.SetName(button, label);
            ToolTip.SetTip(button, GetActionTip(action));
            button.Click += OnActionClick;
            ActionItems.Children.Add(button);
        }

        bool hasPresets = new MainMenuBuilder(trayMenu: true).BuildWorkflowsMenu(onlyEdited: true).Count > 0;
        PresetsSeparator.IsVisible = hasPresets;
        PresetsButton.IsVisible = hasPresets;

        Dispatcher.UIThread.Post(ClampAndSavePosition, DispatcherPriority.Loaded);
    }

    private static string GetShortLabel(HotkeyType action) => action switch
    {
        HotkeyType.RectangleRegion => Strings.ActionsToolbarWindow_ActionRegion,
        HotkeyType.PrintScreen => Strings.ActionsToolbarWindow_ActionFullScreen,
        HotkeyType.ScrollingCapture => Strings.ActionsToolbarWindow_ActionScrolling,
        HotkeyType.ScreenRecorder => Strings.ActionsToolbarWindow_ActionVideo,
        HotkeyType.ScreenRecorderGIF => Strings.ActionsToolbarWindow_ActionGIF,
        HotkeyType.ActiveWindow => Strings.ActionsToolbarWindow_ActionWindow,
        HotkeyType.ActiveMonitor => Strings.ActionsToolbarWindow_ActionMonitor,
        HotkeyType.OCR => Strings.ActionsToolbarWindow_ActionOCR,
        HotkeyType.ScreenColorPicker => Strings.ActionsToolbarWindow_ActionColor,
        _ => action.GetLocalizedDescription()
    };

    private static string GetActionTip(HotkeyType action)
    {
        string description = action.GetLocalizedDescription();
        HotkeySettings? hotkey = ApplicationState.HotkeysConfigOrNull?.Hotkeys?
            .FirstOrDefault(x => x.TaskSettings?.Job == action && x.HotkeyInfo.IsValidHotkey);

        return hotkey == null ? description : $"{description} ({hotkey.HotkeyInfo})";
    }

    private async void OnActionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HotkeyType action })
        {
            await TaskHelpers.ExecuteJob(action);
        }
    }

    private void OnPresetsClick(object? sender, RoutedEventArgs e)
    {
        ContextMenu menu = new() { Placement = PlacementMode.BottomEdgeAlignedLeft };

        foreach (MainMenuEntry entry in new MainMenuBuilder(trayMenu: true).BuildWorkflowsMenu(onlyEdited: true))
        {
            TextBlock icon = new() { Text = entry.Icon, FontSize = 16 };
            icon.Classes.Add("icon");
            icon.Classes.Add("accent-menu-icon");

            MenuItem item = new() { Header = entry.Header, Icon = icon };
            item.Classes.Add("compact-menu-item");
            item.Click += (_, _) =>
            {
                // Capture overlays cannot open while the popup still handles the click.
                // Close the menu first, then run the preset on the next dispatcher turn.
                menu.Close();
                Dispatcher.UIThread.Post(async () =>
                {
                    try
                    {
                        if (entry.ExecuteAsync != null)
                        {
                            await entry.ExecuteAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugHelper.WriteException(ex);
                    }
                }, DispatcherPriority.Background);
            };
            menu.Items.Add(item);
        }

        menu.Open(PresetsButton);
    }

    private void OnLibraryClick(object? sender, RoutedEventArgs e) => MainWindowIntegration.Activate();

    private void OnSettingsClick(object? sender, RoutedEventArgs e) => ApplicationSettingsIntegration.Show();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private ContextMenu CreateToolbarMenu()
    {
        ContextMenu menu = new()
        {
            Cursor = new Cursor(StandardCursorType.Arrow)
        };

        MenuItem close = new() { Header = Strings.ActionsToolbarWindow_Close };
        close.Click += (_, _) => Close();
        menu.Items.Add(close);
        menu.Items.Add(new Separator());

        MenuItem lockPosition = new()
        {
            Header = Strings.ActionsToolbarWindow_LockPosition,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = ApplicationState.Settings.ActionsToolbarLockPosition
        };
        lockPosition.Click += (_, _) =>
        {
            ApplicationState.Settings.ActionsToolbarLockPosition = lockPosition.IsChecked;
            UpdateGripCursor();
            SaveSettings();
        };
        menu.Items.Add(lockPosition);

        MenuItem stayTopmost = new()
        {
            Header = Strings.ActionsToolbarWindow_StayOnTop,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = ApplicationState.Settings.ActionsToolbarStayTopMost
        };
        stayTopmost.Click += (_, _) =>
        {
            ApplicationState.Settings.ActionsToolbarStayTopMost = stayTopmost.IsChecked;
            Topmost = stayTopmost.IsChecked;
            SaveSettings();
        };
        menu.Items.Add(stayTopmost);

        MenuItem runAtStartup = new()
        {
            Header = Strings.ActionsToolbarWindow_OpenAtStartup,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = ApplicationState.Settings.CapturePanelRunAtStartup
        };
        runAtStartup.Click += (_, _) =>
        {
            ApplicationState.Settings.CapturePanelRunAtStartup = runAtStartup.IsChecked;
            SaveSettings();
        };
        menu.Items.Add(runAtStartup);
        menu.Items.Add(new Separator());

        MenuItem edit = new() { Header = Strings.ActionsToolbarWindow_Edit };
        edit.Click += async (_, _) => await ShowEditorAsync();
        menu.Items.Add(edit);

        return menu;
    }

    private async System.Threading.Tasks.Task ShowEditorAsync()
    {
        bool restoreTopmost = ApplicationState.Settings.ActionsToolbarStayTopMost;
        Topmost = false;

        try
        {
            ActionsToolbarEditorWindow editor = new(RefreshToolbar);
            await editor.ShowDialog(this);
        }
        finally
        {
            if (!_closing)
            {
                Topmost = restoreTopmost;
                RefreshToolbar();
            }
        }
    }

    // The whole panel background drags the panel. Buttons keep their own clicks.
    private void OnPanelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) != null)
        {
            return;
        }

        PointerUpdateKind kind = e.GetCurrentPoint(ToolbarFrame).Properties.PointerUpdateKind;
        if (kind == PointerUpdateKind.LeftButtonPressed && !ApplicationState.Settings.ActionsToolbarLockPosition)
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
        else if (kind == PointerUpdateKind.MiddleButtonPressed)
        {
            Close();
            e.Handled = true;
        }
    }

    private void UpdateGripCursor()
    {
        Grip.Cursor = new Cursor(ApplicationState.Settings.ActionsToolbarLockPosition
            ? StandardCursorType.Arrow
            : StandardCursorType.SizeAll);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // A capture hides the panel and then shows the panel again. Only the first show places and activates the panel.
        if (_positionReady)
        {
            return;
        }

        RestorePosition();
        _positionReady = true;
        ClampAndSavePosition();
        Activate();
    }

    private void RestorePosition()
    {
        DrawingPoint saved = ApplicationState.Settings.ActionsToolbarPosition;
        if (!saved.IsEmpty)
        {
            PixelPoint point = new(saved.X, saved.Y);
            if (Screens.All.Any(screen => screen.WorkingArea.Contains(point)))
            {
                Position = point;
                return;
            }
        }

        Screen? screen = Screens.Primary;
        if (screen == null)
        {
            return;
        }

        PixelRect area = screen.WorkingArea;
        PixelSize size = PixelSize.FromSize(ClientSize, screen.Scaling);
        Position = new PixelPoint(
            area.X + (area.Width - size.Width) / 2,
            area.Y + (int)Math.Round(DefaultTopMargin * screen.Scaling));
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (_positionReady)
        {
            ClampAndSavePosition();
        }
    }

    private void ClampAndSavePosition()
    {
        if (!_positionReady || _adjustingPosition)
        {
            return;
        }

        Screen? screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
        if (screen == null)
        {
            return;
        }

        PixelRect area = screen.WorkingArea;
        PixelSize size = PixelSize.FromSize(ClientSize, screen.Scaling);
        int maxX = Math.Max(area.X, area.Right - size.Width);
        int maxY = Math.Max(area.Y, area.Bottom - size.Height);
        PixelPoint adjusted = new(
            Math.Clamp(Position.X, area.X, maxX),
            Math.Clamp(Position.Y, area.Y, maxY));

        if (adjusted != Position)
        {
            _adjustingPosition = true;
            Position = adjusted;
            _adjustingPosition = false;
        }

        ApplicationState.Settings.ActionsToolbarPosition = new DrawingPoint(adjusted.X, adjusted.Y);
    }

    private static string[] GetImageFiles(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?
            .Select(x => x.TryGetLocalPath())
            .OfType<string>()
            .Where(FileHelpers.IsImageFile)
            .ToArray() ?? [];

    private void OnDragEnter(object? sender, DragEventArgs e) => UpdateDragState(e);

    private void OnDragOver(object? sender, DragEventArgs e) => UpdateDragState(e);

    private void UpdateDragState(DragEventArgs e)
    {
        bool supported = GetImageFiles(e).Length > 0;
        e.DragEffects = supported ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.IsVisible = supported;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;

        string[] files = GetImageFiles(e);
        e.DragEffects = files.Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        // Open the editors after the drop returns, so the drag source does not wait for the editors.
        Dispatcher.UIThread.Post(() =>
        {
            foreach (string file in files)
            {
                TaskHelpers.AnnotateImageFromFile(file);
            }
        });
    }

    private static void SaveSettings() => SettingManager.SaveApplicationConfigAsync();
}
