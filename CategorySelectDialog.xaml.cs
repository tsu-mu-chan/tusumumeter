using System.Windows;

namespace YoutubeCounterApp
{
    public partial class CategorySelectDialog : Window
    {
        public string SelectedCategory { get; private set; } = "Counter";

        public CategorySelectDialog(string itemName, string defaultCategory)
        {
            InitializeComponent();
            TxtTargetName.Text = $"「{itemName}」";

            // 推測されたカテゴリを初期選択
            switch (defaultCategory)
            {
                case "Comment":
                    RbComment.IsChecked = true;
                    break;
                case "Reaction":
                    RbReaction.IsChecked = true;
                    break;
                case "Clock":
                    RbClock.IsChecked = true;
                    break;
                default:
                    RbCounter.IsChecked = true;
                    break;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (RbComment.IsChecked == true) SelectedCategory = "Comment";
            else if (RbReaction.IsChecked == true) SelectedCategory = "Reaction";
            else if (RbClock.IsChecked == true) SelectedCategory = "Clock";
            else SelectedCategory = "Counter";

            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}