using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Display;
using Windows.Storage;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Services
{
    public enum ImageKind { Avatar, Photo, Sticker }

    /// <summary>
    /// Картинки из TDLib: аватарки, фото в ленте и стикеры. Экономно для 512 МБ:
    ///  - качаем только когда элемент реально на экране (ленивый геттер под x:Bind);
    ///  - декодируем сразу в размер, в котором рисуем (DecodePixelWidth / масштаб libwebp);
    ///  - один объект на файл, небольшие LRU-кэши по видам; при нехватке памяти — чистим;
    ///  - WebP (стикеры) — нативным libwebp, у W10M своего кодека нет.
    /// Только UI-поток.
    /// </summary>
    public static class Images
    {
        public const int AvatarSize = 48;   // логические px
        public const int PhotoWidth = 260;  // ширина фото в пузыре
        public const int StickerSize = 140; // сторона стикера

        private sealed class Cache
        {
            private readonly int _max;
            private readonly Dictionary<int, LinkedListNode<KeyValuePair<int, ImageSource>>> _map =
                new Dictionary<int, LinkedListNode<KeyValuePair<int, ImageSource>>>();
            private readonly LinkedList<KeyValuePair<int, ImageSource>> _lru = new LinkedList<KeyValuePair<int, ImageSource>>();

            public Cache(int max) { _max = max; }

            public ImageSource TryGet(int id)
            {
                LinkedListNode<KeyValuePair<int, ImageSource>> n;
                if (!_map.TryGetValue(id, out n)) return null;
                _lru.Remove(n);
                _lru.AddFirst(n);
                return n.Value.Value;
            }

            public void Put(int id, ImageSource img)
            {
                if (_map.ContainsKey(id)) return;
                _map[id] = _lru.AddFirst(new KeyValuePair<int, ImageSource>(id, img));
                while (_lru.Count > _max)
                {
                    _map.Remove(_lru.Last.Value.Key);
                    _lru.RemoveLast();
                }
            }

            public void Clear() { _map.Clear(); _lru.Clear(); }
        }

        private static readonly Cache _avatars = new Cache(80);
        private static readonly Cache _photos = new Cache(12);
        private static readonly Cache _stickers = new Cache(24);
        private static readonly Dictionary<long, List<Action<ImageSource>>> _pending = new Dictionary<long, List<Action<ImageSource>>>();
        private static double _scale;

        static Images()
        {
            LowMemory.Trim += () => { _avatars.Clear(); _photos.Clear(); _stickers.Clear(); };
        }

        private static Cache CacheOf(ImageKind k)
        {
            return k == ImageKind.Avatar ? _avatars : k == ImageKind.Photo ? _photos : _stickers;
        }

        private static double Scale
        {
            get
            {
                if (_scale <= 0)
                {
                    try { _scale = DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel; } catch { }
                    if (_scale <= 0) _scale = 1;
                }
                return _scale;
            }
        }

        /// <summary>
        /// Картинка из кэша сразу, иначе null + загрузка; onReady вызовется на UI-потоке.
        /// </summary>
        public static ImageSource Get(ImageKind kind, int fileId, Action<ImageSource> onReady)
        {
            if (fileId == 0) return null;
            var cached = CacheOf(kind).TryGet(fileId);
            if (cached != null) return cached;

            long key = ((long)kind << 32) | (uint)fileId;
            List<Action<ImageSource>> waiters;
            if (_pending.TryGetValue(key, out waiters)) { waiters.Add(onReady); return null; }
            _pending[key] = new List<Action<ImageSource>> { onReady };
            Load(kind, fileId, key);
            return null;
        }

        private static async void Load(ImageKind kind, int fileId, long key)
        {
            ImageSource result = null;
            try
            {
                var r = await TdService.Current.SendAsync(new TdApi.DownloadFile
                {
                    FileId = fileId,
                    Priority = kind == ImageKind.Avatar ? 1 : 2, // фото по тапу — 32, оно важнее
                    Offset = 0,
                    Limit = 0,
                    Synchronous = true
                });
                var file = r as TdApi.File;
                if (file?.Local != null && file.Local.IsDownloadingCompleted && !string.IsNullOrEmpty(file.Local.Path))
                {
                    var sf = await StorageFile.GetFileFromPathAsync(file.Local.Path);
                    var bytes = (await FileIO.ReadBufferAsync(sf)).ToArray();
                    // Формат определяем сами по сигнатуре — нативный модуль трогаем только для WebP,
                    // чтобы его возможная поломка не убила фото и аватарки.
                    if (IsWebP(bytes))
                    {
                        result = DecodeWebP(bytes, kind);
                        if (result == null) result = await TryDecodeBitmap(bytes, kind); // вдруг у системы есть кодек
                    }
                    else
                    {
                        result = await DecodeBitmap(bytes, kind);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("image " + kind + " " + fileId + ": " + ex.Message);
            }

            if (result != null) CacheOf(kind).Put(fileId, result);

            List<Action<ImageSource>> waiters;
            if (_pending.TryGetValue(key, out waiters))
            {
                _pending.Remove(key);
                if (result != null) foreach (var w in waiters) w(result);
            }
        }

        private static async System.Threading.Tasks.Task<ImageSource> DecodeBitmap(byte[] bytes, ImageKind kind)
        {
            var bmp = new BitmapImage { DecodePixelType = DecodePixelType.Logical };
            if (kind == ImageKind.Avatar) { bmp.DecodePixelWidth = AvatarSize; bmp.DecodePixelHeight = AvatarSize; }
            else if (kind == ImageKind.Photo) bmp.DecodePixelWidth = PhotoWidth;
            else bmp.DecodePixelWidth = StickerSize;

            using (var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream())
            {
                await stream.WriteAsync(bytes.AsBuffer());
                stream.Seek(0);
                await bmp.SetSourceAsync(stream);
            }
            return bmp;
        }

        private static bool IsWebP(byte[] d)
        {
            return d != null && d.Length >= 12 &&
                   d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' &&
                   d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P';
        }

        private static bool _webpBroken;

        private static async System.Threading.Tasks.Task<ImageSource> TryDecodeBitmap(byte[] bytes, ImageKind kind)
        {
            try { return await DecodeBitmap(bytes, kind); } catch { return null; }
        }

        private static ImageSource DecodeWebP(byte[] bytes, ImageKind kind)
        {
            if (_webpBroken) return null;
            try
            {
                return DecodeWebPNative(bytes, kind);
            }
            catch (Exception ex)
            {
                // Нативный модуль не загрузился — больше не пытаемся, остальные картинки работают
                _webpBroken = true;
                System.Diagnostics.Debug.WriteLine("WebP decoder unavailable: " + ex);
                return null;
            }
        }

        private static ImageSource DecodeWebPNative(byte[] bytes, ImageKind kind)
        {
            var size = new int[2];
            if (!PagegramNative.WebP.GetSize(bytes, size) || size[0] <= 0 || size[1] <= 0) return null;

            int box = (int)Math.Round((kind == ImageKind.Avatar ? AvatarSize : kind == ImageKind.Photo ? PhotoWidth : StickerSize) * Scale);
            double k = Math.Min(1.0, Math.Min((double)box / size[0], (double)box / size[1]));
            int w = Math.Max(1, (int)(size[0] * k));
            int h = Math.Max(1, (int)(size[1] * k));

            var wb = new WriteableBitmap(w, h);
            if (!PagegramNative.WebP.DecodeInto(bytes, w, h, wb.PixelBuffer)) return null;
            wb.Invalidate();
            return wb;
        }

        public static void Clear()
        {
            _avatars.Clear(); _photos.Clear(); _stickers.Clear();
        }
    }
}
