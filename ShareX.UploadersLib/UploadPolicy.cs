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
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShareX.UploadersLib
{
    /// <summary>
    /// Allowlist of upload destinations. Only destinations the user controls are allowed:
    /// the user's own OneDrive (private links only) and a local or network shared folder.
    /// Public image, text and file hosts, URL shorteners, URL sharing services and custom uploaders are blocked.
    /// <see cref="UploaderFactory"/> registers only allowed services, so a blocked destination has no uploader.
    /// </summary>
    public static class UploadPolicy
    {
        public const FileDestination DefaultFileDestination = FileDestination.OneDrive;

        public static bool IsAllowed(Enum destination) => destination switch
        {
            ImageDestination image => image == ImageDestination.FileUploader,
            TextDestination text => text == TextDestination.FileUploader,
            FileDestination file => file is FileDestination.OneDrive or FileDestination.SharedFolder,
            _ => false
        };

        public static IEnumerable<T> GetAllowed<T>() where T : struct, Enum => Helpers.GetEnums<T>().Where(x => IsAllowed(x));
    }
}
