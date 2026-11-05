using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Pagegram.Models;
using Pagegram.Services;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
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

        // ---- пикер эмодзи и стикеров ----
        public string[] Emojis { get; } = (
            "😀 😃 😄 😁 😆 😅 😂 🤣 😊 😇 🙂 🙃 😉 😌 😍 🥰 😘 😗 😙 😚 😋 😛 😝 😜 🤪 🤨 🧐 🤓 😎 🥳 😏 😒 😞 😔 😟 😕 🙁 😣 😖 " +
            "😫 😩 🥺 😢 😭 😤 😠 😡 🤬 🤯 😳 🥵 🥶 😱 😨 😰 😥 😓 🤗 🤔 🤭 🤫 🤥 😶 😐 😑 😬 🙄 😯 😦 😧 😮 😲 🥱 😴 🤤 😪 😵 " +
            "🤐 🥴 🤢 🤮 🤧 😷 🤒 🤕 🤑 🤠 😈 👿 👹 👺 🤡 💩 👻 💀 👽 🤖 🎃 😺 😸 😹 😻 😼 😽 🙀 😿 😾 " +
            "👍 👎 👌 ✌ 🤞 🤟 🤘 🤙 👈 👉 👆 👇 ☝ ✋ 🤚 🖐 🖖 👋 🤝 🙏 💪 👏 🙌 👐 🤲 ✍ 💅 👀 👁 👄 💋 " +
            "❤ 🧡 💛 💚 💙 💜 🖤 🤍 🤎 💔 ❣ 💕 💞 💓 💗 💖 💘 💝 💯 💢 💥 💫 💦 💨 🔥 ✨ ⭐ 🌟 ⚡ 🎉 🎊 🎁 🏆 🥇 " +
            "🐶 🐱 🐭 🐹 🐰 🦊 🐻 🐼 🐨 🐯 🦁 🐮 🐷 🐸 🐵 🙈 🙉 🙊 🐔 🐧 🐦 🦆 🦉 🐺 🐴 🦄 🐝 🦋 🐌 🐞 🐢 🐍 🐙 🐬 🐳 🦈 " +
            "🍏 🍎 🍐 🍊 🍋 🍌 🍉 🍇 🍓 🍒 🍑 🥭 🍍 🥥 🥝 🍅 🥑 🍆 🥔 🥕 🌽 🌶 🍞 🧀 🍖 🍗 🍔 🍟 🍕 🌭 🌮 🍣 🍩 🍪 🎂 🍰 🍫 🍬 ☕ 🍺 🍷 🥂 " +
            "⚽ 🏀 🏈 ⚾ 🎾 🏐 🎱 🎮 🎲 🎯 🎸 🎧 🎤 🎬 📷 📱 💻 ⌚ 💡 💰 💸 📌 ✅ ❌ ❓ ❗ ⚠ 🚀 ✈ 🚗 🏠 🌍 🌙 ☀ ⛅ 🌧 ❄ 🌈"
        ).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        public ObservableCollection<StickerSetTab> StickerSets { get; } = new ObservableCollection<StickerSetTab>();
        public ObservableCollection<PickerSticker> PickerStickers { get; } = new ObservableCollection<PickerSticker>();
        private bool _setsLoaded;
        private List<TdApi.Sticker> _recentStickers;
        private int _setsRequest;

        /// <summary>Чат для шапки (аватарка, название).</summary>
        public ChatItem Chat { get; private set; }

        private readonly Dictionary<long, MessageItem> _byId = new Dictionary<long, MessageItem>();
        private long _chatId;
        private long _threadId; // != 0 — режим комментариев: ветка в обсуждающей группе
        private int _forumTopicId; // != 0 — режим темы форума
        private bool _forumGeneral; // тема «General» — в ней сообщения без topic_id
        private IList<TdApi.Message> _threadRoots; // сам пост — показываем вверху ленты комментариев
        private bool _rootsShown;
        private bool _channelHasDiscussion; // у канала есть группа обсуждения — кнопка «комментарии» у всех постов
        private bool _showSender;
        private bool _loading;
        private bool _historyEnd;
        private ScrollViewer _scroll;
        // Голосовое: один плеер, таймер прогресса
        private MediaPlayer _voicePlayer;
        private MessageItem _voiceItem;
        private DispatcherTimer _voiceTimer;

        // Играет не больше одного кружка за раз
        private MediaPlayer _notePlayer;
        private MediaPlayerSurface _noteSurface;
        private Border _noteSlot;
        private Grid _playingHost;
        private long _replyToId; // на какое сообщение отвечаем (0 — ни на какое)
        private long _editingId; // какое сообщение редактируем (0 — ни какое)
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
            var topic = e.Parameter as TopicArgs;
            if (topic != null)
            {
                _chatId = topic.ChatId;
                _forumTopicId = topic.TopicId;
                _forumGeneral = topic.IsGeneral;
                Title.Text = (topic.Name ?? string.Empty).ToUpper();
            }
            else if (comments != null)
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
                    var err = (info as TdApi.Error)?.Message ?? "неизвестная ошибка";
                    Subtitle.Text = "не удалось открыть комментарии";
                    Subtitle.Visibility = Visibility.Visible;
                    InputBar.Visibility = Visibility.Collapsed;
                    await new Windows.UI.Popups.MessageDialog("Не удалось открыть комментарии: " + err).ShowAsync();
                    return;
                }
                _chatId = thread.ChatId;
                _threadId = thread.MessageThreadId;
                _threadRoots = thread.Messages;
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
            if (topic != null)
            {
                Subtitle.Text = chat?.Title ?? string.Empty;
                _showSender = true;
            }
            else if (comments != null)
            {
                Subtitle.Text = comments.ChannelTitle ?? string.Empty;
                _showSender = true;
            }
            else
            {
                // Супергруппа с темами — вместо общей ленты показываем список тем
                if (chat != null && chat.IsGroup && chat.SupergroupId != 0)
                {
                    if (chat.IsForum == null)
                    {
                        var sg = await TdService.Current.SendAsync(new TdApi.GetSupergroup { SupergroupId = chat.SupergroupId }) as TdApi.Supergroup;
                        if (!_alive) return;
                        chat.IsForum = sg != null && sg.IsForum;
                    }
                    if (chat.IsForum == true)
                    {
                        Frame.Navigate(typeof(TopicsPage), chat.Id);
                        var back = Frame.BackStack;
                        if (back.Count > 0 && back[back.Count - 1].SourcePageType == typeof(ChatPage)) back.RemoveAt(back.Count - 1);
                        return;
                    }
                }
                Title.Text = (chat?.Title ?? string.Empty).ToUpper();
                Subtitle.Text = chat == null ? string.Empty : chat.IsChannel ? "канал" : chat.IsGroup ? "группа" : string.Empty;
                _showSender = chat != null && chat.IsGroup;
                if (chat != null && chat.IsChannel) SetupChannelBar(chat);
            }
            Subtitle.Visibility = Subtitle.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

            TdService.Current.Update += OnUpdate;
            LowMemory.Trim += OnTrim;
            RichText.LinkClicked += OnLinkClicked;
            TdService.Current.Send(new TdApi.OpenChat { ChatId = _chatId });
            LoadOlder();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _alive = false;
            StopVideoNote();
            StopVoice();
            PickerStickers.Clear();
            TdService.Current.Update -= OnUpdate;
            LowMemory.Trim -= OnTrim;
            RichText.LinkClicked -= OnLinkClicked;
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
                if (_forumTopicId != 0)
                    req = new TdApi.GetForumTopicHistory
                    {
                        ChatId = _chatId,
                        ForumTopicId = _forumTopicId,
                        FromMessageId = fromId,
                        Offset = 0,
                        Limit = Config.HistoryPageSize - batch.Count
                    };
                else if (_threadId != 0)
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

            if (_historyEnd && _threadId != 0 && !_rootsShown && _threadRoots != null)
            {
                _rootsShown = true;
                foreach (var root in _threadRoots) if (root != null) batch.Add(root); // от новых к старым, как и batch
            }

            // TDLib отдаёт от новых к старым — вставляем сверху в обратном порядке
            var ids = new List<long>(batch.Count);
            foreach (var m in batch)
            {
                if (_byId.ContainsKey(m.Id)) continue;
                var item = MessageItem.From(m, _showSender);
                ids.Add(m.Id);
                if (Messages.Count > 0 && Messages[0].CanMergeAlbum(item))
                {
                    Messages[0].MergeAlbum(item, true); // старшее фото альбома — в начало сетки
                    _byId[m.Id] = Messages[0];
                    continue;
                }
                if (_channelHasDiscussion) item.EnableComments();
                _byId[m.Id] = item;
                Messages.Insert(0, item);
                ResolveReply(item);
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
                    if (_forumTopicId != 0 && !InForumTopic(n.Message)) return; // в теме — только её сообщения
                    item = MessageItem.From(n.Message, _showSender);
                    if (Messages.Count > 0 && Messages[Messages.Count - 1].CanMergeAlbum(item))
                    {
                        Messages[Messages.Count - 1].MergeAlbum(item, false); // следующее фото альбома
                        _byId[n.Message.Id] = Messages[Messages.Count - 1];
                        break;
                    }
                    if (_channelHasDiscussion) item.EnableComments();
                    _byId[item.Id] = item;
                    Messages.Add(item);
                    ResolveReply(item);
                    if (!n.Message.IsOutgoing)
                        TdService.Current.Send(new TdApi.ViewMessages { ChatId = _chatId, MessageIds = new List<long> { item.Id }, ForceRead = true });
                    TrimTop(Config.MaxMessagesInMemory);
                    break;

                case TdApi.UpdateMessageSendSucceeded ok:
                    if (ok.Message.ChatId != _chatId || !_byId.TryGetValue(ok.OldMessageId, out item)) return;
                    _byId.Remove(ok.OldMessageId);
                    item.ReplaceAlbumMessageId(ok.OldMessageId, ok.Message.Id);
                    if (item.Id == ok.OldMessageId) item.Id = ok.Message.Id;
                    item.Status = "✓";
                    _byId[ok.Message.Id] = item;
                    break;

                case TdApi.UpdateMessageSendFailed fail:
                    if (fail.Message.ChatId != _chatId || !_byId.TryGetValue(fail.OldMessageId, out item)) return;
                    _byId.Remove(fail.OldMessageId);
                    item.ReplaceAlbumMessageId(fail.OldMessageId, fail.Message.Id);
                    if (item.Id == fail.OldMessageId) item.Id = fail.Message.Id;
                    item.Status = "! " + fail.Error?.Message;
                    _byId[fail.Message.Id] = item;
                    break;

                case TdApi.UpdateMessageContent c:
                    if (c.ChatId == _chatId && _byId.TryGetValue(c.MessageId, out item))
                        item.UpdateContent(c.NewContent);
                    break;

                case TdApi.UpdateMessageInteractionInfo ii:
                    if (ii.ChatId == _chatId && _byId.TryGetValue(ii.MessageId, out item))
                        item.UpdateInteraction(ii.InteractionInfo);
                    break;

                case TdApi.UpdateMessageIsPinned pin:
                    if (pin.ChatId == _chatId && _byId.TryGetValue(pin.MessageId, out item)) item.IsPinned = pin.IsPinned;
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
                            if (item.AlbumId != 0 && item.RemoveAlbumPhoto(id) > 0) continue; // в альбоме ещё есть фото
                            Messages.Remove(item);
                        }
                    }
                    break;
            }
        }

        private bool InForumTopic(TdApi.Message m)
        {
            if (m.TopicId is TdApi.MessageTopicForum f) return f.ForumTopicId == _forumTopicId;
            return _forumGeneral; // без темы — значит в General
        }

        /// <summary>Куда отправлять: ветка комментариев, тема форума или просто чат.</summary>
        private TdApi.MessageTopic CurrentTopic()
        {
            if (_threadId != 0) return new TdApi.MessageTopicThread { MessageThreadId = _threadId };
            if (_forumTopicId != 0 && !_forumGeneral) return new TdApi.MessageTopicForum { ForumTopicId = _forumTopicId };
            return null;
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

            // Есть ли у канала группа обсуждения — тогда комментарии доступны у всех постов
            var full = await TdService.Current.SendAsync(new TdApi.GetSupergroupFullInfo { SupergroupId = chat.SupergroupId }) as TdApi.SupergroupFullInfo;
            if (!_alive || full == null || full.LinkedChatId == 0) return;
            _channelHasDiscussion = true;
            foreach (var m in Messages) m.EnableComments();
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

            if (_editingId != 0)
            {
                // правка своего сообщения — новый текст придёт апдейтом updateMessageContent
                var id = _editingId;
                CancelReply();
                TdService.Current.Send(new TdApi.EditMessageText
                {
                    ChatId = _chatId,
                    MessageId = id,
                    InputMessageContent = new TdApi.InputMessageText
                    {
                        Text = new TdApi.FormattedText { Text = text, Entities = new List<TdApi.TextEntity>() }
                    }
                });
                return;
            }

            // Само сообщение придёт через updateNewMessage (со статусом "отправляется")
            var replyTo = _replyToId != 0 ? new TdApi.InputMessageReplyToMessage { MessageId = _replyToId } : null;
            CancelReply();
            TdService.Current.Send(new TdApi.SendMessage
            {
                ChatId = _chatId,
                ReplyTo = replyTo,
                // в комментариях — в ветку поста
                TopicId = CurrentTopic(),
                InputMessageContent = new TdApi.InputMessageText
                {
                    Text = new TdApi.FormattedText { Text = text, Entities = new List<TdApi.TextEntity>() },
                    ClearDraft = true
                }
            });
        }

        // ---- голосовые: OGG/Opus -> нативный декодер -> WAV в памяти -> MediaPlayer ----

        private async void OnVoiceTapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            e.Handled = true;
            var item = (sender as FrameworkElement)?.DataContext as MessageItem;
            if (item == null || item.VoiceFileId == 0) return;

            // тап по играющему — пауза/продолжить
            if (_voiceItem == item && _voicePlayer != null)
            {
                var session = _voicePlayer.PlaybackSession;
                if (session.PlaybackState == MediaPlaybackState.Playing) { _voicePlayer.Pause(); _voiceTimer?.Stop(); UpdateVoiceProgress(false); }
                else { _voicePlayer.Play(); _voiceTimer?.Start(); UpdateVoiceProgress(true); }
                return;
            }

            StopVoice();
            StopVideoNote();
            _voiceItem = item;
            item.SetVoiceState(true, 0, 0);

            // Каждый шаг отдельно — чтобы при ошибке было видно, где именно сломалось
            string stage = "загрузка";
            try
            {
                var r = await TdService.Current.SendAsync(new TdApi.DownloadFile
                {
                    FileId = item.VoiceFileId, Priority = 32, Offset = 0, Limit = 0, Synchronous = true
                });
                if (!_alive || _voiceItem != item) return;
                var file = r as TdApi.File;
                if (file?.Local == null || !file.Local.IsDownloadingCompleted || string.IsNullOrEmpty(file.Local.Path))
                {
                    FailVoice(item, stage, (r as TdApi.Error)?.Message ?? "файл не скачался");
                    return;
                }

                stage = "чтение файла";
                var sf = await Windows.Storage.StorageFile.GetFileFromPathAsync(file.Local.Path);
                var bytes = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(
                    await Windows.Storage.FileIO.ReadBufferAsync(sf));

                stage = "декодер Opus";
                int rate = PagegramNative.Opus.SampleRate; // заодно проверяем, что нативный модуль грузится
                var pcm = await System.Threading.Tasks.Task.Run(() => PagegramNative.Opus.DecodeOggToPcm(bytes));
                if (!_alive || _voiceItem != item) return;
                if (pcm == null || pcm.Length == 0)
                {
                    FailVoice(item, stage, "не удалось декодировать (" + bytes.Length + " байт)");
                    return;
                }

                stage = "плеер";
                var stream = await WavStream(pcm, rate);
                if (!_alive || _voiceItem != item) { stream.Dispose(); return; }
                var player = new MediaPlayer { AutoPlay = false };
                player.Source = MediaSource.CreateFromStream(stream, "audio/wav");
                player.MediaEnded += (s, a) => OnVoiceFinished(player, null);
                player.MediaFailed += (s, a) => OnVoiceFinished(player, a.ErrorMessage ?? a.Error.ToString());
                _voicePlayer = player;

                if (_voiceTimer == null)
                {
                    _voiceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                    _voiceTimer.Tick += (s, a) => UpdateVoiceProgress(true);
                }
                _voiceTimer.Start();
                player.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("voice: " + ex);
                FailVoice(item, stage, ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void FailVoice(MessageItem item, string stage, string reason)
        {
            if (_voiceItem == item) StopVoice();
            item.SetVoiceError("ошибка (" + stage + "): " + reason);
        }

        private void UpdateVoiceProgress(bool playing)
        {
            if (_voicePlayer == null || _voiceItem == null) return;
            var pos = _voicePlayer.PlaybackSession.Position.TotalSeconds;
            var dur = _voiceItem.VoiceDuration > 0 ? _voiceItem.VoiceDuration : _voicePlayer.PlaybackSession.NaturalDuration.TotalSeconds;
            _voiceItem.SetVoiceState(playing, dur > 0 ? Math.Min(1, pos / dur) : 0, (int)pos);
        }

        private void OnVoiceFinished(MediaPlayer player, string error)
        {
            var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (_voicePlayer != player) return;
                var item = _voiceItem;
                StopVoice();
                if (error != null && item != null) item.SetVoiceError("ошибка (плеер): " + error);
            });
        }

        private void StopVoice()
        {
            _voiceTimer?.Stop();
            if (_voicePlayer != null)
            {
                try { _voicePlayer.Pause(); _voicePlayer.Source = null; } catch { }
                _voicePlayer.Dispose();
                _voicePlayer = null;
            }
            _voiceItem?.SetVoiceState(false, 0, 0);
            _voiceItem = null;
        }

        /// <summary>WAV (PCM 16 бит, моно) в памяти — его MediaPlayer играет без всяких кодеков.</summary>
        private static async System.Threading.Tasks.Task<Windows.Storage.Streams.InMemoryRandomAccessStream> WavStream(short[] pcm, int rate)
        {
            int dataLen = pcm.Length * 2;
            var bytes = new byte[44 + dataLen];
            void Str(int at, string v) { for (int i = 0; i < 4; i++) bytes[at + i] = (byte)v[i]; }
            void I32(int at, int v) { bytes[at] = (byte)v; bytes[at + 1] = (byte)(v >> 8); bytes[at + 2] = (byte)(v >> 16); bytes[at + 3] = (byte)(v >> 24); }
            void I16(int at, int v) { bytes[at] = (byte)v; bytes[at + 1] = (byte)(v >> 8); }
            Str(0, "RIFF"); I32(4, 36 + dataLen); Str(8, "WAVE");
            Str(12, "fmt "); I32(16, 16); I16(20, 1); I16(22, 1); I32(24, rate); I32(28, rate * 2); I16(32, 2); I16(34, 16);
            Str(36, "data"); I32(40, dataLen);
            Buffer.BlockCopy(pcm, 0, bytes, 44, dataLen);

            var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var w = new Windows.Storage.Streams.DataWriter(ms.GetOutputStreamAt(0)))
            {
                w.WriteBytes(bytes);
                await w.StoreAsync();
                w.DetachStream();
            }
            ms.Seek(0);
            return ms;
        }

        // ---- кружки: тап — увеличить и проиграть, конец — уменьшить ----
        // Видео рисуется через Composition: MediaPlayer -> поверхность -> маска-круг (CompositionMaskBrush, 15063+).

        private static Windows.UI.Xaml.Media.LoadedImageSurface _circleMask;

        private async void OnVideoNoteTapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            e.Handled = true;
            var host = sender as Grid;
            var item = host?.DataContext as MessageItem;
            if (item == null || item.VideoNoteFileId == 0) return;

            if (_playingHost == host) { StopVideoNote(); return; } // повторный тап — стоп
            StopVideoNote();
            StopVoice();

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
                if (!_alive || _playingHost != host) return;

                var slot = host.Children[2] as Border; // слот под видео (см. шаблон)
                if (slot == null) { StopVideoNote(); return; }

                var compositor = ElementCompositionPreview.GetElementVisual(slot).Compositor;
                if (_circleMask == null)
                    _circleMask = Windows.UI.Xaml.Media.LoadedImageSurface.StartLoadFromUri(new Uri("ms-appx:///Assets/CircleMask.png"));

                var player = new MediaPlayer { AutoPlay = false, IsLoopingEnabled = false };
                player.Source = MediaSource.CreateFromStorageFile(sf);
                double scale = Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
                int px = (int)Math.Ceiling(200 * 1.3 * scale); // под увеличенный размер, чтобы не мылило
                player.SetSurfaceSize(new Windows.Foundation.Size(px, px));
                var surface = player.GetSurface(compositor);

                var video = compositor.CreateSurfaceBrush(surface.CompositionSurface);
                video.Stretch = CompositionStretch.UniformToFill;
                var mask = compositor.CreateSurfaceBrush(_circleMask);
                mask.Stretch = CompositionStretch.Fill;
                var masked = compositor.CreateMaskBrush();
                masked.Source = video;
                masked.Mask = mask;

                var visual = compositor.CreateSpriteVisual();
                visual.Size = new System.Numerics.Vector2(200, 200);
                visual.Brush = masked;
                ElementCompositionPreview.SetElementChildVisual(slot, visual);

                _notePlayer = player;
                _noteSurface = surface;
                _noteSlot = slot;

                // события MediaPlayer приходят не с UI-потока
                player.MediaEnded += (s, a) => OnNoteFinished(player);
                player.MediaFailed += (s, a) => OnNoteFinished(player);
                player.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("video note: " + ex);
                StopVideoNote();
            }
        }

        private void OnNoteFinished(MediaPlayer player)
        {
            var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (_notePlayer == player) StopVideoNote();
            });
        }

        private void StopVideoNote()
        {
            if (_noteSlot != null) ElementCompositionPreview.SetElementChildVisual(_noteSlot, null);
            if (_notePlayer != null)
            {
                try { _notePlayer.Pause(); _notePlayer.Source = null; } catch { }
                _notePlayer.Dispose(); // отпускаем декодер и файл
            }
            _noteSurface?.Dispose();
            _notePlayer = null;
            _noteSurface = null;
            _noteSlot = null;
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

        // ---- пикер: эмодзи вставляются в текст, стикеры отправляются сразу ----

        private void OnTogglePicker(object sender, RoutedEventArgs e)
        {
            if (Picker.Visibility == Visibility.Visible) { HidePicker(); return; }
            Picker.Visibility = Visibility.Visible;
            Slide(Picker, 300, 0, 0.3, 1, null); // выезжает снизу, как клавиатура
            PickerButton.Focus(FocusState.Programmatic); // убираем фокус с поля — клавиатура спрячется
        }

        /// <summary>Плавный сдвиг по вертикали + прозрачность (0,2 с, замедление в конце — как системные анимации).</summary>
        private static void Slide(FrameworkElement el, double fromY, double toY, double fromOpacity, double toOpacity, Action done)
        {
            var tt = el.RenderTransform as Windows.UI.Xaml.Media.TranslateTransform;
            if (tt == null) { done?.Invoke(); return; }
            var sb = new Windows.UI.Xaml.Media.Animation.Storyboard();
            var move = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = fromY, To = toY, Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new Windows.UI.Xaml.Media.Animation.ExponentialEase
                {
                    Exponent = 5, EasingMode = Windows.UI.Xaml.Media.Animation.EasingMode.EaseOut
                }
            };
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(move, tt);
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(move, "Y");
            var fade = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = fromOpacity, To = toOpacity, Duration = TimeSpan.FromMilliseconds(200)
            };
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, el);
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");
            sb.Children.Add(move);
            sb.Children.Add(fade);
            if (done != null) sb.Completed += (s, a) => done();
            sb.Begin();
        }

        private void OnInputFocused(object sender, RoutedEventArgs e)
        {
            // начали печатать — пикер прячем, место займёт клавиатура
            if (Picker.Visibility == Visibility.Visible) HidePicker();
        }

        private void HidePicker()
        {
            // уезжает вниз, потом прячем и чистим
            Slide(Picker, 0, 300, 1, 0.3, () =>
            {
                Picker.Visibility = Visibility.Collapsed;
                PickerStickers.Clear(); // превью стикеров не держим в памяти, пока пикер закрыт
                SetTabs.SelectedIndex = -1;
            });
        }

        private void OnEmojiTab(object sender, RoutedEventArgs e)
        {
            EmojiGrid.Visibility = Visibility.Visible;
            StickerGrid.Visibility = Visibility.Collapsed;
            SetTabs.Visibility = Visibility.Collapsed;
            PickerHint.Visibility = Visibility.Collapsed;
        }

        private async void OnStickerTab(object sender, RoutedEventArgs e)
        {
            EmojiGrid.Visibility = Visibility.Collapsed;
            StickerGrid.Visibility = Visibility.Visible;
            SetTabs.Visibility = Visibility.Visible;

            if (!_setsLoaded)
            {
                _setsLoaded = true;
                PickerHint.Text = "загрузка…";
                PickerHint.Visibility = Visibility.Visible;

                var recent = await TdService.Current.SendAsync(new TdApi.GetRecentStickers { IsAttached = false }) as TdApi.Stickers;
                var sets = await TdService.Current.SendAsync(new TdApi.GetInstalledStickerSets { StickerType = new TdApi.StickerTypeRegular() }) as TdApi.StickerSets;
                if (!_alive) return;

                StickerSets.Clear();
                if (recent?.StickersValue != null && recent.StickersValue.Count > 0)
                {
                    _recentStickers = new List<TdApi.Sticker>(recent.StickersValue);
                    StickerSets.Add(new StickerSetTab(0, "Недавние"));
                }
                if (sets?.Sets != null)
                    foreach (var si in sets.Sets) StickerSets.Add(new StickerSetTab(si.Id, si.Title));

                if (StickerSets.Count == 0)
                {
                    PickerHint.Text = "Нет установленных наборов стикеров";
                    return;
                }
                PickerHint.Visibility = Visibility.Collapsed;
            }
            if (SetTabs.SelectedIndex < 0 && StickerSets.Count > 0) SetTabs.SelectedIndex = 0;
        }

        private async void OnStickerSetChanged(object sender, SelectionChangedEventArgs e)
        {
            var tab = SetTabs.SelectedItem as StickerSetTab;
            if (tab == null) return;
            int req = ++_setsRequest;
            PickerStickers.Clear();

            IList<TdApi.Sticker> list = null;
            if (tab.Id == 0) list = _recentStickers;
            else
            {
                var set = await TdService.Current.SendAsync(new TdApi.GetStickerSet { SetId = tab.Id }) as TdApi.StickerSet;
                if (!_alive || req != _setsRequest) return;
                list = set?.Stickers;
            }
            if (list == null) return;
            int n = 0;
            foreach (var st in list)
            {
                if (st == null || st.StickerValue == null) continue;
                PickerStickers.Add(new PickerSticker(st));
                if (++n >= 120) break; // огромные наборы не тащим целиком
            }
        }

        private void OnEmojiClick(object sender, ItemClickEventArgs e)
        {
            var emoji = e.ClickedItem as string;
            if (string.IsNullOrEmpty(emoji)) return;
            int pos = Math.Max(0, Math.Min(Input.SelectionStart, Input.Text.Length));
            Input.Text = Input.Text.Insert(pos, emoji);
            Input.SelectionStart = pos + emoji.Length;
        }

        private void OnStickerClick(object sender, ItemClickEventArgs e)
        {
            var st = e.ClickedItem as PickerSticker;
            if (st == null || st.FileId == 0) return;
            var replyTo = _replyToId != 0 ? new TdApi.InputMessageReplyToMessage { MessageId = _replyToId } : null;
            if (_editingId == 0) CancelReply();
            TdService.Current.Send(new TdApi.SendMessage
            {
                ChatId = _chatId,
                TopicId = CurrentTopic(),
                ReplyTo = replyTo,
                InputMessageContent = new TdApi.InputMessageSticker
                {
                    Sticker = new TdApi.InputSticker
                    {
                        Sticker = new TdApi.InputFileId { Id = st.FileId },
                        Width = st.Width,
                        Height = st.Height
                    },
                    Emoji = st.Emoji
                }
            });
        }

        // ---- ответы: плашка-цитата ----

        private async void ResolveReply(MessageItem item)
        {
            if (item.ReplyToMessageId == 0) return;
            MessageItem target;
            if (item.ReplyToMessageId > 0 && _byId.TryGetValue(item.ReplyToMessageId, out target))
            {
                item.SetReply(target.AuthorName, item.HasReplyText ? null : target.ShortText);
                return;
            }
            // исходного нет в загруженном — спросим у TDLib (обычно из локальной базы)
            var r = await TdService.Current.SendAsync(new TdApi.GetRepliedMessage { ChatId = _chatId, MessageId = item.Id }) as TdApi.Message;
            if (!_alive || r == null) return;
            item.SetReply(r.IsOutgoing ? "Вы" : MessageItem.SenderName(r.SenderId), item.HasReplyText ? null : Formatter.Preview(r));
        }

        private void OnReplyQuoteTapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            e.Handled = true;
            var item = (sender as FrameworkElement)?.DataContext as MessageItem;
            MessageItem target;
            if (item != null && _byId.TryGetValue(item.ReplyToMessageId, out target))
                MessageList.ScrollIntoView(target, ScrollIntoViewAlignment.Leading);
        }

        // ---- свайп влево = ответить ----

        private const double ReplyThreshold = 60;

        private void OnBubbleDelta(object sender, Windows.UI.Xaml.Input.ManipulationDeltaRoutedEventArgs e)
        {
            if (InputBar.Visibility != Visibility.Visible) return; // писать нельзя (канал) — и отвечать тоже
            var g = sender as Grid;
            var tt = g?.RenderTransform as Windows.UI.Xaml.Media.TranslateTransform;
            if (tt == null) return;
            double x = Math.Max(-90, Math.Min(0, e.Cumulative.Translation.X)); // только влево, с упором
            tt.X = x;
            var arrow = (g.Parent as Grid)?.Children[0];
            if (arrow != null) arrow.Opacity = Math.Min(1, -x / ReplyThreshold);
            e.Handled = true;
        }

        private void OnBubbleCompleted(object sender, Windows.UI.Xaml.Input.ManipulationCompletedRoutedEventArgs e)
        {
            var g = sender as Grid;
            var tt = g?.RenderTransform as Windows.UI.Xaml.Media.TranslateTransform;
            if (tt == null) return;
            if (tt.X <= -ReplyThreshold)
            {
                var item = g.DataContext as MessageItem;
                if (item != null) StartReply(item);
            }
            // пружиной обратно на место
            var sb = new Windows.UI.Xaml.Media.Animation.Storyboard();
            var back = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = 0, Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new Windows.UI.Xaml.Media.Animation.BackEase { Amplitude = 0.3, EasingMode = Windows.UI.Xaml.Media.Animation.EasingMode.EaseOut }
            };
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(back, tt);
            Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(back, "X");
            sb.Children.Add(back);
            var arrow = (g.Parent as Grid)?.Children[0];
            if (arrow != null)
            {
                var fade = new Windows.UI.Xaml.Media.Animation.DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(150) };
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, arrow);
                Windows.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");
                sb.Children.Add(fade);
            }
            sb.Begin();
        }

        private void StartReply(MessageItem item)
        {
            if (item.Id <= 0) return;
            _replyToId = item.Id;
            ReplyBarSender.Text = "Ответ: " + (item.AuthorName ?? string.Empty);
            ReplyBarText.Text = item.ShortText;
            ShowReplyBar();
            Input.Focus(FocusState.Programmatic);
        }

        private void ShowReplyBar()
        {
            if (ReplyBar.Visibility == Visibility.Visible) return;
            ReplyBar.Visibility = Visibility.Visible;
            Slide(ReplyBar, 40, 0, 0, 1, null);
        }

        private void CancelReply()
        {
            if (_editingId != 0) Input.Text = string.Empty;
            _replyToId = 0;
            _editingId = 0;
            ReplyBar.Visibility = Visibility.Collapsed;
        }

        private void StartEdit(MessageItem item)
        {
            _replyToId = 0;
            _editingId = item.Id;
            ReplyBarSender.Text = "Изменение";
            ReplyBarText.Text = item.ShortText;
            ShowReplyBar();
            Input.Text = item.Text ?? string.Empty;
            Input.SelectionStart = Input.Text.Length;
            Input.Focus(FocusState.Programmatic);
        }

        // ---- реакции ----

        private void OnReactionTapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            e.Handled = true;
            var chip = (sender as FrameworkElement)?.DataContext as ReactionChip;
            if (chip == null || !chip.CanToggle) return;
            // новые счётчики придут апдейтом updateMessageInteractionInfo
            if (chip.IsChosen)
                TdService.Current.Send(new TdApi.RemoveMessageReaction { ChatId = chip.ChatId, MessageId = chip.MessageId, ReactionType = chip.Type });
            else
                TdService.Current.Send(new TdApi.AddMessageReaction
                {
                    ChatId = chip.ChatId, MessageId = chip.MessageId, ReactionType = chip.Type,
                    IsBig = false, UpdateRecentReactions = true
                });
        }

        // ---- меню по долгому нажатию: ответить, копировать, переслать, закрепить, изменить, удалить ----

        private async void OnBubbleRightTapped(object sender, Windows.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            e.Handled = true;
            var anchor = sender as FrameworkElement;
            var item = anchor?.DataContext as MessageItem;
            if (item == null || item.Id <= 0) return;

            // что можно сделать с этим сообщением — говорит сам TDLib
            var props = await TdService.Current.SendAsync(new TdApi.GetMessageProperties { ChatId = _chatId, MessageId = item.Id }) as TdApi.MessageProperties;
            if (!_alive) return;

            // действия — только те, что TDLib разрешает для этого сообщения
            var actions = new List<KeyValuePair<string, Action>>();
            bool canWrite = InputBar.Visibility == Visibility.Visible;
            if (canWrite && (props == null || props.CanBeReplied))
                actions.Add(new KeyValuePair<string, Action>("ответить", () => StartReply(item)));
            if (!string.IsNullOrEmpty(item.Text))
                actions.Add(new KeyValuePair<string, Action>("копировать", () =>
                {
                    var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    dp.SetText(item.Text);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                }));
            if (props != null && props.CanBeForwarded)
                actions.Add(new KeyValuePair<string, Action>("переслать", () => Frame.Navigate(typeof(ForwardPage), new ForwardArgs { FromChatId = _chatId, MessageId = item.Id })));
            if (props != null && props.CanBePinned)
                actions.Add(new KeyValuePair<string, Action>(item.IsPinned ? "открепить" : "закрепить", async () =>
                {
                    TdApi.Function f = item.IsPinned
                        ? (TdApi.Function)new TdApi.UnpinChatMessage { ChatId = _chatId, MessageId = item.Id }
                        : new TdApi.PinChatMessage { ChatId = _chatId, MessageId = item.Id, DisableNotification = false, OnlyForSelf = false };
                    var r = await TdService.Current.SendAsync(f);
                    if (r is TdApi.Error err) await new Windows.UI.Popups.MessageDialog(err.Message).ShowAsync();
                }));
            if (props != null && props.CanBeEdited && canWrite && !item.IsSticker && !item.IsVoice && !item.IsVideoNote)
                actions.Add(new KeyValuePair<string, Action>("изменить", () => StartEdit(item)));
            if (props != null && props.CanBeDeletedForAllUsers)
                actions.Add(new KeyValuePair<string, Action>("удалить у всех", () => Delete(item, true)));
            if (props != null && props.CanBeDeletedOnlyForSelf)
                actions.Add(new KeyValuePair<string, Action>(props.CanBeDeletedForAllUsers ? "удалить только у меня" : "удалить", () => Delete(item, false)));

            // реакции, доступные в этом чате для этого сообщения (только обычные эмодзи без Premium)
            var emojis = new List<string>();
            var avail = await TdService.Current.SendAsync(new TdApi.GetMessageAvailableReactions { ChatId = _chatId, MessageId = item.Id, RowSize = 8 }) as TdApi.AvailableReactions;
            if (!_alive) return;
            if (avail != null)
                foreach (var group in new[] { avail.TopReactions, avail.RecentReactions, avail.PopularReactions })
                    if (group != null)
                        foreach (var ar in group)
                            if (ar != null && !ar.NeedsPremium && ar.Type is TdApi.ReactionTypeEmoji em && !emojis.Contains(em.Emoji))
                                emojis.Add(em.Emoji);

            if (actions.Count == 0 && emojis.Count == 0) return;
            ShowMessageMenu(anchor, item, emojis, actions);
        }

        /// <summary>Меню сообщения: сверху строка реакций (листается вбок), ниже — действия.</summary>
        private void ShowMessageMenu(FrameworkElement anchor, MessageItem item, List<string> emojis, List<KeyValuePair<string, Action>> actions)
        {
            var flyout = new Flyout();
            var root = new StackPanel { MinWidth = 264 };

            if (emojis.Count > 0)
            {
                // Кнопка реакции: тап — поставить (или снять, если уже стоит)
                Func<string, Button> reactionButton = emoji =>
                {
                    var b = new Button
                    {
                        Content = emoji, FontSize = 24, Width = 44, Height = 44, Padding = new Thickness(0),
                        Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Transparent),
                        BorderThickness = new Thickness(0)
                    };
                    b.Click += (s, a) =>
                    {
                        flyout.Hide();
                        bool chosen = false;
                        if (item.Reactions != null)
                            foreach (var c in item.Reactions)
                                if (c.IsChosen && c.Type is TdApi.ReactionTypeEmoji ce && ce.Emoji == emoji) chosen = true;
                        var type = new TdApi.ReactionTypeEmoji { Emoji = emoji };
                        if (chosen)
                            TdService.Current.Send(new TdApi.RemoveMessageReaction { ChatId = _chatId, MessageId = item.Id, ReactionType = type });
                        else
                            TdService.Current.Send(new TdApi.AddMessageReaction { ChatId = _chatId, MessageId = item.Id, ReactionType = type, IsBig = false, UpdateRecentReactions = true });
                    };
                    return b;
                };

                const int quick = 6;
                // Короткий ряд: первые 6 реакций + «▾» раскрыть все
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                for (int i = 0; i < emojis.Count && i < quick; i++) row.Children.Add(reactionButton(emojis[i]));

                // Все реакции — сеткой по 6 в ряд, с прокруткой
                var all = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = quick, ItemWidth = 44, ItemHeight = 44 };
                var allScroll = new ScrollViewer
                {
                    Content = all, MaxHeight = 220, Visibility = Visibility.Collapsed,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled
                };

                if (emojis.Count > quick)
                {
                    var more = new Button
                    {
                        Content = "\uE70D", FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 16,
                        Width = 44, Height = 44, Padding = new Thickness(0), BorderThickness = new Thickness(0),
                        Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Transparent)
                    };
                    more.Click += (s, a) =>
                    {
                        // наполняем сетку только при раскрытии — не держим лишние кнопки зря
                        if (all.Children.Count == 0) foreach (var em in emojis) all.Children.Add(reactionButton(em));
                        row.Visibility = Visibility.Collapsed;
                        allScroll.Visibility = Visibility.Visible;
                    };
                    row.Children.Add(more);
                }

                root.Children.Add(row);
                root.Children.Add(allScroll);
                if (actions.Count > 0)
                    root.Children.Add(new Border { Height = 1, Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 6, 0, 2) });
            }

            foreach (var a in actions)
            {
                var b = new Button
                {
                    Content = a.Key, FontSize = 17,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(12, 10, 12, 10), BorderThickness = new Thickness(0),
                    Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Transparent)
                };
                var act = a.Value;
                b.Click += (s, e) => { flyout.Hide(); act(); };
                root.Children.Add(b);
            }

            flyout.Content = root;
            flyout.ShowAt(anchor);
        }

        private void Delete(MessageItem item, bool forAll)
        {
            // сообщение исчезнет из ленты по апдейту updateDeleteMessages
            TdService.Current.Send(new TdApi.DeleteMessages { ChatId = _chatId, MessageIds = new List<long> { item.Id }, Revoke = forAll });
        }

        private void OnCancelReply(object sender, RoutedEventArgs e) { CancelReply(); }

        // ---- тап по фото (только по самой картинке) ----

        private void OnPhotoTapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as MessageItem;
            if (item != null && item.PhotoFileId != 0)
            {
                e.Handled = true;
                Frame.Navigate(typeof(PhotoPage), new PhotoViewerArgs(item.FullPhotoIds(), 0));
            }
        }

        private void OnAlbumPhotoTapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            var ph = (sender as FrameworkElement)?.DataContext as AlbumPhoto;
            MessageItem item;
            if (ph == null || !_byId.TryGetValue(ph.MessageId, out item)) return;
            e.Handled = true;
            var ids = item.FullPhotoIds();
            int index = Math.Max(0, ids.IndexOf(ph.FullFileId));
            Frame.Navigate(typeof(PhotoPage), new PhotoViewerArgs(ids, index));
        }

        // ---- 📎 отправка фото и файлов ----

        private void OnAttach(object sender, RoutedEventArgs e)
        {
            var menu = new MenuFlyout();
            var photo = new MenuFlyoutItem { Text = "фото" };
            photo.Click += (s, a) => PickAndSendPhotos();
            var file = new MenuFlyoutItem { Text = "файл" };
            file.Click += (s, a) => PickAndSendFile();
            menu.Items.Add(photo);
            menu.Items.Add(file);
            menu.ShowAt(sender as FrameworkElement);
        }

        private async void PickAndSendPhotos()
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail,
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary
            };
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" }) picker.FileTypeFilter.Add(ext);
            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0 || !_alive) return;

            // TDLib читает файлы по пути — копируем выбранное в папку приложения
            var paths = new List<string>();
            foreach (var f in files)
            {
                if (paths.Count >= 10) break; // в альбоме не больше 10
                var path = await CopyForUpload(f);
                if (path != null) paths.Add(path);
            }
            if (paths.Count == 0 || !_alive) return;

            var caption = TakeCaption();
            var contents = new List<TdApi.InputMessageContent>();
            for (int i = 0; i < paths.Count; i++)
            {
                contents.Add(new TdApi.InputMessagePhoto
                {
                    Photo = new TdApi.InputPhoto
                    {
                        Photo = new TdApi.InputFileLocal { Path = paths[i] },
                        AddedStickerFileIds = new List<int>(),
                        Width = 0,
                        Height = 0
                    },
                    Caption = i == 0 ? caption : null // подпись — у первого фото альбома
                });
            }
            SendContents(contents);
        }

        private async void PickAndSendFile()
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                ViewMode = Windows.Storage.Pickers.PickerViewMode.List,
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add("*");
            var file = await picker.PickSingleFileAsync();
            if (file == null || !_alive) return;
            var path = await CopyForUpload(file);
            if (path == null || !_alive) return;
            SendContents(new List<TdApi.InputMessageContent>
            {
                new TdApi.InputMessageDocument
                {
                    Document = new TdApi.InputDocument { Document = new TdApi.InputFileLocal { Path = path } },
                    Caption = TakeCaption()
                }
            });
        }

        /// <summary>Копия файла во временной папке приложения — TDLib читает по пути, а к файлам вне песочницы доступа нет.</summary>
        private static async System.Threading.Tasks.Task<string> CopyForUpload(Windows.Storage.StorageFile f)
        {
            try
            {
                var dir = await Windows.Storage.ApplicationData.Current.TemporaryFolder.CreateFolderAsync("upload", Windows.Storage.CreationCollisionOption.OpenIfExists);
                var copy = await f.CopyAsync(dir, f.Name, Windows.Storage.NameCollisionOption.GenerateUniqueName);
                return copy.Path;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("upload copy: " + ex);
                return null;
            }
        }

        /// <summary>Текст из поля ввода становится подписью к фото/файлу.</summary>
        private TdApi.FormattedText TakeCaption()
        {
            var text = Input.Text.Trim();
            if (_editingId != 0 || text.Length == 0) return null;
            Input.Text = string.Empty;
            return new TdApi.FormattedText { Text = text, Entities = new List<TdApi.TextEntity>() };
        }

        private void SendContents(List<TdApi.InputMessageContent> contents)
        {
            var replyTo = _replyToId != 0 ? new TdApi.InputMessageReplyToMessage { MessageId = _replyToId } : null;
            if (_editingId == 0) CancelReply();
            if (contents.Count == 1)
                TdService.Current.Send(new TdApi.SendMessage
                {
                    ChatId = _chatId, TopicId = CurrentTopic(), ReplyTo = replyTo, InputMessageContent = contents[0]
                });
            else
                TdService.Current.Send(new TdApi.SendMessageAlbum
                {
                    ChatId = _chatId, TopicId = CurrentTopic(), ReplyTo = replyTo, InputMessageContents = contents
                });
        }

        // ---- ссылки в тексте: @username, t.me, url, почта ----

        private async void OnLinkClicked(LinkSpan link)
        {
            if (!_alive) return;
            try
            {
                switch (link.Kind)
                {
                    case LinkKind.Mention:
                        await OpenUsername((link.Arg ?? string.Empty).TrimStart('@'));
                        break;
                    case LinkKind.MentionName:
                        var chat = await TdService.Current.SendAsync(new TdApi.CreatePrivateChat { UserId = link.UserId, Force = false }) as TdApi.Chat;
                        if (chat != null) await OpenChat(chat.Id);
                        break;
                    case LinkKind.Url:
                    case LinkKind.TextUrl:
                        var url = link.Arg ?? string.Empty;
                        if (!url.Contains("://")) url = "http://" + url;
                        Uri uri;
                        if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return;
                        var host = uri.Host.ToLowerInvariant();
                        if (host == "t.me" || host == "telegram.me" || host == "www.t.me" || host == "telegram.dog")
                        {
                            // t.me/username — открываем внутри клиента; инвайты и прочее — в браузере
                            var seg = uri.AbsolutePath.Trim('/').Split('/')[0];
                            if (seg.Length > 0 && seg != "joinchat" && seg != "c" && seg != "s" && !seg.StartsWith("+"))
                            {
                                await OpenUsername(seg);
                                return;
                            }
                        }
                        await Windows.System.Launcher.LaunchUriAsync(uri);
                        break;
                    case LinkKind.Email:
                        await Windows.System.Launcher.LaunchUriAsync(new Uri("mailto:" + link.Arg));
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("link: " + ex);
            }
        }

        private async System.Threading.Tasks.Task OpenUsername(string username)
        {
            if (username.Length == 0) return;
            var r = await TdService.Current.SendAsync(new TdApi.SearchPublicChat { Username = username });
            if (r is TdApi.Chat chat) await OpenChat(chat.Id);
            else await new Windows.UI.Popups.MessageDialog("Не найдено: @" + username).ShowAsync();
        }

        private async System.Threading.Tasks.Task OpenChat(long chatId)
        {
            await ChatStore.Current.EnsureAsync(chatId);
            if (_alive) Frame.Navigate(typeof(ChatPage), chatId);
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

        private async void OnAvatarTapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            e.Handled = true;
            var item = (sender as FrameworkElement)?.DataContext as MessageItem;
            if (item == null) return;
            long chatId = item.SenderChatId;
            if (chatId == 0 && item.SenderUserId != 0)
            {
                // профиль человека = информация о личном чате с ним (создаём, если ещё нет)
                var chat = await TdService.Current.SendAsync(new TdApi.CreatePrivateChat { UserId = item.SenderUserId, Force = false }) as TdApi.Chat;
                if (chat == null || !_alive) return;
                chatId = chat.Id;
            }
            if (chatId == 0) return;
            await ChatStore.Current.EnsureAsync(chatId);
            if (_alive) Frame.Navigate(typeof(ChatInfoPage), chatId);
        }

        private void OnHeaderTapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (_chatId != 0) Frame.Navigate(typeof(ChatInfoPage), _chatId);
        }

    }
}
