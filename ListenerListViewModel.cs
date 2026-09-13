using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace YoutubeCounterApp
{
    public class ListenerListViewModel : INotifyPropertyChanged
    {
        private readonly ListenerService _listenerService;
        private bool _isCurrentSessionOnly = false;
        public bool IsCurrentSessionOnly
        {
            get => _isCurrentSessionOnly;
            set
            {
                if (_isCurrentSessionOnly != value)
                {
                    _isCurrentSessionOnly = value;
                    OnPropertyChanged();
                    ListenersView.Refresh(); // フィルターを再実行
                }
            }
        }

        public ObservableCollection<ListenerInfo> Listeners { get; } = new();
        public ICollectionView ListenersView { get; }

        public string[] SortOptions { get; } = new[] { "🕒 最近のコメント順", "💬 コメント数順", "🌱 はじめまして順" };

        private string _selectedSort = "🕒 最近のコメント順";
        public string SelectedSort
        {
            get => _selectedSort;
            set
            {
                if (_selectedSort != value)
                {
                    _selectedSort = value;
                    OnPropertyChanged();
                    ApplySorting();
                }
            }
        }

        private string? _searchWord;
        public string SearchWord
        {
            get => _searchWord;
            set
            {
                if (_searchWord != value)
                {
                    _searchWord = value;
                    OnPropertyChanged();
                    ListenersView.Refresh();
                }
            }
        }

        private ListenerInfo? _selectedListener;
        public ListenerInfo? SelectedListener
        {
            get => _selectedListener;
            set
            {
                if (_selectedListener != value)
                {
                    _selectedListener = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand SaveChangesCommand { get; }
        public ICommand DeleteListenerCommand { get; }
        public ICommand ImportCsvCommand { get; }

        public ListenerListViewModel(ListenerService listenerService)
        {
            _listenerService = listenerService;

            foreach (var item in _listenerService.GetAllListeners())
            {
                Listeners.Add(item);
            }

            ListenersView = CollectionViewSource.GetDefaultView(Listeners);
            ListenersView.Filter = FilterListeners;

            ApplySorting();

            _listenerService.ListenerUpdated += OnListenerUpdated;

            SaveChangesCommand = new RelayCommand(_ =>
            {
                _listenerService.Save();
                Logger.WriteLog("[Listener UI] メモ等の変更を手動保存しました。");
            });

            DeleteListenerCommand = new RelayCommand(_ => DeleteSelectedListener());
            ImportCsvCommand = new RelayCommand(_ => ExecuteImportCsv());
        }

        private void ApplySorting()
        {
            ListenersView.SortDescriptions.Clear();

            switch (SelectedSort)
            {
                case "💬 コメント数順":
                    ListenersView.SortDescriptions.Add(new SortDescription(nameof(ListenerInfo.CommentCount), ListSortDirection.Descending));
                    break;
                case "🌱 はじめまして順":
                    ListenersView.SortDescriptions.Add(new SortDescription(nameof(ListenerInfo.FirstSeen), ListSortDirection.Ascending));
                    break;
                case "🕒 最近のコメント順":
                default:
                    ListenersView.SortDescriptions.Add(new SortDescription(nameof(ListenerInfo.LastSeen), ListSortDirection.Descending));
                    break;
            }
            ListenersView.Refresh();
        }

        private void OnListenerUpdated(ListenerInfo info)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (!Listeners.Contains(info))
                {
                    Listeners.Add(info);
                }
                ListenersView.Refresh();
            });
        }

        public void Cleanup()
        {
            _listenerService.ListenerUpdated -= OnListenerUpdated;
        }

        private bool FilterListeners(object item)
        {
            if (item is not ListenerInfo info) return false;

            // 💡 今枠のみ絞り込み（今枠の開始以降に来訪、または今枠でギフト・ジュエルがあるリスナー）
            if (IsCurrentSessionOnly)
            {
                bool activeInThisSession = info.LastSeen >= _listenerService.SessionStartTime
                                        || info.CurrentGifts > 0 
                                        || info.CurrentJewels > 0;
                if (!activeInThisSession) return false;
            }

            // 検索ワード絞り込み
            if (string.IsNullOrWhiteSpace(SearchWord)) return true;

            string keyword = SearchWord.ToLowerInvariant();
            return (info.Name != null && info.Name.ToLowerInvariant().Contains(keyword))
                || (info.Memo != null && info.Memo.ToLowerInvariant().Contains(keyword));
        }

        private void DeleteSelectedListener()
        {
            if (SelectedListener == null) return;

            var target = SelectedListener;
            var result = MessageBox.Show(
                $"「{target.Name}」さんをリスナー一覧から削除してもよろしいですか？\n※ コメント回数やメモ等もすべて削除されます。",
                "リスナーの削除確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // 1. コレクションから削除
                    Listeners.Remove(target);
                    SelectedListener = null;

                    // 2. サービス側からの削除 & 保存（※ListenerServiceにメソッドがある場合）
                    // メソッド名が異なる場合は適宜読み替えてください
                    _listenerService.RemoveListener(target);
                    _listenerService.Save();

                    Logger.WriteLog($"[Listener UI] リスナーを削除しました: {target.Name} (ID: {target.ChannelId})");
                }
                catch (Exception ex)
                {
                    Logger.WriteLog($"[ERROR] リスナー削除に失敗しました: {ex.Message}");
                    MessageBox.Show($"削除に失敗しました: {ex.Message}");
                }
            }
        }

        private void ExecuteImportCsv()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "わんコメ CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
                Title = "わんコメのリスナー一覧CSVを選択してください"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    int count = _listenerService.ImportFromOneCommeCsv(dialog.FileName);

                    // UI上のコレクションを最新化
                    Listeners.Clear();
                    foreach (var item in _listenerService.GetAllListeners())
                    {
                        Listeners.Add(item);
                    }
                    ListenersView.Refresh();

                    MessageBox.Show(
                        $"{count} 件のリスナーデータをインポート・統合しました！✨",
                        "インポート完了",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    Logger.WriteLog($"[Import Error] CSVインポート失敗: {ex.Message}");
                    MessageBox.Show($"インポート中にエラーが発生しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}