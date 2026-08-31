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

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        StartWebSocketServer();
        StartStaticFileServer();

        string userDataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webview2_profile");
        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await _webView.EnsureCoreWebView2Async(env);

        _webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        var cookies = await _webView.CoreWebView2.CookieManager.GetCookiesAsync("https://studio.youtube.com");
        bool isLoggedIn = cookies.Any(c => c.Name == "LOGIN_INFO" || c.Name == "SID");

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

    private void ExecuteLoginAction()
    {
        _webView.Visibility = Visibility.Visible;
        _webView.CoreWebView2.Navigate("https://studio.youtube.com");
        StatusText = "YouTube Studioでログインを完了させてね！";

        // イベントハンドラを一旦変数として定義
        EventHandler<CoreWebView2SourceChangedEventArgs> handler = null;

        handler = (s, e) => {
            string url = _webView.Source.ToString();
            if (url.Contains("studio.youtube.com") && !url.Contains("accounts.google.com"))
            {
                // 1回条件を満たしたら、すぐにイベント監視を解除する！
                _webView.CoreWebView2.SourceChanged -= handler;

                Application.Current.Dispatcher.Invoke(() => {
                    _webView.Visibility = Visibility.Collapsed;
                    StatusText = "初回ログイン完了！「チェックを開始」を押してね✨";
                    IsLoginEnabled = false;
                    IsStartEnabled = true;
                });
            }
        };

        // イベントの登録
        _webView.CoreWebView2.SourceChanged += handler;
    }

    /// <summary>
    /// 画面巡回型（15秒ループ）のメイン監視処理
    /// </summary>
    private async Task StartMonitoringAction()
    {
        IsStartEnabled = false;

        while (!IsForceClose)
        {
            try
            {
                // --------------------------------------------------
                // ステップ1: 提示されたロジックで登録者数を取得
                // --------------------------------------------------
                System.Diagnostics.Debug.WriteLine("[Loop] ステップ1: 登録者数取得を開始します");
                StatusText = "登録者数を更新中（アナリティクスへ移動）...";
                
                string fetchedSub = await FetchSubscriberCountAsync();
                System.Diagnostics.Debug.WriteLine($"[Loop] 取得された登録者数: {fetchedSub}");

                // --------------------------------------------------
                // ステップ2: 配信管理画面へ移動して「同接・高評価」を監視
                // --------------------------------------------------
                System.Diagnostics.Debug.WriteLine("[Loop] ステップ2: 配信管理画面へ移動します");
                StatusText = "配信管理画面へ移動中...";
                _webView.CoreWebView2.Navigate("https://studio.youtube.com/channel/self/livestreaming/manage");
                
                // ★NavigationCompletedで固まるのを防ぐため、確実なDelayに変更
                await Task.Delay(4000);

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
                    var result = await _webView.CoreWebView2.ExecuteScriptAsync(selectLiveScript);
                    if (result == "true")
                    {
                        found = true;
                        break;
                    }
                    await Task.Delay(1000);
                }

                if (!found)
                {
                    System.Diagnostics.Debug.WriteLine("[Loop] 配信枠が見つかりませんでした。リトライします。");

                    StatusText = $"登録者: {_currentSubs}人";
                    if (!string.IsNullOrEmpty(_currentSubs) && _currentSubs != "0")
                    {
                        _currentViewers = "0";
                        _currentLikes = "0";
                        UpdateLiveStats(_currentViewers, _currentLikes, _currentSubs);
                    }

                    await Task.Delay(5000);

                    StatusText = "⚠️ 配信中のライブが見つかりませんでした。5秒後にリトライします。";
                    await Task.Delay(5000);
                    continue;
                }

                System.Diagnostics.Debug.WriteLine("[Loop] 配信枠を選択完了。15秒間の同接監視に入ります。");
                StatusText = "配信画面で同接・高評価を監視中...";
                await Task.Delay(2000);

                // 配信管理画面で 5秒ごとに同接・高評価を送信するJSを注入
                InjectLiveStatsObserver();

                // 配信管理画面に15秒間滞在
                await Task.Delay(15000);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Loop Exception] ループ内でエラーが発生しました: {ex.Message}");
                await Task.Delay(3000); // エラー時も少し待って次へ
            }
        }
    }

    /// <summary>
    /// アナリティクス画面へ移動し、「現在の数を表示」から登録者数を取得する専用メソッド
    /// </summary>
    private async Task<string> FetchSubscriberCountAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[FetchSub] アナリティクスへNavigate開始");
            // 1. アナリティクス概要画面へ移動
            _webView.CoreWebView2.Navigate("https://studio.youtube.com/channel/self/analytics/tab-overview");
            
            // ★WaitForPageLoadAsync ではなく 3秒固定で待機（描画待ち）
            await Task.Delay(10000);

            System.Diagnostics.Debug.WriteLine("[FetchSub] 「現在の数を表示」ボタン検索JSを実行");
            // 2. 「現在の数を表示」ボタンを探してクリックするJS
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
            System.Diagnostics.Debug.WriteLine($"[FetchSub] クリック結果: {clicked}");

            if (clicked == "true")
            {
                await Task.Delay(3000); // ポップアップ開く待ち
            }

            System.Diagnostics.Debug.WriteLine("[FetchSub] 数字抽出JSを実行");
            // 3. 表示されたリアルタイムカウンターから数字を取得するJS
            string getSubScript = @"
                    (() => {
                        const getShadowSubCount = (root) => {
                            if (!root) return '';

                            const counterHost = root.querySelector('yta-smooth-counter, #counter');
                            if (counterHost) {
                                // カウンター内のすべてのテキスト要素（または counter-value）から数字だけを順番に抽出
                                const allNodes = Array.from(counterHost.querySelectorAll('*'));
                                let digitsStr = '';
                                
                                for (let node of allNodes) {
                                    // 子要素を持たない末端のテキストノードから数字だけを取り出す
                                    if (node.children.length === 0 && node.textContent.trim()) {
                                        const num = node.textContent.replace(/[^0-9]/g, '');
                                        if (num) {
                                            digitsStr += num;
                                        }
                                    }
                                }

                                if (digitsStr) return digitsStr; 
                            }

                            // Shadow DOM を再帰探索
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
            System.Diagnostics.Debug.WriteLine($"[FetchSub] 取得生データ: {subResult}");

            WriteLog($"[FetchSub] JS生データ: {subResult}");

            if (!string.IsNullOrEmpty(subResult) && subResult != "null" && subResult != "\"\"")
            {
                string cleanSubs = new string(subResult.Where(char.IsDigit).ToArray());
                WriteLog($"[FetchSub] 抽出結果: cleanSubs='{cleanSubs}'");

                if (!string.IsNullOrEmpty(cleanSubs) && cleanSubs != "0")
                {
                    _currentSubs = cleanSubs;
                    UpdateLiveStats(_currentViewers, _currentLikes, _currentSubs);
                    return cleanSubs;
                }
                else
                {
                    // ★【仕込み3】0判定や空文字判定でスキップされた場合
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
            System.Diagnostics.Debug.WriteLine($"[Subscriber Fetch Error] {ex.Message}");
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
        catch { }
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
        var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:8081/");
        listener.Start();

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

                    string initialMessage = !string.IsNullOrEmpty(_lastBroadcastMessage) 
                        ? _lastBroadcastMessage 
                        : $"{{\"viewers\":\"{_currentViewers}\", \"likes\":\"{_currentLikes}\", \"subs\":\"{_currentSubs}\"}}";

                    byte[] buffer = Encoding.UTF8.GetBytes(initialMessage);
                    await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);

                    _ = HandleSocketDisconnect(socket);
                }
            }
            catch { break; }
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
                catch { }
            }
        }
    }

    private void StartStaticFileServer()
    {
        Task.Run(async () =>
        {
            var listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:8080/");
            listener.Start();

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
        });
    }

    #endregion

    private async Task ExecuteQuit()
    {
        IsForceClose = true;
        StatusText = "終了処理中...";
        await Task.Delay(300);
        Application.Current.Shutdown();
    }
}