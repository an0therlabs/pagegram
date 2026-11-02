using Windows.UI;
using Windows.UI.Xaml.Media;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Models
{
    /// <summary>Плашка реакции под сообщением: «👍 3». Своя реакция подсвечена.</summary>
    public sealed class ReactionChip
    {
        private static SolidColorBrush _chosen, _normal;

        public ReactionChip(long chatId, long messageId, TdApi.MessageReaction r)
        {
            ChatId = chatId;
            MessageId = messageId;
            Type = r.Type;
            IsChosen = r.IsChosen;
            string emoji =
                r.Type is TdApi.ReactionTypeEmoji e ? e.Emoji :
                r.Type is TdApi.ReactionTypePaid ? "⭐" :
                "✨"; // кастомные эмодзи — это стикеры, рисуем значком
            Label = emoji + " " + r.TotalCount;
        }

        public long ChatId { get; }
        public long MessageId { get; }
        public TdApi.ReactionType Type { get; }
        public bool IsChosen { get; }
        public string Label { get; }
        public bool CanToggle => !(Type is TdApi.ReactionTypePaid); // платные звёзды не трогаем

        public Brush ChipBrush => IsChosen
            ? (_chosen ?? (_chosen = new SolidColorBrush(Color.FromArgb(0xCC, 0x3A, 0x8D, 0xDE))))
            : (_normal ?? (_normal = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF))));
    }
}
