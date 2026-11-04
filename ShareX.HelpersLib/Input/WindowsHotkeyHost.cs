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

using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace ShareX.HelpersLib;

/// <summary>Windows global hotkeys and tray messages on Avalonia's UI thread.</summary>
public sealed class WindowsHotkeyHost : IHotkeyHost
{
    private readonly int threadId = Environment.CurrentManagedThreadId;
    private readonly Stopwatch repeatLimitTimer = Stopwatch.StartNew();
    private readonly HashSet<HotkeyInfo> registeredHotkeys = new();
    private WindowsNativeWindow window;
    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
    // Windows keeps some Win shortcuts for itself, for example Win+Shift+S for Snipping Tool, so RegisterHotKey fails for them.
    // A low-level keyboard hook sees the keys before Windows does, so ShareX takes these shortcuts with the hook instead.
    private LowLevelHook keyboardHook;
    private HookedHotkey[] hookedHotkeys = [];
    private IntPtr hookWindow;
    private HookedHotkey blockedHotkey; // Used only on the hook thread.
    private bool closing;

    public event HotkeyEventHandler HotkeyPress;
    public event EventHandler Closed;
    public event EventHandler<NativeWindowMessageEventArgs> NativeMessageReceived;

    public IntPtr Handle => GetWindow().Handle;
    public bool IsDisposed { get; private set; }
    public int HotkeyRepeatLimit { get; set; } = 1000;

    public WindowsHotkeyHost()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Global hotkeys require a backend for this platform.");
    }

    private WindowsNativeWindow GetWindow()
    {
        VerifyAccess();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (window == null)
        {
            // Keep a hidden top-level window so Explorer restart and Windows session
            // broadcasts reach the host. Message-only windows miss those broadcasts.
            window = new WindowsNativeWindow("ShareX - Hotkey host", Rectangle.Empty,
                WindowStyles.WS_EX_TOOLWINDOW | WindowStyles.WS_EX_NOACTIVATE);
            window.MessageReceived += OnNativeMessage;
        }
        return window;
    }

    public void Initialize() => _ = Handle;

    public void RegisterHotkey(HotkeyInfo hotkeyInfo)
    {
        VerifyAccess();
        ObjectDisposedException.ThrowIf(IsDisposed || closing, this);
        if (hotkeyInfo == null || hotkeyInfo.Status == HotkeyStatus.Registered) return;
        if (!hotkeyInfo.IsValidHotkey)
        {
            hotkeyInfo.Status = HotkeyStatus.NotConfigured;
            return;
        }
        IntPtr handle = Handle;
        if (hotkeyInfo.ID == 0)
        {
            hotkeyInfo.ID = NativeMethods.GlobalAddAtom(Helpers.GetUniqueID());
            if (hotkeyInfo.ID == 0)
            {
                DebugHelper.WriteLine("Unable to generate unique hotkey ID: " + hotkeyInfo);
                hotkeyInfo.Status = HotkeyStatus.Failed;
                return;
            }
        }
        if (NativeMethods.RegisterHotKey(handle, hotkeyInfo.ID, (uint)hotkeyInfo.ModifiersEnum, (uint)hotkeyInfo.KeyCode))
        {
            registeredHotkeys.Add(hotkeyInfo);
        }
        else if (!(Marshal.GetLastWin32Error() == ERROR_HOTKEY_ALREADY_REGISTERED && hotkeyInfo.Win && HookHotkey(hotkeyInfo)))
        {
            NativeMethods.GlobalDeleteAtom(hotkeyInfo.ID);
            DebugHelper.WriteLine("Unable to register hotkey: " + hotkeyInfo);
            hotkeyInfo.ID = 0;
            hotkeyInfo.Status = HotkeyStatus.Failed;
            return;
        }
        hotkeyInfo.Status = HotkeyStatus.Registered;
    }

    public bool UnregisterHotkey(HotkeyInfo hotkeyInfo)
    {
        VerifyAccess();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (hotkeyInfo == null) return false;
        if (hotkeyInfo.ID > 0 && (UnhookHotkey(hotkeyInfo) || NativeMethods.UnregisterHotKey(Handle, hotkeyInfo.ID)))
        {
            NativeMethods.GlobalDeleteAtom(hotkeyInfo.ID);
            registeredHotkeys.Remove(hotkeyInfo);
            hotkeyInfo.ID = 0;
            hotkeyInfo.Status = HotkeyStatus.NotConfigured;
            return true;
        }
        hotkeyInfo.Status = HotkeyStatus.Failed;
        return false;
    }

    private bool HookHotkey(HotkeyInfo hotkeyInfo)
    {
        try
        {
            if (keyboardHook == null)
            {
                hookWindow = Handle;
                keyboardHook = new LowLevelHook(NativeConstants.WH_KEYBOARD_LL, OnKeyboardInput, "ShareX hotkey input");
            }
        }
        catch (Exception e)
        {
            DebugHelper.WriteException(e, "Unable to start the keyboard hook for hotkeys that Windows reserves.");
            return false;
        }

        Volatile.Write(ref hookedHotkeys, [.. hookedHotkeys, new HookedHotkey(hotkeyInfo, hotkeyInfo.ID, (int)hotkeyInfo.KeyCode, hotkeyInfo.ModifiersEnum)]);
        DebugHelper.WriteLine("Windows reserves this hotkey, so a keyboard hook takes the keys first: " + hotkeyInfo);
        return true;
    }

    private bool UnhookHotkey(HotkeyInfo hotkeyInfo)
    {
        if (!hookedHotkeys.Any(x => x.Info == hotkeyInfo)) return false;

        Volatile.Write(ref hookedHotkeys, hookedHotkeys.Where(x => x.Info != hotkeyInfo).ToArray());
        if (hookedHotkeys.Length == 0) StopKeyboardHook();
        return true;
    }

    private void StopKeyboardHook()
    {
        keyboardHook?.Dispose();
        keyboardHook = null;
    }

    /// <summary>Runs on the hook thread for every key event on the desktop, so the work stays small and never waits for the UI.</summary>
    private bool OnKeyboardInput(int message, IntPtr data)
    {
        int key = Marshal.ReadInt32(data); // vkCode is the first field of KBDLLHOOKSTRUCT.
        bool keyDown = message is (int)KeyEvent.WM_KEYDOWN or (int)KeyEvent.WM_SYSKEYDOWN;
        Modifiers modifiers = GetPressedModifiers();

        if (blockedHotkey != null && blockedHotkey.Key == key)
        {
            // Hide the repeats and the release of the key that started the hotkey, so Windows does not see half of the shortcut.
            // A new press after the modifiers changed is a normal key press, for example when the release went to the lock screen.
            if (!keyDown || blockedHotkey.Modifiers == modifiers)
            {
                if (!keyDown) blockedHotkey = null;
                return true;
            }
            blockedHotkey = null;
        }

        if (!keyDown) return false;

        foreach (HookedHotkey hotkey in Volatile.Read(ref hookedHotkeys))
        {
            if (hotkey.Key == key && hotkey.Modifiers == modifiers)
            {
                blockedHotkey = hotkey;
                // Windows opens Start when Win goes up with no other key pressed since Win went down. A press of an unassigned key prevents that.
                SendUnassignedKeyPress();
                NativeMethods.PostMessage(hookWindow, (uint)WindowsMessages.HOTKEY, hotkey.ID, (key << 16) | (int)modifiers);
                return true;
            }
        }

        return false;
    }

    private static Modifiers GetPressedModifiers()
    {
        static bool IsDown(VirtualKeyCode key) => (NativeMethods.GetAsyncKeyState((int)key) & 0x8000) != 0;

        Modifiers modifiers = Modifiers.None;
        if (IsDown(VirtualKeyCode.MENU)) modifiers |= Modifiers.Alt;
        if (IsDown(VirtualKeyCode.CONTROL)) modifiers |= Modifiers.Control;
        if (IsDown(VirtualKeyCode.SHIFT)) modifiers |= Modifiers.Shift;
        if (IsDown(VirtualKeyCode.LWIN) || IsDown(VirtualKeyCode.RWIN)) modifiers |= Modifiers.Win;
        return modifiers;
    }

    private static void SendUnassignedKeyPress()
    {
        const VirtualKeyCode unassignedKey = (VirtualKeyCode)0xE8;
        InputManager input = new();
        input.AddKeyPress(unassignedKey);
        input.SendInputs();
    }

    private sealed record HookedHotkey(HotkeyInfo Info, ushort ID, int Key, Modifiers Modifiers);

    private void OnNativeMessage(object sender, NativeWindowMessageEventArgs message)
    {
        try
        {
            if (message.Message == (int)WindowsMessages.HOTKEY)
            {
                message.Handled = true;
                if (CheckRepeatLimitTime())
                {
                    uint data = unchecked((uint)message.LParam.ToInt64());
                    HotkeyPress?.Invoke((ushort)message.WParam.ToInt64(), (InputKey)(data >> 16), (Modifiers)(data & 0xffff));
                }
            }
            else if (message.Message == (int)WindowsMessages.CLOSE)
            {
                message.Handled = true;
                Close();
            }
            else NativeMessageReceived?.Invoke(this, message);
        }
        catch (Exception exception)
        {
            // Never let a managed exception unwind through a native window procedure.
            // Avalonia's dispatcher routes it through the application's error handler.
            message.Handled = true;
            Dispatcher.UIThread.Post(ExceptionDispatchInfo.Capture(exception).Throw);
        }
    }

    private bool CheckRepeatLimitTime()
    {
        if (HotkeyRepeatLimit > 0)
        {
            if (repeatLimitTimer.ElapsedMilliseconds < HotkeyRepeatLimit) return false;
            repeatLimitTimer.Restart();
        }
        return true;
    }

    public void Close()
    {
        VerifyAccess();
        if (IsDisposed || closing) return;
        closing = true;
        try
        {
            // Subscribers remove tray icons and unregister hotkeys while the HWND is valid.
            Closed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            foreach (HotkeyInfo hotkey in registeredHotkeys.ToArray())
            {
                NativeMethods.UnregisterHotKey(window.Handle, hotkey.ID);
                NativeMethods.GlobalDeleteAtom(hotkey.ID);
                hotkey.ID = 0;
                hotkey.Status = HotkeyStatus.NotConfigured;
            }
            registeredHotkeys.Clear();
            foreach (HookedHotkey hotkey in hookedHotkeys)
            {
                NativeMethods.GlobalDeleteAtom(hotkey.ID);
                hotkey.Info.ID = 0;
                hotkey.Info.Status = HotkeyStatus.NotConfigured;
            }
            hookedHotkeys = [];
            StopKeyboardHook();
            if (window != null)
            {
                window.MessageReceived -= OnNativeMessage;
                window.Dispose();
                window = null;
            }
            IsDisposed = true;
            closing = false;
        }
    }

    public void Dispose() => Close();

    private void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != threadId)
            throw new InvalidOperationException("The hotkey host must be accessed on the thread that created it.");
    }
}
