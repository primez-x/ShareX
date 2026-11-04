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

using ShareX.HelpersLib;
using ShareX.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using MessageBox = ShareX.AvaloniaUI.MessageBox;
using MessageBoxButtons = ShareX.AvaloniaUI.MessageBoxButtons;
using MessageBoxIcon = ShareX.AvaloniaUI.MessageBoxIcon;

namespace ShareX
{
    public class HotkeyManager
    {
        public List<HotkeySettings> Hotkeys { get; private set; }
        public bool IgnoreHotkeys { get; set; }

        public delegate void HotkeyTriggerEventHandler(HotkeySettings hotkeySetting);
        public delegate void HotkeysToggledEventHandler(bool hotkeysEnabled);

        public HotkeyTriggerEventHandler HotkeyTrigger;
        public HotkeysToggledEventHandler HotkeysToggledTrigger;

        private IHotkeyHost hotkeyHost;

        public HotkeyManager(IHotkeyHost host)
        {
            hotkeyHost = host;
            hotkeyHost.HotkeyPress += OnHotkeyPressed;
            hotkeyHost.Closed += OnHostClosed;
        }

        private void OnHotkeyPressed(ushort id, InputKey key, Modifiers modifier)
        {
            if (!IgnoreHotkeys && (!ApplicationState.Settings.DisableHotkeysOnFullscreen || !CaptureHelpers.IsActiveWindowFullscreen()))
            {
                HotkeySettings hotkeySetting = Hotkeys.Find(x => x.HotkeyInfo.ID == id);

                if (hotkeySetting != null)
                {
                    OnHotkeyTrigger(hotkeySetting);
                }
            }
        }

        private void OnHostClosed(object sender, EventArgs e)
        {
            if (hotkeyHost != null && !hotkeyHost.IsDisposed)
            {
                UnregisterAllHotkeys(false);
            }
        }

        public void UpdateHotkeys(List<HotkeySettings> hotkeys, bool showFailedHotkeys)
        {
            if (Hotkeys != null)
            {
                UnregisterAllHotkeys();
            }

            Hotkeys = hotkeys;

            RegisterAllHotkeys();

            if (showFailedHotkeys)
            {
                ShowFailedHotkeys();
            }
        }

        protected void OnHotkeyTrigger(HotkeySettings hotkeySetting)
        {
            HotkeyTrigger?.Invoke(hotkeySetting);
        }

        public void RegisterHotkey(HotkeySettings hotkeySetting)
        {
            if (!ApplicationState.Settings.DisableHotkeys || hotkeySetting.TaskSettings.Job == HotkeyType.DisableHotkeys)
            {
                UnregisterHotkey(hotkeySetting, false);

                if (hotkeySetting.HotkeyInfo.Status != HotkeyStatus.Registered && hotkeySetting.HotkeyInfo.IsValidHotkey)
                {
                    hotkeyHost.RegisterHotkey(hotkeySetting.HotkeyInfo);

                    if (hotkeySetting.HotkeyInfo.Status == HotkeyStatus.Registered)
                    {
                        DebugHelper.WriteLine("Hotkey registered: " + hotkeySetting);
                    }
                    else if (hotkeySetting.HotkeyInfo.Status == HotkeyStatus.Failed)
                    {
                        DebugHelper.WriteLine("Hotkey register failed: " + hotkeySetting);
                    }
                }
                else
                {
                    hotkeySetting.HotkeyInfo.Status = HotkeyStatus.NotConfigured;
                }
            }

            if (!Hotkeys.Contains(hotkeySetting))
            {
                Hotkeys.Add(hotkeySetting);
            }
        }

        public void RegisterAllHotkeys()
        {
            foreach (HotkeySettings hotkeySetting in Hotkeys.ToArray())
            {
                RegisterHotkey(hotkeySetting);
            }
        }

        public void RegisterFailedHotkeys()
        {
            foreach (HotkeySettings hotkeySetting in Hotkeys.Where(x => x.HotkeyInfo.Status == HotkeyStatus.Failed))
            {
                RegisterHotkey(hotkeySetting);
            }
        }

        public void UnregisterHotkey(HotkeySettings hotkeySetting, bool removeFromList = true)
        {
            if (hotkeySetting.HotkeyInfo.Status == HotkeyStatus.Registered)
            {
                hotkeyHost.UnregisterHotkey(hotkeySetting.HotkeyInfo);

                if (hotkeySetting.HotkeyInfo.Status == HotkeyStatus.NotConfigured)
                {
                    DebugHelper.WriteLine("Hotkey unregistered: " + hotkeySetting);
                }
                else if (hotkeySetting.HotkeyInfo.Status == HotkeyStatus.Failed)
                {
                    DebugHelper.WriteLine("Hotkey unregister failed: " + hotkeySetting);
                }
            }

            if (removeFromList)
            {
                Hotkeys.Remove(hotkeySetting);
            }
        }

        public void UnregisterAllHotkeys(bool removeFromList = true, bool temporary = false)
        {
            if (Hotkeys != null)
            {
                foreach (HotkeySettings hotkeySetting in Hotkeys.ToArray())
                {
                    if (!temporary || hotkeySetting.TaskSettings.Job != HotkeyType.DisableHotkeys)
                    {
                        UnregisterHotkey(hotkeySetting, removeFromList);
                    }
                }
            }
        }

        public void ToggleHotkeys(bool hotkeysDisabled)
        {
            if (!hotkeysDisabled)
            {
                RegisterAllHotkeys();
            }
            else
            {
                UnregisterAllHotkeys(false, true);
            }

            HotkeysToggledTrigger?.Invoke(hotkeysDisabled);
        }

        public void ShowFailedHotkeys()
        {
            List<HotkeySettings> failedHotkeysList = Hotkeys.Where(x => x.HotkeyInfo.Status == HotkeyStatus.Failed).ToList();

            if (failedHotkeysList.Count > 0)
            {
                string failedHotkeys = string.Join("\r\n", failedHotkeysList.Select(x => $"[{x.HotkeyInfo}] {x.TaskSettings}"));
                string hotkeyText = failedHotkeysList.Count > 1 ? Strings.HotkeyManager_ShowFailedHotkeys_hotkeys : Strings.HotkeyManager_ShowFailedHotkeys_hotkey;
                string text = string.Format(Strings.HotkeyManager_ShowFailedHotkeys_Unable_to_register_hotkey, hotkeyText, failedHotkeys);

                MessageBox.Show(text, "ShareX - " + Strings.HotkeyManager_ShowFailedHotkeys_Hotkey_registration_failed, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public void ResetHotkeys()
        {
            UnregisterAllHotkeys();
            Hotkeys.AddRange(GetDefaultHotkeyList());
            RegisterAllHotkeys();

            if (ApplicationState.Settings.DisableHotkeys)
            {
                TaskHelpers.ToggleHotkeys();
            }
        }

        /// <summary>
        /// Win+Shift+S selects a region, the same as Snipping Tool. Some laptops have a screenshot key that sends Win+Shift+S
        /// instead of Print Screen, so this shortcut also makes that key open ShareX.
        /// </summary>
        public static HotkeySettings SnippingToolHotkey() =>
            new HotkeySettings(HotkeyType.RectangleRegion, InputKey.Shift | InputKey.S) { HotkeyInfo = { Win = true } };

        public static bool IsSnippingToolHotkey(HotkeyInfo hotkeyInfo) =>
            hotkeyInfo != null && hotkeyInfo.Win && hotkeyInfo.Hotkey == (InputKey.Shift | InputKey.S);

        public static List<HotkeySettings> GetDefaultHotkeyList()
        {
            return new List<HotkeySettings>
            {
                // Print Screen selects a region, the same as the Windows Snipping Tool that ShareX replaces.
                new HotkeySettings(HotkeyType.RectangleRegion, InputKey.PrintScreen),
                SnippingToolHotkey(),
                new HotkeySettings(HotkeyType.PrintScreen, InputKey.Control | InputKey.PrintScreen),
                new HotkeySettings(HotkeyType.ActiveWindow, InputKey.Alt | InputKey.PrintScreen),
                new HotkeySettings(HotkeyType.ScreenRecorder, InputKey.Shift | InputKey.PrintScreen),
                new HotkeySettings(HotkeyType.ScreenRecorderGIF, InputKey.Control | InputKey.Shift | InputKey.PrintScreen)
            };
        }
    }
}
