using Pagegram.Services;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Models
{
    /// <summary>Тема форума (супергруппы с темами) для списка тем.</summary>
    public sealed class TopicItem
    {
        public TopicItem(TdApi.ForumTopic t)
        {
            var info = t.Info;
            Id = info.ForumTopicId;
            Name = info.Name ?? string.Empty;
            IsGeneral = info.IsGeneral;
            IsClosed = info.IsClosed;
            IsPinned = t.IsPinned;
            Unread = t.UnreadCount;
            Preview = Formatter.Preview(t.LastMessage);
            Time = t.LastMessage != null ? Formatter.Time(t.LastMessage.Date) : string.Empty;

            // иконка темы — цветной кружок с первой буквой (кастомные эмодзи-иконки не рисуем)
            int c = info.Icon?.Color ?? 0x6FB9F0;
            if (c == 0) c = 0x6FB9F0;
            IconBrush = new SolidColorBrush(Color.FromArgb(255, (byte)(c >> 16), (byte)(c >> 8), (byte)c));
            Initial = IsGeneral ? "#" : FirstLetter(Name);
        }

        public int Id { get; }
        public string Name { get; }
        public bool IsGeneral { get; }
        public bool IsClosed { get; }
        public bool IsPinned { get; }
        public int Unread { get; }
        public string Preview { get; }
        public string Time { get; }
        public Brush IconBrush { get; }
        public string Initial { get; }

        public string Title => (IsPinned ? "📌 " : "") + (IsClosed ? "🔒 " : "") + Name;
        public string UnreadText => Unread > 999 ? "999+" : Unread.ToString();
        public Visibility UnreadVisibility => Unread > 0 ? Visibility.Visible : Visibility.Collapsed;

        private static string FirstLetter(string s)
        {
            foreach (var ch in s)
                if (char.IsLetterOrDigit(ch)) return char.ToUpperInvariant(ch).ToString();
            return "?";
        }
    }
}
