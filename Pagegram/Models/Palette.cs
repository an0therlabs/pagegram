using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml.Media;

namespace Pagegram.Models
{
    /// <summary>
    /// Общие кисти. Создаются один раз и шарятся всеми элементами —
    /// вместо отдельной кисти на каждое сообщение.
    /// </summary>
    public static class Palette
    {
        private static SolidColorBrush[] _avatars;
        private static SolidColorBrush _out, _in, _accent;

        /// <summary>Системный акцентный цвет телефона — как у родных приложений W10M.</summary>
        public static SolidColorBrush Accent
        {
            get
            {
                if (_accent == null)
                {
                    Color c;
                    try { c = new UISettings().GetColorValue(UIColorType.Accent); }
                    catch { c = Color.FromArgb(255, 0x00, 0x78, 0xD7); }
                    _accent = new SolidColorBrush(c);
                }
                return _accent;
            }
        }

        // Исходящие — акцентный цвет телефона, входящие — тёмно-серые, текст белый
        public static SolidColorBrush Outgoing => _out ?? (_out = Accent);
        public static SolidColorBrush Incoming => _in ?? (_in = new SolidColorBrush(Color.FromArgb(255, 0x33, 0x33, 0x33)));

        private static SolidColorBrush _transparent, _white, _secondary, _footer;
        public static SolidColorBrush Footer => _footer ?? (_footer = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)));
        public static SolidColorBrush Transparent => _transparent ?? (_transparent = new SolidColorBrush(Colors.Transparent));
        public static SolidColorBrush White => _white ?? (_white = new SolidColorBrush(Colors.White));
        public static SolidColorBrush Secondary => _secondary ?? (_secondary = new SolidColorBrush(Color.FromArgb(255, 0x99, 0x99, 0x99)));

        public static SolidColorBrush Avatar(long id)
        {
            if (_avatars == null)
            {
                _avatars = new[]
                {
                    new SolidColorBrush(Color.FromArgb(255, 0xE1, 0x7A, 0x76)),
                    new SolidColorBrush(Color.FromArgb(255, 0xF0, 0xA0, 0x4B)),
                    new SolidColorBrush(Color.FromArgb(255, 0x9B, 0x7B, 0xD4)),
                    new SolidColorBrush(Color.FromArgb(255, 0x6F, 0xBF, 0x6A)),
                    new SolidColorBrush(Color.FromArgb(255, 0x5A, 0xB3, 0xC9)),
                    new SolidColorBrush(Color.FromArgb(255, 0x52, 0x8D, 0xD6)),
                    new SolidColorBrush(Color.FromArgb(255, 0xD6, 0x6B, 0xA8)),
                };
            }
            var i = (int)((id < 0 ? -id : id) % _avatars.Length);
            return _avatars[i];
        }
    }
}
