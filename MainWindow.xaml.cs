using System;
using System.Collections.Generic;
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
        private List<QueryHistory> _allHistory = new List<QueryHistory>();
        private int _currentPage = 1;
        private const int _pageSize = 10;

        public MainWindow()
        {
            InitializeComponent();
            _dbHelper = new DatabaseHelper();
            LoadHistory();
            
            // Try to load last connection string if exists
            var allHistory = _dbHelper.GetQueryHistory();
            if (allHistory.Count > 0 && !string.IsNullOrEmpty(allHistory[0].ConnectionString))
            {
                ConnectionStringTextBox.Text = allHistory[0].ConnectionString;
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

                var recordCount = dataTable.Rows.Count;

                // Save to history with success status and record count
                try
                {
                    _dbHelper.SaveQuery(connectionString, query, isSuccessful: true, errorMessage: null, recordCount: recordCount);
                }
                catch (Exception saveEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Error saving successful query: {saveEx.Message}");
                }

                // Refresh history
                LoadHistory();

                StatusTextBlock.Text = $"موفق - {recordCount} ردیف بازگردانده شد";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "خطا در اجرای کوئری";
                
                // Normalize connection string for error message
                var normalizedConnStr = NormalizeConnectionString(connectionString);
                
                // Build comprehensive error message
                var errorMessage = BuildErrorMessage(ex, normalizedConnStr);
                
                // Save to history with error (no record count for failed queries)
                try
                {
                    _dbHelper.SaveQuery(connectionString, query, isSuccessful: false, errorMessage: errorMessage, recordCount: null);
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
                _allHistory = _dbHelper.GetQueryHistory();
                _currentPage = 1;
                UpdateHistoryDisplay();
                
                if (_allHistory.Count > 0)
                {
                    StatusTextBlock.Text = $"تاریخچه بارگذاری شد - {_allHistory.Count} کوئری";
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

        private void UpdateHistoryDisplay()
        {
            try
            {
                int totalPages = (int)Math.Ceiling((double)_allHistory.Count / _pageSize);
                
                if (totalPages == 0)
                {
                    totalPages = 1;
                }

                if (_currentPage > totalPages)
                {
                    _currentPage = totalPages;
                }

                if (_currentPage < 1)
                {
                    _currentPage = 1;
                }

                // Get items for current page
                int startIndex = (_currentPage - 1) * _pageSize;
                int endIndex = Math.Min(startIndex + _pageSize, _allHistory.Count);
                
                var pageItems = startIndex < _allHistory.Count 
                    ? _allHistory.Skip(startIndex).Take(endIndex - startIndex).ToList()
                    : new List<QueryHistory>();

                HistoryDataGrid.ItemsSource = null;
                HistoryDataGrid.ItemsSource = pageItems;

                // Update pagination controls
                PageInfoTextBlock.Text = $"صفحه {_currentPage} از {totalPages}";
                
                FirstPageButton.IsEnabled = _currentPage > 1;
                PreviousPageButton.IsEnabled = _currentPage > 1;
                NextPageButton.IsEnabled = _currentPage < totalPages;
                LastPageButton.IsEnabled = _currentPage < totalPages;

                // Update status
                if (_allHistory.Count > 0)
                {
                    StatusTextBlock.Text = $"تاریخچه: {_allHistory.Count} کوئری - صفحه {_currentPage} از {totalPages}";
                }
                else
                {
                    StatusTextBlock.Text = "تاریخچه خالی است";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطا در نمایش تاریخچه:\n{ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void FirstPageButton_Click(object sender, RoutedEventArgs e)
        {
            _currentPage = 1;
            UpdateHistoryDisplay();
        }

        private void PreviousPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                UpdateHistoryDisplay();
            }
        }

        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            int totalPages = (int)Math.Ceiling((double)_allHistory.Count / _pageSize);
            if (_currentPage < totalPages)
            {
                _currentPage++;
                UpdateHistoryDisplay();
            }
        }

        private void LastPageButton_Click(object sender, RoutedEventArgs e)
        {
            int totalPages = (int)Math.Ceiling((double)_allHistory.Count / _pageSize);
            if (totalPages > 0)
            {
                _currentPage = totalPages;
                UpdateHistoryDisplay();
            }
        }

        private void DeleteHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "آیا مطمئن هستید که می‌خواهید تمام تاریخچه را حذف کنید؟\nاین عمل قابل بازگشت نیست.",
                "تأیید حذف تاریخچه",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _dbHelper.DeleteAllHistory();
                    LoadHistory(); // Reload (will be empty now)
                    MessageBox.Show("تمام تاریخچه با موفقیت حذف شد.", "حذف موفق", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"خطا در حذف تاریخچه:\n{ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }


        private void HistoryDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HistoryDataGrid.SelectedItem is QueryHistory selectedHistory)
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
                    var recordInfo = selectedHistory.RecordCount.HasValue 
                        ? $" - {selectedHistory.RecordCount} ردیف" 
                        : "";
                    StatusTextBlock.Text = $"کوئری بارگذاری شد - موفق{recordInfo} - {selectedHistory.ExecutedAt:yyyy/MM/dd HH:mm:ss}";
                }
                else
                {
                    StatusTextBlock.Text = $"کوئری بارگذاری شد - ناموفق - {selectedHistory.ExecutedAt:yyyy/MM/dd HH:mm:ss} (دوبار کلیک کنید تا خطا را ببینید)";
                }
            }
        }

        private void HistoryDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (HistoryDataGrid.SelectedItem is QueryHistory selectedHistory)
            {
                // Show error dialog if there's an error message
                if (!selectedHistory.IsSuccessful && !string.IsNullOrEmpty(selectedHistory.ErrorMessage))
                {
                    var errorDialog = new ErrorDialog(selectedHistory.ErrorMessage)
                    {
                        Owner = this
                    };
                    errorDialog.ShowDialog();
                }
                else if (selectedHistory.IsSuccessful)
                {
                    var recordInfo = selectedHistory.RecordCount.HasValue 
                        ? $" - {selectedHistory.RecordCount} ردیف بازگردانده شد" 
                        : "";
                    MessageBox.Show($"این کوئری با موفقیت اجرا شده است.{recordInfo}", 
                        "کوئری موفق", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
    }
}
