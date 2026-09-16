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

        public ObservableCollection<ListenerInfo> Listeners { get; } = new();
        public ICollectionView ListenersView { get; }

        public string[] SortOptions { get; } = { "🕒 最近のコメント順", "💬 コメント数順", "🌱 はじめまして順" };

        #region バインドプロパティ
        private bool _isCurrentSessionOnly;
        public bool IsCurrentSessionOnly
        {
            get => _isCurrentSessionOnly;
            set
            {
                if (_isCurrentSessionOnly != value)
                {
                    _isCurrentSessionOnly = value;
                    OnPropertyChanged();
                    Logger.WriteLog($"[ListenerFilter] 今枠のみ絞り込み切替: {value}");
                    ListenersView.Refresh();
                }
            }
        }

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
                    Logger.WriteLog($"[ListenerSort] ソート順変更: {value}");
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
        #endregion

        public ICommand SaveChangesCommand { get; }
        public ICommand DeleteListenerCommand { get; }
        public ICommand ImportCsvCommand { get; }

        public ListenerListViewModel(ListenerService listenerService)
        {
            Logger.WriteLog("[Init] ListenerListViewModel の初期化を開始します。");
            _listenerService = listenerService;

            var allListeners = _listenerService.GetAllListeners().ToList();
            foreach (var item in allListeners)
            {
                Listeners.Add(item);
            }
            Logger.WriteLog($"[Listener UI] 既存リスナーデータをロードしました ({Listeners.Count} 件)");

            ListenersView = CollectionViewSource.GetDefaultView(Listeners);
            ListenersView.Filter = FilterListeners;

            ApplySorting();

            _listenerService.ListenerUpdated += OnListenerUpdated;

            SaveChangesCommand = new RelayCommand(_ =>
            {
                Logger.WriteLog("[Listener UI] ユーザーによる手動保存を実行します。");
                _listenerService.Save();
                Logger.WriteLog("[Listener UI] リスナー情報を正常に保存しました。");
            });

            DeleteListenerCommand = new RelayCommand(_ => DeleteSelectedListener());
            ImportCsvCommand = new RelayCommand(_ => ExecuteImportCsv());

            Logger.WriteLog("[Init] ListenerListViewModel の初期化が完了しました。");
        }

        private void ApplySorting()
        {
            ListenersView.SortDescriptions.Clear();

            var (propertyName, direction) = SelectedSort switch
            {
                "💬 コメント数順" => (nameof(ListenerInfo.CommentCount), ListSortDirection.Descending),
                "🌱 はじめまして順" => (nameof(ListenerInfo.FirstSeen), ListSortDirection.Ascending),
                _ => (nameof(ListenerInfo.LastSeen), ListSortDirection.Descending)
            };

            ListenersView.SortDescriptions.Add(new SortDescription(propertyName, direction));
            ListenersView.Refresh();
        }

        private void OnListenerUpdated(ListenerInfo info)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (!Listeners.Contains(info))
                    {
                        Listeners.Add(info);
                        Logger.WriteLog($"[Listener UI] 新規リスナーをリストに追加: {info.Name} (ID: {info.ChannelId})");
                    }
                    ListenersView.Refresh();
                }
                catch (Exception ex)
                {
                    Logger.WriteLog($"[Listener UI Error] リスト更新反映中に例外: {ex.Message}");
                }
            });
        }

        public void Cleanup()
        {
            Logger.WriteLog("[Listener UI] イベント購読を解除してクリーンアップします。");
            _listenerService.ListenerUpdated -= OnListenerUpdated;
        }

        private bool FilterListeners(object item)
        {
            if (item is not ListenerInfo info) return false;

            // 今枠のみ絞り込み
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

            if (result != MessageBoxResult.Yes)
            {
                Logger.WriteLog($"[Listener UI] リスナー削除がキャンセルされました: {target.Name}");
                return;
            }

            try
            {
                Logger.WriteLog($"[Listener UI] リスナー削除処理を開始: {target.Name} (ID: {target.ChannelId})");
                Listeners.Remove(target);
                SelectedListener = null;

                _listenerService.RemoveListener(target);
                _listenerService.Save();

                Logger.WriteLog($"[Listener UI] リスナーを正常に削除しました: {target.Name}");
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[Listener UI Error] リスナー削除に失敗しました ({target.Name}): {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"削除に失敗しました: {ex.Message}");
            }
        }

        private void ExecuteImportCsv()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "わんコメ CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
                Title = "わんコメのリスナー一覧CSVを選択してください"
            };

            if (dialog.ShowDialog() != true)
            {
                Logger.WriteLog("[Listener Import] CSV選択ダイアログがキャンセルされました。");
                return;
            }

            try
            {
                Logger.WriteLog($"[Listener Import] CSVインポートを開始します: {dialog.FileName}");
                int count = _listenerService.ImportFromOneCommeCsv(dialog.FileName);

                Listeners.Clear();
                foreach (var item in _listenerService.GetAllListeners())
                {
                    Listeners.Add(item);
                }
                ListenersView.Refresh();

                Logger.WriteLog($"[Listener Import] インポート成功: {count} 件統合完了");

                MessageBox.Show(
                    $"{count} 件のリスナーデータをインポート・統合しました！✨",
                    "インポート完了",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[Listener Import Error] CSVインポート失敗: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"インポート中にエラーが発生しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}