using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Text.RegularExpressions;

namespace YoutubeCounterApp
{
    public class TemplateSelectViewModel : INotifyPropertyChanged
    {
        private static readonly string[] DefaultCategories = { "Counter","ManualCounter", "Comment", "Reaction", "Clock" };
        private static readonly string[] ImageExtensions = { ".gif", ".png", ".jpg", ".jpeg" };

        private readonly string _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private readonly string _templatesRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates");

        public ObservableCollection<TemplateInfo> Templates { get; } = new();
        public ICollectionView TemplatesView { get; }

        #region 検索
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
                    TemplatesView?.Refresh();
                }
            }
        }
        #endregion

        #region カテゴリ・フィルタ切り替え
        private int _selectedTabIndex = 0;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (_selectedTabIndex != value)
                {
                    _selectedTabIndex = value;
                    OnPropertyChanged();

                    SelectedCategory = _selectedTabIndex switch
                    {
                        0 => "All",
                        1 => "Counter",
                        2 => "ManualCounter", 
                        3 => "Comment",
                        4 => "Reaction",
                        5 => "Clock",
                        6 => "Favorite",
                        _ => "All"
                    };
                }
            }
        }

        private string _selectedCategory = "All";
        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (_selectedCategory != value)
                {
                    _selectedCategory = value;
                    OnPropertyChanged();
                    TemplatesView?.Refresh();
                }
            }
        }
        #endregion

        public ICommand AddTemplateCommand { get; }
        public ICommand OpenTemplateCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }
        public ICommand DeleteTemplateCommand { get; }
        public ICommand DropTemplateCommand { get; }
        public ICommand OpenPreviewCommand { get; }

        public TemplateSelectViewModel()
        {
            Logger.WriteLog("TemplateSelectViewModel の初期化を開始します。");

            AddTemplateCommand = new RelayCommand(_ => AddTemplate());
            OpenTemplateCommand = new RelayCommand(param => OpenTemplateFolder(param as TemplateInfo));
            ToggleFavoriteCommand = new RelayCommand(param => ToggleFavorite(param as TemplateInfo));
            DeleteTemplateCommand = new RelayCommand(param => DeleteTemplate(param as TemplateInfo));
            DropTemplateCommand = new RelayCommand(OnDropTemplate);
            OpenPreviewCommand = new RelayCommand(param => ExecuteOpenPreview(param as TemplateInfo));

            TemplatesView = CollectionViewSource.GetDefaultView(Templates);
            TemplatesView.Filter = FilterTemplates;

            LoadTemplates();
            LoadSettings();

            SelectedTabIndex = 0;
        }

        private bool FilterTemplates(object item)
        {
            if (item is not TemplateInfo t) return false;

            if (SelectedCategory == "Favorite")
            {
                if (!t.IsFavorite) return false;
            }
            else if (SelectedCategory != "All")
            {
                if (!string.Equals(t.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase)) return false;
            }

            if (string.IsNullOrWhiteSpace(SearchWord)) return true;

            var keywords = SearchWord.ToLower().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return keywords.All(k => t.Name != null && t.Name.ToLower().Contains(k));
        }

        public void LoadTemplates()
        {
            EnsureCategoryDirectories();
            Templates.Clear();

            var categoryDirs = Directory.GetDirectories(_templatesRoot);
            foreach (var categoryDir in categoryDirs)
            {
                string categoryName = Path.GetFileName(categoryDir);

                // パターンA: サブフォルダ形式（例: templates/Comment/02_Comment_Simple_Green/）
                foreach (var subDir in Directory.GetDirectories(categoryDir))
                {
                    var htmlFiles = Directory.GetFiles(subDir, "*.html");
                    if (htmlFiles.Length == 0) continue;

                    string htmlPath = htmlFiles.FirstOrDefault(f => Path.GetFileName(f).Equals("index.html", StringComparison.OrdinalIgnoreCase))
                                      ?? htmlFiles[0];

                    string folderName = Path.GetFileName(subDir);
                    string previewPath = FindPreviewImage(subDir, "preview");

                    Templates.Add(CreateTemplateInfo(folderName, subDir, htmlPath, previewPath, categoryName, $"{folderName} のテンプレート"));
                }

                // パターンB: ファイル直置き形式（例: templates/Comment/Comment_Blue.html）
                foreach (var htmlFile in Directory.GetFiles(categoryDir, "*.html"))
                {
                    string baseName = Path.GetFileNameWithoutExtension(htmlFile);
                    string previewPath = FindPreviewImage(categoryDir, baseName);

                    Templates.Add(CreateTemplateInfo(baseName, categoryDir, htmlFile, previewPath, categoryName, $"{baseName} の単体テンプレート"));
                }
            }

            Logger.WriteLog($"テンプレートを {Templates.Count} 件読み込みました。");
        }

        private void EnsureCategoryDirectories()
        {
            if (!Directory.Exists(_templatesRoot))
            {
                Directory.CreateDirectory(_templatesRoot);
            }

            foreach (var category in DefaultCategories)
            {
                string categoryPath = Path.Combine(_templatesRoot, category);
                if (!Directory.Exists(categoryPath))
                {
                    Directory.CreateDirectory(categoryPath);
                }
            }
        }

        private static string FindPreviewImage(string directory, string baseFileName)
        {
            foreach (var ext in ImageExtensions)
            {
                string candidate = Path.Combine(directory, baseFileName + ext);
                if (File.Exists(candidate)) return candidate;
            }
            return string.Empty;
        }

        private static TemplateInfo CreateTemplateInfo(string name, string folderPath, string htmlPath, string previewPath, string category, string description)
        {
            // 1. カテゴリごとのデフォルト値
            int width = category switch
            {
                "Clock" => 360,
                "Comment" => 420,
                "Counter" => 450,
                "ManualCounter" => 360,
                "Reaction" => 320,
                _ => 400
            };
            int height = category switch
            {
                "Clock" => 120,
                "Comment" => 600,
                "Counter" => 140,
                "ManualCounter" => 120,
                "Reaction" => 320,
                _ => 120
            };

            // 2. HTMLから指定サイズを自動検出（あれば上書き）
            var htmlSize = TryExtractSizeFromHtml(htmlPath);
            if (htmlSize.HasValue)
            {
                width = htmlSize.Value.width;
                height = htmlSize.Value.height;
            }

            return new TemplateInfo
            {
                Name = name,
                FolderName = name,
                LocalPath = folderPath,
                HtmlPath = htmlPath,
                PreviewImagePath = previewPath,
                Category = category,
                Description = description,
                RecommendedWidth = width,
                RecommendedHeight = height
            };
        }

        private void OpenTemplateFolder(TemplateInfo? template)
        {
            if (template?.LocalPath != null && Directory.Exists(template.LocalPath))
            {
                Logger.WriteLog($"テンプレートフォルダを開きます: {template.LocalPath}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = template.LocalPath,
                    UseShellExecute = true
                });
            }
        }

        private void ToggleFavorite(TemplateInfo? template)
        {
            if (template == null) return;

            template.IsFavorite = !template.IsFavorite;
            Logger.WriteLog($"お気に入り状態を変更しました: {template.Name} -> {template.IsFavorite}");

            SaveSettings();
            TemplatesView.Refresh();
        }

        private void DeleteTemplate(TemplateInfo? template)
        {
            if (template == null) return;

            var result = MessageBox.Show(
                $"{template.Name} を削除してもよろしいですか？\n(PCから完全に削除されます)",
                "テンプレートの削除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                Logger.WriteLog($"テンプレート削除処理を開始します: {template.Name}");
                template.PreviewImage = null;

                // ファイル直置き形式の場合はそのHTMLと対応画像を削除、フォルダ形式の場合はディレクトリごと削除
                if (File.Exists(template.HtmlPath) && Path.GetFileName(template.LocalPath) != template.Name)
                {
                    File.Delete(template.HtmlPath);
                    if (!string.IsNullOrEmpty(template.PreviewImagePath) && File.Exists(template.PreviewImagePath))
                    {
                        File.Delete(template.PreviewImagePath);
                    }
                }
                else if (template.LocalPath != null && Directory.Exists(template.LocalPath))
                {
                    Directory.Delete(template.LocalPath, true);
                }

                Templates.Remove(template);
                SaveSettings();
                Logger.WriteLog($"テンプレートを正常に削除しました: {template.Name}");
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[ERROR] テンプレート削除に失敗しました ({template.Name}): {ex.Message}");
                MessageBox.Show($"削除に失敗しました: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            try
            {
                var favorites = AppSettings.Instance.FavoriteTemplates;
                if (favorites != null && favorites.Count > 0)
                {
                    var favSet = new HashSet<string>(favorites);
                    foreach (var template in Templates)
                    {
                        if (template.FolderName != null)
                        {
                            template.IsFavorite = favSet.Contains(template.FolderName);
                        }
                    }
                }
                Logger.WriteLog("AppSettings からお気に入り情報を反映しました。");
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[ERROR] お気に入り設定の読み込みに失敗しました: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                AppSettings.Instance.FavoriteTemplates = Templates
                    .Where(t => t.IsFavorite && !string.IsNullOrEmpty(t.FolderName))
                    .Select(t => t.FolderName)
                    .ToList();

                AppSettings.Instance.Save();
                Logger.WriteLog("お気に入り情報を config.json へ保存しました。");
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[ERROR] お気に入り設定の保存に失敗しました: {ex.Message}");
            }
        }

    /// <summary>
    /// フォルダ、Zipファイル、または個別ファイルからのインポート処理（ハイブリッド型）
    /// </summary>
    private void ImportTemplateFromPath(string path)
    {
        Logger.WriteLog($"テンプレートのインポートを試行します: {path}");

        // ファイルが指定され、かつ .zip ではない場合は親フォルダを対象にする
        if (File.Exists(path) && !Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            string? parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent)) path = parent;
        }

        string itemName = Path.GetFileNameWithoutExtension(path);
        string targetCategory;

        // ① 特定カテゴリ（Counter / Comment / Reaction / Clock）表示中はダイアログなしでそのまま追加
        if (SelectedCategory != "All" && SelectedCategory != "Favorite")
        {
            targetCategory = SelectedCategory;
        }
        else
        {
            // ② 「すべて」または「お気に入り」タブ時はキーワードから初期値を推測し、ダイアログで確認
            string guessedCategory = GuessCategoryFromPath(path);

            var dialog = new CategorySelectDialog(itemName, guessedCategory)
            {
                Owner = Application.Current.Windows.OfType<TemplateSelectWindow>().FirstOrDefault()
            };

            if (dialog.ShowDialog() != true)
            {
                Logger.WriteLog("インポートがユーザーによってキャンセルされました。");
                return;
            }

            targetCategory = dialog.SelectedCategory;
        }

        string destinationDir = Path.Combine(_templatesRoot, targetCategory);
        Directory.CreateDirectory(destinationDir);

        if (Directory.Exists(path))
        {
            string folderName = Path.GetFileName(path);
            string targetPath = Path.Combine(destinationDir, folderName);

            if (!Directory.Exists(targetPath))
            {
                CopyDirectory(path, targetPath);
                Logger.WriteLog($"フォルダからテンプレートを追加しました: [{targetCategory}] {folderName}");
                LoadTemplates();
                LoadSettings();
            }
            else
            {
                MessageBox.Show($"「{folderName}」は既に {targetCategory} 内に存在します。");
            }
        }
        else if (File.Exists(path) && Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            string folderName = Path.GetFileNameWithoutExtension(path);
            string targetPath = Path.Combine(destinationDir, folderName);

            if (!Directory.Exists(targetPath))
            {
                ZipFile.ExtractToDirectory(path, targetPath);
                UnwrapSingleFolderIfNeeded(targetPath);
                Logger.WriteLog($"Zipファイルからテンプレートを展開・追加しました: [{targetCategory}] {folderName}");
                LoadTemplates();
                LoadSettings();
            }
            else
            {
                MessageBox.Show($"「{folderName}」は既に {targetCategory} 内に存在します。");
            }
        }
    }

    /// <summary>
    /// フォルダ名やファイル名からカテゴリを推測するヘルパー
    /// </summary>
    private static string GuessCategoryFromPath(string path)
    {
        string name = Path.GetFileName(path).ToLowerInvariant();

        // 💡 手動カウンター判定を優先
        if (name.Contains("manual") || name.Contains("マニュアル") || name.Contains("手動") || name.Contains("もくひょう") || name.Contains("おはよう") || name.Contains("あいさつ"))
        {
            return "ManualCounter";
        }
        if (name.Contains("comment") || name.Contains("chat") || name.Contains("コメント") || name.Contains("チャット"))
        {
            return "Comment";
        }
        if (name.Contains("clock") || name.Contains("time") || name.Contains("時計") || name.Contains("タイマー") || name.Contains("同時視聴"))
        {
            return "Clock";
        }
        if (name.Contains("reaction") || name.Contains("リアクション") || name.Contains("タンク") || name.Contains("絵文字"))
        {
            return "Reaction";
        }
        if (name.Contains("count") || name.Contains("sub") || name.Contains("高評価") || name.Contains("登録") || name.Contains("同接") || name.Contains("カウンター"))
        {
            return "Counter";
        }

        return "Counter";
    }

        private static void UnwrapSingleFolderIfNeeded(string targetPath)
        {
            try
            {
                var files = Directory.GetFiles(targetPath);
                var subDirs = Directory.GetDirectories(targetPath);

                if (files.Length == 0 && subDirs.Length == 1)
                {
                    string singleSubDir = subDirs[0];
                    string tempPath = targetPath + "_temp";

                    Directory.Move(singleSubDir, tempPath);
                    Directory.Delete(targetPath, true);
                    Directory.Move(tempPath, targetPath);
                    Logger.WriteLog($"ZIP解凍後の二重フォルダ構造を解消しました: {targetPath}");
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[ERROR] フォルダ階層の調整に失敗しました: {ex.Message}");
            }
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            }
            foreach (var subDir in Directory.GetDirectories(source))
            {
                CopyDirectory(subDir, Path.Combine(target, Path.GetFileName(subDir)));
            }
        }

        private void OnDropTemplate(object? parameter)
        {
            if (parameter is DragEventArgs e && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
                {
                    Logger.WriteLog($"ドラッグ＆ドロップによるインポートを検知しました (件数: {files.Length})");
                    foreach (var path in files)
                    {
                        ImportTemplateFromPath(path);
                    }
                }
            }
        }

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

        private void ExecuteOpenPreview(TemplateInfo? item)
        {
            if (item == null) return;

            try
            {
                string targetPath = !string.IsNullOrEmpty(item.HtmlPath) && File.Exists(item.HtmlPath)
                    ? item.HtmlPath
                    : item.LocalPath;

                if (File.Exists(targetPath))
                {
                    // 余計なパラメータを付けず、ブラウザにそのまま渡す
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = targetPath,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[Preview] プレビュー起動失敗: {ex.Message}");
            }
        }

        private static (int width, int height)? TryExtractSizeFromHtml(string htmlPath)
        {
            if (!File.Exists(htmlPath)) return null;

            try
            {
                // 先頭5KB程度のみ読み込めば十分（ファイル全体を読み込まず高速化）
                string content;
                using (var reader = new StreamReader(htmlPath))
                {
                    char[] buffer = new char[5120];
                    int read = reader.Read(buffer, 0, buffer.Length);
                    content = new string(buffer, 0, read);
                }

                // 1. <meta name="obs-size" content="450x650"> を検索
                var metaMatch = Regex.Match(content, @"<meta\s+name=[""']obs-size[""']\s+content=[""'](?<w>\d+)\s*[x×,]\s*(?<h>\d+)[""']", RegexOptions.IgnoreCase);
                if (metaMatch.Success)
                {
                    return (int.Parse(metaMatch.Groups["w"].Value), int.Parse(metaMatch.Groups["h"].Value));
                }

                // 2. コメント形式 <!-- obs-size: 450x650 --> を検索
                var commentMatch = Regex.Match(content, @"obs-(?:recommended-)?size:\s*(?<w>\d+)\s*[x×,]\s*(?<h>\d+)", RegexOptions.IgnoreCase);
                if (commentMatch.Success)
                {
                    return (int.Parse(commentMatch.Groups["w"].Value), int.Parse(commentMatch.Groups["h"].Value));
                }

                // 3. CSS内の body または #chat-wrapper などの width/height (例: width: 450px; height: 600px;) から推測
                var widthMatch = Regex.Match(content, @"(?:body|container|wrapper)\s*\{[^}]*?width:\s*(?<w>\d+)px", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                var heightMatch = Regex.Match(content, @"(?:body|container|wrapper)\s*\{[^}]*?height:\s*(?<h>\d+)px", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (widthMatch.Success && heightMatch.Success)
                {
                    return (int.Parse(widthMatch.Groups["w"].Value), int.Parse(heightMatch.Groups["h"].Value));
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog($"[Template] HTMLサイズ抽出エラー ({htmlPath}): {ex.Message}");
            }

            return null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}