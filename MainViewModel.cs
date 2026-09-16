using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
    private readonly WebView2 _mainWebView;
    private readonly WebView2 _chatWebView;
    private readonly LocalServerService _serverService = new();
    private readonly YouTubeCrawlerService _crawlerService;
    private readonly ListenerService _listenerService = new();

    private ListenerListWindow? _listenerWindow;
    private CancellationTokenSource? _monitoringCts;

    // --- 状態管理 ---
    private string _currentViewers = "0";
    private string _currentLikes = "0";
    private string _currentSubs = "0";
    private string _currentVideoId = "";
    private string _lastBroadcastMessage = "";

    private string _statusText = "初期化中...";
    private bool _isLoginEnabled = true;
    private bool _isStartEnabled = true;
    public bool IsForceClose { get; private set; }
    public bool _isAccountSwitching;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) 
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public bool IsLoginEnabled { get => _isLoginEnabled; set { _isLoginEnabled = value; OnPropertyChanged(); } }
    public bool IsStartEnabled { get => _isStartEnabled; set { _isStartEnabled = value; OnPropertyChanged(); } }

    // --- チャット設定 ---
    private bool _isAllChatMode = true;
    public bool IsAllChatMode
    {
        get => _isAllChatMode;
        set
        {
            if (_isAllChatMode != value)
            {
                _isAllChatMode = value;
                OnPropertyChanged();
                Logger.WriteLog($"[Config] チャットモード変更: 全チャット={value}");
                if (value) IsTopChatMode = false;
            }
        }
    }

    private bool _isTopChatMode;
    public bool IsTopChatMode
    {
        get => _isTopChatMode;
        set
        {
            if (_isTopChatMode != value)
            {
                _isTopChatMode = value;
                OnPropertyChanged();
                Logger.WriteLog($"[Config] チャットモード変更: トップチャット={value}");
                if (value) IsAllChatMode = false;
            }
        }
    }

    // --- コマンド ---
    public ICommand LoginCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand QuitCommand { get; }
    public ICommand OpenMenuCommand { get; }
    public ICommand CloseMenuCommand { get; }
    public ICommand OpenTemplateWindowCommand { get; }
    public ICommand OpenBasicSettingsWindowCommand { get; }
    public ICommand OpenListenerWindowCommand { get; }

    public MainViewModel(WebView2 mainWebView, WebView2 chatWebView)
    {
        _mainWebView = mainWebView;
        _chatWebView = chatWebView;
        _crawlerService = new YouTubeCrawlerService(_mainWebView, _chatWebView);

        AppSettings.Load();
        _isAllChatMode = AppSettings.Instance.IsAllChatMode;
        Logger.WriteLog($"[Config] 設定ロード完了 (AllChat: {_isAllChatMode}, StaticPort: {AppSettings.Instance.StaticFilePort}, WSPort: {AppSettings.Instance.WebSocketPort})");

        LoginCommand = new RelayCommand(_ => ExecuteLoginAction());
        StartCommand = new RelayCommand(async _ => await StartMonitoringAction());
        QuitCommand = new RelayCommand(async _ => await ExecuteQuit());

        OpenMenuCommand = new RelayCommand(_ => ToggleMenu(true));
        CloseMenuCommand = new RelayCommand(_ => ToggleMenu(false));
        OpenTemplateWindowCommand = new RelayCommand(_ => OpenDialog<TemplateSelectWindow>());
        OpenBasicSettingsWindowCommand = new RelayCommand(_ => OpenDialog<BasicSettingsDialog>());
        OpenListenerWindowCommand = new RelayCommand(_ => ShowListenerListWindow());

        Logger.WriteLog("[Init] MainViewModel (マルチWebView2構成) の初期化を開始します。");
        _ = InitializeAsync();
    }

    // ==================================================
    // 初期化・ライフサイクル
    // ==================================================
    private async Task InitializeAsync()
    {
        try
        {
            Logger.WriteLog($"[Init] サーバー起動中... (Static: {AppSettings.Instance.StaticFilePort}, WS: {AppSettings.Instance.WebSocketPort})");
            _serverService.Start(AppSettings.Instance.StaticFilePort, AppSettings.Instance.WebSocketPort);
            Logger.WriteLog("[Init] サーバーの起動に成功しました。");

            Logger.WriteLog("[Init] WebView2 の初期化を開始します...");
            await _crawlerService.InitializeWebViewsAsync(OnWebMessageReceived);
            Logger.WriteLog("[Init] WebView2 の初期化が完了しました。");

            bool isLoggedIn = await _crawlerService.CheckLoginStatusAsync();
            Logger.WriteLog($"[Init] クッキー確認完了。ログイン判定: {(isLoggedIn ? "ログイン済" : "未ログイン")}");

            if (isLoggedIn)
            {
                if (AppSettings.Instance.AutoStartMonitoring)
                {
                    Logger.WriteLog("[Init] 自動監視設定 (AutoStartMonitoring) が有効です。監視ループを開始します。");
                    StatusText = "ログイン済みです。自動接続中…✨";
                    IsLoginEnabled = false;
                    IsStartEnabled = false;
                    await StartMonitoringAction();
                }
                else
                {
                    StatusText = "待機中。「チェックを開始」を押してね✨";
                    IsLoginEnabled = false;
                    IsStartEnabled = true;
                }
            }
            else
            {
                StatusText = "「初回ログイン」ボタンからログインしてください✨";
                IsLoginEnabled = true;
                IsStartEnabled = false;
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLog($"[Init Error] 初期化処理中に重大な例外が発生しました: {ex.Message}\n{ex.StackTrace}");
            StatusText = "初期化中にエラーが発生しました。ログを確認してください。";
            IsLoginEnabled = true;
            IsStartEnabled = false;
        }
    }

    private void ExecuteLoginAction()
    {
        Logger.WriteLog("[Login] ユーザーがログインボタンを押下しました。ブラウザ画面を表示します。");
        _mainWebView.Visibility = Visibility.Visible;
        StatusText = "YouTube Studioでログインを完了させてね！";

        _crawlerService.NavigateLogin(() =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _mainWebView.Visibility = Visibility.Collapsed;
                StatusText = "初回ログイン完了！「チェックを開始」を押してね✨";
                IsLoginEnabled = false;
                IsStartEnabled = true;
                Logger.WriteLog("[Login] ログイン処理コールバックを受信。画面を非表示にして待機状態へ遷移しました。");
            });
        });
    }

    private async Task ExecuteQuit()
    {
        Logger.WriteLog("[App] 終了処理を開始します。");
        IsForceClose = true;
        _monitoringCts?.Cancel();
        StatusText = "終了処理中...";

        try
        {
            Logger.WriteLog("[App] リスナーデータを保存中...");
            _listenerService.Save();
            Logger.WriteLog("[App] ローカルサーバーとソケットを開放中...");
            _serverService.Dispose();
        }
        catch (Exception ex)
        {
            Logger.WriteLog($"[App Error] 終了リソース開放中に例外: {ex.Message}");
        }

        await Task.Delay(300);
        Logger.WriteLog("[App] アプリケーションをシャットダウンします。");
        Application.Current.Shutdown();
    }

    // ==================================================
    // 監視ループ
    // ==================================================
    private async Task StartMonitoringAction()
    {
        Logger.WriteLog("[Loop] 監視ループ (StartMonitoringAction) を開始します。");
        IsStartEnabled = false;

        _monitoringCts?.Cancel();
        _monitoringCts = new CancellationTokenSource();
        var token = _monitoringCts.Token;

        while (!token.IsCancellationRequested && !IsForceClose)
        {
            if (_isAccountSwitching)
            {
                Logger.WriteLog("[Loop] アカウント切り替え待機中...");
                await SafeDelay(1000, token);
                continue;
            }

            if (!await EnsureSessionValidAsync(token)) continue;

            try
            {
                // ステップ1: 登録者数取得
                if (!await ProcessSubscribersAsync()) continue;

                // ステップ2: 配信枠探索
                bool found = await ProcessLiveStreamSelectionAsync(token);
                if (!found) continue;

                // ステップ3: VIDEO_ID取得とチャット接続
                await ProcessChatAttachmentAsync();

                // ステップ4: 同接・リアクション監視
                int stayDuration = AppSettings.Instance.StayDurationSeconds;
                StatusText = "配信画面で同接・高評価・リアクションを取得中...";
                Logger.WriteLog($"[Loop] 配信画面オブザーバーを注入。{stayDuration}秒間滞在してリアルタイム監視を行います。");
                _crawlerService.InjectLiveStatsObserver();

                await SafeDelay(stayDuration * 1000, token);

                Logger.WriteLog("[Loop] 滞在時間終了。メモリ最適化を実行します。");
                _crawlerService.CleanMemoryFootprint();
            }
            catch (OperationCanceledException)
            {
                Logger.WriteLog("[Loop] キャンセル要求を検知しました。ループを抜けます。");
                break;
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[Loop Exception] 監視ループ内で例外が発生しました: {ex.Message}\n{ex.StackTrace}");
                await SafeDelay(3000, token);
            }
        }

        Logger.WriteLog("[Loop] 監視ループを正常に終了しました。");
    }

    private async Task<bool> EnsureSessionValidAsync(CancellationToken token)
    {
        bool isSessionValid = await _crawlerService.VerifyActiveSessionAsync();
        if (isSessionValid) return true;

        Logger.WriteLog("[Loop Warning] セッションが無効（または再認証画面）です。ユーザーの再ログイン待機に移行します。");
        StatusText = "セッションが切れました。「ログイン」ボタンから再ログインしてください✨";
        IsLoginEnabled = true;
        IsStartEnabled = false;

        while (!token.IsCancellationRequested && !await _crawlerService.CheckLoginStatusAsync())
        {
            await SafeDelay(2000, token);
        }

        if (token.IsCancellationRequested) return false;

        StatusText = "再ログイン完了！監視を自動再開します✨";
        Logger.WriteLog("[Loop] セッション復帰を確認しました。監視を再開します。");
        IsLoginEnabled = false;
        await SafeDelay(1000, token);
        return false;
    }

    private async Task<bool> ProcessSubscribersAsync()
    {
        Logger.WriteLog("[Loop] [ステップ1] 登録者数の取得を開始（アナリティクスへ移動）");
        StatusText = "登録者数を更新中（アナリティクスへ移動）...";

        string fetchedSub = await _crawlerService.FetchSubscriberCountAsync();
        if (_isAccountSwitching)
        {
            Logger.WriteLog("[Loop] 登録者数取得中にアカウント切り替えを検知。中断します。");
            return false;
        }

        if (!string.IsNullOrEmpty(fetchedSub))
        {
            _currentSubs = fetchedSub;
            UpdateLiveStats(_currentViewers, _currentLikes, _currentSubs);
            Logger.WriteLog($"[Loop] [ステップ1 完了] 最新登録者数: {_currentSubs}");
        }
        else
        {
            Logger.WriteLog("[Loop Warning] 登録者数の取得結果が空でした。前回の値を維持します。");
        }
        return true;
    }

    private async Task<bool> ProcessLiveStreamSelectionAsync(CancellationToken token)
    {
        Logger.WriteLog("[Loop] [ステップ2] 配信管理画面へ移動します");
        StatusText = "配信管理画面へ移動中...";

        if (_isAccountSwitching) return false;
        await _crawlerService.NavigateLiveStreamManageAsync();

        if (_isAccountSwitching) return false;
        StatusText = "配信枠を自動選択中...";

        bool found = await _crawlerService.TrySelectLiveStreamAsync(() => _isAccountSwitching || IsForceClose);
        if (_isAccountSwitching) return false;

        if (!found)
        {
            Logger.WriteLog("[Loop] 配信中のライブ枠が見つかりませんでした（オフライン状態）。");
            _currentViewers = "0";
            _currentLikes = "0";
            UpdateLiveStats(_currentViewers, _currentLikes, _currentSubs);

            int retrySeconds = AppSettings.Instance.OfflineRetrySeconds;
            StatusText = $"配信オフライン（登録者数: {_currentSubs}人）/ {retrySeconds}秒後にリトライ";
            Logger.WriteLog($"[Loop] オフライン待機開始: {retrySeconds}秒後にリトライします。");

            for (int sec = 0; sec < retrySeconds; sec++)
            {
                if (_isAccountSwitching || token.IsCancellationRequested) break;
                await SafeDelay(1000, token);
            }
            return false;
        }

        Logger.WriteLog("[Loop] [ステップ2 完了] 配信枠を選択しました。コントロールルームの読み込みを待機します。");
        await SafeDelay(3000, token);
        return true;
    }

    private async Task ProcessChatAttachmentAsync()
    {
        Logger.WriteLog("[Loop] [ステップ3] VIDEO_ID の抽出を開始します。");
        string videoId = await _crawlerService.ExtractVideoIdAsync();

        if (!string.IsNullOrEmpty(videoId))
        {
            if (videoId != _currentVideoId)
            {
                Logger.WriteLog($"[Loop] 新しいVIDEO_IDを検出: 旧={_currentVideoId} -> 新={videoId}。2号機（コメント専用）を再接続します。");
                _currentVideoId = videoId;
                await _crawlerService.AttachChatWebViewAsync(_currentVideoId, IsAllChatMode);
            }
            else
            {
                Logger.WriteLog($"[Loop] VIDEO_IDに変更はありません ({_currentVideoId})。チャット接続を維持します。");
            }
        }
        else
        {
            Logger.WriteLog("[Loop Warning] VIDEO_ID の抽出に失敗しました。チャット接続をスキップします。");
        }
    }

    private static async Task SafeDelay(int milliseconds, CancellationToken token)
    {
        try
        {
            await Task.Delay(milliseconds, token);
        }
        catch (OperationCanceledException) { }
    }

    // ==================================================
    // WebView メッセージハンドリング
    // ==================================================
    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string rawJson = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(rawJson) || rawJson == "LOADING") return;

            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
            {
                Logger.WriteLog($"[WebView Warning] typeプロパティのないメッセージを受信: {rawJson}");
                return;
            }

            string msgType = typeProp.GetString() ?? "";

            switch (msgType)
            {
                case "LIVE_STATS":
                    HandleLiveStatsMessage(root);
                    break;
                case "CHAT_MESSAGE":
                    HandleChatMessage(root, rawJson);
                    break;
                case "GIFT_EVENT":
                    HandleGiftEvent(root, rawJson);
                    break;
                case "REACTION":
                    Logger.WriteLog($"[Reaction] リアクション受信: {rawJson}");
                    _ = _serverService.BroadcastAsync(rawJson);
                    break;
                case "RAW_CHAT_DEBUG":
                    HandleRawChatDebug(root);
                    break;
                default:
                    Logger.WriteLog($"[WebView] 未処理のメッセージタイプを受信: {msgType}");
                    break;
            }
        }
        catch (JsonException jex)
        {
            Logger.WriteLog($"[WebView Error] JSONパース失敗: {jex.Message}");
        }
        catch (Exception ex)
        {
            Logger.WriteLog($"[WebView Error] メッセージ処理中に予期せぬ例外: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void HandleLiveStatsMessage(JsonElement root)
    {
        string viewers = root.TryGetProperty("viewers", out var v) ? v.GetString() ?? "0" : "0";
        string likes = root.TryGetProperty("likes", out var l) ? l.GetString() ?? "0" : "0";
        UpdateLiveStats(viewers, likes, _currentSubs);
    }

    private void HandleChatMessage(JsonElement root, string rawJson)
    {
        string author = root.TryGetProperty("author", out var a) ? a.GetString() ?? "" : "";
        string channelId = root.TryGetProperty("authorId", out var id) ? id.GetString() ?? "" : "";
        string iconUrl = root.TryGetProperty("avatar", out var av) ? av.GetString() ?? "" :
                         root.TryGetProperty("avatarUrl", out var av2) ? av2.GetString() ?? "" : "";
        bool isMember = root.TryGetProperty("isMember", out var m) && m.GetBoolean();
        int jewels = root.TryGetProperty("jewels", out var j) ? j.GetInt32() : 0;

        string broadcastJson = rawJson;

        if (!string.IsNullOrEmpty(author))
        {
            var listener = _listenerService.RecordComment(author, channelId, iconUrl, isMember);

            if (jewels > 0)
            {
                _listenerService.RecordGiftOrJewel(author, channelId, iconUrl, 0, jewels);
                Logger.WriteLog($"💎 [ジュエル検知] {author} さんから {jewels} ジュエルを受信 (累計: {listener.CurrentJewels})");
            }

            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(rawJson);
                if (dict != null)
                {
                    dict["commentCount"] = listener.CommentCount;
                    dict["isFirstTime"] = (listener.CommentCount == 1);
                    dict["currentGifts"] = listener.CurrentGifts;
                    dict["currentJewels"] = listener.CurrentJewels;
                    broadcastJson = JsonSerializer.Serialize(dict);
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[Chat Warning] クライアント用メタデータ付与失敗: {ex.Message}");
                broadcastJson = rawJson;
            }
        }

        _ = _serverService.BroadcastAsync(broadcastJson);
    }

    private void HandleGiftEvent(JsonElement root, string rawJson)
    {
        string author = root.TryGetProperty("author", out var a) ? a.GetString() ?? "" : "";
        string channelId = root.TryGetProperty("authorId", out var id) ? id.GetString() ?? "" : "";
        string iconUrl = root.TryGetProperty("avatar", out var av) ? av.GetString() ?? "" : "";
        int gifts = root.TryGetProperty("gifts", out var g) ? g.GetInt32() : 1;

        if (string.IsNullOrEmpty(author))
        {
            Logger.WriteLog("[Gift Warning] 送信者名のないメンギフイベントを受信したため破棄しました。");
            return;
        }

        var listener = _listenerService.RecordGiftOrJewel(author, channelId, iconUrl, gifts, 0);
        Logger.WriteLog($"🎁 [メンギフ検知] {author} さんからギフト {gifts} 個を受信 (枠内累計: {listener.CurrentGifts})");

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(rawJson);
            if (dict != null)
            {
                dict["currentGifts"] = listener.CurrentGifts;
                _ = _serverService.BroadcastAsync(JsonSerializer.Serialize(dict));
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLog($"[Gift Warning] メンギフ送信用JSON成形失敗: {ex.Message}");
        }

        _ = _serverService.BroadcastAsync(rawJson);
    }

    private static void HandleRawChatDebug(JsonElement root)
    {
        string tag = root.TryGetProperty("tag", out var tg) ? tg.GetString() ?? "" : "";
        string text = root.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";
        string html = root.TryGetProperty("html", out var ht) ? ht.GetString() ?? "" : "";

        if (!string.IsNullOrWhiteSpace(text))
        {
            Logger.WriteLog($"[RAW_DOM] <{tag}> Text: {text} | HTML: {html}");
        }
    }

    private void UpdateLiveStats(string viewers, string likes, string subs)
    {
        if (!string.IsNullOrEmpty(viewers)) _currentViewers = viewers;
        if (!string.IsNullOrEmpty(likes)) _currentLikes = likes;
        if (!string.IsNullOrEmpty(subs) && subs != "0") _currentSubs = subs;

        string message = $"{{\"type\":\"LIVE_STATS\", \"viewers\":\"{_currentViewers}\", \"likes\":\"{_currentLikes}\", \"subs\":\"{_currentSubs}\"}}";

        if (_lastBroadcastMessage != message)
        {
            _lastBroadcastMessage = message;
            _serverService.LastBroadcastMessage = message;
            _ = _serverService.BroadcastAsync(message);

            StatusText = $"同接: {_currentViewers}人 | 高評価: {_currentLikes} | 登録者: {_currentSubs}人";
            Logger.WriteLog($"[Stats Broadcast] 同接: {_currentViewers}人, 高評価: {_currentLikes}, 登録者: {_currentSubs}人");
        }
    }

    // ==================================================
    // 画面操作ヘルパー
    // ==================================================
    private static void ToggleMenu(bool open)
    {
        if (Application.Current.MainWindow is MainWindow main)
        {
            Logger.WriteLog($"[UI] メニューを{(open ? "開きました" : "閉じました")}");
            if (open) main.OpenMenu();
            else main.CloseMenu();
        }
    }

    private static void OpenDialog<T>() where T : Window, new()
    {
        Logger.WriteLog($"[UI] ダイアログ表示: {typeof(T).Name}");
        var dialog = new T { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
    }

    private void ShowListenerListWindow()
    {
        ToggleMenu(false);

        if (_listenerWindow != null && _listenerWindow.IsLoaded)
        {
            Logger.WriteLog("[UI] リスト画面をアクティブ化します。");
            if (_listenerWindow.WindowState == WindowState.Minimized)
            {
                _listenerWindow.WindowState = WindowState.Normal;
            }
            _listenerWindow.Activate();
            return;
        }

        Logger.WriteLog("[UI] リスト画面を新規作成・表示します。");
        _listenerWindow = new ListenerListWindow(_listenerService)
        {
            Owner = Application.Current.MainWindow
        };
        _listenerWindow.Closed += (_, _) =>
        {
            Logger.WriteLog("[UI] リスト画面が閉じられました。");
            _listenerWindow = null;
        };
        _listenerWindow.Show();
    }
}