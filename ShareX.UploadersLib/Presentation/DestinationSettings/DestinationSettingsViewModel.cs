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

using ShareX.AvaloniaUI.Controls;
using ShareX.AvaloniaUI.Theming;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace ShareX.UploadersLib;

public sealed class DestinationSettingsViewModel : INotifyPropertyChanged
{
    private SettingsNavigationItem? _selectedNavigationItem;

    // Only destinations that UploadPolicy allows have a settings page.
    internal static DestinationCategoryDefinition[] Categories { get; } =
    [
        Category("file-uploaders", Localization.Strings.DestinationSettings_Category_File_uploaders, LucideIcons.file_up,
            Page("onedrive", "OneDrive", "OneDrive"),
            new DestinationPageDefinition("shared-folder", Localization.Strings.DestinationSettings_Page_Shared_folder,
                ["LocalhostAccount", "LocalhostSelected"], "SharedFolder"))
    ];

    public ObservableCollection<SettingsNavigationItem> NavigationItems { get; } = [];

    public SettingsNavigationItem? SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set
        {
            if (value is { Children.Count: > 0 })
            {
                value = value.Children[0];
            }

            if (ReferenceEquals(_selectedNavigationItem, value)) return;
            _selectedNavigationItem = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedNavigationItem)));
            SelectedPageChanged?.Invoke(value?.Id);
        }
    }

    public event Action<string?>? SelectedPageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public DestinationSettingsViewModel()
    {
        foreach (DestinationCategoryDefinition category in Categories)
        {
            NavigationItems.Add(new SettingsNavigationItem(category.Id, category.Title,
                children: category.Pages.Select(page => new SettingsNavigationItem(page.Id, page.Title))));
        }

        SelectedNavigationItem = NavigationItems[0].Children[0];
    }

    public void NavigateTo(string? pageId)
    {
        SettingsNavigationItem? item = NavigationItems.SelectMany(x => x.Children).FirstOrDefault(x => x.Id == pageId);
        if (item != null) SelectedNavigationItem = item;
    }

    private static DestinationCategoryDefinition Category(string id, string title, string icon, params DestinationPageDefinition[] pages) => new(id, title, icon, pages);
    private static DestinationPageDefinition Page(string id, string title, string prefix, params string[] aliases) => new(id, title, [prefix], aliases);
}
