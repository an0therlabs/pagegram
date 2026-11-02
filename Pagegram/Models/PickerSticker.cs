using System;
using System.ComponentModel;
using Pagegram.Services;
using Windows.UI.Xaml.Media;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Models
{
    /// <summary>Стикер в пикере. Превью (thumbnail) грузится лениво и держится слабо.</summary>
    public sealed class PickerSticker : INotifyPropertyChanged
    {
        public PickerSticker(TdApi.Sticker s)
        {
            FileId = s.StickerValue?.Id ?? 0;
            Width = s.Width;
            Height = s.Height;
            Emoji = s.Emoji ?? string.Empty;
            // превью: thumbnail (обычно WebP 128px); у статичного WebP без превью — сам стикер
            var th = s.Thumbnail;
            if (th?.File != null && (th.Format is TdApi.ThumbnailFormatWebp || th.Format is TdApi.ThumbnailFormatPng || th.Format is TdApi.ThumbnailFormatJpeg))
                _thumbFileId = th.File.Id;
            else if (s.Format is TdApi.StickerFormatWebp)
                _thumbFileId = FileId;
        }

        public int FileId { get; }
        public int Width { get; }
        public int Height { get; }
        public string Emoji { get; }

        private readonly int _thumbFileId;
        private WeakReference<ImageSource> _img;
        private bool _loading;

        /// <summary>Пока картинки нет — показываем эмодзи стикера.</summary>
        public string Placeholder => Emoji;

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

    /// <summary>Вкладка набора стикеров в пикере. Id == 0 — «Недавние».</summary>
    public sealed class StickerSetTab
    {
        public StickerSetTab(long id, string title) { Id = id; Title = title ?? string.Empty; }
        public long Id { get; }
        public string Title { get; }
    }
}
