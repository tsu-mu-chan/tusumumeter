using System;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace YoutubeCounterApp
{
    public class PathToImageConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string path && File.Exists(path))
            {
                try
                {
                    byte[] buffer = File.ReadAllBytes(path);
                    var ms = new MemoryStream(buffer);

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = ms;
                    // ★ サムネイル表示用に横幅を260px程度に制限してデコード（メモリ消費を1/5以下に抑制）
                    bitmap.DecodePixelWidth = 260;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
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