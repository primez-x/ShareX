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

using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShareX.HistoryLib
{
    public class HistoryManagerSQLite : HistoryManager, IDisposable
    {
        /// <summary>
        /// Raised after items are appended, edited or deleted, on the thread that changed the history.
        /// The event is static, so subscribers keep working when the history database reconnects.
        /// </summary>
        public static event EventHandler<HistoryChangedEventArgs> HistoryChanged;

        // Callers on different threads share one SqliteConnection. Every use of the connection takes this lock.
        private readonly object connectionLock = new object();
        private SqliteConnection connection;

        public HistoryManagerSQLite(string filePath) : base(filePath)
        {
            Connect(filePath);
            EnsureDatabase();
        }

        private void Connect(string filePath)
        {
            FileHelpers.CreateDirectoryFromFilePath(filePath);

            string connectionString = $"Data Source={filePath}";
            connection = new SqliteConnection(connectionString);
            connection.Open();

            SetBusyTimeout(5000);
        }

        private void SetBusyTimeout(int milliseconds)
        {
            using (SqliteCommand cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA busy_timeout = {milliseconds};";
                cmd.ExecuteNonQuery();
            }
        }

        private void EnsureDatabase()
        {
            using (SqliteCommand cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS History (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    FileName TEXT,
    FilePath TEXT,
    DateTime TEXT,
    Type TEXT,
    Host TEXT,
    URL TEXT,
    ThumbnailURL TEXT,
    DeletionURL TEXT,
    ShortenedURL TEXT,
    Tags TEXT
);
";
                cmd.ExecuteNonQuery();
            }
        }

        internal override List<HistoryItem> Load(string dbPath)
        {
            lock (connectionLock)
            {
                using (SqliteCommand cmd = new SqliteCommand("SELECT * FROM History;", connection))
                {
                    return ReadItems(cmd);
                }
            }
        }

        /// <summary>
        /// Returns up to <paramref name="count"/> items that have a file path, newest first (highest Id first).
        /// To get the next page, pass the Id of the last returned item as <paramref name="beforeId"/>. Pass 0 for the first page.
        /// <paramref name="search"/> matches the file name or the file path. SQLite LIKE ignores case for ASCII letters only.
        /// </summary>
        public List<HistoryItem> GetFileHistoryPage(long beforeId, int count, string search)
        {
            lock (connectionLock)
            {
                using (SqliteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT * FROM History
WHERE FilePath IS NOT NULL AND FilePath <> ''
AND (@BeforeId <= 0 OR Id < @BeforeId)
AND (@Pattern = '' OR FileName LIKE @Pattern ESCAPE '!' OR FilePath LIKE @Pattern ESCAPE '!')
ORDER BY Id DESC
LIMIT @Count;";
                    cmd.Parameters.AddWithValue("@BeforeId", beforeId);
                    cmd.Parameters.AddWithValue("@Pattern", string.IsNullOrEmpty(search) ? "" : "%" + EscapeLikePattern(search) + "%");
                    cmd.Parameters.AddWithValue("@Count", count);
                    return ReadItems(cmd);
                }
            }
        }

        private static string EscapeLikePattern(string text)
        {
            return text.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_");
        }

        private static List<HistoryItem> ReadItems(SqliteCommand cmd)
        {
            List<HistoryItem> items = new List<HistoryItem>();

            using (SqliteDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    HistoryItem item = new HistoryItem()
                    {
                        Id = (long)reader["Id"],
                        FileName = reader["FileName"].ToString(),
                        FilePath = reader["FilePath"].ToString(),
                        DateTime = DateTime.Parse(reader["DateTime"].ToString()),
                        Type = reader["Type"].ToString(),
                        Host = reader["Host"].ToString(),
                        URL = reader["URL"].ToString(),
                        ThumbnailURL = reader["ThumbnailURL"].ToString(),
                        DeletionURL = reader["DeletionURL"].ToString(),
                        ShortenedURL = reader["ShortenedURL"].ToString(),
                        Tags = JsonConvert.DeserializeObject<Dictionary<string, string>>(reader["Tags"]?.ToString() ?? "{}")
                    };

                    items.Add(item);
                }
            }

            return items;
        }

        protected override bool Append(string dbPath, IEnumerable<HistoryItem> historyItems)
        {
            HistoryItem[] items = historyItems.ToArray();

            lock (connectionLock)
            {
                using (SqliteTransaction transaction = connection.BeginTransaction())
                {
                    foreach (HistoryItem item in items)
                    {
                        using (SqliteCommand cmd = connection.CreateCommand())
                        {
                            cmd.CommandText = @"
INSERT INTO History
(FileName, FilePath, DateTime, Type, Host, URL, ThumbnailURL, DeletionURL, ShortenedURL, Tags)
VALUES (@FileName, @FilePath, @DateTime, @Type, @Host, @URL, @ThumbnailURL, @DeletionURL, @ShortenedURL, @Tags);
SELECT last_insert_rowid();";
                            cmd.Parameters.AddWithValue("@FileName", item.FileName ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@FilePath", item.FilePath ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@DateTime", item.DateTime.ToString("o") ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@Type", item.Type ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@Host", item.Host ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@URL", item.URL ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@ThumbnailURL", item.ThumbnailURL ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@DeletionURL", item.DeletionURL ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@ShortenedURL", item.ShortenedURL ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@Tags", item.Tags != null ? JsonConvert.SerializeObject(item.Tags) : (object)DBNull.Value);
                            item.Id = (long)cmd.ExecuteScalar();
                        }
                    }

                    transaction.Commit();
                }
            }

            OnHistoryChanged(HistoryChangeKind.Added, items);
            return true;
        }

        public void Edit(HistoryItem item)
        {
            lock (connectionLock)
            {
                EditCore(item);
            }

            OnHistoryChanged(HistoryChangeKind.Edited, new[] { item });
        }

        private void EditCore(HistoryItem item)
        {
            using (SqliteTransaction transaction = connection.BeginTransaction())
            using (SqliteCommand cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
UPDATE History SET
FileName = @FileName,
FilePath = @FilePath,
DateTime = @DateTime,
Type = @Type,
Host = @Host,
URL = @URL,
ThumbnailURL = @ThumbnailURL,
DeletionURL = @DeletionURL,
ShortenedURL = @ShortenedURL,
Tags = @Tags
WHERE Id = @Id;";
                cmd.Parameters.AddWithValue("@FileName", item.FileName ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@FilePath", item.FilePath ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@DateTime", item.DateTime.ToString("o") ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Type", item.Type ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Host", item.Host ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@URL", item.URL ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@ThumbnailURL", item.ThumbnailURL ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@DeletionURL", item.DeletionURL ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@ShortenedURL", item.ShortenedURL ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Tags", item.Tags != null ? JsonConvert.SerializeObject(item.Tags) : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Id", item.Id);
                cmd.ExecuteNonQuery();

                transaction.Commit();
            }
        }

        public void Delete(params HistoryItem[] items)
        {
            if (items != null && items.Length > 0)
            {
                lock (connectionLock)
                {
                    using (SqliteTransaction transaction = connection.BeginTransaction())
                    using (SqliteCommand cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM History WHERE Id = @Id;";
                        SqliteParameter idParam = cmd.CreateParameter();
                        idParam.ParameterName = "@Id";
                        cmd.Parameters.Add(idParam);

                        foreach (HistoryItem item in items)
                        {
                            idParam.Value = item.Id;
                            cmd.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                }

                OnHistoryChanged(HistoryChangeKind.Deleted, items);
            }
        }

        private void OnHistoryChanged(HistoryChangeKind kind, IReadOnlyList<HistoryItem> items)
        {
            if (items.Count > 0)
            {
                HistoryChanged?.Invoke(this, new HistoryChangedEventArgs(kind, items));
            }
        }

        public void MigrateFromJSON(string jsonFilePath)
        {
            HistoryManagerJSON jsonManager = new HistoryManagerJSON(jsonFilePath);
            List<HistoryItem> items = jsonManager.Load(jsonFilePath);

            if (items.Count > 0)
            {
                Append(items);
            }
        }

        public void Dispose()
        {
            lock (connectionLock)
            {
                if (connection != null)
                {
                    connection.Close();
                    connection.Dispose();
                    SqliteConnection.ClearPool(connection);
                }
            }
        }
    }
}
