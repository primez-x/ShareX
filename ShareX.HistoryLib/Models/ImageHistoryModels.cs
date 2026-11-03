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
using System.IO;

namespace ShareX.HistoryLib;

public sealed class ImageHistoryEntry : ThumbnailGalleryItem
{
    public ImageHistoryEntry(HistoryItem item, int thumbnailWidth, int thumbnailHeight,
        ImageHistoryThumbnailHeaderPosition headerPosition) : base(thumbnailWidth, thumbnailHeight)
    {
        Item = item;
        HeaderPosition = headerPosition switch
        {
            ImageHistoryThumbnailHeaderPosition.None => ImageHistoryThumbnailHeaderPosition.None,
            ImageHistoryThumbnailHeaderPosition.Top => ImageHistoryThumbnailHeaderPosition.Top,
            _ => ImageHistoryThumbnailHeaderPosition.Bottom
        };
    }

    public HistoryItem Item { get; }
    public override string? FilePath => Item.FilePath;
    public string FileName => string.IsNullOrWhiteSpace(Item.FileName) ? Path.GetFileName(Item.FilePath) : Item.FileName;
    public ImageHistoryThumbnailHeaderPosition HeaderPosition { get; }
    public bool ShowHeaderTop => HeaderPosition == ImageHistoryThumbnailHeaderPosition.Top;
    public bool ShowHeaderBottom => HeaderPosition == ImageHistoryThumbnailHeaderPosition.Bottom;
    public bool IsFavorite => Item.Favorite;
    public string PlaceholderIcon => IsVideo ? LucideIcons.file_video : LucideIcons.image;

    public void RefreshMetadata()
    {
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(IsVideo));
        OnPropertyChanged(nameof(ShowPlayIcon));
        OnPropertyChanged(nameof(PlaceholderIcon));
    }
}
