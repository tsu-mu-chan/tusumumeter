using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace YoutubeCounterApp
{
    public class YouTubeCrawlerService
    {
        private readonly WebView2 _mainWebView;
        private readonly WebView2 _chatWebView;

        public YouTubeCrawlerService(WebView2 mainWebView, WebView2 chatWebView)
        {
            _mainWebView = mainWebView;
            _chatWebView = chatWebView;
        }

        public async Task InitializeWebViewsAsync(EventHandler<CoreWebView2WebMessageReceivedEventArgs> onMessageReceived)
        {
            string userDataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webview2_profile");
            Logger.WriteLog($"[Init] WebView2環境を初期化中... プロファイルパス: {userDataFolder}");

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);

            await _mainWebView.EnsureCoreWebView2Async(env);
            await _chatWebView.EnsureCoreWebView2Async(env);

            string userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
            _mainWebView.CoreWebView2.Settings.UserAgent = userAgent;
            _chatWebView.CoreWebView2.Settings.UserAgent = userAgent;

            _mainWebView.CoreWebView2.WebMessageReceived += onMessageReceived;
            _chatWebView.CoreWebView2.WebMessageReceived += onMessageReceived;
        }

        public async Task<bool> CheckLoginStatusAsync()
        {
            var cookies = await _mainWebView.CoreWebView2.CookieManager.GetCookiesAsync("https://studio.youtube.com");
            return cookies.Any(c => c.Name == "LOGIN_INFO" || c.Name == "SID");
        }

        public void NavigateLogin(Action onLoginSuccess)
        {
            _mainWebView.CoreWebView2.Navigate("https://studio.youtube.com");

            EventHandler<CoreWebView2SourceChangedEventArgs>? handler = null;
            handler = (s, e) =>
            {
                string url = _mainWebView.Source.ToString();
                if (url.Contains("studio.youtube.com") && !url.Contains("accounts.google.com"))
                {
                    Logger.WriteLog($"[Login] ログイン成功を検知しました。URL: {url}");
                    _mainWebView.CoreWebView2.SourceChanged -= handler;
                    onLoginSuccess.Invoke();
                }
            };
            _mainWebView.CoreWebView2.SourceChanged += handler;
        }

        public async Task<string> FetchSubscriberCountAsync()
        {
            try
            {
                _mainWebView.CoreWebView2.Navigate("https://studio.youtube.com/channel/self/analytics/tab-overview");
                await Task.Delay(8000);

                await _mainWebView.CoreWebView2.ExecuteScriptAsync(YouTubeScripts.ClickSeeLiveCount);
                await Task.Delay(3000);

                var subResult = await _mainWebView.CoreWebView2.ExecuteScriptAsync(YouTubeScripts.GetSubscriberCount);
                if (!string.IsNullOrEmpty(subResult) && subResult != "null" && subResult != "\"\"")
                {
                    string cleanSubs = new string(subResult.Where(char.IsDigit).ToArray());
                    if (!string.IsNullOrEmpty(cleanSubs) && cleanSubs != "0")
                    {
                        return cleanSubs;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[Subscriber Fetch Error] 登録者数取得時例外: {ex}");
            }
            return string.Empty;
        }

        public async Task NavigateLiveStreamManageAsync()
        {
            _mainWebView.CoreWebView2.Navigate("https://studio.youtube.com/channel/self/livestreaming/manage");
            await Task.Delay(5000);
        }

        public async Task<bool> TrySelectLiveStreamAsync(Func<bool> isCancelling)
        {
            for (int i = 0; i < 10; i++)
            {
                if (isCancelling()) return false;

                try
                {
                    var scriptTask = _mainWebView.CoreWebView2.ExecuteScriptAsync(YouTubeScripts.SelectLive);
                    var timeoutTask = Task.Delay(3000);

                    var completedTask = await Task.WhenAny(scriptTask, timeoutTask);
                    if (completedTask == scriptTask && await scriptTask == "true")
                    {
                        return true;
                    }
                }
                catch (Exception jsEx)
                {
                    Logger.WriteLog($"[Loop Warning] JS実行中にエラーが発生しました: {jsEx.Message}");
                }

                await Task.Delay(1000);
            }
            return false;
        }

        public async Task<string> ExtractVideoIdAsync()
        {
            try
            {
                string videoId = await _mainWebView.CoreWebView2.ExecuteScriptAsync(YouTubeScripts.ExtractVideoId);
                if (string.IsNullOrEmpty(videoId) || videoId == "null") return "";
                return videoId.Trim('"');
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[ExtractVideoId Error] 例外発生: {ex.Message}");
                return "";
            }
        }

        public async Task AttachChatWebViewAsync(string videoId, bool isAllChatMode)
        {
            try
            {
                string popoutUrl = $"https://www.youtube.com/live_chat?is_popout=1&v={videoId}";
                Logger.WriteLog($"[ChatWebView] 一般ポップアウトチャットへ接続中: {popoutUrl}");

                var tcs = new TaskCompletionSource<bool>();
                EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;

                handler = (s, e) =>
                {
                    _chatWebView.CoreWebView2.NavigationCompleted -= handler;
                    tcs.TrySetResult(e.IsSuccess);
                };

                _chatWebView.CoreWebView2.NavigationCompleted += handler;
                _chatWebView.CoreWebView2.Navigate(popoutUrl);

                await Task.WhenAny(tcs.Task, Task.Delay(10000));
                await Task.Delay(2000);

                await SwitchChatModeAsync(isAllChatMode);
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[ChatWebView Error] 2号機の接続中に例外が発生しました: {ex}");
            }
        }

        public async Task SwitchChatModeAsync(bool isAllChat)
        {
            try
            {
                if (_chatWebView?.CoreWebView2 == null) return;

                Logger.WriteLog($"[ChatMode] チャット表示モードを『{(isAllChat ? "すべてのチャット" : "トップチャット")}』へ切り替え試行中...");
                string switchScript = YouTubeScripts.CreateSwitchChatModeScript(isAllChat);
                string result = await _chatWebView.CoreWebView2.ExecuteScriptAsync(switchScript);
                Logger.WriteLog($"[ChatMode] 切り替えスクリプト実行結果: {result}");

                await Task.Delay(2000);

                await InjectChatObserverAsync();
                InjectChatAndReactionObserver();
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[ChatMode Error] モード切り替え中にエラーが発生しました: {ex.Message}");
            }
        }

        public async Task InjectChatObserverAsync()
        {
            if (_chatWebView?.CoreWebView2 == null) return;
            await _chatWebView.CoreWebView2.ExecuteScriptAsync(YouTubeScripts.ChatObserver);
            Logger.WriteLog("[ChatWebView] コメントObserverを注入/更新しました⚡");
        }

        public async void InjectLiveStatsObserver()
        {
            try
            {
                await _mainWebView.CoreWebView2.ExecuteScriptAsync(YouTubeScripts.LiveStatsObserver);
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[Observer Error] 1号機ステータス取得例外: {ex}");
            }
        }

        public async void InjectChatAndReactionObserver()
        {
            try
            {
                if (_chatWebView?.CoreWebView2 == null) return;
                await _chatWebView.CoreWebView2.ExecuteScriptAsync(YouTubeScripts.ChatAndReactionObserver);
                Logger.WriteLog("[2号機] リアクションObserverを注入しました⚡");
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[ChatObserver Error] 2号機Observer注入例外: {ex.Message}");
            }
        }

        /// <summary>
        /// 巡回用WebView2がログイン画面へ弾かれていないかを検証
        /// </summary>
        public async Task<bool> VerifyActiveSessionAsync()
        {
            try
            {
                // クッキーの存在チェック
                bool hasCookies = await CheckLoginStatusAsync();
                if (!hasCookies) return false;

                // 現在のURLがログイン画面やアカウント選択画面になっていないかチェック
                string currentUrl = string.Empty;
                await _mainWebView.Dispatcher.InvokeAsync(() =>
                {
                    currentUrl = _mainWebView.Source?.ToString() ?? string.Empty;
                });

                if (currentUrl.Contains("accounts.google.com") || currentUrl.Contains("signin"))
                {
                    Logger.WriteLog($"[Session] アカウント再認証画面への遷移を検知しました: {currentUrl}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[Session Error] セッション確認中に例外: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 長時間運用時のメモリ消費を抑制するため、ブラウザキャッシュとガベージコレクションを実行
        /// </summary>
        public void CleanMemoryFootprint()
        {
            try
            {
                // .NET側の不要オブジェクトを回収
                GC.Collect(2, GCCollectionMode.Optimized, false);
                GC.WaitForPendingFinalizers();
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[Memory Clean Error] {ex.Message}");
            }
        }
    }
}