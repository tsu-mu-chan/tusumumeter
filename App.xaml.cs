using System.Windows;

namespace YoutubeCounterApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // アプリ起動時に非同期で更新チェック
            AppSettings.Load();
            var updater = new AutoUpdater();
            await updater.CheckAndUpdateAsync();
        } 
   }
}