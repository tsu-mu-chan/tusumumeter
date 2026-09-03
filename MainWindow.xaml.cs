using System.Windows;
using System.Windows.Media.Animation;
using Microsoft.Web.WebView2.Core;

namespace YoutubeCounterApp;

public partial class MainWindow : Window
{
    private MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(webView);
        this.DataContext = _viewModel;

        webView.NavigationCompleted += WebView_NavigationCompleted;
    }

    // アニメーションを開始するメソッド
    public void OpenMenu() => ((Storyboard)this.Resources["OpenMenu"]).Begin();
    public void CloseMenu() => ((Storyboard)this.Resources["CloseMenu"]).Begin();

        // MainWindow.xaml.cs 内に追加
    public async Task SwitchYoutubeAccountAsync()
    {
        // 1. 定期監視ループを一時停止（フラグ等があれば false に設定）
        // _isMonitoring = false; 

        if (webView != null)
        {
            _viewModel._isAccountSwitching = true;

            webView.Visibility = Visibility.Visible;
            
            if (webView.CoreWebView2 == null)
            {
                await webView.EnsureCoreWebView2Async();
            }

            // Googleのアカウント選択画面（選択後はStudioへ戻る）へ遷移
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
                // 1. WebView2 を再び隠す（非表示にする）
                webView.Visibility = Visibility.Collapsed;

                // 2. フラグを下ろして監視ループを再開させる！
                _viewModel._isAccountSwitching = false;
            }
        }
    }
}