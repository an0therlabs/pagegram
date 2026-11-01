using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Pagegram.Models;
using Pagegram.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Views
{
    public sealed partial class ChatPage : Page
    {
        public ObservableCollection<MessageItem> Messages { get; } = new ObservableCollection<MessageItem>();

        private readonly Dictionary<long, MessageItem> _byId = new Dictionary<long, MessageItem>();
        private long _chatId;
        private bool _showSender;
        private bool _loading;
        private bool _historyEnd;
        private ScrollViewer _scroll;

        public ChatPage()
        {
            InitializeComponent();
            MessageList.Loaded += (s, e) =>
            {
                if (_scroll != null) return;
                _scroll = ChatsPage.FindScrollViewer(MessageList);
                if (_scroll != null) _scroll.ViewChanged += OnViewChanged;
            };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            _chatId = (long)e.Parameter;
            var chat = ChatStore.Current.Get(_chatId);
            Title.Text = chat?.Title ?? string.Empty;
            _showSender = chat != null && chat.IsGroup;
            if (chat != null && chat.IsChannel) Input.PlaceholderText = "Сообщение в канал";

            TdService.Current.Update += OnUpdate;
            LowMemory.Trim += OnTrim;
            TdService.Current.Send(new TdApi.OpenChat { ChatId = _chatId });
            LoadOlder();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            TdService.Current.Update -= OnUpdate;
            LowMemory.Trim -= OnTrim;
            TdService.Current.Send(new TdApi.CloseChat { ChatId = _chatId });

            // Явно отпускаем всё, чтобы память освободилась сразу после выхода из чата
            if (_scroll != null) _scroll.ViewChanged -= OnViewChanged;
            MessageList.ItemsSource = null;
            Messages.Clear();
            _byId.Clear();
        }

        // ---- история ----

        private async void LoadOlder()
        {
            if (_loading || _historyEnd) return;
            _loading = true;

            long fromId = Messages.Count > 0 ? Messages[0].Id : 0;
            var batch = new List<TdApi.Message>();

            // TDLib при первом запросе может вернуть только 1-2 сообщения из локальной базы,
            // поэтому догоняем несколькими запросами до нужного размера страницы.
            for (int attempt = 0; attempt < 4 && batch.Count < Config.HistoryPageSize; attempt++)
            {
                var r = await TdService.Current.SendAsync(new TdApi.GetChatHistory
                {
                    ChatId = _chatId,
                    FromMessageId = fromId,
                    Offset = 0,
                    Limit = Config.HistoryPageSize - batch.Count,
                    OnlyLocal = false
                });
                var msgs = (r as TdApi.Messages)?.MessagesValue;
                if (msgs == null || msgs.Count == 0) { _historyEnd = true; break; }
                foreach (var m in msgs) { if (m != null) batch.Add(m); }
                fromId = msgs[msgs.Count - 1].Id;
            }

            if (_chatId == 0 || MessageList.ItemsSource == null) { _loading = false; return; } // ушли со страницы

            // TDLib отдаёт от новых к старым — вставляем сверху в обратном порядке
            var ids = new List<long>(batch.Count);
            foreach (var m in batch)
            {
                if (_byId.ContainsKey(m.Id)) continue;
                var item = MessageItem.From(m, _showSender);
                _byId[m.Id] = item;
                Messages.Insert(0, item);
                ids.Add(m.Id);
            }
            if (ids.Count > 0)
                TdService.Current.Send(new TdApi.ViewMessages { ChatId = _chatId, MessageIds = ids, ForceRead = true });

            _loading = false;
        }

        private void OnViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_scroll.VerticalOffset < 300) LoadOlder();
        }

        private bool IsNearBottom =>
            _scroll == null || _scroll.VerticalOffset >= _scroll.ScrollableHeight - 200;

        // ---- апдейты ----

        private void OnUpdate(TdApi.BaseObject u)
        {
            MessageItem item;
            switch (u)
            {
                case TdApi.UpdateNewMessage n:
                    if (n.Message.ChatId != _chatId || _byId.ContainsKey(n.Message.Id)) return;
                    item = MessageItem.From(n.Message, _showSender);
                    _byId[item.Id] = item;
                    Messages.Add(item);
                    if (!n.Message.IsOutgoing)
                        TdService.Current.Send(new TdApi.ViewMessages { ChatId = _chatId, MessageIds = new List<long> { item.Id }, ForceRead = true });
                    TrimTop(Config.MaxMessagesInMemory);
                    break;

                case TdApi.UpdateMessageSendSucceeded ok:
                    if (ok.Message.ChatId != _chatId || !_byId.TryGetValue(ok.OldMessageId, out item)) return;
                    _byId.Remove(ok.OldMessageId);
                    item.Id = ok.Message.Id;
                    item.Status = "✓";
                    _byId[item.Id] = item;
                    break;

                case TdApi.UpdateMessageSendFailed fail:
                    if (fail.Message.ChatId != _chatId || !_byId.TryGetValue(fail.OldMessageId, out item)) return;
                    _byId.Remove(fail.OldMessageId);
                    item.Id = fail.Message.Id;
                    item.Status = "! " + fail.Error?.Message;
                    _byId[item.Id] = item;
                    break;

                case TdApi.UpdateMessageContent c:
                    if (c.ChatId == _chatId && _byId.TryGetValue(c.MessageId, out item))
                        item.Text = Formatter.Content(c.NewContent);
                    break;

                case TdApi.UpdateDeleteMessages d:
                    if (d.ChatId != _chatId || !d.IsPermanent) return;
                    foreach (var id in d.MessageIds)
                    {
                        if (_byId.TryGetValue(id, out item))
                        {
                            _byId.Remove(id);
                            Messages.Remove(item);
                        }
                    }
                    break;
            }
        }

        /// <summary>Срезаем старые сообщения сверху, если пользователь внизу.</summary>
        private void TrimTop(int keep)
        {
            if (Messages.Count <= keep || !IsNearBottom) return;
            while (Messages.Count > keep)
            {
                _byId.Remove(Messages[0].Id);
                Messages.RemoveAt(0);
            }
            _historyEnd = false; // срезанное можно будет подгрузить снова
        }

        private void OnTrim()
        {
            TrimTop(Config.LowMemoryKeepMessages);
        }

        // ---- действия ----

        private void OnSend(object sender, RoutedEventArgs e)
        {
            var text = Input.Text.Trim();
            if (text.Length == 0) return;
            Input.Text = string.Empty;

            // Само сообщение придёт через updateNewMessage (со статусом "отправляется")
            TdService.Current.Send(new TdApi.SendMessage
            {
                ChatId = _chatId,
                InputMessageContent = new TdApi.InputMessageText
                {
                    Text = new TdApi.FormattedText { Text = text, Entities = new List<TdApi.TextEntity>() },
                    ClearDraft = true
                }
            });
        }

        private void OnMessageClick(object sender, ItemClickEventArgs e)
        {
            var m = e.ClickedItem as MessageItem;
            if (m != null && m.PhotoFileId != 0) Frame.Navigate(typeof(PhotoPage), m.PhotoFileId);
        }
    }
}
