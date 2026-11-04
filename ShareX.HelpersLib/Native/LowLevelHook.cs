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

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX.HelpersLib;

/// <summary>
/// Runs a low-level keyboard or mouse hook on a dedicated thread. Windows calls a low-level hook for every input event
/// on the whole desktop, so a busy UI thread must never be the thread that answers the hook.
/// </summary>
public sealed class LowLevelHook : IDisposable
{
    /// <summary>Gets the hook message and event data. Returns true to stop the event from reaching other applications.</summary>
    public delegate bool InputCallback(int message, IntPtr data);

    private readonly int hookType;
    private readonly InputCallback onInput;
    private readonly HookProc callback;
    private readonly Thread thread;
    private IntPtr handle;
    private uint threadId;
    private int stopRequested;
    private int disposed;
    private Exception failure;

    public Exception Failure => Volatile.Read(ref failure);

    public LowLevelHook(int hookType, InputCallback onInput, string threadName)
    {
        this.hookType = hookType;
        this.onInput = onInput;
        callback = OnHook;
        TaskCompletionSource initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
        thread = new Thread(() => Run(initialized))
        {
            IsBackground = true,
            Name = threadName,
            Priority = ThreadPriority.AboveNormal
        };
        thread.Start();
        try { initialized.Task.GetAwaiter().GetResult(); }
        catch
        {
            thread.Join();
            throw;
        }
    }

    private void Run(TaskCompletionSource initialized)
    {
        try
        {
            threadId = GetCurrentThreadId();
            // Create the queue before reporting readiness, so Dispose can post WM_QUIT even immediately after construction.
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            handle = SetWindowsHookEx(hookType, callback, NativeMethods.GetModuleHandle(null), 0);
            if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            initialized.SetResult();

            while (Volatile.Read(ref stopRequested) == 0)
            {
                int result = GetMessage(out NativeMessage message, IntPtr.Zero, 0, 0);
                if (result == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (result == 0) break;
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception ex)
        {
            Volatile.Write(ref failure, ex);
            initialized.TrySetException(ex);
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(handle);
                handle = IntPtr.Zero;
            }
            GC.KeepAlive(callback);
        }
    }

    private IntPtr OnHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && onInput(message.ToInt32(), data))
        {
            return (IntPtr)1;
        }

        return NativeMethods.CallNextHookEx(handle, code, message, data);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Volatile.Write(ref stopRequested, 1);
        // The hook is removed by its owning thread. No UI dispatch is involved.
        PostThreadMessage(threadId, 0x0012, IntPtr.Zero, IntPtr.Zero); // WM_QUIT
        if (Thread.CurrentThread != thread)
        {
            thread.Join();
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public POINT Position;
        public uint Private;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetMessage(out NativeMessage message, IntPtr window, uint min, uint max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint min, uint max, uint remove);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessage(ref NativeMessage message);
}
