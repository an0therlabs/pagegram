using System;
using System.ComponentModel;
using Pagegram.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Pagegram.Models
{
    /// <summary>
    /// Слайд просмотрщика аватарок. Картинку держит слабо: FlipView реализует только соседние
    /// слайды, ушедшие могут освободиться и при возврате загрузятся заново (файл уже на диске).
    /// </summary>
    public sealed class AvatarSlide : INotifyPropertyChanged
    {
        public AvatarSlide(int fileId, string date)
        {
            _fileId = fileId;
            Date = date ?? string.Empty;
        }

        public string Date { get; }

        private readonly int _fileId;
        private WeakReference<ImageSource> _img;
        private bool _loading;

        public Visibility LoadingVisibility => _loading ? Visibility.Visible : Visibility.Collapsed;

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
            var img = await Images.LoadFullAsync(_fileId);
            _loading = false;
            if (img != null) _img = new WeakReference<ImageSource>(img);
            Raise(nameof(LoadingVisibility));
            Raise(nameof(Image));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string n) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); }
    }
}
