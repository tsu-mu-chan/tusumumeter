using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace YoutubeCounterApp;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly WebView2 _webView;
    private readonly List<WebSocket> _sockets = new();

    private string _currentViewers = "0";
    private string _currentLikes = "0";
    private string _currentSubs = "0";
    private string _lastBroadcastMessage = "";

    private string _statusText = "初期化中...";
    private bool _isLoginEnabled = true;
    private bool _isStartEnabled = true;
    private CancellationTokenSource? _navigationTimeoutCts;
    public bool IsForceClose { get; private set; } = false;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public bool IsLoginEnabled { get => _isLoginEnabled; set { _isLoginEnabled = value; OnPropertyChanged(); } }
    public bool IsStartEnabled { get => _isStartEnabled; set { _isStartEnabled = value; OnPropertyChanged(); } }

    public ICommand LoginCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand QuitCommand { get; }
    public ICommand OpenMenuCommand { get; }
    public ICommand CloseMenuCommand { get; }
    public ICommand OpenTemplateWindowCommand { get; }

    public ICommand OpenBasicSettingsWindowCommand {get;}

    public bool _isAccountSwitching = false;

    public MainViewModel(WebView2 webView)
    {
        _webView = webView;

        LoginCommand = new RelayCommand(_ => ExecuteLoginAction());
        StartCommand = new RelayCommand(async _ => await StartMonitoringAction());
        QuitCommand = new RelayCommand(async _ => await ExecuteQuit());

        OpenMenuCommand = new RelayCommand(_ => (Application.Current.MainWindow as MainWindow)?.OpenMenu());
        CloseMenuCommand = new RelayCommand(_ => (Application.Current.MainWindow as MainWindow)?.CloseMenu());
        OpenTemplateWindowCommand = new RelayCommand(_ => {
            var templateWin = new TemplateSelectWindow { Owner = Application.Current.MainWindow };
            templateWin.ShowDialog();
            });
        OpenBasicSettingsWindowCommand = new RelayCommand(_ => {
            var basicsttingWin = new BasicSettingsDialog { Owner = Application.Current.MainWindow };
            basicsttingWin.ShowDialog();
        });
             
        WriteLog("[Init] MainViewModelのインスタンス化を開始します。");
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            WriteLog("[Init] サーバーの起動処理を開始します。");
            StartWebSocketServer();
            StartStaticFileServer();

            string userDataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webview2_profile");
            WriteLog($"[Init] WebView2環境を初期化中... プロファイルパス: {userDataFolder}");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);

            _webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            var cookies = await _webView.CoreWebView2.CookieManager.GetCookiesAsync("https://studio.youtube.com");
            bool isLoggedIn = cookies.Any(c => c.Name == "LOGIN_INFO" || c.Name == "SID");

            WriteLog($"[Init] クッキー確認完了。ログイン判定: {isLoggedIn}");

            if (isLoggedIn)
            {
                StatusText = "ログイン済みです。自動接続中…✨";
                IsLoginEnabled = false;
                IsStartEnabled = false;
                await StartMonitoringAction();
            }
            else
            {
                StatusText = "初回ログインをしてね✨";
                IsLoginEnabled = true;
                IsStartEnabled = false;
            }
        }
        catch (Exception ex)
        {
            WriteLog($"[Init Error] 初期化処理中に重大な例外が発生しました: {ex}");
        }
    }

    private void ExecuteLoginAction()
    {
        WriteLog("[Login] ユーザーがログインボタンを押下しました。YouTube Studioを開きます。");
        _webView.Visibility = Visibility.Visible;
        _webView.CoreWebView2.Navigate("https://studio.youtube.com");
        StatusText = "YouTube Studioでログインを完了させてね！";

        EventHandler<CoreWebView2SourceChangedEventArgs> handler = null;

        handler = (s, e) => {
            string url = _webView.Source.ToString();
            if (url.Contains("studio.youtube.com") && !url.Contains("accounts.google.com"))
            {
                WriteLog($"[Login] ログイン成功を検知しました。URL: {url}");
                _webView.CoreWebView2.SourceChanged -= handler;

                Application.Current.Dispatcher.Invoke(() => {
                    _webView.Visibility = Visibility.Collapsed;
                    StatusText = "初回ログイン完了！「チェックを開始」を押してね✨";
                    IsLoginEnabled = false;
                    IsStartEnabled = true;
                });
            }
        };

        _webView.CoreWebView2.SourceChanged += handler;
    }

    /// <summary>
    /// 画面巡回型（15秒ループ）のメイン監視処理
    /// </summary>
    private async Task StartMonitoringAction()
    {
        WriteLog("[Loop] 監視ループ (StartMonitoringAction) を開始します。");
        IsStartEnabled = false;

        while (!IsForceClose)
        {
            if (_isAccountSwitching)
            {
                WriteLog("[Loop] アカウント切り替え中のため待機します...");
                await Task.Delay(1000);
                continue;
            }

            try
            {
                // --------------------------------------------------
                // ステップ1: 登録者数を取得
                // --------------------------------------------------
                WriteLog("[Loop] ステップ1: 登録者数取得を開始します");
                StatusText = "登録者数を更新中（アナリティクスへ移動）...";
                
                string fetchedSub = await FetchSubscriberCountAsync();

                if (_isAccountSwitching) continue;

                WriteLog($"[Loop] 取得完了 登録者数: {fetchedSub}");

                // --------------------------------------------------
                // ステップ2: 配信管理画面へ移動して「同接・高評価」を監視
                // --------------------------------------------------
                WriteLog("[Loop] ステップ2: 配信管理画面へ移動します");
                StatusText = "配信管理画面へ移動中...";

                if (_isAccountSwitching) continue;

                _webView.CoreWebView2.Navigate("https://studio.youtube.com/channel/self/livestreaming/manage");
                
                await Task.Delay(4000);

                if (_isAccountSwitching) continue;

                StatusText = "配信枠を自動選択中...";

                string selectLiveScript = @"
                    (() => {
                        const rows = Array.from(document.querySelectorAll('ytcp-video-row'));
                        const liveRow = rows.find(row => {
                            const t = row.innerText;
                            return (t.includes('ライブ') || t.includes('Live') || t.includes('配信中')) && !t.includes('近日配信');
                        });
                        if (liveRow) {
                            const titleLink = liveRow.querySelector('#video-title') || liveRow.querySelector('a');
                            if (titleLink) { titleLink.click(); return true; }
                        }
                        return false;
                    })();
                ";

                bool found = false;
                for (int i = 0; i < 10; i++)
                {
                    if (_isAccountSwitching) break;

                    try
                    {
                        // 【対策】JavaScriptの実行がフリーズしないよう、3秒でタイムアウトを設定
                        var scriptTask = _webView.CoreWebView2.ExecuteScriptAsync(selectLiveScript);
                        var timeoutTask = Task.Delay(3000);

                        var completedTask = await Task.WhenAny(scriptTask, timeoutTask);

                        if (completedTask == scriptTask)
                        {
                            string result = await scriptTask;
                            if (result == "true")
                            {
                                found = true;
                                break;
                            }
                        }
                        else
                        {
                            WriteLog($"[Loop Warning] 配信枠検索JSの実行がタイムアウトしました ({i + 1}/10回目)");
                        }
                    }
                    catch (Exception jsEx)
                    {
                        WriteLog($"[Loop Warning] JS実行中にエラーが発生しました: {jsEx.Message}");
                    }

                    await Task.Delay(1000);
                }

                if (_isAccountSwitching) continue;

                if (!found)
                {
                    WriteLog("[Loop] 配信枠が見つかりませんでした（または読み込みタイムアウト）。リトライします。");

                    StatusText = $"登録者: {_currentSubs}人";
                    if (!string.IsNullOrEmpty(_currentSubs) && _currentSubs != "0")
                    {
                        _currentViewers = "0";
                        _currentLikes = "0";
                        UpdateLiveStats(_currentViewers, _currentLikes, _currentSubs);
                    }

                    StatusText = "⚠️ 配信中のライブが見つかりませんでした。5秒後にリトライします。";
                    await Task.Delay(5000);
                    continue;
                }

                WriteLog("[Loop] 配信枠の選択に成功しました。同接監視に入ります (15秒滞在)");
                StatusText = "配信画面で同接・高評価を取得中...";
                await Task.Delay(2000);

                if (_isAccountSwitching) continue;

                InjectLiveStatsObserver();

                await Task.Delay(15000);
            }
            catch (Exception ex)
            {
                WriteLog($"[Loop Exception] ループ処理中に例外が発生しました: {ex}");
                await Task.Delay(3000);
            }
        }

        WriteLog("[Loop] 監視ループを終了しました。");
    }

    /// <summary>
    /// アナリティクス画面へ移動し、「現在の数を表示」から登録者数を取得する専用メソッド
    /// </summary>
    private async Task<string> FetchSubscriberCountAsync()
    {
        try
        {
            WriteLog("[FetchSub] アナリティクス概要へ移動します");
            _webView.CoreWebView2.Navigate("https://studio.youtube.com/channel/self/analytics/tab-overview");
            
            await Task.Delay(10000);

            WriteLog("[FetchSub] 「現在の数を表示」ボタンの探索スクリプトを実行");
            string clickSeeLiveCountScript = @"
                (() => {
                    const findAndClickBtn = (root) => {
                        if (!root) return false;

                        const directTarget = root.querySelector('#see-explore-subscribers-link, #see_explore_subscribers_button, [id*=""see-explore-subscribers""]');
                        if (directTarget) {
                            directTarget.click();
                            return true;
                        }

                        const elements = Array.from(root.querySelectorAll('a, button, ytcp-button, ytcp-button-shape'));
                        for (let el of elements) {
                            const aria = el.getAttribute ? (el.getAttribute('aria-label') || '') : '';
                            const title = el.getAttribute ? (el.getAttribute('title') || '') : '';
                            const txt = el.innerText || el.textContent || '';

                            if (aria.includes('現在の数を表示') || title.includes('現在の数を表示') || txt.includes('現在の数を表示')) {
                                const linkParent = el.closest('a') || el;
                                linkParent.click();
                                return true;
                            }
                        }

                        const children = Array.from(root.querySelectorAll('*'));
                        for (let child of children) {
                            if (child.shadowRoot) {
                                if (findAndClickBtn(child.shadowRoot)) return true;
                            }
                        }
                        return false;
                    };

                    return findAndClickBtn(document);
                })();
            ";

            var clicked = await _webView.CoreWebView2.ExecuteScriptAsync(clickSeeLiveCountScript);
            WriteLog($"[FetchSub] ボタンクリックJS実行結果: {clicked}");

            if (clicked == "true")
            {
                await Task.Delay(3000);
            }

            WriteLog("[FetchSub] 数字抽出スクリプトを実行");
            string getSubScript = @"
                    (() => {
                        const getShadowSubCount = (root) => {
                            if (!root) return '';

                            const counterHost = root.querySelector('yta-smooth-counter, #counter');
                            if (counterHost) {
                                const allNodes = Array.from(counterHost.querySelectorAll('*'));
                                let digitsStr = '';
                                
                                for (let node of allNodes) {
                                    if (node.children.length === 0 && node.textContent.trim()) {
                                        const num = node.textContent.replace(/[^0-9]/g, '');
                                        if (num) {
                                            digitsStr += num;
                                        }
                                    }
                                }

                                if (digitsStr) return digitsStr; 
                            }

                            const children = Array.from(root.querySelectorAll('*'));
                            for (let child of children) {
                                if (child.shadowRoot) {
                                    const res = getShadowSubCount(child.shadowRoot);
                                    if (res) return res;
                                }
                            }
                            return '';
                        };

                        return getShadowSubCount(document);
                    })();
            ";

            var subResult = await _webView.CoreWebView2.ExecuteScriptAsync(getSubScript);
            WriteLog($"[FetchSub] JS生データ: {subResult}");

            if (!string.IsNullOrEmpty(subResult) && subResult != "null" && subResult != "\"\"")
            {
                string cleanSubs = new string(subResult.Where(char.IsDigit).ToArray());
                WriteLog($"[FetchSub] 数字抽出結果: cleanSubs='{cleanSubs}'");

                if (!string.IsNullOrEmpty(cleanSubs) && cleanSubs != "0")
                {
                    _currentSubs = cleanSubs;
                    UpdateLiveStats(_currentViewers, _currentLikes, _currentSubs);
                    return cleanSubs;
                }
                else
                {
                    WriteLog("[FetchSub] cleanSubsが空または0のため更新をスキップしました");
                }
            }
            else
            {
                WriteLog("[FetchSub] JSからの返り値が空/nullでした");
            }
        }
        catch (Exception ex)
        {
            WriteLog($"[Subscriber Fetch Error] 登録者数取得中に例外が発生しました: {ex}");
        }

        return _currentSubs;
    }

    private void WriteLog(string message)
    {
        try
        {
            string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log.txt");
            string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            System.IO.File.AppendAllText(logPath, logLine);
        }
        catch { /* ログ書き込みエラーでアプリを落とさないための空キャッチ */ }
    }

    /// <summary>
    /// 配信画面滞在中に【5秒おき】で同接・高評価を送信するJS
    /// </summary>
    private async void InjectLiveStatsObserver()
    {
        try
        {
            string observerScript = @"
                (() => {
                    const fetchLiveStats = () => {
                        let viewers = '0';
                        let likes = '0';

                        const allCards = Array.from(document.querySelectorAll('ytcp-quick-stat, .metric-container, div'));

                        allCards.forEach(card => {
                            const txt = card.innerText || '';
                            if (txt.includes('同時視聴者数') || txt.includes('Concurrent viewers')) {
                                const valEl = card.querySelector('.value, #value, .metric-value, .value-text');
                                if (valEl) viewers = valEl.innerText.trim();
                            }
                            if (txt.includes('高評価') || txt.includes('Likes')) {
                                const valEl = card.querySelector('.value, #value, .metric-value, .value-text');
                                if (valEl) likes = valEl.innerText.trim();
                            }
                        });

                        window.chrome.webview.postMessage(JSON.stringify({
                            type: 'LIVE_STATS',
                            viewers: viewers.replace(/[^0-9]/g, ''),
                            likes: likes.replace(/[^0-9]/g, '')
                        }));
                    };

                    fetchLiveStats();
                    setInterval(fetchLiveStats, 5000);
                })();
            ";

            await _webView.CoreWebView2.ExecuteScriptAsync(observerScript);
            WriteLog("[Observer] 5秒間隔のリアルタイム監視スクリプトの注入に成功しました");
        }
        catch (Exception ex)
        {
            WriteLog($"[Observer Error] スクリプト注入時に例外が発生しました: {ex}");
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string rawJson = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(rawJson) || rawJson == "LOADING") return;

            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("type", out var typeProp))
            {
                string msgType = typeProp.GetString() ?? "";

                if (msgType == "LIVE_STATS")
                {
                    string viewers = root.TryGetProperty("viewers", out var v) ? v.GetString() ?? "0" : "0";
                    string likes = root.TryGetProperty("likes", out var l) ? l.GetString() ?? "0" : "0";
                    UpdateLiveStats(viewers, likes, _currentSubs);
                }
            }
        }
        catch (Exception ex)
        {
            WriteLog($"[MessageReceived Error] WebMessage受信用ロジックで例外が発生しました: {ex.Message}");
        }
    }

    private void UpdateLiveStats(string viewers, string likes, string subs)
    {
        if (!string.IsNullOrEmpty(viewers)) _currentViewers = viewers;
        if (!string.IsNullOrEmpty(likes)) _currentLikes = likes;

        if (!string.IsNullOrEmpty(subs) && subs != "0")
        {
            _currentSubs = subs;
        }
        
        string message = $"{{\"viewers\":\"{_currentViewers}\", \"likes\":\"{_currentLikes}\", \"subs\":\"{_currentSubs}\"}}";

        if (_lastBroadcastMessage != message)
        {
            _lastBroadcastMessage = message;
            BroadcastWebSocketMessage(message);

            StatusText = $"同接: {_currentViewers}人 | 高評価: {_currentLikes} | 登録者: {_currentSubs}人";
            WriteLog($"[Update] 状態を更新・送信しました -> 同接: {_currentViewers}, 高評価: {_currentLikes}, 登録者: {_currentSubs}");
        }
    }

    private Task WaitForPageLoadAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = null!;
        handler = (s, e) => {
            _webView.CoreWebView2.NavigationCompleted -= handler;
            tcs.SetResult(true);
        };
        _webView.CoreWebView2.NavigationCompleted += handler;
        return tcs.Task;
    }

    #region WebSocket & 静的ファイルサーバー

    private async void StartWebSocketServer()
    {
        try
        {
            var listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:8081/");
            listener.Start();
            WriteLog("[Server] WebSocketサーバーが起動しました (http://localhost:8081/)");

            while (true)
            {
                try
                {
                    var context = await listener.GetContextAsync();
                    if (context.Request.IsWebSocketRequest)
                    {
                        var wsContext = await context.AcceptWebSocketAsync(null);
                        var socket = wsContext.WebSocket;
                        lock (_sockets) { _sockets.Add(socket); }

                        WriteLog("[Server] 新しいWebSocketクライアントが接続されました");

                        string initialMessage = !string.IsNullOrEmpty(_lastBroadcastMessage) 
                            ? _lastBroadcastMessage 
                            : $"{{\"viewers\":\"{_currentViewers}\", \"likes\":\"{_currentLikes}\", \"subs\":\"{_currentSubs}\"}}";

                        byte[] buffer = Encoding.UTF8.GetBytes(initialMessage);
                        await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);

                        _ = HandleSocketDisconnect(socket);
                    }
                }
                catch (Exception ex)
                {
                    WriteLog($"[Server Exec Error] WebSocket受け入れ時にエラーが発生しました: {ex.Message}");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            WriteLog($"[Server Start Error] WebSocketサーバーの開始に失敗しました: {ex.Message}");
        }
    }

    private async Task HandleSocketDisconnect(WebSocket socket)
    {
        var buffer = new byte[1024];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch { }
        finally
        {
            lock (_sockets) { _sockets.Remove(socket); }
            socket.Dispose();
            WriteLog("[Server] WebSocketクライアントが切断されました");
        }
    }

    private async void BroadcastWebSocketMessage(string message)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(message);
        List<WebSocket> listCopy;
        lock (_sockets) { listCopy = _sockets.ToList(); }

        foreach (var socket in listCopy)
        {
            if (socket.State == WebSocketState.Open)
            {
                try
                {
                    await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    WriteLog($"[Broadcast Error] メッセージ送信失敗: {ex.Message}");
                }
            }
        }
    }

    private void StartStaticFileServer()
    {
        Task.Run(async () =>
        {
            try
            {
                var listener = new HttpListener();
                listener.Prefixes.Add("http://localhost:8080/");
                listener.Start();
                WriteLog("[StaticServer] 静的ファイルサーバーが起動しました (http://localhost:8080/)");

                while (true)
                {
                    try
                    {
                        var context = await listener.GetContextAsync();
                        string rawPath = context.Request.Url?.LocalPath.TrimStart('/') ?? "";
                        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rawPath);

                        if (File.Exists(filePath))
                        {
                            byte[] buffer = await File.ReadAllBytesAsync(filePath);
                            context.Response.ContentLength64 = buffer.Length;
                            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        }
                        else
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                        }
                        context.Response.OutputStream.Close();
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"[StaticServer Error] 静的ファイルサーバーの開始に失敗しました: {ex.Message}");
            }
        });
    }

    #endregion

    private async Task ExecuteQuit()
    {
        WriteLog("[Quit] アプリケーションの終了処理を開始します。");
        IsForceClose = true;
        StatusText = "終了処理中...";
        await Task.Delay(300);
        Application.Current.Shutdown();
    }
}