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

using Avalonia;
using Avalonia.Controls;
using ShareX.AvaloniaUI.Imaging;
using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using DrawingBitmap = SkiaSharp.SKBitmap;
using DrawingSize = System.Drawing.Size;

namespace ShareX.HistoryLib;

/// <summary>
/// One row of a virtualized thumbnail grid. A ListBox with a VirtualizingStackPanel virtualizes the rows.
/// </summary>
public sealed class ThumbnailGalleryRow
{
    public ObservableCollection<ThumbnailGalleryItem> Items { get; } = [];
}

/// <summary>
/// A thumbnail grid item. The thumbnail loads while a container shows the item and unloads when no container shows the item.
/// </summary>
public abstract class ThumbnailGalleryItem : INotifyPropertyChanged, IDisposable
{
    private AvaloniaBitmap? _thumbnail;
    private CancellationTokenSource? _loadCancellation;
    private ThumbnailLoader? _loader;
    private int _realizationCount;
    private bool _isSelected;
    private bool _isLoading;

    protected ThumbnailGalleryItem(int thumbnailWidth, int thumbnailHeight)
    {
        ThumbnailWidth = Math.Max(32, thumbnailWidth);
        ThumbnailHeight = Math.Max(32, thumbnailHeight);
    }

    /// <summary>The file that the thumbnail shows. A null path or a missing file shows the placeholder.</summary>
    public abstract string? FilePath { get; }
    public int ThumbnailWidth { get; }
    public int ThumbnailHeight { get; }
    public bool HasThumbnail => Thumbnail != null;
    public bool ShowPlaceholder => Thumbnail == null;
    public bool IsVideo => FileHelpers.IsVideoFile(FilePath);
    public bool ShowPlayIcon => HasThumbnail && IsVideo;

    public AvaloniaBitmap? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            if (ReferenceEquals(_thumbnail, value)) return;
            _thumbnail?.Dispose();
            _thumbnail = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasThumbnail));
            OnPropertyChanged(nameof(ShowPlaceholder));
            OnPropertyChanged(nameof(ShowPlayIcon));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public Task RealizeAsync(ThumbnailLoader loader)
    {
        _loader = loader;
        _realizationCount++;
        return EnsureThumbnailAsync();
    }

    /// <summary>Loads the thumbnail again, for example after the file was written or replaced.</summary>
    public void ReloadThumbnail()
    {
        if (_realizationCount == 0) return;

        Thumbnail = null;

        if (IsLoading)
        {
            // The cancelled load starts again, because the item is still realized and has no thumbnail.
            _loadCancellation?.Cancel();
            return;
        }

        _ = EnsureThumbnailAsync();
    }

    private async Task EnsureThumbnailAsync()
    {
        if (Thumbnail != null || IsLoading || _loader == null) return;

        CancellationTokenSource cancellation = new();
        _loadCancellation = cancellation;
        CancellationToken token = cancellation.Token;
        IsLoading = true;
        bool retryAfterCancellation = false;

        try
        {
            AvaloniaBitmap? thumbnail = await _loader.LoadAsync(FilePath, ThumbnailWidth, ThumbnailHeight, token);
            if (_realizationCount > 0 && !token.IsCancellationRequested)
            {
                Thumbnail = thumbnail;
            }
            else
            {
                thumbnail?.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            // Thumbnail requests are routinely cancelled when virtualized items
            // leave the viewport. Cancellation is not an error for this operation.
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex);
        }
        finally
        {
            retryAfterCancellation = token.IsCancellationRequested && _realizationCount > 0 && Thumbnail == null;
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
            }
            IsLoading = false;
            cancellation.Dispose();
        }

        if (retryAfterCancellation)
        {
            await EnsureThumbnailAsync();
        }
    }

    public void Unrealize()
    {
        if (_realizationCount > 0) _realizationCount--;
        if (_realizationCount > 0) return;

        _loadCancellation?.Cancel();
        Thumbnail = null;
    }

    public void Dispose()
    {
        _realizationCount = 0;
        _loadCancellation?.Cancel();
        _loadCancellation = null;
        Thumbnail = null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Keeps an ordered item list, lays the items out in rows of <see cref="Columns"/> items,
/// and loads thumbnails only for the items that a realized container shows.
/// </summary>
public sealed class ThumbnailGallery<T> : IDisposable where T : ThumbnailGalleryItem
{
    private readonly List<T> _items = [];
    private readonly Dictionary<Control, ThumbnailGalleryItem> _realizedControls = [];

    public ThumbnailLoader Loader { get; } = new();
    public ObservableCollection<ThumbnailGalleryRow> Rows { get; } = [];
    public IReadOnlyList<T> Items => _items;
    public int Columns { get; private set; } = 1;

    public int IndexOf(T item) => _items.IndexOf(item);

    public void Add(T item)
    {
        _items.Add(item);
        AddToLastRow(item);
    }

    private void AddToLastRow(ThumbnailGalleryItem item)
    {
        ThumbnailGalleryRow? row = Rows.Count > 0 ? Rows[^1] : null;
        if (row == null || row.Items.Count >= Columns)
        {
            row = new ThumbnailGalleryRow();
            Rows.Add(row);
        }
        row.Items.Add(item);
    }

    public void Insert(int index, T item)
    {
        index = Math.Clamp(index, 0, _items.Count);
        _items.Insert(index, item);
        int rowIndex = index / Columns;

        if (rowIndex >= Rows.Count)
        {
            AddToLastRow(item);
            return;
        }

        // Move the last item of each full row to the start of the next row, from the last row backward.
        // The item joins the next row before the item leaves the current row, so the item stays realized and keeps the thumbnail.
        for (int r = Rows.Count - 1; r >= rowIndex; r--)
        {
            ThumbnailGalleryRow row = Rows[r];
            if (row.Items.Count < Columns) continue;

            ThumbnailGalleryRow next;
            if (r + 1 < Rows.Count)
            {
                next = Rows[r + 1];
            }
            else
            {
                next = new ThumbnailGalleryRow();
                Rows.Add(next);
            }

            next.Items.Insert(0, row.Items[^1]);
            row.Items.RemoveAt(row.Items.Count - 1);
        }

        Rows[rowIndex].Items.Insert(index - rowIndex * Columns, item);
    }

    public void Remove(T item)
    {
        int index = _items.IndexOf(item);
        if (index < 0) return;

        _items.RemoveAt(index);
        int rowIndex = index / Columns;

        // Pull the first item of each next row into the previous row. The item joins before the item leaves.
        for (int r = rowIndex; r + 1 < Rows.Count; r++)
        {
            Rows[r].Items.Add(Rows[r + 1].Items[0]);
            Rows[r + 1].Items.RemoveAt(0);
        }

        Rows[rowIndex].Items.Remove(item);
        if (Rows[^1].Items.Count == 0) Rows.RemoveAt(Rows.Count - 1);
        item.Dispose();
    }

    public void Clear()
    {
        foreach (T item in _items) item.Dispose();
        _realizedControls.Clear();
        _items.Clear();
        Rows.Clear();
    }

    /// <summary>Sets the column count from the available width. Returns true when the count changed.</summary>
    public bool UpdateColumns(double availableWidth, double cardWidth)
    {
        int columns = Math.Max(1, (int)Math.Floor(Math.Max(1, availableWidth) / Math.Max(1, cardWidth)));
        if (columns == Columns) return false;

        Columns = columns;
        Rows.Clear();
        foreach (T item in _items) AddToLastRow(item);
        return true;
    }

    public async Task AttachAsync(Control? control)
    {
        if (control?.DataContext is not ThumbnailGalleryItem item) return;
        if (_realizedControls.TryGetValue(control, out ThumbnailGalleryItem? previous))
        {
            if (ReferenceEquals(previous, item)) return;
            previous.Unrealize();
        }
        _realizedControls[control] = item;
        await item.RealizeAsync(Loader);
    }

    public void Detach(Control? control)
    {
        if (control != null && _realizedControls.Remove(control, out ThumbnailGalleryItem? item)) item.Unrealize();
    }

    public void Dispose()
    {
        Clear();
        Loader.Dispose();
    }
}

/// <summary>Decodes thumbnails off the UI thread. A semaphore limits the number of parallel decodes.</summary>
public sealed class ThumbnailLoader : IDisposable
{
    private readonly SemaphoreSlim _workers = new(Math.Clamp(Environment.ProcessorCount / 2, 2, 4));
    private bool _disposed;

    public async Task<AvaloniaBitmap?> LoadAsync(string? filePath, int width, int height, CancellationToken token)
    {
        if (_disposed || token.IsCancellationRequested || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;

        try
        {
            await _workers.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        try
        {
            return await Task.Run(() => LoadCore(filePath, width, height, token));
        }
        finally
        {
            _workers.Release();
        }
    }

    private static AvaloniaBitmap? LoadCore(string filePath, int width, int height, CancellationToken token)
    {
        if (token.IsCancellationRequested) return null;

        try
        {
            using DrawingBitmap? shellThumbnail = NativeMethods.GetFileThumbnail(filePath, new DrawingSize(width, height));
            if (shellThumbnail != null)
            {
                if (token.IsCancellationRequested) return null;
                return BitmapConversionHelpers.CreatePreview(shellThumbnail, new PixelSize(width, height));
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex);
        }

        if (token.IsCancellationRequested || FileHelpers.IsVideoFile(filePath)) return null;
        using FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        AvaloniaBitmap thumbnail = AvaloniaBitmap.DecodeToWidth(stream, width);
        if (token.IsCancellationRequested)
        {
            thumbnail.Dispose();
            return null;
        }
        if (thumbnail.PixelSize.Height <= height) return thumbnail;

        thumbnail.Dispose();
        stream.Position = 0;
        AvaloniaBitmap heightScaledThumbnail = AvaloniaBitmap.DecodeToHeight(stream, height);
        if (!token.IsCancellationRequested) return heightScaledThumbnail;
        heightScaledThumbnail.Dispose();
        return null;
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
