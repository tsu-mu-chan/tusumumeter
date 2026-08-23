using System.Drawing;
using System.Windows;

namespace YoutubeCounterApp
{
    public enum UpdateDialogResult
    {
        Yes,
        No,
        Skip
    }

    public partial class UpdateWindow : Window
    {
        public UpdateDialogResult Result { get; private set; } = UpdateDialogResult.No;

        public UpdateWindow(string version)
        {
            InitializeComponent();
            MessageText.Text = $"新しいバージョン ({version}) が配信されています！\nアップデートを実行しますか？";
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            Result = UpdateDialogResult.Yes;
            DialogResult = true;
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            Result = UpdateDialogResult.No;
            DialogResult = false;
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            Result = UpdateDialogResult.Skip;
            DialogResult = false;
        }
    }
}