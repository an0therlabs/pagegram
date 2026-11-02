using System.Collections.Generic;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Models
{
    public enum LinkKind { Mention, MentionName, Url, TextUrl, Email }

    /// <summary>Кусок текста-ссылка. Offset/Length — в UTF-16, как и у TDLib, и как у string в C#.</summary>
    public sealed class LinkSpan
    {
        public int Offset;
        public int Length;
        public LinkKind Kind;
        public string Arg;   // url / @username / email
        public long UserId;  // для MentionName
    }

    /// <summary>
    /// Текст сообщения + ссылки в нём. Неизменяемый: при правке сообщения создаётся новый,
    /// и TextBlock перестраивает разметку (см. Views/RichText).
    /// </summary>
    public sealed class RichContent
    {
        public RichContent(string text, List<LinkSpan> links)
        {
            Text = text ?? string.Empty;
            Links = links;
        }

        public string Text { get; }
        public List<LinkSpan> Links { get; } // null — ссылок нет

        public static RichContent From(TdApi.FormattedText ft, string fallbackText)
        {
            if (ft == null) return new RichContent(fallbackText, null);
            List<LinkSpan> links = null;
            if (ft.Entities != null)
            {
                foreach (var e in ft.Entities)
                {
                    LinkSpan s = null;
                    string piece = Slice(ft.Text, e.Offset, e.Length);
                    switch (e.Type)
                    {
                        case TdApi.TextEntityTypeMention _: s = new LinkSpan { Kind = LinkKind.Mention, Arg = piece }; break;
                        case TdApi.TextEntityTypeMentionName mn: s = new LinkSpan { Kind = LinkKind.MentionName, UserId = mn.UserId }; break;
                        case TdApi.TextEntityTypeUrl _: s = new LinkSpan { Kind = LinkKind.Url, Arg = piece }; break;
                        case TdApi.TextEntityTypeTextUrl tu: s = new LinkSpan { Kind = LinkKind.TextUrl, Arg = tu.Url }; break;
                        case TdApi.TextEntityTypeEmailAddress _: s = new LinkSpan { Kind = LinkKind.Email, Arg = piece }; break;
                    }
                    if (s == null || piece == null) continue;
                    s.Offset = e.Offset;
                    s.Length = e.Length;
                    if (links == null) links = new List<LinkSpan>();
                    links.Add(s);
                }
            }
            if (links != null) links.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            return new RichContent(ft.Text, links);
        }

        private static string Slice(string t, int off, int len)
        {
            if (t == null || off < 0 || len <= 0 || off + len > t.Length) return null;
            return t.Substring(off, len);
        }
    }
}
