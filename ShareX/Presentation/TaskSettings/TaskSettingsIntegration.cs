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
using System;

namespace ShareX;

public static class TaskSettingsIntegration
{
    private static TaskSettingsWindow? _window;
    private static TaskSettings? _settings;

    public static void Show(TaskSettings settings, bool isDefault, Action? closed = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_window != null)
            {
                if (ReferenceEquals(_settings, settings))
                {
                    _window.Activate();
                    return;
                }

                // A request for other settings replaces the open window. The old window still runs the close callback and saves.
                _window.Close();
            }

            if (!isDefault)
            {
                settings.SetDefaultSettings();
            }

            TaskSettingsWindow window = new(settings, isDefault);
            _window = window;
            _settings = settings;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_window, window))
                {
                    _window = null;
                    _settings = null;
                }

                closed?.Invoke();
            };
            window.Show();
        });
    }
}
