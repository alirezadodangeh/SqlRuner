using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace SqlRuner
{
    public class QueryHistory
    {
        public int Id { get; set; }
        public string ConnectionString { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty;
        public DateTime ExecutedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsSuccessful { get; set; }
        public int? RecordCount { get; set; }
        public string Status { get => IsSuccessful ? "✓ موفق" : "✗ ناموفق"; }
        public string RecordCountDisplay { get => RecordCount.HasValue ? RecordCount.Value.ToString() : "-"; }
    }

    public class DatabaseHelper
    {
        private readonly string _dbPath;

        public DatabaseHelper()
        {
            _dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SqlRuner", "history.db");
            var directory = Path.GetDirectoryName(_dbPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory!);
            }
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
            connection.Open();

            var createTableQuery = @"
                CREATE TABLE IF NOT EXISTS QueryHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ConnectionString TEXT NOT NULL,
                    Query TEXT NOT NULL,
                    ExecutedAt DATETIME NOT NULL,
                    ErrorMessage TEXT,
                    IsSuccessful INTEGER DEFAULT 1,
                    RecordCount INTEGER
                )";

            using var command = new SQLiteCommand(createTableQuery, connection);
            command.ExecuteNonQuery();

            // Add new columns if table already exists (for migration)
            try
            {
                using var alterCommand1 = new SQLiteCommand("ALTER TABLE QueryHistory ADD COLUMN ErrorMessage TEXT", connection);
                alterCommand1.ExecuteNonQuery();
            }
            catch { /* Column may already exist */ }

            try
            {
                using var alterCommand2 = new SQLiteCommand("ALTER TABLE QueryHistory ADD COLUMN IsSuccessful INTEGER DEFAULT 1", connection);
                alterCommand2.ExecuteNonQuery();
            }
            catch { /* Column may already exist */ }

            try
            {
                using var alterCommand3 = new SQLiteCommand("ALTER TABLE QueryHistory ADD COLUMN RecordCount INTEGER", connection);
                alterCommand3.ExecuteNonQuery();
            }
            catch { /* Column may already exist */ }
        }

        public void SaveQuery(string connectionString, string query, bool isSuccessful = true, string? errorMessage = null, int? recordCount = null)
        {
            try
            {
                using var connection = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                connection.Open();

                var insertQuery = @"
                    INSERT INTO QueryHistory (ConnectionString, Query, ExecutedAt, ErrorMessage, IsSuccessful, RecordCount)
                    VALUES (@connectionString, @query, @executedAt, @errorMessage, @isSuccessful, @recordCount)";

                using var command = new SQLiteCommand(insertQuery, connection);
                command.Parameters.AddWithValue("@connectionString", connectionString ?? string.Empty);
                command.Parameters.AddWithValue("@query", query ?? string.Empty);
                command.Parameters.AddWithValue("@executedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                command.Parameters.AddWithValue("@errorMessage", errorMessage ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@isSuccessful", isSuccessful ? 1 : 0);
                command.Parameters.AddWithValue("@recordCount", recordCount.HasValue ? (object)recordCount.Value : DBNull.Value);

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving query: {ex.Message}");
                throw;
            }
        }

        public List<QueryHistory> GetQueryHistory()
        {
            var history = new List<QueryHistory>();

            try
            {
                using var connection = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                connection.Open();

                var selectQuery = @"
                    SELECT Id, ConnectionString, Query, ExecutedAt, ErrorMessage, IsSuccessful, RecordCount
                    FROM QueryHistory
                    ORDER BY ExecutedAt DESC";

                using var command = new SQLiteCommand(selectQuery, connection);
                using var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    DateTime executedAt;
                    var executedAtValue = reader.GetValue(3);
                    
                    if (executedAtValue is DateTime dt)
                    {
                        executedAt = dt;
                    }
                    else if (executedAtValue is string str && DateTime.TryParse(str, out var parsedDate))
                    {
                        executedAt = parsedDate;
                    }
                    else
                    {
                        executedAt = DateTime.Now;
                    }

                    var errorMessage = reader.IsDBNull(4) ? null : reader.GetString(4);
                    var isSuccessful = reader.IsDBNull(5) ? true : reader.GetInt32(5) == 1;
                    var recordCount = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6);

                    history.Add(new QueryHistory
                    {
                        Id = reader.GetInt32(0),
                        ConnectionString = reader.GetString(1),
                        Query = reader.GetString(2),
                        ExecutedAt = executedAt,
                        ErrorMessage = errorMessage,
                        IsSuccessful = isSuccessful,
                        RecordCount = recordCount
                    });
                }
            }
            catch (Exception ex)
            {
                // Log error or handle it as needed
                System.Diagnostics.Debug.WriteLine($"Error loading history: {ex.Message}");
            }

            return history;
        }

        public int GetHistoryCount()
        {
            try
            {
                using var connection = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                connection.Open();

                var countQuery = "SELECT COUNT(*) FROM QueryHistory";
                using var command = new SQLiteCommand(countQuery, connection);
                var result = command.ExecuteScalar();
                return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting history count: {ex.Message}");
                return 0;
            }
        }

        public void DeleteAllHistory()
        {
            try
            {
                using var connection = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                connection.Open();

                var deleteQuery = "DELETE FROM QueryHistory";
                using var command = new SQLiteCommand(deleteQuery, connection);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting history: {ex.Message}");
                throw;
            }
        }
    }
}
