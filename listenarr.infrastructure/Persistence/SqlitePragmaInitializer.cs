/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence
{
    public static class SqlitePragmaInitializer
    {
        public static void ApplyPragmas(DbContext context)
        {
            Execute(context, @"PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA journal_size_limit=6144000;");
        }

        /// <summary>The database's journal mode ("wal" once <see cref="ApplyPragmas"/> has run), or null off SQLite.</summary>
        public static string? ReadJournalMode(DbContext context) =>
            Execute(context, "PRAGMA journal_mode;")?.ToString();

        private static object? Execute(DbContext context, string sql)
        {
            if (context.Database.GetDbConnection() is not SqliteConnection connection)
            {
                return null;
            }

            if (connection.State != System.Data.ConnectionState.Open)
            {
                connection.Open();
            }

            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            return cmd.ExecuteScalar();
        }
    }
}

