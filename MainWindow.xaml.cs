using System.Windows;
using System.Windows.Media.Animation;

namespace YoutubeCounterApp;

public partial class MainWindow : Window
{
    private MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(webView);
        this.DataContext = _viewModel;
    }

    // アニメーションを開始するメソッド
    public void OpenMenu() => ((Storyboard)this.Resources["OpenMenu"]).Begin();
    public void CloseMenu() => ((Storyboard)this.Resources["CloseMenu"]).Begin();

    // private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    // {
    //     if (!_viewModel.IsForceClose)
    //     {
    //         e.Cancel = true;
    //         _viewModel.StatusText = "🌸 下の『× CLOSE』ボタンから終了してね！";
    //         MessageBox.Show("終了する時は、アプリ内の「× CLOSE」ボタンを押してね🌸", "おしらせ");
    //     }
    // }
}