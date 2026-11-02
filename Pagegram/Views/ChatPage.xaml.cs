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
    /// <summary>Открыть комментарии к посту канала.</summary>
    public sealed class CommentsArgs
    {
        public long ChannelChatId { get; set; }
        public long PostId { get; set; }
        public string ChannelTitle { get; set; }
    }

    public sealed partial class ChatPage : Page
    {
        public ObservableCollection<MessageItem> Messages { get; } = new ObservableCollection<MessageItem>();

        /// <summary>Чат для шапки (аватарка, название).</summary>
        public ChatItem Chat { get; private set; }

        private readonly Dictionary<long, MessageItem> _byId = new Dictionary<long, MessageItem>();
        private long _chatId;
        private long _threadId; // != 0 — режим комментариев: ветка в обсуждающей группе
        private bool _showSender;
        private bool _loading;
        private bool _historyEnd;
        private ScrollViewer _scroll;
        // Один плеер на всю страницу — переезжает в тот кружок, который играет
        private MediaElement _player;
        private Grid _playingHost;
        private bool _alive; // страница открыта (x:Bind ставит ItemsSource позже OnNavigatedTo, на него не опираемся)

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

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            _alive = true;
            var comments = e.Parameter as CommentsArgs;
            if (comments != null)
            {
                // Комментарии живут в связанной группе обсуждения, в ветке этого поста
                Title.Text = "КОММЕНТАРИИ";
                var info = await TdService.Current.SendAsync(new TdApi.GetMessageThread
                {
                    ChatId = comments.ChannelChatId,
                    MessageId = comments.PostId
                });
                if (!_alive) return;
                var thread = info as TdApi.MessageThreadInfo;
                if (thread == null)
                {
                    Subtitle.Text = (info as TdApi.Error)?.Message ?? "не удалось открыть комментарии";
                    InputBar.Visibility = Visibility.Collapsed;
                    return;
                }
                _chatId = thread.ChatId;
                _threadId = thread.MessageThreadId;
            }
            else
            {
                _chatId = (long)e.Parameter;
            }

            // Чат из поиска/контактов мог ещё не дойти до стора апдейтом — подтянем
            var chat = await ChatStore.Current.EnsureAsync(_chatId);
            if (!_alive) return; // успели уйти со страницы
            Chat = chat;
            Bindings.Update();
            if (comments != null)
            {
                Subtitle.Text = comments.ChannelTitle ?? string.Empty;
                _showSender = true;
            }
            else
            {
                Title.Text = (chat?.Title ?? string.Empty).ToUpper();
                Subtitle.Text = chat == null ? string.Empty : chat.IsChannel ? "канал" : chat.IsGroup ? "группа" : string.Empty;
                _showSender = chat != null && chat.IsGroup;
                if (chat != null && chat.IsChannel) SetupChannelBar(chat);
            }
            Subtitle.Visibility = Subtitle.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

            TdService.Current.Update += OnUpdate;
            LowMemory.Trim += OnTrim;
            TdService.Current.Send(new TdApi.OpenChat { ChatId = _chatId });
            LoadOlder();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _alive = false;
            StopVideoNote();
            _player = null;
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
                TdApi.Function req;
                if (_threadId != 0)
                    req = new TdApi.GetMessageThreadHistory
                    {
                        ChatId = _chatId,
                        MessageId = _threadId,
                        FromMessageId = fromId,
                        Offset = 0,
                        Limit = Config.HistoryPageSize - batch.Count
                    };
                else
                    req = new TdApi.GetChatHistory
                    {
                        ChatId = _chatId,
                        FromMessageId = fromId,
                        Offset = 0,
                        Limit = Config.HistoryPageSize - batch.Count,
                        OnlyLocal = false
                    };
                var r = await TdService.Current.SendAsync(req);
                var msgs = (r as TdApi.Messages)?.MessagesValue;
                if (msgs == null || msgs.Count == 0) { _historyEnd = true; break; }
                foreach (var m in msgs) { if (m != null) batch.Add(m); }
                fromId = msgs[msgs.Count - 1].Id;
            }

            if (!_alive) { _loading = false; return; } // ушли со страницы

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
                    if (_threadId != 0 && !InThread(n.Message)) return; // в комментариях — только своя ветка
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
                        item.UpdateContent(c.NewContent);
                    break;

                case TdApi.UpdateMessageInteractionInfo ii:
                    if (ii.ChatId == _chatId && _byId.TryGetValue(ii.MessageId, out item))
                        item.SetReplyInfo(ii.InteractionInfo);
                    break;

                case TdApi.UpdateChatNotificationSettings ns:
                    if (ns.ChatId == _chatId) UpdateMuteText();
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

        private bool InThread(TdApi.Message m)
        {
            if (m.Id == _threadId) return true;
            return m.TopicId is TdApi.MessageTopicThread t && t.MessageThreadId == _threadId;
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

        // ---- канал: полоса уведомлений вместо ввода ----

        private async void SetupChannelBar(ChatItem chat)
        {
            // Пока не знаем права — показываем полосу уведомлений (как у обычного подписчика)
            ShowMuteBar(true);
            UpdateMuteText();

            var sg = await TdService.Current.SendAsync(new TdApi.GetSupergroup { SupergroupId = chat.SupergroupId }) as TdApi.Supergroup;
            if (!_alive || sg == null) return;
            bool canPost = sg.Status is TdApi.ChatMemberStatusCreator ||
                           (sg.Status is TdApi.ChatMemberStatusAdministrator adm && adm.Rights != null && adm.Rights.CanPostMessages);
            ShowMuteBar(!canPost);
        }

        private void ShowMuteBar(bool show)
        {
            MuteBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            InputBar.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateMuteText()
        {
            MuteBar.Content = Chat != null && Chat.IsMuted ? "ВКЛЮЧИТЬ УВЕДОМЛЕНИЯ" : "ВЫКЛЮЧИТЬ УВЕДОМЛЕНИЯ";
        }

        private async void OnToggleMute(object sender, RoutedEventArgs e)
        {
            var settings = Chat?.NotificationSettings;
            if (settings == null) return;
            bool mute = !Chat.IsMuted;
            settings.UseDefaultMuteFor = false;
            settings.MuteFor = mute ? int.MaxValue : 0; // > 366 дней TDLib считает «навсегда»
            MuteBar.IsEnabled = false;
            var r = await TdService.Current.SendAsync(new TdApi.SetChatNotificationSettings
            {
                ChatId = _chatId,
                NotificationSettings = settings
            });
            MuteBar.IsEnabled = true;
            // Точное состояние придёт апдейтом updateChatNotificationSettings, но покажем сразу
            if (r is TdApi.Error) { settings.MuteFor = mute ? 0 : int.MaxValue; }
            UpdateMuteText();
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
                // в комментариях — в ветку поста
                TopicId = _threadId != 0 ? new TdApi.MessageTopicThread { MessageThreadId = _threadId } : null,
                InputMessageContent = new TdApi.InputMessageText
                {
                    Text = new TdApi.FormattedText { Text = text, Entities = new List<TdApi.TextEntity>() },
                    ClearDraft = true
                }
            });
        }

        // ---- кружки: тап — увеличить и проиграть, конец — уменьшить ----

        private async void OnVideoNoteTapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            e.Handled = true;
            var host = sender as Grid;
            var item = host?.DataContext as MessageItem;
            if (item == null || item.VideoNoteFileId == 0) return;

            if (_playingHost == host) { StopVideoNote(); return; } // повторный тап — стоп
            StopVideoNote();

            _playingHost = host;
            AnimateScale(host, 1.3);

            var r = await TdService.Current.SendAsync(new TdApi.DownloadFile
            {
                FileId = item.VideoNoteFileId, Priority = 32, Offset = 0, Limit = 0, Synchronous = true
            });
            var file = r as TdApi.File;
            if (!_alive || _playingHost != host) return;
            if (file?.Local == null || !file.Local.IsDownloadingCompleted || string.IsNullOrEmpty(file.Local.Path))
            {
                StopVideoNote();
                return;
            }

            try
            {
                var sf = await Windows.Storage.StorageFile.GetFileFromPathAsync(file.Local.Path);
                var stream = await sf.OpenReadAsync();
                if (!_alive || _playingHost != host) { stream.Dispose(); return; }

                if (_player == null)
                {
                    _player = new MediaElement
                    {
                        AutoPlay = true,
                        AreTransportControlsEnabled = false,
                        Stretch = Windows.UI.Xaml.Media.Stretch.UniformToFill,
                        IsHitTestVisible = false
                    };
                    _player.MediaEnded += (s, a) => StopVideoNote();
                    _player.MediaFailed += (s, a) => StopVideoNote();
                }
                var slot = host.Children[2] as Border; // слот под плеер (см. шаблон)
                if (slot == null) { stream.Dispose(); StopVideoNote(); return; }
                slot.Child = _player;
                _player.SetSource(stream, string.IsNullOrEmpty(sf.ContentType) ? "video/mp4" : sf.ContentType);
            }
            catch
            {
                StopVideoNote();
            }
        }

        private void StopVideoNote()
        {
            if (_player != null)
            {
                _player.Stop();
                _player.Source = null; // отпускаем поток и декодер
                var parent = _player.Parent as Border;
                if (parent != null) parent.Child = null;
            }
            if (_playingHost != null) AnimateScale(_playingHost, 1.0);
            _playingHost = null;
        }

        private static void AnimateScale(Grid host, double to)
        {
            var st = host.RenderTransform as Windows.UI.Xaml.Media.ScaleTransform;
            if (st == null) return;
            var sb = new Windows.UI.Xaml.Media.Animation.Storyboard();
            foreach (var prop in new[] { "ScaleX", "ScaleY" })
            {
                var a = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    To = to,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new Windows.UI.Xaml.Media.Animation.CubicEase()
                };
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(a, st);
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(a, prop);
                sb.Children.Add(a);
            }
            sb.Begin();
        }

        private void OnCommentsClick(object sender, RoutedEventArgs e)
        {
            var m = (sender as FrameworkElement)?.DataContext as MessageItem;
            if (m == null) return;
            Frame.Navigate(typeof(ChatPage), new CommentsArgs
            {
                ChannelChatId = m.ChatId,
                PostId = m.Id,
                ChannelTitle = Chat?.Title
            });
        }

        private void OnHeaderTapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (_chatId != 0) Frame.Navigate(typeof(ChatInfoPage), _chatId);
        }

        private void OnMessageClick(object sender, ItemClickEventArgs e)
        {
            var m = e.ClickedItem as MessageItem;
            if (m != null && m.PhotoFileId != 0) Frame.Navigate(typeof(PhotoPage), m.PhotoFileId);
        }
    }
}
