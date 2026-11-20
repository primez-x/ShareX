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
using ShareX.AvaloniaUI.Integration;
using ShareX.HelpersLib;
using ShareX.ImageEditor.Integration;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace ShareX.ScreenCaptureLib.Presentation.RegionCapture;

/// <summary>UI-thread boundary for the Avalonia region capture experience.</summary>
public static class RegionCaptureIntegration
{
    public static Rectangle LastRegionRectangle { get; internal set; }

    /// <summary>
    /// Shows region selection for a frozen screenshot. When <see cref="AvaloniaRegionCaptureRequest.CaptureMonitor"/>
    /// is set, every monitor gets its own window: the cursor moves freely between monitors, and only the window
    /// under the cursor redraws during a drag.
    /// </summary>
    public static Task<AvaloniaRegionCaptureResult?> CaptureAsync(AvaloniaRegionCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            AvaloniaBootstrapper.EnsureInitialized();
        }
        catch (Exception ex)
        {
            DisposeRequest(request);
            return Task.FromException<AvaloniaRegionCaptureResult?>(ex);
        }

        TaskCompletionSource<AvaloniaRegionCaptureResult?> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completion.TrySetResult(await ShowWindowsAsync(request));
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return completion.Task;
    }

    private static async Task<AvaloniaRegionCaptureResult?> ShowWindowsAsync(AvaloniaRegionCaptureRequest request)
    {
        List<RegionCaptureWindow> windows = [];
        List<Task<AvaloniaRegionCaptureResult?>> captures = [];
        IReadOnlyList<SimpleWindowInfo>? windowRegions = null;

        // Window detection skips every selection window, including the windows that open after the detection starts.
        void ApplyWindowRegions()
        {
            if (windowRegions == null)
            {
                return;
            }

            HashSet<IntPtr> overlays = windows.Select(window => window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero).ToHashSet();
            List<SimpleWindowInfo> regions = windowRegions.Where(region => !overlays.Contains(region.Handle)).ToList();
            foreach (RegionCaptureWindow window in windows)
            {
                window.SetWindowRegions(regions);
            }
        }

        void Show(AvaloniaRegionCaptureRequest monitorRequest)
        {
            RegionCaptureWindow? window = null;

            try
            {
                window = new RegionCaptureWindow(monitorRequest);
                captures.Add(window.CaptureAsync());
            }
            catch
            {
                if (window == null || !window.IsVisible)
                {
                    DisposeRequest(monitorRequest);
                }
                else
                {
                    window.CancelCapture();
                }

                throw;
            }

            windows.Add(window);
            window.SelectionStarted += (sender, e) =>
            {
                foreach (RegionCaptureWindow other in windows.Where(other => other != sender))
                {
                    other.ClearSelectionForOtherMonitor();
                }
            };
            ApplyWindowRegions();
        }

        try
        {
            try
            {
                ImageEditorIntegration.Initialize();
            }
            catch
            {
                DisposeRequest(request);
                throw;
            }

            Show(request);

            if (request.RegionCaptureOptions.DetectWindows)
            {
                _ = LoadWindowRegionsAsync(request.RegionCaptureOptions, regions =>
                {
                    windowRegions = regions;
                    ApplyWindowRegions();
                });
            }

            if (request.CaptureMonitor != null)
            {
                // The monitor under the cursor shows first. Each other monitor is captured off the UI thread, because a
                // capture of a USB (DisplayLink) monitor can take several hundred milliseconds.
                foreach (Rectangle bounds in GetOtherMonitors(request.ScreenBounds))
                {
                    SKBitmap? screenshot;
                    try
                    {
                        screenshot = await Task.Run(() => request.CaptureMonitor(bounds));
                    }
                    catch (Exception ex)
                    {
                        // Keep selection on the monitors that opened, for example when a monitor disconnects.
                        DebugHelper.WriteException(ex);
                        continue;
                    }

                    if (screenshot == null)
                    {
                        continue;
                    }

                    // The user can finish or cancel on the first monitor before every monitor opens.
                    if (captures.Any(capture => capture.IsCompleted))
                    {
                        screenshot.Dispose();
                        break;
                    }

                    Show(new AvaloniaRegionCaptureRequest
                    {
                        Screenshot = screenshot,
                        ScreenBounds = bounds,
                        RegionCaptureOptions = request.RegionCaptureOptions,
                        ImageEditorOptions = request.ImageEditorOptions,
                        EnableAnnotations = request.EnableAnnotations
                    });
                }
            }
        }
        catch
        {
            foreach (RegionCaptureWindow window in windows)
            {
                window.CancelCapture();
            }

            throw;
        }

        Task<AvaloniaRegionCaptureResult?> first = await Task.WhenAny(captures);
        foreach (RegionCaptureWindow window in windows)
        {
            window.CancelCapture();
        }

        foreach (Task<AvaloniaRegionCaptureResult?> capture in captures.Where(capture => capture != first))
        {
            _ = capture.ContinueWith(task => DebugHelper.WriteException(task.Exception!),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        return await first;
    }

    private static Rectangle[] GetOtherMonitors(Rectangle shown)
    {
        return DesktopScreen.AllScreens
            .Select(screen => screen.Bounds)
            .Where(bounds => bounds != shown && bounds.Width > 0 && bounds.Height > 0)
            .ToArray();
    }

    private static async Task LoadWindowRegionsAsync(RegionCaptureOptions options, Action<IReadOnlyList<SimpleWindowInfo>> loaded)
    {
        try
        {
            WindowsRectangleList list = new WindowsRectangleList
            {
                IncludeChildWindows = options.DetectControls,
                Timeout = 5000
            };

            loaded(await Task.Run(list.GetWindowInfoList));
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex);
        }
    }

    private static void DisposeRequest(AvaloniaRegionCaptureRequest request)
    {
        request.Screenshot.Dispose();
        request.CursorBitmap?.Dispose();
    }
}
