using System.ComponentModel;
using Pagegram.Services;
using Windows.UI.Xaml.Media;

namespace Pagegram.Models
{
    /// <summary>Контакт: имя, статус «в сети / был(а)…», круглая аватарка (ленивая).</summary>
    public sealed class PersonItem : INotifyPropertyChanged
    {
        public PersonItem(long userId, string name, string status, int photoFileId)
        {
            UserId = userId;
            Name = name ?? string.Empty;
            Status = status ?? string.Empty;
            _photoFileId = photoFileId;
        }

        public long UserId { get; }
        public string Name { get; }
        public string Status { get; }

        public Brush AvatarBrush => Palette.Avatar(UserId);

        public string Initial
        {
            get
            {
                foreach (var ch in Name)
                    if (char.IsLetterOrDigit(ch)) return char.ToUpperInvariant(ch).ToString();
                return "?";
            }
        }

        private readonly int _photoFileId;
        private ImageSource _photo;
        private bool _requested;

        public ImageSource Photo
        {
            get
            {
                if (_photo == null && !_requested && _photoFileId != 0)
                {
                    _requested = true;
                    _photo = Images.Get(ImageKind.Avatar, _photoFileId, img =>
                    {
                        _photo = img;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Photo)));
                    });
                }
                return _photo;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
