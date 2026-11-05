using Windows.Storage;

namespace Pagegram.Services
{
    /// <summary>Локальные настройки клиента (хранятся в LocalSettings приложения).</summary>
    public static class AppSettings
    {
        private static ApplicationDataContainer Store => ApplicationData.Current.LocalSettings;

        private static T Get<T>(string key, T def)
        {
            try
            {
                object v;
                if (Store.Values.TryGetValue(key, out v) && v is T) return (T)v;
            }
            catch { }
            return def;
        }

        private static void Set(string key, object value)
        {
            try { Store.Values[key] = value; } catch { }
        }

        /// <summary>Размер текста сообщений (13–22).</summary>
        public static double MessageFontSize
        {
            get { return Get("MessageFontSize", 16.0); }
            set { Set("MessageFontSize", value); }
        }

        /// <summary>Загружать фото в ленте автоматически. Выключено — экономия трафика и памяти, фото по тапу.</summary>
        public static bool AutoLoadPhotos
        {
            get { return Get("AutoLoadPhotos", true); }
            set { Set("AutoLoadPhotos", value); }
        }
    }
}
