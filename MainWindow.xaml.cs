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
    


}