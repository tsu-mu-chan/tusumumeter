using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace YoutubeCounterApp
{
    public class ListenerInfo : INotifyPropertyChanged
    {
        public string ChannelId { get; set; } = "";

        private string _name = "";
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
        }

        private string _iconUrl = "";
        public string IconUrl
        {
            get => _iconUrl;
            set
            {
                // ★ 既に有効なアイコンが入っている場合、空文字やnullによる上書きを拒否して死守する
                if (string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(_iconUrl))
                {
                    return;
                }

                if (_iconUrl != value)
                {
                    _iconUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _commentCount = 0;
        public int CommentCount
        {
            get => _commentCount;
            set { if (_commentCount != value) { _commentCount = value; OnPropertyChanged(); } }
        }

        private int _currentGifts = 0;
        public int CurrentGifts
        {
            get => _currentGifts;
            set { if (_currentGifts != value) { _currentGifts = value; OnPropertyChanged(); } }
        }

        private int _currentJewels = 0;
        public int CurrentJewels
        {
            get => _currentJewels;
            set { if (_currentJewels != value) { _currentJewels = value; OnPropertyChanged(); } }
        }

        private DateTime _firstSeen;
        public DateTime FirstSeen
        {
            get => _firstSeen;
            set { if (_firstSeen != value) { _firstSeen = value; OnPropertyChanged(); } }
        }

        private DateTime _lastSeen;
        public DateTime LastSeen
        {
            get => _lastSeen;
            set 
            { 
                if (_lastSeen != value) 
                { 
                    _lastSeen = value; 
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PrimaryActiveTimeText)); // 時間更新時に表示も再計算
                } 
            }
        }

        private bool _isMember = false;
        public bool IsMember
        {
            get => _isMember;
            set { if (_isMember != value) { _isMember = value; OnPropertyChanged(); } }
        }

        private string _memo = "";
        public string Memo
        {
            get => _memo;
            set { if (_memo != value) { _memo = value; OnPropertyChanged(); } }
        }

        // ==========================================
        // 💡 活動分析用データ（0〜23時 ＆ 曜日）
        // ==========================================
        public Dictionary<int, int> HourlyActivity { get; set; } = new();
        public Dictionary<DayOfWeek, int> WeeklyActivity { get; set; } = new();

        /// <summary>
        /// 発言日時を記録・集計
        /// </summary>
        public void RecordTimestamp(DateTime dt)
        {
            int hour = dt.Hour;
            HourlyActivity[hour] = HourlyActivity.GetValueOrDefault(hour, 0) + 1;

            var day = dt.DayOfWeek;
            WeeklyActivity[day] = WeeklyActivity.GetValueOrDefault(day, 0) + 1;

            OnPropertyChanged(nameof(PrimaryActiveTimeText));
        }

        /// <summary>
        /// 主な活動時間帯テキスト（UI表示用）
        /// </summary>
        public string PrimaryActiveTimeText
        {
            get
            {
                if (HourlyActivity == null || HourlyActivity.Count == 0)
                {
                    if (LastSeen != default && LastSeen != DateTime.MinValue)
                    {
                        return GetTimeSlotLabel(LastSeen.Hour);
                    }
                    return "データ収集中 🌱";
                }

                int morning = HourlyActivity.Where(k => k.Key >= 5 && k.Key < 12).Sum(k => k.Value);
                int day = HourlyActivity.Where(k => k.Key >= 12 && k.Key < 18).Sum(k => k.Value);
                int night = HourlyActivity.Where(k => k.Key >= 18 && k.Key <= 23).Sum(k => k.Value);
                int midnight = HourlyActivity.Where(k => k.Key >= 0 && k.Key < 5).Sum(k => k.Value);

                var slots = new (string Label, int Count)[]
                {
                    ("🌅 朝型 (5-11時)", morning),
                    ("☀️ 昼型 (12-17時)", day),
                    ("🌙 夜型 (18-23時)", night),
                    ("🦉 深夜型 (0-4時)", midnight)
                };

                var top = slots.OrderByDescending(s => s.Count).First();
                if (top.Count == 0) return "データ収集中 🌱";

                int peakHour = HourlyActivity.OrderByDescending(k => k.Value).First().Key;
                return $"{top.Label}（{peakHour}時台多め）";
            }
        }

        private static string GetTimeSlotLabel(int hour)
        {
            if (hour >= 5 && hour < 12) return "🌅 朝によく来訪";
            if (hour >= 12 && hour < 18) return "☀️ 昼によく来訪";
            if (hour >= 18 && hour <= 23) return "🌙 夜によく来訪";
            return "🦉 深夜によく来訪";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}