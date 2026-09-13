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
    private readonly WebView2 _mainWebView; // 1号機：巡回・登録者数・VIDEO_ID取得用
    private readonly WebView2 _chatWebView; // 2号機：コメント専用（ポップアウトチャット固定）
    private readonly LocalServerService _serverService = new();
    private readonly YouTubeCrawlerService _crawlerService;
    private readonly ListenerService _listenerService = new();
    private ListenerListWindow? _listenerWindow = null;

    private string _currentViewers = "0";
    private string _currentLikes = "0";
    private string _currentSubs = "0";
    private string _currentVideoId = "";
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
    public ICommand OpenBasicSettingsWindowCommand { get; }
    public ICommand OpenListenerWindowCommand { get; }

    public bool _isAccountSwitching = false;

    // --- 設定連携用プロパティ ---
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
                // 💡 すべてのチャットが変更されたら、トップチャット側にも通知
                if (value)
                {
                    IsTopChatMode = false;
                }
            }
        }
    }

    private bool _isTopChatMode = false;
    public bool IsTopChatMode
    {
        get => _isTopChatMode;
        set
        {
            if (_isTopChatMode != value)
            {
                _isTopChatMode = value;
                OnPropertyChanged();
                // 💡 トップチャットが変更されたら、すべてのチャット側にも通知
                if (value)
                {
                    IsAllChatMode = false;
                }
            }
        }
    }

    public MainViewModel(WebView2 mainWebView, WebView2 chatWebView)
    {
        _mainWebView = mainWebView;
        _chatWebView = chatWebView;
        _crawlerService = new YouTubeCrawlerService(_mainWebView, _chatWebView);

        // --------------------------------------------------
        // config.json から設定をロードし、フィールドに初期反映
        // --------------------------------------------------
        AppSettings.Load();
        _isAllChatMode = AppSettings.Instance.IsAllChatMode;

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
        OpenListenerWindowCommand = new RelayCommand(_ =>
        {
            // メニューを開いていた場合は閉じる
            (Application.Current.MainWindow as MainWindow)?.CloseMenu();

            // 既に開いている場合は、手前にアクティブ化して終了
            if (_listenerWindow != null && _listenerWindow.IsLoaded)
            {
                if (_listenerWindow.WindowState == WindowState.Minimized)
                {
                    _listenerWindow.WindowState = WindowState.Normal;
                }
                _listenerWindow.Activate();
                return;
            }

            // 新規作成してモーダレス表示
            _listenerWindow = new ListenerListWindow(_listenerService)
            {
                Owner = Application.Current.MainWindow
            };

            // 閉じられたら参照をクリア
            _listenerWindow.Closed += (s, e) =>
            {
                _listenerWindow = null;
            };

            _listenerWindow.Show(); // ★ ShowDialog() から Show() へ変更
        });

        Logger.WriteLog("[Init] MainViewModel (マルチWebView2構成) のインスタンス化を開始します。");
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            Logger.WriteLog("[Init] サーバーの起動処理を開始します。");
            _serverService.Start(AppSettings.Instance.StaticFilePort, AppSettings.Instance.WebSocketPort);

            await _crawlerService.InitializeWebViewsAsync(OnWebMessageReceived);

            bool isLoggedIn = await _crawlerService.CheckLoginStatusAsync();
            Logger.WriteLog($"[Init] クッキー確認完了。ログイン判定: {isLoggedIn}");

            if (isLoggedIn)
            {
                if (AppSettings.Instance.AutoStartMonitoring)
                {
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
        }
        catch (Exception ex)
        {
            Logger.WriteLog($"[Init Error] 初期化処理中に重大な例外が発生しました: {ex}");
        }
    }

    private void ExecuteLoginAction()
    {
        Logger.WriteLog("[Login] ユーザーがログインボタンを押下しました。YouTube Studioを開きます。");
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
            });
        });
    }

    /// <summary>
    /// 1号機（巡回用）の監視ループ
    /// </summary>
    private async Task StartMonitoringAction()
    {
        Logger.WriteLog("[Loop] 監視ループ (StartMonitoringAction) を開始します。");
        IsStartEnabled = false;

        while (!IsForceClose)
        {
            if (_isAccountSwitching)
            {
                Logger.WriteLog("[Loop] アカウント切り替え中のため待機します...");
                await Task.Delay(1000);
                continue;
            }

            bool isSessionValid = await _crawlerService.VerifyActiveSessionAsync();
            if (!isSessionValid)
            {
                Logger.WriteLog("[Loop Warning] セッションが無効（または再認証画面）です。待機状態に移行します。");
                StatusText = "セッションが切れました。「ログイン」ボタンから再ログインしてください✨";
                IsLoginEnabled = true;
                IsStartEnabled = false;

                // ユーザーが再ログインを完了するまで待機（ポーリング）
                while (!IsForceClose && !await _crawlerService.CheckLoginStatusAsync())
                {
                    await Task.Delay(2000);
                }

                if (IsForceClose) break;

                StatusText = "再ログイン完了！監視を自動再開します✨";
                IsLoginEnabled = false;
                await Task.Delay(1000);
                continue;
            }

            try
            {
                // --------------------------------------------------
                // ステップ1: 登録者数を取得（アナリティクスへ移動）
                // --------------------------------------------------
                Logger.WriteLog("[Loop] ステップ1: 登録者数取得を開始します");
                StatusText = "登録者数を更新中（アナリティクスへ移動）...";

                string fetchedSub = await _crawlerService.FetchSubscriberCountAsync();
                if (_isAccountSwitching) continue;

                if (!string.IsNullOrEmpty(fetchedSub))
                {
                    _currentSubs = fetchedSub;
                    UpdateLiveStats(_currentViewers, _currentLikes, _currentSubs);
                    Logger.WriteLog($"[Loop] 取得完了 登録者数: {_currentSubs}");
                }

                // --------------------------------------------------
                // ステップ2: 配信管理画面へ移動して配信枠を選択
                // --------------------------------------------------
                Logger.WriteLog("[Loop] ステップ2: 配信管理画面へ移動します");
                StatusText = "配信管理画面へ移動中...";

                if (_isAccountSwitching) continue;
                await _crawlerService.NavigateLiveStreamManageAsync();

                if (_isAccountSwitching) continue;
                StatusText = "配信枠を自動選択中...";

                // 配信枠の自動探索（キャンセル条件をラムダ式で渡す）
                bool found = await _crawlerService.TrySelectLiveStreamAsync(() => _isAccountSwitching || IsForceClose);

                if (_isAccountSwitching) continue;

                if (!found)
                {
                    Logger.WriteLog("[Loop] 配信中のライブ枠が見つかりませんでした。");
                    _currentViewers = "0";
                    _currentLikes = "0";

                    UpdateLiveStats(_currentViewers, _currentLikes, _currentSubs);

                    // ★ 設定ファイルからリトライ秒数を取得して反映
                    int retrySeconds = AppSettings.Instance.OfflineRetrySeconds;
                    StatusText = $"配信オフライン（登録者数: {_currentSubs}人）/ {retrySeconds}秒後にリトライ";

                    for (int sec = 0; sec < retrySeconds; sec++)
                    {
                        if (_isAccountSwitching || IsForceClose) break;
                        await Task.Delay(1000);
                    }
                    continue;
                }

                Logger.WriteLog("[Loop] 配信枠の選択に成功しました。VIDEO_IDとステータスの取得を開始します。");
                await Task.Delay(3000); // 配信コントロールルームの読み込み待ち

                // --------------------------------------------------
                // ステップ3: VIDEO_IDの取得 ＆ 2号機（チャット）の接続
                // --------------------------------------------------
                string videoId = await _crawlerService.ExtractVideoIdAsync();
                if (!string.IsNullOrEmpty(videoId) && videoId != _currentVideoId)
                {
                    _currentVideoId = videoId;
                    Logger.WriteLog($"[Loop] 新しいVIDEO_IDを検出: {_currentVideoId}。2号機（コメント専用）を接続します。");
                    await _crawlerService.AttachChatWebViewAsync(_currentVideoId, IsAllChatMode);
                }

                // --------------------------------------------------
                // ステップ4: 配信管理画面で同接・高評価を監視（15秒滞在）
                // --------------------------------------------------
                StatusText = "配信画面で同接・高評価・リアクションを取得中...";
                _crawlerService.InjectLiveStatsObserver();

                await Task.Delay(AppSettings.Instance.StayDurationSeconds * 1000);

                _crawlerService.CleanMemoryFootprint();
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[Loop Exception] ループ処理中に例外が発生しました: {ex}");
                await Task.Delay(3000);
            }
        }

        Logger.WriteLog("[Loop] 監視ループを終了しました。");
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
                else if (msgType == "CHAT_MESSAGE")
                {
                    string author = root.TryGetProperty("author", out var a) ? a.GetString() ?? "" : "";
                    string channelId = root.TryGetProperty("authorId", out var id) ? id.GetString() ?? "" : "";
                    string iconUrl = "";
                    if (root.TryGetProperty("avatar", out var av)) iconUrl = av.GetString() ?? "";
                    else if (root.TryGetProperty("avatarUrl", out var av2)) iconUrl = av2.GetString() ?? "";                    
                    bool isMember = root.TryGetProperty("isMember", out var m) && m.GetBoolean();
                    int jewels = root.TryGetProperty("jewels", out var j) ? j.GetInt32() : 0;

                    string broadcastJson = rawJson;

                    if (!string.IsNullOrEmpty(author))
                    {
                        var listener = _listenerService.RecordComment(author, channelId, iconUrl, isMember);

                        // ★ ジュエルが含まれている場合は枠内ジュエル数を加算
                        if (jewels > 0)
                        {
                            _listenerService.RecordGiftOrJewel(author, channelId, iconUrl, 0, jewels);
                            Logger.WriteLog($"💎 [検知] {author} さんから {jewels} ジュエルを受信しました！");
                        }

                        // クライアント側（HTML側）で初見演出や回数バッジを出せるよう、既存JSONを拡張
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
                        catch
                        {
                            broadcastJson = rawJson;
                        }
                    }

                    _ = _serverService.BroadcastAsync(broadcastJson);
                }
                else if (msgType == "GIFT_EVENT")
                {
                    // ★ メンバーシップギフト購入の検知
                    string author = root.TryGetProperty("author", out var a) ? a.GetString() ?? "" : "";
                    string channelId = root.TryGetProperty("authorId", out var id) ? id.GetString() ?? "" : "";
                    string iconUrl = root.TryGetProperty("avatar", out var av) ? av.GetString() ?? "" : "";
                    int gifts = root.TryGetProperty("gifts", out var g) ? g.GetInt32() : 1;

                    if (!string.IsNullOrEmpty(author))
                    {
                        var listener = _listenerService.RecordGiftOrJewel(author, channelId, iconUrl, gifts, 0);
                        Logger.WriteLog($"🎁 [検知] {author} さんからメンギフ {gifts} 個を受信しました！");

                        // OBS等のオーバーレイ画面にもメンギフ通知を配信
                        try
                        {
                            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(rawJson);
                            if (dict != null)
                            {
                                dict["currentGifts"] = listener.CurrentGifts;
                                _ = _serverService.BroadcastAsync(JsonSerializer.Serialize(dict));
                            }
                        }
                        catch
                        {
                            _ = _serverService.BroadcastAsync(rawJson);
                        }
                    }
                }
                else if (msgType == "REACTION")
                {
                    // リアクション転送
                    Logger.WriteLog($"[Reaction] リアクション検知: {rawJson}");
                    _ = _serverService.BroadcastAsync(rawJson);
                }
                else if (msgType == "RAW_CHAT_DEBUG")
                {
                string tag = root.TryGetProperty("tag", out var tg) ? tg.GetString() ?? "" : "";
                string text = root.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";
                string html = root.TryGetProperty("html", out var ht) ? ht.GetString() ?? "" : "";

                // 空要素は除外してログに出力
                if (!string.IsNullOrWhiteSpace(text))
                {
                    Logger.WriteLog($"[RAW_DOM] <{tag}> Text: {text} | HTML: {html}");
                }
            }
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLog($"[MessageReceived Error] 例外が発生しました: {ex.Message}");
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
            Logger.WriteLog($"[Update] 状態更新 -> 同接: {_currentViewers}, 高評価: {_currentLikes}, 登録者: {_currentSubs}");
        }
    }

    private async Task ExecuteQuit()
    {
        IsForceClose = true;
        StatusText = "終了処理中...";

        _listenerService.Save(); // ★ リスナー情報の最終保存
        _serverService.Dispose(); // ポートとソケットを安全に開放[cite: 4]
        await Task.Delay(300); //[cite: 4]
        Application.Current.Shutdown(); //[cite: 4]
    }
}