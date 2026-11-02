using System;
using System.Collections.Generic;
using Windows.Storage;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Services
{
    /// <summary>
    /// Круглые аватарки. Экономно:
    ///  - качаем только маленькое превью (small, ~160px), и только когда строка реально видна
    ///    (геттер свойства Photo дёргает x:Bind при реализации элемента списка);
    ///  - декодируем сразу под размер кружка (DecodePixelWidth), ~36 КБ на картинку;
    ///  - один BitmapImage на файл, все сообщения одного человека делят его;
    ///  - кэш ограничен, старые выкидываются; при нехватке памяти чистим целиком.
    /// Работает только на UI-потоке.
    /// </summary>
    public static class Avatars
    {
        public const int Size = 48;       // логические пиксели, больший из кружков
        private const int MaxCached = 80;

        private static readonly Dictionary<int, LinkedListNode<KeyValuePair<int, ImageSource>>> _cache =
            new Dictionary<int, LinkedListNode<KeyValuePair<int, ImageSource>>>();
        private static readonly LinkedList<KeyValuePair<int, ImageSource>> _lru =
            new LinkedList<KeyValuePair<int, ImageSource>>();
        private static readonly Dictionary<int, List<Action<ImageSource>>> _pending =
            new Dictionary<int, List<Action<ImageSource>>>();

        static Avatars()
        {
            LowMemory.Trim += Clear;
        }

        /// <summary>
        /// Возвращает картинку сразу, если она в кэше. Иначе запускает загрузку
        /// и вызовет onReady, когда (и если) картинка будет готова.
        /// </summary>
        public static ImageSource Get(int fileId, Action<ImageSource> onReady)
        {
            if (fileId == 0) return null;

            LinkedListNode<KeyValuePair<int, ImageSource>> node;
            if (_cache.TryGetValue(fileId, out node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Value;
            }

            List<Action<ImageSource>> waiters;
            if (_pending.TryGetValue(fileId, out waiters))
            {
                waiters.Add(onReady);
                return null;
            }

            _pending[fileId] = new List<Action<ImageSource>> { onReady };
            Load(fileId);
            return null;
        }

        private static async void Load(int fileId)
        {
            ImageSource result = null;
            try
            {
                var r = await TdService.Current.SendAsync(new TdApi.DownloadFile
                {
                    FileId = fileId,
                    Priority = 1,           // ниже, чем фото по тапу (32)
                    Offset = 0,
                    Limit = 0,
                    Synchronous = true
                });
                var file = r as TdApi.File;
                if (file?.Local != null && file.Local.IsDownloadingCompleted && !string.IsNullOrEmpty(file.Local.Path))
                {
                    var sf = await StorageFile.GetFileFromPathAsync(file.Local.Path);
                    using (var stream = await sf.OpenReadAsync())
                    {
                        var bmp = new BitmapImage
                        {
                            DecodePixelType = DecodePixelType.Logical,
                            DecodePixelWidth = Size,
                            DecodePixelHeight = Size
                        };
                        await bmp.SetSourceAsync(stream);
                        result = bmp;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("avatar " + fileId + ": " + ex.Message);
            }

            if (result != null) Put(fileId, result);

            List<Action<ImageSource>> waiters;
            if (_pending.TryGetValue(fileId, out waiters))
            {
                _pending.Remove(fileId);
                if (result != null)
                    foreach (var w in waiters) w(result);
            }
        }

        private static void Put(int fileId, ImageSource img)
        {
            if (_cache.ContainsKey(fileId)) return;
            var node = _lru.AddFirst(new KeyValuePair<int, ImageSource>(fileId, img));
            _cache[fileId] = node;
            while (_lru.Count > MaxCached)
            {
                var last = _lru.Last;
                _lru.RemoveLast();
                _cache.Remove(last.Value.Key);
            }
        }

        public static void Clear()
        {
            _cache.Clear();
            _lru.Clear();
        }
    }
}
