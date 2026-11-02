using System;
using System.ComponentModel;
using Pagegram.Services;
using Windows.UI.Xaml.Media;

namespace Pagegram.Models
{
    /// <summary>
    /// Плитка в сетке фото на экране информации. Картинку держит слабо:
    /// ушла за экран — может освободиться, вернулась — возьмётся из кэша/с диска.
    /// </summary>
    public sealed class MediaThumb : INotifyPropertyChanged
    {
        public MediaThumb(long messageId, int thumbFileId, int fullFileId)
        {
            MessageId = messageId;
            _thumbFileId = thumbFileId;
            FullFileId = fullFileId;
        }

        public long MessageId { get; }
        public int FullFileId { get; }   // для просмотра на весь экран

        private readonly int _thumbFileId;
        private WeakReference<ImageSource> _img;
        private bool _loading;

        public ImageSource Image
        {
            get
            {
                ImageSource img;
                if (_img != null && _img.TryGetTarget(out img)) return img;
                if (_loading || _thumbFileId == 0) return null;
                _loading = true;
                img = Images.Get(ImageKind.Thumb, _thumbFileId, loaded =>
                {
                    _loading = false;
                    _img = new WeakReference<ImageSource>(loaded);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
                });
                if (img != null) { _loading = false; _img = new WeakReference<ImageSource>(img); }
                return img;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
