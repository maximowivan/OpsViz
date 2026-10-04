using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpsViz.Import;

namespace OpsViz.Views
{
    public partial class DbConnectionWindow : Window
    {
        public string ConnectionString { get; private set; }

        public DbConnectionWindow(string current)
        {
            InitializeComponent();
            if (!string.IsNullOrWhiteSpace(current)) ConnBox.Text = current;
        }

        void BtnCopySnippet_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(SnippetBox.Text); }
            catch { }
        }

        void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            string cs = SqlTransferLoader.NormalizeConnectionString(ConnBox.Text);
            if (string.IsNullOrWhiteSpace(cs))
            {
                ShowStatus("Сначала вставьте строку подключения.", false);
                return;
            }
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            try
            {
                SqlTransferLoader.TestConnection(cs);
                ShowStatus("Подключено: сервер отвечает, запрос разрешен.", true);
            }
            catch (Exception ex)
            {
                ShowStatus("Не подключилось: " + ex.Message, false);
            }
            finally { Mouse.OverrideCursor = null; }
        }

        void ShowStatus(string text, bool ok)
        {
            StatusText.Text = text;
            StatusText.Foreground = ok
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0xA3, 0x4A))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDC, 0x26, 0x26));
            StatusText.Visibility = Visibility.Visible;
        }

        void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            string cs = SqlTransferLoader.NormalizeConnectionString(ConnBox.Text);
            if (string.IsNullOrWhiteSpace(cs))
            {
                ShowStatus("Строка пустая — вставьте результат из Excel или нажмите «Отмена».", false);
                return;
            }
            ConnectionString = cs;
            DialogResult = true;
        }

        void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
