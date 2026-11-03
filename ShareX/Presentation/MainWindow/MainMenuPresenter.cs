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
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ShareX.AvaloniaUI.Theming;
using ShareX.HelpersLib;
using ShareX.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace ShareX;

/// <summary>
/// Builds and shows context menus, grid menus and the tray menu from <see cref="MainMenuEntry"/> lists.
/// Tracks the one open menu and owns the tray menu lifecycle. Independent of any window layout.
/// </summary>
internal sealed class MainMenuPresenter : IDisposable
{
    private readonly ITrayIconService _trayIconService;
    private readonly MainMenuBuilder _trayMenuBuilder = new(trayMenu: true);
    private ContextMenu? _activeContextMenu;
    private Flyout? _activeGridMenu;
    private Window? _trayMenuAnchor;
    private bool _disposed;

    public MainMenuPresenter(ITrayIconService trayIconService)
    {
        _trayIconService = trayIconService;
        _trayIconService.RightButtonDown += OnTrayIconRightButtonDown;
        _trayIconService.RightButtonUp += OnTrayIconRightButtonUp;
    }

    public bool IsTrayMenuOpen => _trayMenuAnchor != null;

    public void ShowTrayMenu()
    {
        if (_trayMenuAnchor != null)
        {
            CloseActiveContextMenu();
            CloseTrayMenuAnchor();
            return;
        }

        CloseActiveContextMenu();

        System.Drawing.Point cursorPosition = CaptureHelpers.GetCursorPosition();
        PixelPoint anchorPosition = new(cursorPosition.X, cursorPosition.Y);
        Window anchor = CreateTrayMenuAnchor(anchorPosition);
        _trayMenuAnchor = anchor;
        anchor.Deactivated += OnTrayMenuAnchorDeactivated;
        anchor.Show();
        anchor.Position = anchorPosition;
        anchor.Activate();

        ContextMenu menu = BuildContextMenu(_trayMenuBuilder.BuildTrayMenu());
        menu.Closed += OnTrayMenuClosed;

        try
        {
            if (!TryOpenContextMenu(menu, anchor, PlacementMode.BottomEdgeAlignedLeft))
            {
                menu.Closed -= OnTrayMenuClosed;
                CloseTrayMenuAnchor();
            }
            else if (ApplicationState.Settings.TrayAutoExpandCaptureMenu && menu.Items.OfType<MenuItem>().FirstOrDefault() is MenuItem captureItem)
            {
                Dispatcher.UIThread.Post(captureItem.Open);
            }
        }
        catch
        {
            menu.Closed -= OnTrayMenuClosed;
            CloseTrayMenuAnchor();
            throw;
        }
    }

    public void WarmUpTrayMenu()
    {
        if (_disposed)
        {
            return;
        }

        Window anchor = CreateTrayMenuAnchor(new PixelPoint(-32000, -32000));
        anchor.ShowActivated = false;
        anchor.Topmost = false;
        ContextMenu menu = BuildContextMenu(_trayMenuBuilder.BuildTrayMenu());
        menu.Opacity = 0;
        menu.IsHitTestVisible = false;

        try
        {
            anchor.Show();
            menu.Open(anchor);

            // Leave the invisible popup attached for one layout pass so Avalonia
            // creates the popup host, templates and styles before the first click.
            Dispatcher.UIThread.Post(() =>
            {
                menu.Close();

                if (anchor.IsVisible)
                {
                    anchor.Close();
                }
            }, DispatcherPriority.Loaded);
        }
        catch (Exception e)
        {
            DebugHelper.WriteException(e);

            if (anchor.IsVisible)
            {
                anchor.Close();
            }
        }
    }

    public static ContextMenu BuildContextMenu(IEnumerable<MainMenuEntry> entries)
    {
        ContextMenu menu = new();

        foreach (Control item in BuildMenuControls(entries, menu))
        {
            menu.Items.Add(item);
        }

        return menu;
    }

    public static Flyout BuildGridMenu(IEnumerable<MainMenuCategory> categories)
    {
        Flyout flyout = new()
        {
            Placement = PlacementMode.RightEdgeAlignedTop
        };
        flyout.FlyoutPresenterClasses.Add("tool-grid-flyout");
        flyout.Content = BuildGridMenuContent(categories, entry => ExecuteGridMenuEntry(flyout, entry));
        return flyout;
    }

    public bool TryOpenContextMenu(ContextMenu menu, Control placementTarget, PlacementMode placement)
    {
        if (_activeContextMenu != null || _activeGridMenu != null)
        {
            return false;
        }

        _activeContextMenu = menu;
        menu.Placement = placement;
        menu.Closed += OnActiveContextMenuClosed;

        try
        {
            menu.Open(placementTarget);
            return true;
        }
        catch
        {
            menu.Closed -= OnActiveContextMenuClosed;
            _activeContextMenu = null;
            throw;
        }
    }

    public bool TryOpenGridMenu(Flyout flyout, Control placementTarget)
    {
        if (_activeContextMenu != null || _activeGridMenu != null)
        {
            return false;
        }

        _activeGridMenu = flyout;
        flyout.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeGridMenu, flyout))
            {
                _activeGridMenu = null;
            }
        };

        try
        {
            flyout.ShowAt(placementTarget);
            return true;
        }
        catch
        {
            _activeGridMenu = null;
            throw;
        }
    }

    public void CloseActiveContextMenu()
    {
        if (_activeGridMenu != null)
        {
            Flyout flyout = _activeGridMenu;
            _activeGridMenu = null;
            flyout.Hide();
        }

        if (_activeContextMenu == null)
        {
            return;
        }

        ContextMenu menu = _activeContextMenu;
        _activeContextMenu = null;
        menu.Closed -= OnActiveContextMenuClosed;
        menu.Close();
    }

    public void CloseAllMenus()
    {
        CloseActiveContextMenu();
        CloseTrayMenuAnchor();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CloseAllMenus();
        _trayIconService.RightButtonDown -= OnTrayIconRightButtonDown;
        _trayIconService.RightButtonUp -= OnTrayIconRightButtonUp;
    }

    internal static TextBlock CreateAccentMenuIcon(string glyph, double size)
    {
        TextBlock icon = CreateLucideText(glyph, size);
        icon.Classes.Add("accent-menu-icon");
        return icon;
    }

    private void OnTrayIconRightButtonDown()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
            {
                CloseAllMenus();
            }
        });
    }

    private void OnTrayIconRightButtonUp()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
            {
                ShowTrayMenu();
            }
        });
    }

    private void OnTrayMenuClosed(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            menu.Closed -= OnTrayMenuClosed;
        }

        CloseTrayMenuAnchor();
        SettingManager.SaveAllSettingsAsync();
    }

    private void OnTrayMenuAnchorDeactivated(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _trayMenuAnchor))
        {
            CloseActiveContextMenu();
            CloseTrayMenuAnchor();
        }
    }

    private void CloseTrayMenuAnchor()
    {
        Window? anchor = _trayMenuAnchor;
        _trayMenuAnchor = null;

        if (anchor != null)
        {
            anchor.Deactivated -= OnTrayMenuAnchorDeactivated;

            if (anchor.IsVisible)
            {
                anchor.Close();
            }
        }
    }

    private void OnActiveContextMenuClosed(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            menu.Closed -= OnActiveContextMenuClosed;

            if (ReferenceEquals(_activeContextMenu, menu))
            {
                _activeContextMenu = null;
            }
        }
    }

    private static Window CreateTrayMenuAnchor(PixelPoint position) => new()
    {
        Width = 1,
        Height = 1,
        MinWidth = 1,
        MinHeight = 1,
        Position = position,
        WindowStartupLocation = WindowStartupLocation.Manual,
        WindowDecorations = Avalonia.Controls.WindowDecorations.None,
        ShowInTaskbar = false,
        CanResize = false,
        Topmost = true,
        Opacity = 0,
        Background = Brushes.Transparent,
        RequestedThemeVariant = ThemeManager.GetCurrentTheme()
    };

    private static Control BuildGridMenuContent(IEnumerable<MainMenuCategory> categories, Action<MainMenuEntry> execute)
    {
        IReadOnlyList<MainMenuCategory> categoryList = categories.ToArray();
        Grid root = new()
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };

        Button editButton = new()
        {
            Classes = { "tool-grid-edit-button" },
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };
        editButton.ZIndex = 1;
        root.Children.Add(editButton);

        StackPanel content = new()
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        root.Children.Add(content);

        bool isEditing = false;

        void RebuildContent()
        {
            editButton.Content = CreateGridMenuEditButtonContent(isEditing);
            PopulateGridMenuCategories(content, categoryList, isEditing, execute);
        }

        editButton.Click += (_, _) =>
        {
            isEditing = !isEditing;
            RebuildContent();
        };

        RebuildContent();
        return root;
    }

    private static void PopulateGridMenuCategories(StackPanel content, IReadOnlyList<MainMenuCategory> categories,
        bool isEditing, Action<MainMenuEntry> execute)
    {
        content.Children.Clear();

        bool hasCategory = false;
        foreach (MainMenuCategory category in categories)
        {
            IReadOnlyList<MainMenuEntry> entries = category.Entries
                .Where(x => x.IsVisible && !x.IsSeparator && (isEditing || IsToolVisible(x)))
                .ToArray();
            if (entries.Count == 0)
            {
                continue;
            }

            if (hasCategory)
            {
                Border separator = new()
                {
                    Classes = { "tool-grid-category-separator" }
                };
                content.Children.Add(separator);
            }

            TextBlock categoryHeader = new()
            {
                Text = category.Header,
                Classes = { "tool-grid-category-header" },
                Margin = new Thickness(7, hasCategory ? 3 : 1, 7, 3)
            };
            content.Children.Add(categoryHeader);

            UniformGrid itemGrid = new()
            {
                Columns = 3,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };

            foreach (MainMenuEntry entry in entries)
            {
                if (isEditing)
                {
                    bool isVisible = IsToolVisible(entry);
                    ToggleSwitch visibilitySwitch = new()
                    {
                        IsChecked = isVisible,
                        IsHitTestVisible = false,
                        Focusable = false,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    };
                    visibilitySwitch.Classes.Add("tool-grid-visibility-switch");
                    visibilitySwitch.Resources["ToggleSwitchPreContentMargin"] = new GridLength(0);
                    visibilitySwitch.Resources["ToggleSwitchPostContentMargin"] = new GridLength(0);

                    ToggleButton visibilityButton = new()
                    {
                        Classes = { "tool-grid-button", "tool-grid-visibility-button" },
                        Content = CreateGridMenuVisibilityContent(entry, visibilitySwitch),
                        IsChecked = isVisible
                    };
                    visibilityButton.Click += (_, _) =>
                    {
                        bool showTool = visibilityButton.IsChecked == true;
                        visibilitySwitch.IsChecked = showTool;
                        SetToolVisibility(entry, showTool);
                    };
                    itemGrid.Children.Add(visibilityButton);
                }
                else
                {
                    Button button = new()
                    {
                        Classes = { "tool-grid-button" },
                        Content = CreateGridMenuItemContent(entry),
                        IsEnabled = entry.IsEnabled
                    };
                    button.Click += (_, _) => execute(entry);
                    itemGrid.Children.Add(button);
                }
            }

            content.Children.Add(itemGrid);
            hasCategory = true;
        }
    }

    private static Control CreateGridMenuEditButtonContent(bool isEditing)
    {
        StackPanel content = new()
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6
        };
        content.Children.Add(CreateAccentMenuIcon(isEditing ? LucideIcons.check : LucideIcons.pencil, 14));
        content.Children.Add(new TextBlock
        {
            Text = isEditing ? Strings.MainMenuBuilder_FinishEditingTools : Strings.MainMenuBuilder_EditTools,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        return content;
    }

    private static Control CreateGridMenuVisibilityContent(MainMenuEntry entry, ToggleSwitch visibilitySwitch)
    {
        StackPanel content = new()
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        content.Children.Add(visibilitySwitch);
        content.Children.Add(CreateGridMenuItemContent(entry));
        return content;
    }

    private static bool IsToolVisible(MainMenuEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Id))
        {
            return true;
        }

        List<string> hiddenTools = ApplicationState.Settings.HiddenTools ??= new List<string>();
        return !hiddenTools.Any(x => string.Equals(x, entry.Id, StringComparison.Ordinal));
    }

    private static void SetToolVisibility(MainMenuEntry entry, bool isVisible)
    {
        if (string.IsNullOrEmpty(entry.Id))
        {
            return;
        }

        List<string> hiddenTools = ApplicationState.Settings.HiddenTools ??= new List<string>();
        bool changed;

        if (isVisible)
        {
            changed = hiddenTools.RemoveAll(x => string.Equals(x, entry.Id, StringComparison.Ordinal)) > 0;
        }
        else if (!hiddenTools.Any(x => string.Equals(x, entry.Id, StringComparison.Ordinal)))
        {
            hiddenTools.Add(entry.Id);
            changed = true;
        }
        else
        {
            changed = false;
        }

        if (changed)
        {
            SettingManager.SaveApplicationConfigAsync();
        }
    }

    private static Control CreateGridMenuItemContent(MainMenuEntry entry)
    {
        StackPanel content = new()
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8
        };
        TextBlock icon = CreateAccentMenuIcon(entry.Icon, 16);
        icon.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        content.Children.Add(icon);

        TextBlock label = new()
        {
            Text = entry.Header,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        content.Children.Add(label);
        return content;
    }

    private static void ExecuteGridMenuEntry(Flyout flyout, MainMenuEntry entry)
    {
        flyout.Hide();
        QueueGridMenuEntry(entry);
    }

    private static void ExecuteGridMenuEntry(ContextMenu menu, MainMenuEntry entry)
    {
        menu.Close();
        QueueGridMenuEntry(entry);
    }

    private static void QueueGridMenuEntry(MainMenuEntry entry)
    {
        if (entry.ExecuteAsync == null)
        {
            return;
        }

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await entry.ExecuteAsync();
                MainWindowIntegration.RefreshMenus();
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex);
            }
        }, DispatcherPriority.Background);
    }

    private static IEnumerable<Control> BuildMenuControls(IEnumerable<MainMenuEntry> entries, ContextMenu menu)
    {
        foreach (MainMenuEntry entry in entries.Where(x => x.IsVisible))
        {
            if (entry.IsSeparator)
            {
                Separator separator = new();
                separator.Classes.Add("compact-menu-separator");
                yield return separator;
                continue;
            }

            MenuItem item = new()
            {
                Header = CreateMenuHeader(entry),
                Tag = entry,
                InputGesture = entry.InputGesture,
                IsEnabled = entry.IsEnabled,
                IsChecked = entry.IsChecked,
                StaysOpenOnClick = entry.StaysOpenOnClick,
                ToggleType = entry.ToggleType switch
                {
                    MainMenuToggleType.CheckBox => MenuItemToggleType.CheckBox,
                    MainMenuToggleType.Radio => MenuItemToggleType.Radio,
                    _ => MenuItemToggleType.None
                }
            };
            item.Classes.Add("compact-menu-item");
            if (entry.BoldWhenChecked)
            {
                item.Classes.Add("bold-when-checked");
            }

            if (entry.BitmapIcon is { Length: > 0 })
            {
                item.Icon = CreateBitmapMenuIcon(entry.BitmapIcon, menu);
            }
            else if (!string.IsNullOrEmpty(entry.Icon))
            {
                item.Icon = CreateAccentMenuIcon(entry.Icon, 16);
            }

            if (entry.CreateCategories != null)
            {
                AddLazyGridSubmenu(item, entry.CreateCategories, menu);
            }
            else if (entry.CreateChildren != null)
            {
                AddLazySubmenu(item, entry.CreateChildren, menu);

                if (entry.ExecuteAsync != null)
                {
                    item.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
                    {
                        PointerPoint point = e.GetCurrentPoint(item);
                        if (point.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed &&
                            new Rect(item.Bounds.Size).Contains(point.Position))
                        {
                            item.IsChecked = item.ToggleType switch
                            {
                                MenuItemToggleType.CheckBox => !item.IsChecked,
                                MenuItemToggleType.Radio => true,
                                _ => item.IsChecked
                            };
                            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
                        }
                    }, RoutingStrategies.Tunnel, handledEventsToo: true);
                }
            }

            if (entry.ExecuteAsync != null)
            {
                item.Click += (_, e) =>
                {
                    if (!ReferenceEquals(e.Source, item))
                    {
                        return;
                    }

                    if (!entry.StaysOpenOnClick)
                    {
                        // Modal WinForms dialogs and capture overlays cannot be opened while
                        // the Avalonia popup is still processing the click event. Dismiss the
                        // entire menu first, then run the command on the next dispatcher turn.
                        menu.Close();
                    }

                    Dispatcher.UIThread.Post(async () =>
                    {
                        try
                        {
                            await entry.ExecuteAsync();
                            if (entry.StaysOpenOnClick)
                            {
                                RefreshMenuHeaders(menu.Items);
                            }
                            else
                            {
                                MainWindowIntegration.RefreshMenus();
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugHelper.WriteException(ex);
                        }
                    }, DispatcherPriority.Background);
                };
            }

            yield return item;
        }
    }

    private static void RefreshMenuHeaders(IEnumerable<object?> items)
    {
        foreach (MenuItem item in items.OfType<MenuItem>())
        {
            if (item.Tag is MainMenuEntry entry)
            {
                item.Header = CreateMenuHeader(entry);
            }

            RefreshMenuHeaders(item.Items);
        }
    }

    private static object CreateMenuHeader(MainMenuEntry entry)
    {
        string header = entry.Header;
        string? accentText = entry.AccentText;
        if (string.IsNullOrEmpty(accentText))
        {
            return header;
        }

        int accentIndex = header.IndexOf(accentText, StringComparison.CurrentCulture);
        if (accentIndex < 0)
        {
            return header;
        }

        Run accentRun = new()
        {
            Text = accentText,
            Foreground = Application.Current?.FindResource(ThemeManager.GetCurrentTheme(), "ShareX.Brush.Accent.Start") as IBrush
        };
        InlineCollection inlines = new();
        inlines.Add(header[..accentIndex]);
        inlines.Add(accentRun);
        inlines.Add(header[(accentIndex + accentText.Length)..]);

        return new TextBlock
        {
            Inlines = inlines,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
    }

    private static void AddLazyGridSubmenu(MenuItem item, Func<IReadOnlyList<MainMenuCategory>> createCategories, ContextMenu menu)
    {
        item.Resources["FlyoutThemeMaxWidth"] = double.PositiveInfinity;
        item.Resources["MenuFlyoutPresenterThemePadding"] = new Thickness(10);
        item.Resources["MenuFlyoutScrollerMargin"] = new Thickness(0);

        // Keep the submenu indicator while deferring the grid construction until the submenu opens.
        item.Items.Add(new MenuItem { IsVisible = false });

        bool isPopulated = false;
        item.SubmenuOpened += (_, e) =>
        {
            if (isPopulated || !ReferenceEquals(e.Source, item))
            {
                return;
            }

            item.Items.Clear();

            MenuItem gridHost = new()
            {
                Header = BuildGridMenuContent(createCategories(), entry => ExecuteGridMenuEntry(menu, entry)),
                Focusable = false,
                StaysOpenOnClick = true
            };
            gridHost.Classes.Add("tool-grid-submenu-host");
            item.Items.Add(gridHost);

            isPopulated = true;
        };
    }

    private static void AddLazySubmenu(MenuItem item, Func<IReadOnlyList<MainMenuEntry>> createChildren, ContextMenu menu)
    {
        // Keep the submenu indicator without constructing every nested menu before
        // the root context menu can be displayed.
        item.Items.Add(new MenuItem { IsVisible = false });

        bool isPopulated = false;
        item.SubmenuOpened += (_, e) =>
        {
            if (isPopulated || !ReferenceEquals(e.Source, item))
            {
                return;
            }

            item.Items.Clear();

            foreach (Control child in BuildMenuControls(createChildren(), menu))
            {
                item.Items.Add(child);
            }

            isPopulated = true;
        };
    }

    private static TextBlock CreateLucideText(string glyph, double size)
    {
        TextBlock text = new()
        {
            Text = glyph,
            FontSize = size,
            FontWeight = FontWeight.Normal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        text.Classes.Add("icon");
        return text;
    }

    private static Image CreateBitmapMenuIcon(byte[] data, ContextMenu menu)
    {
        using MemoryStream stream = new(data, writable: false);
        AvaloniaBitmap bitmap = new(stream);
        menu.Closed += (_, _) => bitmap.Dispose();

        return new Image
        {
            Source = bitmap,
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform
        };
    }
}
