using System.Collections.Generic;
using System.Collections.ObjectModel;
using Pagegram.Models;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Services
{
    /// <summary>
    /// Список чатов, собранный из апдейтов TDLib. Живёт только на UI-потоке.
    /// Main отсортирован по ChatPosition.Order (по убыванию), как в официальных клиентах.
    /// </summary>
    public sealed class ChatStore
    {
        public static ChatStore Current { get; } = new ChatStore();

        private readonly Dictionary<long, ChatItem> _all = new Dictionary<long, ChatItem>();

        public ObservableCollection<ChatItem> Main { get; } = new ObservableCollection<ChatItem>();

        public ChatItem Get(long id)
        {
            ChatItem c;
            return _all.TryGetValue(id, out c) ? c : null;
        }

        /// <summary>
        /// Гарантирует, что чат есть в сторе (например, только что созданный личный чат
        /// или результат поиска, апдейт которого ещё не дошёл до UI).
        /// </summary>
        public async System.Threading.Tasks.Task<ChatItem> EnsureAsync(long chatId)
        {
            var item = Get(chatId);
            if (item != null) return item;
            var r = await TdService.Current.SendAsync(new TdApi.GetChat { ChatId = chatId });
            if (r is TdApi.Chat chat && Get(chatId) == null)
                Handle(new TdApi.UpdateNewChat { Chat = chat });
            return Get(chatId);
        }

        public void Clear()
        {
            _all.Clear();
            Main.Clear();
        }

        public void Handle(TdApi.BaseObject update)
        {
            ChatItem item;
            switch (update)
            {
                case TdApi.UpdateNewChat n:
                {
                    var c = n.Chat;
                    item = new ChatItem(c.Id) { Title = c.Title, Unread = c.UnreadCount, PhotoFileId = c.Photo?.Small?.Id ?? 0 };
                    if (c.Type is TdApi.ChatTypeBasicGroup) item.IsGroup = true;
                    else if (c.Type is TdApi.ChatTypeSupergroup sg) { item.IsChannel = sg.IsChannel; item.IsGroup = !sg.IsChannel; }
                    if (c.LastMessage != null)
                    {
                        item.Preview = Formatter.Preview(c.LastMessage);
                        item.Time = Formatter.Time(c.LastMessage.Date);
                    }
                    _all[c.Id] = item;
                    ApplyPositions(item, c.Positions);
                    break;
                }

                case TdApi.UpdateChatTitle t:
                    if ((item = Get(t.ChatId)) != null) item.Title = t.Title;
                    break;

                case TdApi.UpdateChatPhoto ph:
                    if ((item = Get(ph.ChatId)) != null) item.PhotoFileId = ph.Photo?.Small?.Id ?? 0;
                    break;

                case TdApi.UpdateChatLastMessage m:
                    if ((item = Get(m.ChatId)) == null) break;
                    item.Preview = Formatter.Preview(m.LastMessage);
                    item.Time = m.LastMessage != null ? Formatter.Time(m.LastMessage.Date) : string.Empty;
                    ApplyPositions(item, m.Positions);
                    break;

                case TdApi.UpdateChatPosition p:
                    if ((item = Get(p.ChatId)) != null) ApplyPosition(item, p.Position);
                    break;

                case TdApi.UpdateChatDraftMessage d:
                    if ((item = Get(d.ChatId)) != null) ApplyPositions(item, d.Positions);
                    break;

                case TdApi.UpdateChatReadInbox r:
                    if ((item = Get(r.ChatId)) != null) item.Unread = r.UnreadCount;
                    break;
            }
        }

        private void ApplyPositions(ChatItem item, IList<TdApi.ChatPosition> positions)
        {
            if (positions == null) return;
            foreach (var p in positions) ApplyPosition(item, p);
        }

        private void ApplyPosition(ChatItem item, TdApi.ChatPosition pos)
        {
            // Показываем только основной список (без архива и папок) — проще и легче
            if (!(pos.List is TdApi.ChatListMain)) return;
            if (item.Order == pos.Order && item.InMain == (pos.Order != 0)) return;
            item.Order = pos.Order;
            Reposition(item);
        }

        private void Reposition(ChatItem item)
        {
            if (item.InMain)
            {
                int cur = Main.IndexOf(item);
                if (item.Order != 0 && cur >= 0 && IsInPlace(cur, item.Order)) return;
                if (cur >= 0) Main.RemoveAt(cur);
                item.InMain = false;
            }
            if (item.Order == 0) return;

            // бинарный поиск по убыванию Order
            int lo = 0, hi = Main.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (Main[mid].Order > item.Order) lo = mid + 1; else hi = mid;
            }
            Main.Insert(lo, item);
            item.InMain = true;
        }

        private bool IsInPlace(int i, long order)
        {
            if (i > 0 && Main[i - 1].Order < order) return false;
            if (i < Main.Count - 1 && Main[i + 1].Order > order) return false;
            return true;
        }
    }
}
