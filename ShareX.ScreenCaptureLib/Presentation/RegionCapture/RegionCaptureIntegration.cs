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
using System.Diagnostics;
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

    // A selection window that is built and not shown yet. The window layout takes 40-110 ms to build.
    private static RegionCaptureWindow? _spareWindow;

    /// <summary>
    /// Builds a selection window when the app is idle. The next region capture shows this window, so the capture does
    /// not wait for the window layout to build.
    /// </summary>
    public static void PrepareWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_spareWindow != null)
            {
                return;
            }

            try
            {
                _spareWindow = new RegionCaptureWindow();
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex);
            }
        }, DispatcherPriority.ApplicationIdle);
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
            RegionCaptureWindow? window = _spareWindow;
            _spareWindow = null;

            try
            {
                window ??= new RegionCaptureWindow();
                captures.Add(window.CaptureAsync(monitorRequest));
            }
            catch
            {
                if (window == null || !window.IsVisible)
                {
                    window?.Close();
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

        // Screenshots of the other monitors that no window owns yet.
        Queue<(Rectangle Bounds, Task<SKBitmap?> Screenshot)> otherMonitors = new();

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

            // Capture the other monitors off the UI thread while the first window opens. All monitors then show the
            // same moment, and a slow USB (DisplayLink) monitor does not delay the first window.
            if (request.CaptureMonitor is { } captureMonitor)
            {
                foreach (Rectangle bounds in GetOtherMonitors(request.ScreenBounds))
                {
                    otherMonitors.Enqueue((bounds, Task.Run<SKBitmap?>(() => captureMonitor(bounds))));
                }
            }

            Show(request);
            LogFirstDraw(windows[0], request.StartTimestamp);

            if (request.RegionCaptureOptions.DetectWindows)
            {
                _ = LoadWindowRegionsAsync(request.RegionCaptureOptions, regions =>
                {
                    windowRegions = regions;
                    ApplyWindowRegions();
                });
            }

            while (otherMonitors.TryPeek(out (Rectangle Bounds, Task<SKBitmap?> Screenshot) monitor))
            {
                // Let the open windows draw before the next window takes the UI thread, so the monitor under the cursor
                // shows first and stays responsive while the other windows open.
                await WhenDrawnAsync(windows[^1], captures[^1]);

                SKBitmap? screenshot;
                try
                {
                    screenshot = await monitor.Screenshot;
                }
                catch (Exception ex)
                {
                    // Keep selection on the monitors that opened, for example when a monitor disconnects.
                    DebugHelper.WriteException(ex);
                    screenshot = null;
                }

                otherMonitors.Dequeue();

                if (screenshot == null)
                {
                    continue;
                }

                // The user can finish or cancel on the first monitor before every monitor opens.
                if (captures.Any(capture => capture.IsCompleted))
                {
                    screenshot.Dispose();
                    continue;
                }

                Show(new AvaloniaRegionCaptureRequest
                {
                    Screenshot = screenshot,
                    ScreenBounds = monitor.Bounds,
                    RegionCaptureOptions = request.RegionCaptureOptions,
                    ImageEditorOptions = request.ImageEditorOptions,
                    EnableAnnotations = request.EnableAnnotations
                });
            }
        }
        catch
        {
            foreach ((Rectangle _, Task<SKBitmap?> screenshot) in otherMonitors)
            {
                _ = screenshot.ContinueWith(task => task.Result?.Dispose(), TaskContinuationOptions.OnlyOnRanToCompletion);
            }

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

        PrepareWindow();

        foreach (Task<AvaloniaRegionCaptureResult?> capture in captures.Where(capture => capture != first))
        {
            _ = capture.ContinueWith(task => DebugHelper.WriteException(task.Exception!),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        return await first;
    }

    // The second animation frame starts after the first frame of the window drew.
    private static void LogFirstDraw(RegionCaptureWindow window, long startTimestamp)
    {
        window.RequestAnimationFrame(_ =>
        {
            if (window.IsVisible)
            {
                window.RequestAnimationFrame(_ => DebugHelper.WriteLine(
                    $"Region capture shown in {Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F0} ms."));
            }
        });
    }

    // Esc can close a window before the window draws, so the window result also ends the wait.
    private static Task WhenDrawnAsync(RegionCaptureWindow window, Task<AvaloniaRegionCaptureResult?> capture)
    {
        if (capture.IsCompleted)
        {
            return Task.CompletedTask;
        }

        TaskCompletionSource drawn = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.RequestAnimationFrame(_ => drawn.TrySetResult());
        return Task.WhenAny(drawn.Task, capture);
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
