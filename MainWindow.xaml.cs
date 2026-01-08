using System;
using System.Data;
using System.Data.Common;
using System.Data.SQLite;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.SqlClient;

namespace SqlRuner
{
    public partial class MainWindow : Window
    {
        private readonly DatabaseHelper _dbHelper;

        public MainWindow()
        {
            InitializeComponent();
            _dbHelper = new DatabaseHelper();
            LoadHistory();
            
            // Try to load last connection string if exists
            var lastHistory = _dbHelper.GetQueryHistory().FirstOrDefault();
            if (lastHistory != null && !string.IsNullOrEmpty(lastHistory.ConnectionString))
            {
                ConnectionStringTextBox.Text = lastHistory.ConnectionString;
            }
        }

        private void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            var connectionString = ConnectionStringTextBox.Text.Trim();

            if (string.IsNullOrEmpty(connectionString))
            {
                MessageBox.Show("لطفا Connection String را وارد کنید.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StatusTextBlock.Text = "در حال تست اتصال...";
            TestConnectionButton.IsEnabled = false;

            try
            {
                // Normalize connection string
                connectionString = NormalizeConnectionString(connectionString);
                
                // Try to connect
                var lowerConnStr = connectionString.ToLower();
                if (lowerConnStr.Contains("server=") || 
                    lowerConnStr.Contains("initial catalog=") ||
                    lowerConnStr.Contains("integrated security="))
                {
                    // SQL Server
                    using var connection = new SqlConnection(connectionString);
                    connection.Open();
                    connection.Close();
                    StatusTextBlock.Text = "✓ اتصال موفق بود!";
                    MessageBox.Show($"اتصال به SQL Server موفق بود!\n\nConnection String استفاده شده:\n{connectionString}", 
                        "اتصال موفق", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // SQLite
                    using var connection = new SQLiteConnection(connectionString);
                    connection.Open();
                    connection.Close();
                    StatusTextBlock.Text = "✓ اتصال موفق بود!";
                    MessageBox.Show($"اتصال به SQLite موفق بود!\n\nConnection String استفاده شده:\n{connectionString}", 
                        "اتصال موفق", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "✗ اتصال ناموفق";
                var errorMessage = BuildErrorMessage(ex);
                errorMessage += $"\n\n═══════════════════════════════════\nConnection String استفاده شده:\n{connectionString}";
                
                var errorDialog = new ErrorDialog(errorMessage)
                {
                    Owner = this
                };
                errorDialog.ShowDialog();
            }
            finally
            {
                TestConnectionButton.IsEnabled = true;
            }
        }

        private string NormalizeConnectionString(string connectionString)
        {
            // Replace double backslashes with single backslash (from XAML/XML)
            connectionString = connectionString.Replace("\\\\", "\\");
            
            // If it looks like a file path, convert to SQLite connection string
            if (!connectionString.Contains("=") && (connectionString.Contains(".db") || connectionString.Contains(".sqlite")))
            {
                return $"Data Source={connectionString};Version=3;";
            }
            
            return connectionString;
        }

        private void ExecuteButton_Click(object sender, RoutedEventArgs e)
        {
            var connectionString = ConnectionStringTextBox.Text.Trim();
            var query = QueryTextBox.Text.Trim();

            if (string.IsNullOrEmpty(connectionString))
            {
                MessageBox.Show("لطفا Connection String را وارد کنید.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(query))
            {
                MessageBox.Show("لطفا کوئری SQL را وارد کنید.", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StatusTextBlock.Text = "در حال اجرا...";

            try
            {
                // Execute query based on connection string
                DataTable dataTable = ExecuteQuery(connectionString, query);

                // Display results
                ResultsDataGrid.ItemsSource = dataTable.DefaultView;
                ResultsDataGrid.AutoGenerateColumns = true;

                // Save to history with success status
                try
                {
                    _dbHelper.SaveQuery(connectionString, query, isSuccessful: true, errorMessage: null);
                }
                catch (Exception saveEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Error saving successful query: {saveEx.Message}");
                }

                // Refresh history
                LoadHistory();

                StatusTextBlock.Text = $"موفق - {dataTable.Rows.Count} ردیف بازگردانده شد";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "خطا در اجرای کوئری";
                
                // Normalize connection string for error message
                var normalizedConnStr = NormalizeConnectionString(connectionString);
                
                // Build comprehensive error message
                var errorMessage = BuildErrorMessage(ex, normalizedConnStr);
                
                // Save to history with error
                try
                {
                    _dbHelper.SaveQuery(connectionString, query, isSuccessful: false, errorMessage: errorMessage);
                }
                catch (Exception saveEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Error saving failed query: {saveEx.Message}");
                }

                // Refresh history
                LoadHistory();

                // Show error dialog (copyable)
                ResultsDataGrid.ItemsSource = null;
                var errorDialog = new ErrorDialog(errorMessage)
                {
                    Owner = this
                };
                errorDialog.ShowDialog();
            }
        }

        private string BuildErrorMessage(Exception ex, string? actualConnectionString = null)
        {
            var errorMessage = $"خطا در اجرای کوئری:\n{ex.Message}";
            
            if (!string.IsNullOrEmpty(actualConnectionString))
            {
                errorMessage += $"\n\nConnection String استفاده شده:\n{actualConnectionString}";
            }
            
            // Add inner exception details
            if (ex.InnerException != null)
            {
                errorMessage += $"\n\nجزئیات بیشتر:\n{ex.InnerException.Message}";
                
                // Add stack trace for inner exception if available
                if (!string.IsNullOrEmpty(ex.InnerException.StackTrace))
                {
                    errorMessage += $"\n\nStack Trace:\n{ex.InnerException.StackTrace}";
                }
            }
            
            // Add main stack trace
            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                errorMessage += $"\n\nStack Trace اصلی:\n{ex.StackTrace}";
            }
            
            // Add helpful tips based on error type
            var lowerMessage = ex.Message.ToLower();
            if (lowerMessage.Contains("unable to open") || lowerMessage.Contains("could not find") || lowerMessage.Contains("no such file"))
            {
                errorMessage += "\n\n═══════════════════════════════════\nنکات:\n- مسیر فایل دیتابیس را بررسی کنید\n- مطمئن شوید فایل دیتابیس وجود دارد\n- دسترسی لازم برای خواندن فایل را بررسی کنید";
            }
            else if (lowerMessage.Contains("login failed") || lowerMessage.Contains("authentication") || lowerMessage.Contains("cannot open database"))
            {
                errorMessage += "\n\n═══════════════════════════════════\nنکات:\n- نام کاربری و رمز عبور را بررسی کنید\n- دسترسی به دیتابیس را بررسی کنید\n- Integrated Security را در Connection String بررسی کنید";
            }
            else if (lowerMessage.Contains("instance failure") || (lowerMessage.Contains("server") && (lowerMessage.Contains("not found") || lowerMessage.Contains("cannot connect"))))
            {
                errorMessage += "\n\n═══════════════════════════════════\nراهنمای رفع مشکل 'Instance failure':\n\n" +
                    "1️⃣ بررسی نام Instance:\n" +
                    "   • برای Named Instance: Server=نام_سرور\\نام_Instance\n" +
                    "   • برای Default Instance: Server=نام_سرور (بدون \\Instance)\n" +
                    "   • در Connection String: Data Source=DESKTOP-L87JH9I\\SQL2022\n\n" +
                    "2️⃣ بررسی SQL Server Service:\n" +
                    "   • Services.msc را باز کنید (Win+R → services.msc)\n" +
                    "   • پیدا کنید: SQL Server (SQL2022) یا SQL Server (MSSQLSERVER)\n" +
                    "   • مطمئن شوید Status = Running\n" +
                    "   • اگر متوقف است، راست کلیک → Start\n\n" +
                    "3️⃣ بررسی SQL Server Configuration Manager:\n" +
                    "   • SQL Server Configuration Manager را باز کنید\n" +
                    "   • SQL Server Services → SQL Server (Instance Name)\n" +
                    "   • بررسی کنید که Status = Running\n\n" +
                    "4️⃣ بررسی SQL Server Browser (برای Named Instances):\n" +
                    "   • در Services.msc پیدا کنید: SQL Server Browser\n" +
                    "   • مطمئن شوید Status = Running\n" +
                    "   • Startup Type باید Automatic باشد\n\n" +
                    "5️⃣ تست اتصال:\n" +
                    "   • SQL Server Management Studio (SSMS) را باز کنید\n" +
                    "   • Server name را تست کنید: DESKTOP-L87JH9I\\SQL2022\n" +
                    "   • اگر در SSMS کار می‌کند، Connection String را بررسی کنید\n\n" +
                    "6️⃣ فرمت Connection String:\n" +
                    "   • اگر در TextBox از \\\\ استفاده کردید، درست است\n" +
                    "   • یا می‌توانید از: DESKTOP-L87JH9I\\\\SQL2022\n" +
                    "   • مثال کامل:\n" +
                    "     Data Source=DESKTOP-L87JH9I\\SQL2022;Initial Catalog=Portal_NewBpms_34;Integrated Security=True;\n\n" +
                    "7️⃣ بررسی نام دقیق Instance:\n" +
                    "   • SQL Server Management Studio را باز کنید\n" +
                    "   • در Connect to Server ببینید چه Instance هایی در دسترس است\n" +
                    "   • یا از PowerShell: Get-Service | Where-Object {$_.DisplayName -like '*SQL Server*'}";
            }
            else if (lowerMessage.Contains("timeout"))
            {
                errorMessage += "\n\n═══════════════════════════════════\nنکات:\n- Connection Timeout را افزایش دهید\n- سرور را بررسی کنید\n- کوئری ممکن است خیلی طولانی باشد";
            }

            return errorMessage;
        }

        private DataTable ExecuteQuery(string connectionString, string query)
        {
            // Normalize connection string
            connectionString = NormalizeConnectionString(connectionString);

            // Try to detect database type from connection string
            var lowerConnStr = connectionString.ToLower();
            
            // Check for SQL Server
            if (lowerConnStr.Contains("server=") || 
                lowerConnStr.Contains("initial catalog=") ||
                lowerConnStr.Contains("integrated security=") ||
                (lowerConnStr.Contains("data source=") && lowerConnStr.Contains("initial catalog=")))
            {
                return ExecuteSqlServerQuery(connectionString, query);
            }
            // Check for SQLite
            else if (lowerConnStr.Contains("data source=") && lowerConnStr.Contains("version=3"))
            {
                return ExecuteSQLiteQuery(connectionString, query);
            }
            // If connection string looks like a file path (contains .db or .sqlite), treat as SQLite
            else if (connectionString.Contains(".db") || connectionString.Contains(".sqlite") || 
                     connectionString.Contains(".sqlite3"))
            {
                // If it's just a file path, convert to proper SQLite connection string
                if (!connectionString.Contains("Data Source=") && !connectionString.Contains("="))
                {
                    connectionString = $"Data Source={connectionString};Version=3;";
                }
                return ExecuteSQLiteQuery(connectionString, query);
            }
            // Default: Try SQLite first (most common)
            else
            {
                try
                {
                    // Try SQLite format
                    if (!connectionString.Contains("Data Source=") && !connectionString.Contains("="))
                    {
                        connectionString = $"Data Source={connectionString};Version=3;";
                    }
                    return ExecuteSQLiteQuery(connectionString, query);
                }
                catch (Exception sqliteEx)
                {
                    // If SQLite fails, try SQL Server
                    try
                    {
                        return ExecuteSqlServerQuery(connectionString, query);
                    }
                    catch (Exception sqlServerEx)
                    {
                        throw new Exception($"نمی‌توان به دیتابیس متصل شد.\n\nخطای SQLite: {sqliteEx.Message}\n\nخطای SQL Server: {sqlServerEx.Message}\n\nلطفا Connection String را بررسی کنید.", sqliteEx);
                    }
                }
            }
        }

        private DataTable ExecuteSQLiteQuery(string connectionString, string query)
        {
            try
            {
                using var connection = new SQLiteConnection(connectionString);
                connection.Open();

                using var adapter = new SQLiteDataAdapter(query, connection);
                var dataTable = new DataTable();
                adapter.Fill(dataTable);

                return dataTable;
            }
            catch (Exception ex)
            {
                throw new Exception($"خطا در اتصال به SQLite:\n{ex.Message}\n\nConnection String: {connectionString}", ex);
            }
        }

        private DataTable ExecuteSqlServerQuery(string connectionString, string query)
        {
            try
            {
                using var connection = new SqlConnection(connectionString);
                connection.Open();

                using var adapter = new Microsoft.Data.SqlClient.SqlDataAdapter(query, connection);
                var dataTable = new DataTable();
                adapter.Fill(dataTable);

                return dataTable;
            }
            catch (Exception ex)
            {
                throw new Exception($"خطا در اتصال به SQL Server:\n{ex.Message}\n\nConnection String: {connectionString}", ex);
            }
        }

        private void LoadHistory()
        {
            try
            {
                var history = _dbHelper.GetQueryHistory();
                HistoryListBox.ItemsSource = null; // Clear first to force refresh
                HistoryListBox.ItemsSource = history;
                
                if (history.Count > 0)
                {
                    StatusTextBlock.Text = $"تاریخچه بارگذاری شد - {history.Count} کوئری";
                }
                else
                {
                    StatusTextBlock.Text = "تاریخچه خالی است";
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "خطا در بارگذاری تاریخچه";
                MessageBox.Show($"خطا در بارگذاری تاریخچه:\n{ex.Message}\n\n{ex.StackTrace}", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }


        private void HistoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HistoryListBox.SelectedItem is QueryHistory selectedHistory)
            {
                // Populate query text box with selected history item
                QueryTextBox.Text = selectedHistory.Query;
                
                // Also update connection string if it's different
                if (ConnectionStringTextBox.Text != selectedHistory.ConnectionString)
                {
                    ConnectionStringTextBox.Text = selectedHistory.ConnectionString;
                }

                if (selectedHistory.IsSuccessful)
                {
                    StatusTextBlock.Text = $"کوئری بارگذاری شد - موفق - {selectedHistory.ExecutedAt:yyyy/MM/dd HH:mm:ss}";
                }
                else
                {
                    StatusTextBlock.Text = $"کوئری بارگذاری شد - ناموفق - {selectedHistory.ExecutedAt:yyyy/MM/dd HH:mm:ss}";
                    
                    // Show error dialog if there's an error message
                    if (!string.IsNullOrEmpty(selectedHistory.ErrorMessage))
                    {
                        var errorDialog = new ErrorDialog(selectedHistory.ErrorMessage)
                        {
                            Owner = this
                        };
                        errorDialog.ShowDialog();
                    }
                }
            }
        }
    }
}
