using System;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Services
{
    /// <summary>Превращает контент TDLib в короткие строки. Никаких картинок — только текст.</summary>
    public static class Formatter
    {
        public static string FullName(string first, string last)
        {
            if (string.IsNullOrEmpty(last)) return first ?? string.Empty;
            if (string.IsNullOrEmpty(first)) return last;
            return first + " " + last;
        }

        public static string Time(int unixDate)
        {
            if (unixDate <= 0) return string.Empty;
            var dt = DateTimeOffset.FromUnixTimeSeconds(unixDate).ToLocalTime();
            var now = DateTimeOffset.Now;
            if (dt.Date == now.Date) return dt.ToString("HH:mm");
            if (dt.Year == now.Year) return dt.ToString("dd.MM");
            return dt.ToString("dd.MM.yy");
        }

        /// <summary>«в сети», «был(а) в 14:05», «был(а) недавно»…</summary>
        public static string UserStatus(TdApi.UserStatus s)
        {
            switch (s)
            {
                case TdApi.UserStatusOnline _: return "в сети";
                case TdApi.UserStatusOffline o:
                {
                    var dt = DateTimeOffset.FromUnixTimeSeconds(o.WasOnline).ToLocalTime();
                    var now = DateTimeOffset.Now;
                    if (dt.Date == now.Date) return "был(а) в " + dt.ToString("HH:mm");
                    if (dt.Date == now.Date.AddDays(-1)) return "был(а) вчера в " + dt.ToString("HH:mm");
                    return "был(а) " + dt.ToString("dd.MM.yy");
                }
                case TdApi.UserStatusRecently _: return "был(а) недавно";
                case TdApi.UserStatusLastWeek _: return "был(а) на этой неделе";
                case TdApi.UserStatusLastMonth _: return "был(а) в этом месяце";
                default: return "был(а) давно";
            }
        }

        public static string Preview(TdApi.Message m)
        {
            if (m == null) return string.Empty;
            var s = Content(m.Content);
            s = s.Replace('\n', ' ').Replace('\r', ' ');
            if (s.Length > Config.PreviewMaxLength) s = s.Substring(0, Config.PreviewMaxLength) + "…";
            return s;
        }

        public static string Content(TdApi.MessageContent c)
        {
            switch (c)
            {
                case null: return string.Empty;
                case TdApi.MessageText t: return t.Text?.Text ?? string.Empty;
                case TdApi.MessagePhoto p: return WithCaption("📷 Фото", p.Caption);
                case TdApi.MessageVideo v: return WithCaption("🎬 Видео", v.Caption);
                case TdApi.MessageAnimation a: return WithCaption("GIF", a.Caption);
                case TdApi.MessageDocument d: return WithCaption("📎 " + (d.Document?.FileName ?? "Файл"), d.Caption);
                case TdApi.MessageAudio au:
                    return WithCaption("🎵 " + FullName(au.Audio?.Performer, au.Audio?.Title).Trim(), au.Caption);
                case TdApi.MessageVoiceNote vn: return WithCaption("🎤 Голосовое " + Duration(vn.VoiceNote?.Duration ?? 0), vn.Caption);
                case TdApi.MessageVideoNote _: return "⭕ Видеосообщение";
                case TdApi.MessageSticker s: return (s.Sticker?.Emoji ?? "") + " Стикер";
                case TdApi.MessageLocation _: return "📍 Геопозиция";
                case TdApi.MessageContact ct: return "👤 " + FullName(ct.Contact?.FirstName, ct.Contact?.LastName);
                case TdApi.MessagePoll _: return "📊 Опрос";
                case TdApi.MessageCall _: return "📞 Звонок";
                case TdApi.MessageChatAddMembers _: return "Добавлены участники";
                case TdApi.MessageChatJoinByLink _: return "Вступил(а) по ссылке";
                case TdApi.MessageChatDeleteMember _: return "Участник покинул чат";
                case TdApi.MessagePinMessage _: return "Сообщение закреплено";
                case TdApi.MessageChatChangeTitle ch: return "Название изменено: " + ch.Title;
                case TdApi.MessageChatChangePhoto _: return "Фото чата изменено";
                case TdApi.MessageContactRegistered _: return "Теперь в Telegram";
                case TdApi.MessageUnsupported _: return "[Не поддерживается]";
                default:
                    // Имя типа без префикса Message — лучше, чем ничего
                    var n = c.GetType().Name;
                    if (n.StartsWith("Message")) n = n.Substring(7);
                    return "[" + n + "]";
            }
        }

        private static string WithCaption(string prefix, TdApi.FormattedText caption)
        {
            var t = caption?.Text;
            return string.IsNullOrEmpty(t) ? prefix : prefix + "\n" + t;
        }

        private static string Duration(int sec)
        {
            return (sec / 60) + ":" + (sec % 60).ToString("00");
        }

        /// <summary>
        /// Выбирает размер фото: самый большой, но не больше MaxPhotoSide.
        /// Оригиналы 2560px на Люмии с 512 МБ — это смерть.
        /// </summary>
        public static TdApi.PhotoSize PickPhotoSize(TdApi.Photo photo)
        {
            if (photo?.Sizes == null || photo.Sizes.Count == 0) return null;
            TdApi.PhotoSize best = null, smallest = null;
            foreach (var s in photo.Sizes)
            {
                int side = Math.Max(s.Width, s.Height);
                if (smallest == null || side < Math.Max(smallest.Width, smallest.Height)) smallest = s;
                if (side <= Config.MaxPhotoSide && (best == null || side > Math.Max(best.Width, best.Height))) best = s;
            }
            return best ?? smallest;
        }
    }
}
