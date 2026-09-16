using System;
using System.Windows;
using System.Windows.Media;

namespace YoutubeCounterApp
{
    public partial class BasicSettingsDialog : Window
    {
        public BasicSettingsDialog()
        {
            InitializeComponent();

            if (Application.Current.MainWindow is MainWindow mainWin)
            {
                this.Owner = mainWin;
                this.DataContext = mainWin.DataContext;
            }

            LoadSettingsToUi();
            this.Closed += BasicSettingsDialog_Closed;
        }

        private void LoadSettingsToUi()
        {
            var s = AppSettings.Instance;

            TbStayDuration.Text = s.StayDurationSeconds.ToString();
            TbOfflineRetry.Text = s.OfflineRetrySeconds.ToString();
            TbStaticPort.Text = s.StaticFilePort.ToString();
            TbWsPort.Text = s.WebSocketPort.ToString();

            CbAutoStart.IsChecked = s.AutoStartMonitoring;

            // スキップ状態の読み込み
            if (!string.IsNullOrEmpty(s.SkippedVersion))
            {
                CbSkipVersion.IsChecked = true;
                CbSkipVersion.IsEnabled = true;
                TxtSkipVersionDetail.Text = $"v{s.SkippedVersion} の通知を停止中（チェックを外すと次回再通知）";
                TxtSkipVersionDetail.Foreground = Brushes.DarkOrange;
            }
            else
            {
                CbSkipVersion.IsChecked = false;
                CbSkipVersion.IsEnabled = false;
                TxtSkipVersionDetail.Text = "（現在スキップ中のバージョンはありません）";
                TxtSkipVersionDetail.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
            }
        }

        private async void BtnSwitchAccount_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWin)
            {
                this.Close();
                await mainWin.SwitchYoutubeAccountAsync();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void BasicSettingsDialog_Closed(object? sender, EventArgs e)
        {
            var s = AppSettings.Instance;

            if (int.TryParse(TbStayDuration.Text, out int stay) && stay >= 5)
            {
                s.StayDurationSeconds = stay;
            }

            if (int.TryParse(TbOfflineRetry.Text, out int retry) && retry >= 5)
            {
                s.OfflineRetrySeconds = retry;
            }

            if (int.TryParse(TbStaticPort.Text, out int staticPort) && staticPort > 1024 && staticPort <= 65535)
            {
                s.StaticFilePort = staticPort;
            }

            if (int.TryParse(TbWsPort.Text, out int wsPort) && wsPort > 1024 && wsPort <= 65535)
            {
                s.WebSocketPort = wsPort;
            }

            s.AutoStartMonitoring = CbAutoStart.IsChecked ?? true;

            // チェックが外された場合はスキップバージョンをクリア
            if (CbSkipVersion.IsChecked == false && !string.IsNullOrEmpty(s.SkippedVersion))
            {
                Logger.WriteLog($"[Settings] バージョンスキップを解除しました (以前: {s.SkippedVersion})");
                s.SkippedVersion = "";
            }

            s.Save();
            Logger.WriteLog("[Settings] ダイアログ終了に伴い基本設定を更新・保存しました。");
        }
    }
}