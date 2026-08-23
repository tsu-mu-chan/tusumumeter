using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
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
    private string _lastCount = "OFFLINE";
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
        // 1. WebSocket サーバー起動
        StartWebSocketServer();

        // 2. テンプレート配信用の静的ファイルサーバー起動
        StartStaticFileServer();

        // 3. WebView2 の初期化（ユーザーデータフォルダーを指定してログイン保持）
        string userDataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webview2_profile");
        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await _webView.EnsureCoreWebView2Async(env);

        _webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
       _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

       // ★ YouTube Studio のログインクッキーがあるか直接調べる
       var cookies = await _webView.CoreWebView2.CookieManager.GetCookiesAsync("https://studio.youtube.com");
       bool isLoggedIn = cookies.Any(c => c.Name == "LOGIN_INFO" || c.Name == "SID");

        // 4. ログイン済みかどうかの確認
        if (isLoggedIn)
        {
            //_webView.Visibility = Visibility.Visible;
            StatusText = "ログイン済みです。自動接続中…✨";
            IsLoginEnabled = false;
            IsStartEnabled = false;
            // 起動と同時に自動で同接チェックを開始
            await StartMonitoringAction();
        }
        else
        {
            StatusText = "初回ログインをしてね✨";
            IsLoginEnabled = true;
            IsStartEnabled = false;
        }
    }

    /// <summary>
    /// 初回ログインボタン押下時（WebView2を表示してユーザーにログインしてもらう）
    /// </summary>
    private void ExecuteLoginAction()
    {
        _webView.Visibility = Visibility.Visible;
        _webView.CoreWebView2.Navigate("https://studio.youtube.com");
        StatusText = "YouTube Studioでログインを完了させてね！";

        // ログイン完了を検知するイベント
        _webView.CoreWebView2.SourceChanged += (s, e) => {
            string url = _webView.Source.ToString();
            if (url.Contains("studio.youtube.com") && !url.Contains("accounts.google.com"))
            {
                Application.Current.Dispatcher.Invoke(() => {
                    _webView.Visibility = Visibility.Collapsed;
                    StatusText = "初回ログイン完了！「同接チェックを開始」を押してね✨";
                    IsLoginEnabled = false;
                    IsStartEnabled = true;
                });
            }
        };
    }

    /// <summary>
    /// 同接チェック自動監視ロジック
    /// </summary>
    private async Task StartMonitoringAction()
    {
        IsStartEnabled = false;
        StatusText = "配信管理画面へ移動中...";

        _webView.CoreWebView2.Navigate("https://studio.youtube.com/channel/self/livestreaming/manage");

        await WaitForPageLoadAsync();

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
            StatusText = "⚠️ 配信中のライブが見つかりませんでした。";
            IsStartEnabled = true;
            return;
        }

        StatusText = "同接チェックを開始しました！✨";

        await Task.Delay(3000);
        InjectMutationObserver();
    }

    /// <summary>
    /// DOMの変化（同接数値の変更）をリアルタイムに検知するJSを注入
    /// </summary>
    private async void InjectMutationObserver()
    {
        string observerScript = @"
            (() => {
                const observeViewer = () => {
                    const selectors = ['.label', '.title', '.value-label', 'ytcp-quick-stat .name', '.metric-name-container'];
                    const labels = Array.from(document.querySelectorAll(selectors.join(',')));
                    const target = labels.find(el => el && el.innerText && el.innerText.includes('同時視聴者数'));
                    if (!target) return;

                    const container = target.closest('ytcp-quick-stat') || target.closest('.metric-container') || target.parentElement.parentElement;
                    const valueEl = container ? container.querySelector('.value, #value, .metric-value, .value-text') : null;

                    if (valueEl) {
                        window.chrome.webview.postMessage(valueEl.innerText.trim());

                        const observer = new MutationObserver(() => {
                            window.chrome.webview.postMessage(valueEl.innerText.trim());
                        });
                        observer.observe(valueEl, { characterData: true, childList: true, subtree: true });
                    }
                };

                observeViewer();
                setInterval(observeViewer, 5000);
            })();
        ";

        await _webView.CoreWebView2.ExecuteScriptAsync(observerScript);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string rawCount = e.TryGetWebMessageAsString();
        if (string.IsNullOrEmpty(rawCount) || rawCount == "LOADING") return;

        UpdateViewerCount(rawCount);
    }

    private void UpdateViewerCount(string rawCount)
    {
        string cleanCount = new string(rawCount.Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(cleanCount)) cleanCount = "0";

        if (_lastCount != cleanCount)
        {
            _lastCount = cleanCount;
            BroadcastWebSocketMessage(_lastCount);
            StatusText = $"現在の視聴者数: {_lastCount}人 (監視中)";
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

    #region WebSocket & 静的ファイルサーバー (標準ライブラリ実装)

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

                    byte[] buffer = Encoding.UTF8.GetBytes(_lastCount);
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