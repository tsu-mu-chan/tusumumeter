using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace YoutubeCounterApp
{
    public class TemplateInfo : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public string LocalPath { get; set; } = string.Empty;
        public string HtmlPath { get; set; } = string.Empty;
        public string PreviewImagePath { get; set; } = string.Empty;
        public BitmapSource? PreviewImage { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        // 💡 推奨サイズ（幅・高さ）
        private int _recommendedWidth = 400;
        public int RecommendedWidth
        {
            get => _recommendedWidth;
            set
            {
                if (_recommendedWidth != value)
                {
                    _recommendedWidth = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RecommendedSizeText));
                }
            }
        }

        private int _recommendedHeight = 120;
        public int RecommendedHeight
        {
            get => _recommendedHeight;
            set
            {
                if (_recommendedHeight != value)
                {
                    _recommendedHeight = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RecommendedSizeText));
                }
            }
        }

        // 💡 XAMLバインド用プロパティ
        public string RecommendedSizeText => $"{RecommendedWidth} × {RecommendedHeight} px";

        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                if (_isFavorite != value)
                {
                    _isFavorite = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}