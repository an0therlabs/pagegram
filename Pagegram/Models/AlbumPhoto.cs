using System;
using System.ComponentModel;
using Pagegram.Services;
using Windows.UI.Xaml.Media;

namespace Pagegram.Models
{
    /// <summary>
    /// Фото в просмотрщике или в сетке альбома. Картинку держит слабо: ушла с экрана —
    /// может освободиться, вернулась — возьмётся из кэша или с диска.
    /// </summary>
    public sealed class AlbumPhoto : INotifyPropertyChanged
    {
        public AlbumPhoto(long messageId, int thumbFileId, int fullFileId)
        {
            MessageId = messageId;
            ThumbFileId = thumbFileId;
            FullFileId = fullFileId;
        }

        public long MessageId { get; set; }
        public int ThumbFileId { get; }
        public int FullFileId { get; }

        private WeakReference<ImageSource> _img;
        private bool _loading;

        /// <summary>Превью для сетки альбома (учитывает «автозагрузку фото»).</summary>
        public ImageSource Thumb
        {
            get
            {
                if (!AppSettings.AutoLoadPhotos || ThumbFileId == 0) return null;
                ImageSource img;
                if (_img != null && _img.TryGetTarget(out img)) return img;
                if (_loading) return null;
                _loading = true;
                img = Images.Get(ImageKind.Thumb, ThumbFileId, loaded =>
                {
                    _loading = false;
                    _img = new WeakReference<ImageSource>(loaded);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumb)));
                });
                if (img != null) { _loading = false; _img = new WeakReference<ImageSource>(img); }
                return img;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>Слайд просмотрщика фото: полноразмерная картинка под экран, грузится лениво.</summary>
    public sealed class PhotoSlide : INotifyPropertyChanged
    {
        public PhotoSlide(int fileId) { FileId = fileId; }

        public int FileId { get; }
        private WeakReference<ImageSource> _img;
        private bool _loading;

        public Windows.UI.Xaml.Visibility LoadingVisibility =>
            _loading ? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;

        public ImageSource Image
        {
            get
            {
                ImageSource img;
                if (_img != null && _img.TryGetTarget(out img)) return img;
                if (!_loading) Load();
                return null;
            }
        }

        private async void Load()
        {
            _loading = true;
            Raise(nameof(LoadingVisibility));
            var img = await Images.LoadFullAsync(FileId);
            _loading = false;
            if (img != null) _img = new WeakReference<ImageSource>(img);
            Raise(nameof(LoadingVisibility));
            Raise(nameof(Image));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string n) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); }
    }
}
