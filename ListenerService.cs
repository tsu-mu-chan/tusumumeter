using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace YoutubeCounterApp
{
    public class ListenerService
    {
        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "listeners.json");
        private readonly ConcurrentDictionary<string, ListenerInfo> _listeners = new();
        private bool _isDirty = false;

        public event Action<ListenerInfo>? ListenerUpdated;
        public DateTime SessionStartTime { get; private set; } = DateTime.Now;

        public ListenerService()
        {
            Load();
        }

        private static string NormalizeKey(string authorName, string channelId)
        {
            if (!string.IsNullOrWhiteSpace(authorName))
            {
                return authorName.Trim().ToLowerInvariant();
            }
            return !string.IsNullOrWhiteSpace(channelId) ? channelId.Trim().ToLowerInvariant() : "unknown";
        }

        /// <summary>
        /// デフォルトの人型アイコン、プレースホルダー画像、空URLを判定するガード
        /// </summary>
        private static bool IsDefaultOrBlankAvatar(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return true;
            string lower = url.ToLowerInvariant();
            return lower.StartsWith("data:") 
                || lower.Contains("default_user") 
                || lower.Contains("silhouette") 
                || lower.Contains("photo.jpg") 
                || lower.Contains("avatar_initial")
                || lower.Contains("yt3.ggpht.com/a/default-user");
        }

        public void Load()
        {
            if (!File.Exists(FilePath)) return;

            try
            {
                string json = File.ReadAllText(FilePath);
                var list = JsonSerializer.Deserialize<List<ListenerInfo>>(json);
                if (list != null)
                {
                    _listeners.Clear();
                    foreach (var item in list)
                    {
                        string key = NormalizeKey(item.Name, item.ChannelId);

                        _listeners.AddOrUpdate(key, item, (_, existing) =>
                        {
                            existing.CommentCount += item.CommentCount;
                            existing.CurrentGifts += item.CurrentGifts;
                            existing.CurrentJewels += item.CurrentJewels;

                            // 有効なカスタムアイコン画像を持っている方を優先して生き残らせる
                            if (IsDefaultOrBlankAvatar(existing.IconUrl) && !IsDefaultOrBlankAvatar(item.IconUrl))
                            {
                                existing.IconUrl = item.IconUrl;
                            }

                            if (item.LastSeen > existing.LastSeen) existing.LastSeen = item.LastSeen;
                            if (item.FirstSeen < existing.FirstSeen && item.FirstSeen != default) existing.FirstSeen = item.FirstSeen;
                            if (item.IsMember) existing.IsMember = true;
                            if (string.IsNullOrWhiteSpace(existing.Memo) && !string.IsNullOrWhiteSpace(item.Memo)) existing.Memo = item.Memo;
                            return existing;
                        });
                    }
                    Logger.WriteLog($"[Listener] リスナーデータを {_listeners.Count} 件ロードしました（重複統合済）。");
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[Listener Error] ロード失敗: {ex.Message}");
            }
        }

        public void Save()
        {
            if (!_isDirty && File.Exists(FilePath)) return;

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_listeners.Values, options);
                File.WriteAllText(FilePath, json);
                _isDirty = false;
                Logger.WriteLog($"[Listener] リスナーデータを保存しました ({_listeners.Count} 件)。");
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[Listener Error] 保存失敗: {ex.Message}");
            }
        }

        public ListenerInfo RecordComment(string authorName, string channelId, string iconUrl, bool isMember)
        {
            if (string.IsNullOrWhiteSpace(authorName)) return null!;

            string key = NormalizeKey(authorName, channelId);
            var now = DateTime.Now;

            var info = _listeners.AddOrUpdate(
                key,
                _ =>
                {
                    var newListener = new ListenerInfo
                    {
                        ChannelId = channelId,
                        Name = authorName,
                        IconUrl = !IsDefaultOrBlankAvatar(iconUrl) ? iconUrl : "",
                        CommentCount = 1,
                        FirstSeen = now,
                        LastSeen = now,
                        IsMember = isMember
                    };
                    newListener.RecordTimestamp(now);
                    return newListener;
                },
                (_, existing) =>
                {
                    existing.Name = authorName;
                    if (!string.IsNullOrEmpty(channelId)) existing.ChannelId = channelId;

                    // 有効なアバター画像のみ更新を許可（人型やブランクによる上書きを遮断）
                    if (!IsDefaultOrBlankAvatar(iconUrl))
                    {
                        if (existing.IconUrl != iconUrl)
                        {
                            Logger.WriteLog($"[Icon Updated] {authorName}: '{existing.IconUrl}' -> '{iconUrl}'");
                            existing.IconUrl = iconUrl;
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(existing.IconUrl))
                    {
                        Logger.WriteLog($"[Icon Guarded] {authorName} の既存アイコンを保護しました");
                    }

                    existing.CommentCount++;
                    existing.LastSeen = now;
                    existing.RecordTimestamp(now); // 活動時間帯の蓄積
                    if (isMember) existing.IsMember = true;
                    return existing;
                }
            );

            _isDirty = true;
            ListenerUpdated?.Invoke(info);
            return info;
        }

        public ListenerInfo RecordGiftOrJewel(string authorName, string channelId, string iconUrl, int gifts, int jewels)
        {
            if (string.IsNullOrWhiteSpace(authorName)) return null!;

            string key = NormalizeKey(authorName, channelId);
            var now = DateTime.Now;

            var info = _listeners.AddOrUpdate(
                key,
                _ => new ListenerInfo
                {
                    ChannelId = channelId,
                    Name = authorName,
                    IconUrl = !IsDefaultOrBlankAvatar(iconUrl) ? iconUrl : "",
                    CommentCount = 0,
                    CurrentGifts = gifts,
                    CurrentJewels = jewels,
                    FirstSeen = now,
                    LastSeen = now,
                    IsMember = gifts > 0
                },
                (_, existing) =>
                {
                    existing.Name = authorName;
                    if (!string.IsNullOrEmpty(channelId)) existing.ChannelId = channelId;

                    // ギフト・ジュエル時も人型画像による上書きを完全ブロック
                    if (!IsDefaultOrBlankAvatar(iconUrl))
                    {
                        if (existing.IconUrl != iconUrl)
                        {
                            Logger.WriteLog($"[Icon Updated Gift/Jewel] {authorName}: '{existing.IconUrl}' -> '{iconUrl}'");
                            existing.IconUrl = iconUrl;
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(existing.IconUrl))
                    {
                        Logger.WriteLog($"[Icon Guarded Gift/Jewel] {authorName} の既存アイコンを保護しました");
                    }

                    existing.CurrentGifts += gifts;
                    existing.CurrentJewels += jewels;
                    existing.LastSeen = now;
                    if (gifts > 0) existing.IsMember = true;
                    return existing;
                }
            );

            _isDirty = true;
            ListenerUpdated?.Invoke(info);
            return info;
        }

        /// <summary>
        /// 指定したリスナーをデータおよび保存対象から完全に削除
        /// </summary>
        public bool RemoveListener(ListenerInfo info)
        {
            if (info == null) return false;

            string key = NormalizeKey(info.Name, info.ChannelId);
            if (_listeners.TryRemove(key, out _))
            {
                _isDirty = true;
                Save(); // 即座に listeners.json へ反映
                Logger.WriteLog($"[Listener] リスナーを削除しました: {info.Name} (Key: {key})");
                return true;
            }
            return false;
        }

        public void ResetSessionStats()
        {
            SessionStartTime = DateTime.Now; // 💡 枠の開始時刻を更新
            foreach (var item in _listeners.Values)
            {
                item.CurrentGifts = 0;
                item.CurrentJewels = 0;
            }
            Logger.WriteLog($"[Listener] 枠内集計をリセットし、セッション開始時刻を更新しました: {SessionStartTime:HH:mm:ss}");
        }

        /// <summary>
        /// わんコメのリスナーCSVファイル（users.csv）をインポートして統合
        /// </summary>
        public int ImportFromOneCommeCsv(string csvFilePath)
        {
            if (!File.Exists(csvFilePath)) return 0;

            int importedCount = 0;
            // わんコメのCSVはUTF-8
            using var reader = new StreamReader(csvFilePath, System.Text.Encoding.UTF8);

            string? headerLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(headerLine)) return 0;

            // ヘッダー行をカンマ分割
            var headers = ParseCsvLine(headerLine);
            int idxId = headers.FindIndex(h => h.Equals("id", StringComparison.OrdinalIgnoreCase) || h.Equals("userId", StringComparison.OrdinalIgnoreCase));
            int idxUsername = headers.FindIndex(h => h.Equals("username", StringComparison.OrdinalIgnoreCase) || h.Equals("name", StringComparison.OrdinalIgnoreCase) || h.Equals("user", StringComparison.OrdinalIgnoreCase));
            int idxNick = headers.FindIndex(h => h.Equals("nickname", StringComparison.OrdinalIgnoreCase));
            int idxIcon = headers.FindIndex(h => h.Equals("icon", StringComparison.OrdinalIgnoreCase) || h.Equals("profileImage", StringComparison.OrdinalIgnoreCase) || h.Equals("avatar", StringComparison.OrdinalIgnoreCase));
            int idxLast = headers.FindIndex(h => h.Equals("lcts", StringComparison.OrdinalIgnoreCase) || h.Equals("lastVisit", StringComparison.OrdinalIgnoreCase) || h.Equals("lastSeen", StringComparison.OrdinalIgnoreCase));
            int idxCount = headers.FindIndex(h => h.Equals("tc", StringComparison.OrdinalIgnoreCase) || h.Equals("count", StringComparison.OrdinalIgnoreCase) || h.Equals("commentCount", StringComparison.OrdinalIgnoreCase));
            int idxBadges = headers.FindIndex(h => h.Equals("badges", StringComparison.OrdinalIgnoreCase));
            int idxMemo = headers.FindIndex(h => h.Equals("memo", StringComparison.OrdinalIgnoreCase));

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cols = ParseCsvLine(line);
                if (cols.Count == 0) continue;

                // 1. チャンネルID取得（yt- プレフィックスを除去してYouTube標準のUC...に正規化）
                string rawId = (idxId >= 0 && idxId < cols.Count) ? cols[idxId] : "";
                string channelId = rawId.StartsWith("yt-", StringComparison.OrdinalIgnoreCase) ? rawId.Substring(3) : rawId;

                // 2. 名前の取得（ニックネーム優先、無ければusername）
                string name = "";
                if (idxNick >= 0 && idxNick < cols.Count && !string.IsNullOrWhiteSpace(cols[idxNick]))
                {
                    name = cols[idxNick];
                }
                else if (idxUsername >= 0 && idxUsername < cols.Count)
                {
                    name = cols[idxUsername];
                    // @はまだの中の人 のような先頭の@を整える
                    if (name.StartsWith("@")) name = name.Substring(1);
                }

                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(channelId)) continue;

                // 3. アイコンURL
                string iconUrl = (idxIcon >= 0 && idxIcon < cols.Count) ? cols[idxIcon] : "";

                // 4. コメント総数（tc）
                int commentCount = 0;
                if (idxCount >= 0 && idxCount < cols.Count)
                {
                    int.TryParse(cols[idxCount], out commentCount);
                }

                // 5. 最終来訪日時（lcts: 2026-09-07T04:00:54.207Z）
                DateTime lastSeen = DateTime.Now;
                if (idxLast >= 0 && idxLast < cols.Count && !string.IsNullOrWhiteSpace(cols[idxLast]))
                {
                    if (DateTime.TryParse(cols[idxLast], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsedUtc))
                    {
                        lastSeen = parsedUtc.ToLocalTime(); // 日本時間に変換
                    }
                }

                // 6. メンバー判定（badges列に "メンバー" または "member" が含まれるか）
                bool isMember = false;
                if (idxBadges >= 0 && idxBadges < cols.Count && !string.IsNullOrWhiteSpace(cols[idxBadges]))
                {
                    string b = cols[idxBadges];
                    if (b.Contains("メンバー") || b.Contains("member", StringComparison.OrdinalIgnoreCase))
                    {
                        isMember = true;
                    }
                }

                // 7. メモ
                string memo = "";
                if (idxMemo >= 0 && idxMemo < cols.Count)
                {
                    memo = cols[idxMemo];
                }

                string key = NormalizeKey(name, channelId);

                _listeners.AddOrUpdate(
                    key,
                    _ =>
                    {
                        var newListener = new ListenerInfo
                        {
                            ChannelId = channelId,
                            Name = !string.IsNullOrWhiteSpace(name) ? name : "Unknown",
                            IconUrl = !IsDefaultOrBlankAvatar(iconUrl) ? iconUrl : "",
                            CommentCount = commentCount,
                            FirstSeen = lastSeen,
                            LastSeen = lastSeen,
                            IsMember = isMember,
                            Memo = memo
                        };
                        if (lastSeen != DateTime.MinValue)
                        {
                            newListener.RecordTimestamp(lastSeen);
                        }
                        return newListener;
                    },
                    (_, existing) =>
                    {
                        // 既存のリスナーが存在する場合はデータを統合・加算
                        if (!string.IsNullOrWhiteSpace(name) && (string.IsNullOrWhiteSpace(existing.Name) || existing.Name == "Unknown"))
                        {
                            existing.Name = name;
                        }
                        if (!string.IsNullOrWhiteSpace(channelId) && string.IsNullOrWhiteSpace(existing.ChannelId))
                        {
                            existing.ChannelId = channelId;
                        }

                        // わんコメの総コメント数の方が大きければ上書き、または加算
                        if (commentCount > existing.CommentCount)
                        {
                            existing.CommentCount = commentCount;
                        }

                        if (lastSeen > existing.LastSeen)
                        {
                            existing.LastSeen = lastSeen;
                        }

                        if (lastSeen != DateTime.MinValue)
                        {
                            existing.RecordTimestamp(lastSeen); // 活動時間帯の蓄積
                        }

                        if (isMember) existing.IsMember = true;

                        if (IsDefaultOrBlankAvatar(existing.IconUrl) && !IsDefaultOrBlankAvatar(iconUrl))
                        {
                            existing.IconUrl = iconUrl;
                        }

                        if (string.IsNullOrWhiteSpace(existing.Memo) && !string.IsNullOrWhiteSpace(memo))
                        {
                            existing.Memo = memo;
                        }

                        return existing;
                    }
                );

                importedCount++;
            }

            if (importedCount > 0)
            {
                _isDirty = true;
                Save();
                Logger.WriteLog($"[Listener Import] わんコメCSVから {importedCount} 件のリスナー情報をインポート・統合しました。");
            }

            return importedCount;
        }

        /// <summary>
        /// カンマ区切り行をパース（ダブルクォートで囲まれた文字列に対応）
        /// </summary>
        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var inQuotes = false;
            var currentField = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"')
                    {
                        currentField.Append('\"');
                        i++; // エスケープされた引用符をスキップ
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(currentField.ToString().Trim());
                    currentField.Clear();
                }
                else
                {
                    currentField.Append(c);
                }
            }
            result.Add(currentField.ToString().Trim());
            return result;
        }

        public IEnumerable<ListenerInfo> GetAllListeners() => _listeners.Values;
    }
}