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

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using System.ComponentModel;

namespace ShareX.AvaloniaUI.Controls;

/// <summary>
/// Builds the page, card and row layout shared by the code-built settings windows.
/// The style classes come from Theming/SettingsStyles.axaml.
/// </summary>
public static class SettingsControls
{
    public static ScrollViewer Page(string id, string title, string icon, params Control[] controls) =>
        Page(id, title, icon, double.PositiveInfinity, controls);

    /// <summary>
    /// A finite <paramref name="maxWidth"/> limits the content width and aligns the content to the left.
    /// </summary>
    public static ScrollViewer Page(string id, string title, string icon, double maxWidth, params Control[] controls)
    {
        StackPanel content = new()
        {
            Margin = new Thickness(28, 24, 28, 32),
            HorizontalAlignment = double.IsInfinity(maxWidth) ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
            MaxWidth = maxWidth
        };

        content.Children.Add(PageTitle(title, icon));

        foreach (Control control in controls)
        {
            content.Children.Add(control);
        }

        ScrollViewer page = new()
        {
            Content = content,
            IsVisible = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        SettingsSearch.SetPageId(page, id);
        return page;
    }

    public static Grid PageTitle(string title, string icon)
    {
        Grid header = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 9
        };
        header.Classes.Add("page-title");
        SettingsSearch.SetIsPageTitle(header, true);

        TextBlock iconBlock = new() { Text = icon };
        iconBlock.Classes.Add("icon");
        iconBlock.Classes.Add("page-title-icon");
        TextBlock titleBlock = new() { Text = title };
        titleBlock.Classes.Add("page-title-text");
        Grid.SetColumn(titleBlock, 1);
        header.Children.Add(iconBlock);
        header.Children.Add(titleBlock);
        return header;
    }

    /// <summary>
    /// An empty <paramref name="title"/> builds a card without a heading.
    /// </summary>
    public static Border Card(string title, params Control[] controls)
    {
        StackPanel panel = new() { Spacing = 4 };
        if (!string.IsNullOrWhiteSpace(title))
        {
            TextBlock heading = new() { Text = title };
            heading.Classes.Add("section-title");
            panel.Children.Add(heading);
        }

        foreach (Control control in controls)
        {
            panel.Children.Add(control);
        }

        Border card = new() { Child = panel };
        card.Classes.Add("section-card");
        SettingsSearch.SetIsPanel(card, true);
        return card;
    }

    /// <summary>
    /// Places a label beside an editor. The label also becomes the accessible name of the editor
    /// unless the editor already has one.
    /// </summary>
    public static Grid Row(string label, Control editor, string? tooltip = null)
    {
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("210,*"),
            ColumnSpacing = 8
        };
        TextBlock rowLabel = Label(label);
        if (tooltip != null)
        {
            ToolTip.SetTip(rowLabel, tooltip);
            ToolTip.SetTip(editor, tooltip);
        }

        if (string.IsNullOrEmpty(AutomationProperties.GetName(editor)))
        {
            AutomationProperties.SetName(editor, label.TrimEnd().TrimEnd(':', '\uFF1A').TrimEnd());
        }

        row.Children.Add(rowLabel);
        Grid.SetColumn(editor, 1);
        row.Children.Add(editor);
        return row;
    }

    public static TextBlock Label(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.Normal,
        VerticalAlignment = VerticalAlignment.Center
    };

    public static TextBlock Hint(string text)
    {
        TextBlock hint = new() { Text = text };
        hint.Classes.Add("hint");
        return hint;
    }

    public static CheckBox Check(string text, Func<bool> getter, Action<bool> setter) =>
        Check(text, new BoundValue<bool>(getter(), setter));

    public static CheckBox Check(string text, BoundValue<bool> value)
    {
        CheckBox checkBox = new() { Content = text, DataContext = value };
        checkBox.Classes.Add("setting");
        checkBox.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(BoundValue<bool>.Value))
        {
            Source = value,
            Mode = BindingMode.TwoWay
        });
        return checkBox;
    }
}

/// <summary>
/// Holds one setting value for a two-way binding and writes each change back through the setter.
/// </summary>
public sealed class BoundValue<T> : INotifyPropertyChanged
{
    private T _value;
    private readonly Action<T> _setter;

    public T Value
    {
        get => _value;
        set
        {
            if (EqualityComparer<T>.Default.Equals(_value, value))
            {
                return;
            }

            _value = value;
            _setter(value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BoundValue(T value, Action<T> setter)
    {
        _value = value;
        _setter = setter;
    }
}
