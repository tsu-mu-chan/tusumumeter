using System;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace YoutubeCounterApp
{
    public partial class BasicSettingsDialog : Window
    {
        public BasicSettingsDialog()
        {
            InitializeComponent();
        }

        private async void BtnSwitchAccount_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWin)
            {
                // ダイアログを先に閉じる
                this.Close();

                // メイン画面側のアカウント切替処理を実行
                await mainWin.SwitchYoutubeAccountAsync();
            }
        }
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}