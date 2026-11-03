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

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Collections.Generic;
using System.Linq;

namespace ShareX;

/// <summary>
/// The hotkey list editor. The Hotkey settings window and the Shortcuts page of the Settings window both host this view.
/// </summary>
public partial class HotkeySettingsView : UserControl
{
    private HotkeySettingsViewModel? ViewModel => DataContext as HotkeySettingsViewModel;

    public HotkeySettingsView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnAddClick(object? sender, RoutedEventArgs e) => ViewModel?.Add();
    private void OnRemoveClick(object? sender, RoutedEventArgs e) => ViewModel?.RemoveSelected();
    private void OnEditClick(object? sender, RoutedEventArgs e) => ViewModel?.EditSelectedTask();
    private void OnDuplicateClick(object? sender, RoutedEventArgs e) => ViewModel?.DuplicateSelected();
    private void OnMoveUpClick(object? sender, RoutedEventArgs e) => ViewModel?.MoveSelected(-1);
    private void OnMoveDownClick(object? sender, RoutedEventArgs e) => ViewModel?.MoveSelected(1);
    private void OnEnableHotkeysClick(object? sender, RoutedEventArgs e) => ViewModel?.EnableHotkeys();
    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.IsResetConfirmationVisible = true;
        }
    }
    private void OnCancelResetClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.IsResetConfirmationVisible = false;
        }
    }
    private void OnConfirmResetClick(object? sender, RoutedEventArgs e) => ViewModel?.ConfirmReset();

    private void OnEditRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: HotkeySettingsItem item })
        {
            ViewModel?.EditTask(item);
        }
    }

    private void OnTaskClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null || sender is not Button { DataContext: HotkeySettingsItem item } button)
        {
            return;
        }

        ViewModel.SelectedItem = item;

        List<MenuItem> rootItems = [];

        foreach (IGrouping<string, HotkeyTaskOption> group in item.TaskOptions.GroupBy(x => x.Category))
        {
            List<MenuItem> taskItems = group.Select(option => CreateTaskMenuItem(item, option)).ToList();

            if (string.IsNullOrWhiteSpace(group.Key))
            {
                rootItems.AddRange(taskItems);
            }
            else
            {
                rootItems.Add(new MenuItem
                {
                    Header = group.Key,
                    ItemsSource = taskItems
                });
            }
        }

        ContextMenu menu = new()
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            PlacementTarget = button,
            ItemsSource = rootItems
        };
        menu.Open(button);
    }

    private MenuItem CreateTaskMenuItem(HotkeySettingsItem item, HotkeyTaskOption option)
    {
        MenuItem menuItem = new()
        {
            Header = option.Name,
            Icon = CreateTaskMenuIcon(option.Icon),
            ToggleType = MenuItemToggleType.Radio,
            IsChecked = item.SelectedTask?.Value == option.Value
        };
        menuItem.Click += (_, _) => ViewModel?.ChangeTask(item, option);
        return menuItem;
    }

    private static Control? CreateTaskMenuIcon(string icon)
    {
        if (string.IsNullOrEmpty(icon))
        {
            return null;
        }

        TextBlock textBlock = new()
        {
            Text = icon,
            FontSize = 16,
            Width = 18,
            TextAlignment = Avalonia.Media.TextAlignment.Center
        };
        textBlock.Classes.Add("icon");
        textBlock.Classes.Add("accent-menu-icon");
        return textBlock;
    }

    private void OnHotkeyClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: HotkeySettingsItem item } button)
        {
            ViewModel?.ToggleCapture(item);
            button.Focus();
        }
    }

    private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control { DataContext: HotkeySettingsItem item } || !item.IsCapturing)
        {
            return;
        }

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            ViewModel?.ClearAndStopCapture();
            return;
        }

        bool isWindowsKey = e.Key is Key.LWin or Key.RWin;
        bool isModifierKey = e.Key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin;

        HotkeyGesture gesture = new(
            isWindowsKey ? string.Empty : e.Key.ToString(),
            e.KeyModifiers.HasFlag(KeyModifiers.Control),
            e.KeyModifiers.HasFlag(KeyModifiers.Shift),
            e.KeyModifiers.HasFlag(KeyModifiers.Alt),
            isWindowsKey || e.KeyModifiers.HasFlag(KeyModifiers.Meta));

        ViewModel?.ApplyGesture(item, gesture);

        if (isModifierKey)
        {
            item.IsCapturing = true;
        }
    }

    private void OnHotkeyKeyUp(object? sender, KeyEventArgs e)
    {
        if (sender is Control { DataContext: HotkeySettingsItem item }
            && item.IsCapturing
            && e.Key == Key.PrintScreen)
        {
            e.Handled = true;
            OnHotkeyKeyDown(sender, e);
        }
    }

    private void OnHotkeyLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: HotkeySettingsItem item } && item.IsCapturing)
        {
            ViewModel?.StopCapture();
        }
    }
}
