using System;
using System.IO;
using System.Text;
using System.Windows;
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