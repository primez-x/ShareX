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
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ShareX.Tools;

internal sealed class MouseHighlighterMouseHook : IDisposable
{
    private readonly MouseHighlighterInputBuffer _input;
    private readonly LowLevelHook _hook;

    public Exception? Failure => _hook.Failure;

    public MouseHighlighterMouseHook(MouseHighlighterInputBuffer input)
    {
        _input = input;
        _hook = new LowLevelHook(NativeConstants.WH_MOUSE_LL, OnMouseInput, "ShareX mouse highlighter input");
    }

    private bool OnMouseInput(int mouseMessage, IntPtr data)
    {
        // The POINT is the first field of MSLLHOOKSTRUCT. This callback
        // only publishes values; it never invokes or waits for the UI.
        System.Drawing.Point point = new(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4));
        _input.SetPosition(point);
        if (mouseMessage is 0x0201 or 0x0202 or 0x0204 or 0x0205 or 0x0207 or 0x0208)
        {
            bool swapped = NativeMethods.GetSystemMetrics(SystemMetric.SM_SWAPBUTTON) != 0;
            bool right = mouseMessage is 0x0204 or 0x0205;
            MouseHighlightButton button = mouseMessage is 0x0207 or 0x0208
                ? MouseHighlightButton.Middle
                : right != swapped ? MouseHighlightButton.Secondary : MouseHighlightButton.Primary;
            bool pressed = mouseMessage is 0x0201 or 0x0204 or 0x0207;
            _input.PublishButton(new MouseHighlighterButtonEvent(button, pressed, point, Stopwatch.GetTimestamp()));
        }

        return false;
    }

    public void Dispose() => _hook.Dispose();
}
