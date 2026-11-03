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
using ShareX.HelpersLib;
using ShareX.HistoryLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ShareX;

internal enum CaptureLibraryFilter
{
    All,
    Images,
    Videos,
    OtherFiles
}

/// <summary>
/// The data of the main window library: history items that have an existing local file, newest first, one card per file,
/// loaded in batches. In-session tasks that run or failed show above the history items.
/// History and task changes update the library live.
/// </summary>
internal sealed class CaptureLibrary : IDisposable
{
    private const int PageSize = 200;
    private const int BatchSize = 120;

    private readonly ThumbnailGallery<CaptureLibraryItem> _gallery = new();
    private readonly HashSet<string> _historyPaths = new(StringComparer.OrdinalIgnoreCase);
    private int _thumbnailWidth;
    private int _thumbnailHeight;
    private long _cursor;
    private bool _hasMore;
    private bool _loading;
    private int _version;
    private bool _disposed;

    public CaptureLibrary()
    {
        TaskManager.TaskAdded += OnTaskEvent;
        TaskManager.TaskRemoved += OnTaskEvent;
        TaskManager.TaskChanged += OnTaskEvent;
        HistoryManagerSQLite.HistoryChanged += OnHistoryChanged;
    }

    public ThumbnailGallery<CaptureLibraryItem> Gallery => _gallery;
    public IReadOnlyList<CaptureLibraryItem> Items => _gallery.Items;
    public string Search { get; private set; } = string.Empty;
    public CaptureLibraryFilter Filter { get; private set; }
    public bool IsLoaded { get; private set; }
    public bool HasMore => _hasMore;
    public bool HasSearchOrFilter => Search.Length > 0 || Filter != CaptureLibraryFilter.All;

    /// <summary>Raised on the UI thread after items were added, removed or loaded.</summary>
    public event Action? ItemsChanged;

    public Task ReloadAsync(string search, CaptureLibraryFilter filter, int thumbnailWidth, int thumbnailHeight)
    {
        Search = search.Trim();
        Filter = filter;
        _thumbnailWidth = thumbnailWidth;
        _thumbnailHeight = thumbnailHeight;
        _version++;
        _gallery.Clear();
        _historyPaths.Clear();
        _cursor = 0;
        _hasMore = true;
        _loading = false;
        IsLoaded = false;

        foreach (WorkerTask task in TaskManager.Tasks.ToArray())
        {
            SyncTask(task);
        }

        ItemsChanged?.Invoke();
        return LoadMoreAsync();
    }

    public async Task LoadMoreAsync()
    {
        if (_disposed || _loading || !_hasMore) return;

        HistoryManagerSQLite? manager = ApplicationState.HistoryManager;
        int version = _version;
        string search = Search;
        CaptureLibraryFilter filter = Filter;
        long cursor = _cursor;
        HashSet<string> knownPaths = new(_historyPaths, StringComparer.OrdinalIgnoreCase);
        _loading = true;

        try
        {
            HistoryBatch batch = manager == null
                ? new HistoryBatch([], cursor, false)
                : await Task.Run(() => ReadBatch(manager, cursor, search, filter, knownPaths));

            if (version != _version || _disposed) return;

            _cursor = batch.Cursor;
            _hasMore = batch.HasMore;

            foreach (HistoryItem item in batch.Items)
            {
                if (_historyPaths.Add(item.FilePath))
                {
                    _gallery.Add(new CaptureLibraryItem(item, _thumbnailWidth, _thumbnailHeight));
                }
            }

            SyncTasks();
        }
        catch (Exception e)
        {
            DebugHelper.WriteException(e);
            if (version == _version) _hasMore = false;
        }
        finally
        {
            if (version == _version && !_disposed)
            {
                _loading = false;
                IsLoaded = true;
                ItemsChanged?.Invoke();
            }
        }
    }

    private static HistoryBatch ReadBatch(HistoryManagerSQLite manager, long cursor, string search, CaptureLibraryFilter filter,
        HashSet<string> knownPaths)
    {
        List<HistoryItem> result = [];

        while (result.Count < BatchSize)
        {
            List<HistoryItem> page = manager.GetFileHistoryPage(cursor, PageSize, search);
            if (page.Count == 0) return new HistoryBatch(result, cursor, false);

            cursor = page[^1].Id;

            // Pages are newest first, so the first item of a file path is the newest item of that file.
            foreach (HistoryItem item in page)
            {
                if (MatchesFilter(item.FilePath, filter) && knownPaths.Add(item.FilePath) && File.Exists(item.FilePath))
                {
                    result.Add(item);
                }
            }

            if (page.Count < PageSize) return new HistoryBatch(result, cursor, false);
        }

        return new HistoryBatch(result, cursor, true);
    }

    public static bool MatchesFilter(string? filePath, CaptureLibraryFilter filter) => filter switch
    {
        CaptureLibraryFilter.Images => FileHelpers.IsImageFile(filePath),
        CaptureLibraryFilter.Videos => FileHelpers.IsVideoFile(filePath),
        CaptureLibraryFilter.OtherFiles => !FileHelpers.IsImageFile(filePath) && !FileHelpers.IsVideoFile(filePath),
        _ => true
    };

    private bool Matches(string? fileName, string? filePath) =>
        MatchesFilter(filePath ?? fileName, Filter) &&
        (Search.Length == 0 ||
            fileName?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true ||
            filePath?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true);

    // Before the first load, the library has no items to update. The first load reads the tasks and the history.
    private bool IsStarted => _version > 0 && !_disposed;

    private void OnTaskEvent(WorkerTask task) => Dispatcher.UIThread.Post(() =>
    {
        if (!IsStarted) return;
        SyncTask(task);
        ItemsChanged?.Invoke();
    });

    private void SyncTasks()
    {
        foreach (CaptureLibraryItem item in _gallery.Items.Where(x => x.Task != null).ToArray())
        {
            SyncTask(item.Task!);
        }
    }

    /// <summary>Adds, updates or removes the card of a task, so the library shows only tasks that run or failed.</summary>
    private void SyncTask(WorkerTask task)
    {
        CaptureLibraryItem? item = _gallery.Items.FirstOrDefault(x => ReferenceEquals(x.Task, task));
        bool show = !_disposed && TaskManager.Tasks.Contains(task) && ShouldShowTask(task);

        if (show && item == null)
        {
            _gallery.Insert(0, new CaptureLibraryItem(task, _thumbnailWidth, _thumbnailHeight));
        }
        else if (!show && item != null)
        {
            _gallery.Remove(item);
        }
        else
        {
            item?.RefreshTask();
        }
    }

    private bool ShouldShowTask(WorkerTask task)
    {
        TaskInfo? info = task.Info;
        if (info == null || !Matches(info.FileName, info.FilePath)) return false;
        if (task.IsBusy) return true;

        // A failed task with a file also has a history item. The history card then shows the file.
        return task.Status == TaskStatus.Failed && (string.IsNullOrEmpty(info.FilePath) || !_historyPaths.Contains(info.FilePath));
    }

    private int TaskCount => _gallery.Items.TakeWhile(x => x.IsTask).Count();

    private void OnHistoryChanged(object? sender, HistoryChangedEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (!IsStarted || !ReferenceEquals(sender, ApplicationState.HistoryManager)) return;

        foreach (HistoryItem item in e.Items.OrderBy(x => x.Id))
        {
            // An added item replaces the older card of the same file.
            CaptureLibraryItem? existing = _gallery.Items.FirstOrDefault(x => x.History != null &&
                (x.History.Id == item.Id || (e.Kind == HistoryChangeKind.Added &&
                    string.Equals(x.History.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase))));

            // The library does not show edited items that are not loaded yet. The items show when the batch loads.
            if (existing == null && e.Kind != HistoryChangeKind.Added) continue;

            int index = existing == null ? 0 : _gallery.IndexOf(existing);

            if (existing != null)
            {
                _historyPaths.Remove(existing.History!.FilePath);
                _gallery.Remove(existing);
            }

            if (e.Kind == HistoryChangeKind.Deleted) continue;

            // An added item is the newest item, so the card goes to the top, below the task cards. An edited item keeps the position.
            if (e.Kind == HistoryChangeKind.Added) index = TaskCount;

            if (!string.IsNullOrEmpty(item.FilePath) && Matches(item.FileName, item.FilePath) && File.Exists(item.FilePath) &&
                _historyPaths.Add(item.FilePath))
            {
                _gallery.Insert(index, new CaptureLibraryItem(item, _thumbnailWidth, _thumbnailHeight));
            }
        }

        SyncTasks();
        ItemsChanged?.Invoke();
    });

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        TaskManager.TaskAdded -= OnTaskEvent;
        TaskManager.TaskRemoved -= OnTaskEvent;
        TaskManager.TaskChanged -= OnTaskEvent;
        HistoryManagerSQLite.HistoryChanged -= OnHistoryChanged;
        _gallery.Dispose();
    }

    private sealed record HistoryBatch(List<HistoryItem> Items, long Cursor, bool HasMore);
}
