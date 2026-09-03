using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.IO.Compression; // zip解凍用に追加

namespace YoutubeCounterApp
{
    public class TemplateSelectViewModel : INotifyPropertyChanged
    {
        // 画面に表示するテンプレートのリスト
        public ObservableCollection<TemplateInfo> Templates { get; } = new ObservableCollection<TemplateInfo>();
        
        // JSON設定パス
        private readonly string _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

#region 検索
        private string? _searchWord;
        public string SearchWord
        {
            get => _searchWord;
            set 
            { 
                _searchWord = value; 
                OnPropertyChanged(); 
                TemplatesView?.Refresh(); // リアルタイム絞り込み
            }
        }
#endregion

#region ソートラジオボタン
        public ICollectionView TemplatesView { get; }

        private string _currentFilter = "All";
        public string CurrentFilter
        {
            get => _currentFilter;
            set
            {
                if (_currentFilter != value)
                {
                    _currentFilter = value;
                    OnPropertyChanged(); // 変更を通知

                    // ラジオボタンの「チェック状態」も変わったことを通知する
                    OnPropertyChanged(nameof(IsAllSelected));
                    OnPropertyChanged(nameof(IsFavoriteSelected));
                    OnPropertyChanged(nameof(IsCustomSelected));

                    TemplatesView.Refresh(); // フィルタを再適用して画面を更新！
                }
            }
        }

        // XAMLのラジオボタンと繋ぐためのプロパティ
        public bool IsAllSelected
        {
            get => CurrentFilter == "All";
            set 
            {
                if (value) CurrentFilter = "All"; 
            }
        }

        public bool IsFavoriteSelected
        {
            get => CurrentFilter == "Favorite";
            set 
            { 
                if (value) CurrentFilter = "Favorite"; 
            }
        }

        public bool IsCustomSelected
        {
            get => CurrentFilter == "Custom";
            set
            {
                if (value) CurrentFilter = "Custom";
            }
        }
#endregion

        public ICommand AddTemplateCommand { get; }
        public ICommand OpenTemplateCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }
        public ICommand DeleteTemplateCommand { get; }
        public ICommand DropTemplateCommand { get; }

        public TemplateSelectViewModel()
        {
            WriteLog("TemplateSelectViewModel の初期化を開始します。");

            LoadTemplates();
            LoadSettings();

            AddTemplateCommand = new RelayCommand(_ => AddTemplate());
            OpenTemplateCommand = new RelayCommand(param => OpenTemplateFolder(param as TemplateInfo));
            ToggleFavoriteCommand = new RelayCommand(param => ToggleFavorite(param as TemplateInfo)); 
            DeleteTemplateCommand = new RelayCommand(param => DeleteTemplate(param as TemplateInfo)); 
            DropTemplateCommand = new RelayCommand(param => OnDropTemplate(param));

            // 表示用のビューを作成
            TemplatesView = CollectionViewSource.GetDefaultView(Templates);
            
            // 絞り込みルールを定義
            TemplatesView.Filter = item =>
            {
                if (item is TemplateInfo t)
                {
                    // ラジオボタンによるフィルタ
                    if (CurrentFilter == "Favorite" && !t.IsFavorite) return false;
                    if (CurrentFilter == "Custom") return false; // 今後の拡張用

                    // 検索機能によるフィルタ
                    if (string.IsNullOrWhiteSpace(SearchWord)) return true;

                    // スペースで分割して「すべて含まれているか」をチェック
                    var keywords = SearchWord.ToLower().Split(new[] { ' ', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    return keywords.All(k => t.Name != null && t.Name.ToLower().Contains(k));                
                }
                return false;
            };

            IsAllSelected = true;
        }

        /// <summary>
        /// ログ出力用ヘルパーメソッド
        /// </summary>
        private void WriteLog(string message)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log.txt");
                string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [Template] {message}{Environment.NewLine}";
                File.AppendAllText(logPath, logLine);
            }
            catch { /* ログ書き込み失敗でアプリを破綻させないための保護 */ }
        }

        private void LoadTemplates()
        {
            Templates.Clear();

            // 実行ファイルと同じ階層の "templates" フォルダを探す
            string templatesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates");

            if (!Directory.Exists(templatesPath))
            {
                WriteLog($"templates フォルダが見つかりません: {templatesPath}");
                return;
            }

            foreach (var dir in Directory.GetDirectories(templatesPath))
            {
                var dirInfo = new DirectoryInfo(dir);
                string[] exts = { ".gif", ".png", ".jpg", ".jpeg" };
                string? imagePath = null;
                    
                foreach (var ext in exts)
                {
                    var tempPath = Path.Combine(dir, "preview" + ext);
                    if (File.Exists(tempPath))
                    {
                        imagePath = tempPath;
                        break; // 見つかったらループを抜ける
                    }
                } 

                Templates.Add(new TemplateInfo
                {
                    Name = dirInfo.Name,
                    FolderName = dirInfo.Name,
                    Description = $"{dirInfo.Name} スタイルのカウンターテンプレートですよ～",
                    LocalPath = dir,
                    PreviewImagePath = imagePath
                });
            }

            WriteLog($"テンプレートを {Templates.Count} 件読み込みました。");
        }

        // フォルダを中身ごとコピーする補助関数（サブフォルダ対応）
        private void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            
            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            }

            foreach (var subDir in Directory.GetDirectories(source))
            {
                CopyDirectory(subDir, Path.Combine(target, Path.GetFileName(subDir)));
            }
        }

        private void OpenTemplateFolder(TemplateInfo? template)
        {
            if (template != null && Directory.Exists(template.LocalPath))
            {
                WriteLog($"テンプレートフォルダを開きます: {template.LocalPath}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = template.LocalPath,
                    UseShellExecute = true // フォルダを規定のアプリ（エクスプローラー）で開く
                });
            }
        }

        private void ToggleFavorite(TemplateInfo? template)
        {
            if (template != null)
            {
                template.IsFavorite = !template.IsFavorite;
                WriteLog($"お気に入り状態を変更しました: {template.Name} -> {template.IsFavorite}");
                
                // ★を切り替えた瞬間にファイルに書き込む
                SaveSettings(); 
                TemplatesView.Refresh(); // 画面をリフレッシュして非表示にする
            }
        }

        private void DeleteTemplate(TemplateInfo? template)
        {
            if (template == null) return;

            // 誤操作防止の確認メッセージ
            var result = MessageBox.Show(
                $"{template.Name} を削除してもよろしいですか？\n(PCからフォルダが完全に削除されます)", 
                "テンプレートの削除", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    WriteLog($"テンプレート削除処理を開始します: {template.Name}");

                    //【重要】まず、このアイテムの画像をnullにしてバインドを解除する
                    template.PreviewImage = null;
                    
                    // 少しだけ待機してWPFに「もう使ってないよ」と分からせる（おまじない）
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

                    // 1. PC上のフォルダを削除
                    if (Directory.Exists(template.LocalPath))
                    {
                        Directory.Delete(template.LocalPath, true); // trueで中身もろとも削除
                    }

                    // 2. リスト（Templates）から削除して画面を更新
                    Templates.Remove(template);
                    
                    // 3. お気に入り設定などの保存処理
                    SaveSettings(); 

                    WriteLog($"テンプレートを正常に削除しました: {template.Name}");
                }
                catch (Exception ex)
                {
                    WriteLog($"[ERROR] テンプレート削除に失敗しました ({template.Name}): {ex.Message}");
                    MessageBox.Show($"削除に失敗しました: {ex.Message}");
                }
            }
        }

        // --- 設定ファイル読み込み ---
        private void LoadSettings()
        {
            if (File.Exists(_settingsPath))
            {
                try
                {
                    string json = File.ReadAllText(_settingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    
                    if (settings?.FavoriteTemplates != null)
                    {
                        foreach (var template in Templates)
                        {
                            // 保存されているリストに名前があれば★をつける
                            template.IsFavorite = settings.FavoriteTemplates.Contains(template.FolderName);
                        }
                    }
                    WriteLog("設定ファイル (settings.json) からお気に入り情報を読み込みました。");
                }
                catch (Exception ex)
                {
                    WriteLog($"[ERROR] 設定ファイルの読み込みに失敗しました: {ex.Message}");
                }
            }
        }

        // --- 設定ファイル保存 ---
        private void SaveSettings()
        {
            try
            {
                var settings = new AppSettings
                {
                    FavoriteTemplates = Templates
                        .Where(t => t.IsFavorite && t.FolderName != null)
                        .Select(t => t.FolderName!)
                        .ToList()
                };
                
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
                WriteLog("設定ファイル (settings.json) を更新・保存しました。");
            }
            catch (Exception ex)
            {
                WriteLog($"[ERROR] 設定の保存に失敗しました: {ex.Message}");
            }
        }

        // --- INotifyPropertyChanged の実装 ---
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        /// <summary>
        /// フォルダ、Zipファイル、またはフォルダ内のファイル(index.html等)からテンプレートを追加する汎用処理
        /// </summary>
        private void ImportTemplateFromPath(string path)
        {
            WriteLog($"テンプレートのインポートを試行します: {path}");
            string templatesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates");

            // ファイルが指定され、かつ .zip ではない場合（例: 解凍済みフォルダ内の index.html が選ばれた場合）は親フォルダを対象にする
            if (File.Exists(path) && !Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                string? parentDir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    path = parentDir;
                }
            }

            if (Directory.Exists(path))
            {
                // フォルダの場合
                string folderName = Path.GetFileName(path);
                string targetPath = Path.Combine(templatesDir, folderName);

                if (!Directory.Exists(targetPath))
                {
                    CopyDirectory(path, targetPath);
                    WriteLog($"フォルダからテンプレートを追加しました: {folderName}");
                    LoadTemplates();
                    LoadSettings();
                }
                else
                {
                    WriteLog($"インポートスキップ: すでに同名のフォルダが存在します ({folderName})");
                    MessageBox.Show($"「{folderName}」は既に存在します。");
                }
            }
            else if (File.Exists(path) && Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                // Zipファイルの場合
                string folderName = Path.GetFileNameWithoutExtension(path);
                string targetPath = Path.Combine(templatesDir, folderName);

                if (!Directory.Exists(targetPath))
                {
                    // Zipを解凍してコピー
                    ZipFile.ExtractToDirectory(path, targetPath);
                    UnwrapSingleFolderIfNeeded(targetPath);
                    WriteLog($"Zipファイルからテンプレートを展開・追加しました: {folderName}");
                    LoadTemplates();
                    LoadSettings();
                }
                else
                {
                    WriteLog($"インポートスキップ: すでに同名のZip展開先が存在します ({folderName})");
                    MessageBox.Show($"「{folderName}」は既に存在します。");
                }
            }
        }

        /// <summary>
        /// ZIP解凍後にフォルダが二重構造（例: templates/Sample/Sample/preview.png）になっている場合、階層を1つ引き上げる
        /// </summary>
        private void UnwrapSingleFolderIfNeeded(string targetPath)
        {
            try
            {
                var files = Directory.GetFiles(targetPath);
                var subDirs = Directory.GetDirectories(targetPath);

                // ルート直下にファイルがなく、サブフォルダが1つだけ存在する場合は二重構造と判断
                if (files.Length == 0 && subDirs.Length == 1)
                {
                    string singleSubDir = subDirs[0];
                    string tempPath = targetPath + "_temp";

                    // 一時フォルダを経由してサブフォルダの中身を直下に移動
                    Directory.Move(singleSubDir, tempPath);
                    Directory.Delete(targetPath, true);
                    Directory.Move(tempPath, targetPath);
                    WriteLog($"ZIP解凍後の二重フォルダ構造を解消しました: {targetPath}");
                }
            }
            catch (Exception ex)
            {
                WriteLog($"[ERROR] フォルダ階層の調整に失敗しました: {ex.Message}");
            }
        }

        /// <summary>
        /// ドラッグ＆ドロップで受け取ったパスの処理
        /// </summary>
        private void OnDropTemplate(object? parameter)
        {
            if (parameter is DragEventArgs e && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                WriteLog($"ドラッグ＆ドロップによるインポートを検知しました (件数: {files.Length})");
                foreach (var path in files)
                {
                    ImportTemplateFromPath(path);
                }
            }
        }

        /// <summary>
        /// ファイル（Zip）またはフォルダダイアログからの追加
        /// </summary>
        private void AddTemplate()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "テンプレート（Zipファイルまたはフォルダ内のindex.html）を選択してください",
                Filter = "対応ファイル (*.zip;index.html)|*.zip;index.html;*.html|すべてのファイル (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                ImportTemplateFromPath(dialog.FileName);
            } 
        }
    }

    public class PathToImageConverter : System.Windows.Data.IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string path && File.Exists(path))
            {
                try
                {
                    // ファイルを一度バイト配列として完全に読み込む
                    byte[] buffer = File.ReadAllBytes(path);
                    
                    var ms = new MemoryStream(buffer);
                    
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = ms;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad; // 読み込み時に全て展開
                    bitmap.EndInit();
                    bitmap.Freeze(); // メモリ上に固定され、元のストリームが不要になる
                    return bitmap;
                    
                }
                catch 
                { 
                    return null; 
                }
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) 
            => throw new NotImplementedException();
    }
}