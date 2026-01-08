using System.Windows;

namespace SqlRuner
{
    public partial class ErrorDialog : Window
    {
        public ErrorDialog(string errorMessage)
        {
            InitializeComponent();
            ErrorTextBox.Text = errorMessage;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(ErrorTextBox.Text);
            MessageBox.Show("خطا در کلیپ‌بورد کپی شد.", "کپی شد", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
