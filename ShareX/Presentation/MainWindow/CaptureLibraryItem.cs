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

using ShareX.AvaloniaUI.Theming;
using ShareX.HelpersLib;
using ShareX.HistoryLib;
using ShareX.Localization;
using System;
using System.Globalization;
using System.IO;

namespace ShareX;

/// <summary>
/// One card of the capture library: a history item, or an in-session task that runs or failed.
/// </summary>
internal sealed class CaptureLibraryItem : ThumbnailGalleryItem
{
    private string? _taskFilePath;

    public CaptureLibraryItem(HistoryItem history, int thumbnailWidth, int thumbnailHeight) : base(thumbnailWidth, thumbnailHeight)
    {
        History = history;
    }

    public CaptureLibraryItem(WorkerTask task, int thumbnailWidth, int thumbnailHeight) : base(thumbnailWidth, thumbnailHeight)
    {
        Task = task;
        _taskFilePath = task.Info?.FilePath;
    }

    public HistoryItem? History { get; }
    public WorkerTask? Task { get; }
    public bool IsTask => Task != null;
    public override string? FilePath => History != null ? History.FilePath : Task?.Info?.FilePath;
    public bool IsImage => FileHelpers.IsImageFile(FilePath);
    public DateTime DateTime => History?.DateTime ?? Task?.Info?.TaskStartTime ?? DateTime.MinValue;

    public string FileName
    {
        get
        {
            string? name = History != null ? History.FileName : Task?.Info?.FileName;
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileName(FilePath);
            return string.IsNullOrWhiteSpace(name) ? Strings.MainWindow_UntitledCapture : name;
        }
    }

    /// <summary>The relative time for a history item, or the status for a task.</summary>
    public string Subtitle => Task != null ? GetTaskStatus(Task) : FormatRelativeTime(DateTime);

    public double Progress => Task?.Info?.Progress?.Percentage ?? 0;
    public bool IsProgressVisible => Task != null && Task.IsWorking && Task.Info?.Progress != null;
    public bool IsFailed => Task?.Status == TaskStatus.Failed;

    public string PlaceholderIcon
    {
        get
        {
            string? path = FilePath ?? FileName;
            if (FileHelpers.IsVideoFile(path)) return LucideIcons.file_video;
            if (FileHelpers.IsImageFile(path)) return LucideIcons.file_image;
            if (FileHelpers.IsTextFile(path)) return LucideIcons.file_text;
            return LucideIcons.file;
        }
    }

    /// <summary>Updates the card after the task reports a change.</summary>
    public void RefreshTask()
    {
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(IsProgressVisible));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(PlaceholderIcon));
        OnPropertyChanged(nameof(IsVideo));
        OnPropertyChanged(nameof(ShowPlayIcon));

        string? filePath = Task?.Info?.FilePath;
        if (!string.Equals(filePath, _taskFilePath, StringComparison.OrdinalIgnoreCase) || (!HasThumbnail && !IsLoading))
        {
            _taskFilePath = filePath;
            ReloadThumbnail();
        }
    }

    public void RefreshTime()
    {
        if (History != null) OnPropertyChanged(nameof(Subtitle));
    }

    private static string GetTaskStatus(WorkerTask task)
    {
        string? status = task.Info?.Status;
        return string.IsNullOrEmpty(status) ? task.Status.ToString() : status;
    }

    internal static string FormatRelativeTime(DateTime dateTime)
    {
        TimeSpan age = DateTime.Now - dateTime;

        if (age < TimeSpan.FromMinutes(1)) return Strings.MainWindow_JustNow;
        if (age < TimeSpan.FromHours(1)) return string.Format(Strings.MainWindow_MinutesAgo, (int)age.TotalMinutes);
        if (dateTime.Date == DateTime.Today) return string.Format(Strings.MainWindow_HoursAgo, (int)age.TotalHours);
        if (dateTime.Date == DateTime.Today.AddDays(-1)) return Strings.MainWindow_Yesterday;
        return dateTime.ToString("d", CultureInfo.CurrentCulture);
    }
}
