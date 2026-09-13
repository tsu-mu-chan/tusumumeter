using System;
using System.Windows;

namespace YoutubeCounterApp
{
    public partial class BasicSettingsDialog : Window
    {
        public BasicSettingsDialog()
        {
            InitializeComponent();

            // メイン画面の DataContext (MainViewModel) を引き継ぐ
            if (Application.Current.MainWindow is MainWindow mainWin)
            {
                this.Owner = mainWin;
                this.DataContext = mainWin.DataContext;
            }

            // 現在の設定値をUIコントロールに反映
            LoadSettingsToUi();

            // ウィンドウが閉じられたときに自動で設定を保存するハンドラーを登録
            this.Closed += BasicSettingsDialog_Closed;
        }

        /// <summary>
        /// 現在の AppSettings の値を入力欄へセットする
        /// </summary>
        private void LoadSettingsToUi()
        {
            var s = AppSettings.Instance;

            TbStayDuration.Text = s.StayDurationSeconds.ToString();
            TbOfflineRetry.Text = s.OfflineRetrySeconds.ToString();
            TbStaticPort.Text = s.StaticFilePort.ToString();
            TbWsPort.Text = s.WebSocketPort.ToString();

            CbAutoStart.IsChecked = s.AutoStartMonitoring;
        }

        /// <summary>
        /// アカウント切替ボタンをクリックした時の処理
        /// </summary>
        private async void BtnSwitchAccount_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWin)
            {
                // ダイアログを先に閉じる（Closedイベント経由でSaveも実行されます）
                this.Close();

                // メイン画面側のアカウント切替処理を実行
                await mainWin.SwitchYoutubeAccountAsync();
            }
        }

        /// <summary>
        /// CLOSEボタンをクリックした時の処理
        /// </summary>
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close(); // Close() を呼ぶことで BasicSettingsDialog_Closed が発火します
        }

        /// <summary>
        /// ダイアログが閉じられるタイミングでUIの値をバリデーションして config.json へ保存
        /// </summary>
        private void BasicSettingsDialog_Closed(object? sender, EventArgs e)
        {
            var s = AppSettings.Instance;

            // --- 入力値の検証と反映（無効な文字や小さすぎる値は既存値をキープ） ---
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

            // ファイルへ永続化
            s.Save();
            Logger.WriteLog("[Settings] ダイアログ終了に伴い基本設定を更新・保存しました。");
        }
    }
}