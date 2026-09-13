using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using Microsoft.Web.WebView2.Core;
using System.Windows.Input;

namespace YoutubeCounterApp;

public partial class MainWindow : Window
{
    private MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        // XAML側に配置した2つのWebView2 (webView と chatWebView) をViewModelへ渡します
        _viewModel = new MainViewModel(webView, chatWebView);
        this.DataContext = _viewModel;

        // 1号機（巡回・ログイン用）のページ遷移完了イベント
        webView.NavigationCompleted += WebView_NavigationCompleted;
    }

    // アニメーションを開始するメソッド
    public void OpenMenu() => ((Storyboard)this.Resources["OpenMenu"]).Begin();
    public void CloseMenu() => ((Storyboard)this.Resources["CloseMenu"]).Begin();

    /// <summary>
    /// YouTubeアカウント切り替え処理
    /// </summary>
    public async Task SwitchYoutubeAccountAsync()
    {
        if (webView != null)
        {
            _viewModel._isAccountSwitching = true;

            // 1号機を表示してユーザーにアカウントを選択してもらう
            webView.Visibility = Visibility.Visible;

            if (webView.CoreWebView2 == null)
            {
                await webView.EnsureCoreWebView2Async();
            }

            // Googleのアカウント選択画面へ遷移
            string switchUrl = "https://www.youtube.com/channel_switcher";
            webView.CoreWebView2.Navigate(switchUrl);
        }
    }

    private void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        // アカウント切り替えモード中（フラグが true）のときだけ判定
        if (_viewModel._isAccountSwitching)
        {
            string currentUrl = webView.Source?.ToString() ?? "";

            // 1. Googleログイン、アカウント選択、サインイン・アウト中などの中間ページは無視する
            if (currentUrl.Contains("accounts.google.com") || 
                currentUrl.Contains("AccountChooser") || 
                currentUrl.Contains("channel_switcher") ||
                currentUrl.Contains("signin") ||
                currentUrl.Contains("logout") ||
                currentUrl.Contains("ServiceLogin"))
            {
                return;
            }

            // 2. 切り替え直後に YouTube トップページ（www.youtube.com）へ着地した場合
            // -> 自動で YouTube Studio へ飛ばす
            if (currentUrl.Contains("www.youtube.com") && !currentUrl.Contains("studio.youtube.com"))
            {
                webView.CoreWebView2.Navigate("https://studio.youtube.com");
                return;
            }

            // 3. 無事に YouTube Studio に到達したら完了処理を行う
            if (currentUrl.Contains("studio.youtube.com"))
            {
                // 1号機（WebView2）を再び非表示に戻す
                webView.Visibility = Visibility.Collapsed;

                // フラグを下ろして監視ループを再開させる
                _viewModel._isAccountSwitching = false;
            }
        }
    }

 // --------------------------------------------------
        // 自作タイトルバー用イベントハンドラー
        // --------------------------------------------------

        // タイトルバーを掴んでウィンドウ移動 ＆ ダブルクリックで最大化切り替え
        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
            }
            else if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        // 最小化ボタン
        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        // 最大化ボタン
        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        // 最大化 ↔ 通常サイズの切り替え
        private void ToggleMaximize()
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
                if (BtnMaximize != null) BtnMaximize.Content = "▢";
            }
            else
            {
                this.WindowState = WindowState.Maximized;
                if (BtnMaximize != null) BtnMaximize.Content = "❐";
            }
        }

        // 閉じるボタン
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
}