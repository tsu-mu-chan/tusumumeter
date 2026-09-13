using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace YoutubeCounterApp
{
    public class AppSettings
    {
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        public static AppSettings Instance { get; set; } = new AppSettings();

        // チャット取得モード (true: すべてのチャット / false: トップチャット)
        public bool IsAllChatMode { get; set; } = true;

        // お気に入りテンプレート一覧
        public List<string> FavoriteTemplates { get; set; } = new();

        public string SkippedVersion { get; set; } = "";

        // --- 1. ポート番号設定 ---
        public int StaticFilePort { get; set; } = 8080;
        public int WebSocketPort { get; set; } = 8081;

        // --- 2. 巡回インターバル設定 (秒) ---
        public int StayDurationSeconds { get; set; } = 15;      // 配信画面での滞在秒数
        public int OfflineRetrySeconds { get; set; } = 30;      // オフライン時のリトライ待機秒数

        // --- 4. スタートアップ・最小化設定 ---
        public bool AutoStartMonitoring { get; set; } = true;   // ログイン確認後、自動で監視を開始するか

        public static void Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        Instance = loaded;
                        Logger.WriteLog("[Settings] config.json を正常に読み込みました。");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[Settings Error] 設定の読み込みに失敗しました: {ex.Message}");
            }
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
                Logger.WriteLog("[Settings] config.json を正常に保存しました。");
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[Settings Error] 設定の保存に失敗しました: {ex.Message}");
            }
        }
    }
}