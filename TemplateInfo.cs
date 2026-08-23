using System.IO;
using System.Windows.Media.Imaging;

public class TemplateInfo : BindableBase
{
    public string? Name { get; set; }
    public string? FolderName { get; set; }
    public string? Description { get; set; }
    public BitmapImage? PreviewImage { get; set; }
    public string? LocalPath { get; set; }
    public string PreviewImagePath { get; set; }

    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value); // お気に入りのON/OFFで見た目を変える
    }
}