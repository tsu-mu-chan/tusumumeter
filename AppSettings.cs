using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace YoutubeCounterApp // 名前空間はプロジェクトに合わせてね
{
    public class AppSettings
    {
        public static AppSettings Instance { get; set; } = new AppSettings();
        public List<string> FavoriteTemplates { get; set; } = new List<string>();
        /// <summary>
        /// ユーザーがスキップを選択したバージョン
        /// </summary>
        public string SkippedVersion { get; set; } = "";

        /// <summary>
        /// 設定を config.json から読み込むメソッド
        /// </summary>
        public static void Load()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        Instance = loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }
        }
        /// <summary>
        /// 設定を config.json に保存するメソッド
        /// </summary>
        public void Save()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }
    }
}