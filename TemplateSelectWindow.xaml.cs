using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace YoutubeCounterApp
{
    /// <summary>
    /// TemplateSelectWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class TemplateSelectWindow : Window
    {
        public TemplateSelectWindow()
        {
            InitializeComponent();
            
            // ViewModelをデータコンテキストに設定
            // ※TemplateSelectViewModelが定義されている前提です
            this.DataContext = new TemplateSelectViewModel();
        }

        /// <summary>
        /// ⑨ 「ここをドラッグしてOBSに入れる」テキストのドラッグ操作
        /// </summary>
        private void DragLabel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 1. クリックされた要素からDataContext（TemplateInfo）を取得
            if (sender is FrameworkElement element && element.DataContext is TemplateInfo template)
            {
                try
                {
                    // 1. テンプレートの index.html へのフルパスを取得
                    // ViewModel側でLocalPathがセットされている前提です
                    string indexPath = Path.Combine(template.LocalPath, "index.html");

                    if (!File.Exists(indexPath)) return;

                    DataObject data = new DataObject();

                    // 2. 重要：FileDropとして「実際のファイルパス」を渡す
                    // これにより、OBSは「ローカルのブラウザソース」として認識し、ポップアップを出しません
                    string[] filePaths = new string[] { indexPath };
                    data.SetData(DataFormats.FileDrop, filePaths);

                    // 💡 念のため、他の形式でもパスを渡しておくと安定します
                    data.SetData(DataFormats.UnicodeText, indexPath);

                    // 3. ドラッグ開始
                    DragDrop.DoDragDrop(element, data, DragDropEffects.Copy);
                }
                catch (Exception ex)
                {
                    // エラーが発生した場合はデバッグ出力（必要に応じてMessageBoxなど）
                    System.Diagnostics.Debug.WriteLine($"Drag error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 🎛️ 手動カウンター用：OBSカスタムブラウザドック用URLのコピー処理
        /// </summary>
        private void CopyDockUrlButton_Click(object sender, RoutedEventArgs e)
        {
            // ボタンの Tag または DataContext から TemplateInfo を取得
            TemplateInfo template = null;
            if (sender is Button btn)
            {
                template = btn.Tag as TemplateInfo ?? btn.DataContext as TemplateInfo;
            }

            if (template != null)
            {
                try
                {
                    // 1. テンプレートフォルダ内の control.html を特定
                    string controlPath = Path.Combine(template.LocalPath, "control.html");

                    if (!File.Exists(controlPath))
                    {
                        MessageBox.Show(
                            "このテンプレートフォルダ内に操作用リモコン（control.html）が見つかりませんでした。",
                            "確認",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning
                        );
                        return;
                    }

                    // 2. フルパスをWeb表記スラッシュ(/)に正規化
                    string fullPath = Path.GetFullPath(controlPath).Replace('\\', '/');

                    // 3. OBSのローカルブラウザソースと同一オリジンで通信できる疑似スキームURLを生成
                    string obsDockUrl = $"http://absolute/{fullPath}";

                    // 4. クリップボードへコピー
                    Clipboard.SetText(obsDockUrl);

                    MessageBox.Show(
                        "OBSカスタムブラウザドック用のURLをコピーしました！ ✨\n\n" +
                        "【OBSへの登録手順】\n" +
                        "1. OBS上部のメニュー「ドック」➔「カスタムブラウザドック」を開く\n" +
                        "2. ドック名に「手動カウンター」などの名前を入力\n" +
                        "3. URL欄に貼り付け（Ctrl + V）して「適用」を押す",
                        "コピー完了",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"URLのコピー中にエラーが発生しました:\n{ex.Message}",
                        "エラー",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
        }

        /// <summary>
        /// ウィンドウを閉じる処理（×ボタン用など）
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Grid_Drop(object sender, DragEventArgs e)
        {
            if (DataContext is TemplateSelectViewModel vm)
            {
                // ViewModelのDrop処理を呼び出す
                vm.DropTemplateCommand.Execute(e);
            }
        }
    }
}