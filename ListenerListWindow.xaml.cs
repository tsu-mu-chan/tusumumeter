using System;
using System.Windows;

namespace YoutubeCounterApp
{
    public partial class ListenerListWindow : Window
    {
        public ListenerListWindow(ListenerService listenerService)
        {
            InitializeComponent();
            DataContext = new ListenerListViewModel(listenerService);
            this.Closed += ListenerListWindow_Closed;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ListenerListWindow_Closed(object? sender, EventArgs e)
        {
            if (DataContext is ListenerListViewModel vm)
            {
                vm.SaveChangesCommand.Execute(null);
                vm.Cleanup(); // ★ 購読解除
            }
        }

        private void Border_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
    }
}