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

using Avalonia.Threading;

namespace ShareX;

public static class HotkeySettingsIntegration
{
    private static HotkeySettingsWindow? _window;
    private static HotkeySettingsViewModel? _viewModel;
    private static int _viewModelUsers;

    public static void Show()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_window != null)
            {
                if (!_window.IsVisible)
                {
                    _window.Show();
                }
                _window.Activate();
                return;
            }

            HotkeySettingsViewModel? viewModel = AcquireViewModel();
            if (viewModel == null)
            {
                return;
            }

            _window = new HotkeySettingsWindow(viewModel);
            _window.Closed += (_, _) =>
            {
                _window = null;
                ReleaseViewModel();
            };
            _window.Show();
        });
    }

    /// <summary>
    /// Every open hotkey editor shares one view model, so the editors show the same list and only one
    /// service registers hotkeys. Call <see cref="ReleaseViewModel"/> when the editor closes.
    /// Returns null before the hotkey manager starts.
    /// </summary>
    internal static HotkeySettingsViewModel? AcquireViewModel()
    {
        if (_viewModel == null)
        {
            if (ApplicationState.HotkeyManager == null)
            {
                return null;
            }

            _viewModel = new HotkeySettingsViewModel(new HotkeySettingsAvaloniaService(ApplicationState.HotkeyManager, OnEditorsClosed));
        }

        _viewModelUsers++;
        return _viewModel;
    }

    internal static void ReleaseViewModel()
    {
        if (_viewModel == null || --_viewModelUsers > 0)
        {
            return;
        }

        HotkeySettingsViewModel viewModel = _viewModel;
        _viewModel = null;
        viewModel.Dispose();
    }

    private static void OnEditorsClosed()
    {
        if (!ApplicationLifecycle.IsClosing)
        {
            MainWindowIntegration.RefreshMenus();
            ActionsToolbarWindowIntegration.Refresh();
            SettingManager.SaveHotkeysConfigAsync();
        }
    }
}
