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

        /// <summary>Чат для шапки (аватарка, название).</summary>
        public ChatItem Chat { get; private set; }

        private readonly Dictionary<long, MessageItem> _byId = new Dictionary<long, MessageItem>();
        private long _chatId;
        private long _threadId; // != 0 — режим комментариев: ветка в обсуждающей группе
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
            StopVoice();
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
                if (_channelHasDiscussion) item.EnableComments();
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
                    if (_channelHasDiscussion) item.EnableComments();
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

            var r = await TdService.Current.SendAsync(new TdApi.DownloadFile
            {
                FileId = item.VoiceFileId, Priority = 32, Offset = 0, Limit = 0, Synchronous = true
            });
            var file = r as TdApi.File;
            if (!_alive || _voiceItem != item) return;
            if (file?.Local == null || !file.Local.IsDownloadingCompleted || string.IsNullOrEmpty(file.Local.Path)) { StopVoice(); return; }

            try
            {
                var sf = await Windows.Storage.StorageFile.GetFileFromPathAsync(file.Local.Path);
                var bytes = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(
                    await Windows.Storage.FileIO.ReadBufferAsync(sf));
                // декодирование — не на UI-потоке
                var pcm = await System.Threading.Tasks.Task.Run(() => PagegramNative.Opus.DecodeOggToPcm(bytes));
                if (!_alive || _voiceItem != item) return;
                if (pcm == null || pcm.Length == 0) { StopVoice(); return; }

                var stream = await WavStream(pcm, PagegramNative.Opus.SampleRate);
                if (!_alive || _voiceItem != item) { stream.Dispose(); return; }
                var player = new MediaPlayer { AutoPlay = false };
                player.Source = MediaSource.CreateFromStream(stream, "audio/wav");
                player.MediaEnded += (s, a) => OnVoiceFinished(player);
                player.MediaFailed += (s, a) => OnVoiceFinished(player);
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
                StopVoice();
            }
        }

        private void UpdateVoiceProgress(bool playing)
        {
            if (_voicePlayer == null || _voiceItem == null) return;
            var pos = _voicePlayer.PlaybackSession.Position.TotalSeconds;
            var dur = _voiceItem.VoiceDuration > 0 ? _voiceItem.VoiceDuration : _voicePlayer.PlaybackSession.NaturalDuration.TotalSeconds;
            _voiceItem.SetVoiceState(playing, dur > 0 ? Math.Min(1, pos / dur) : 0, (int)pos);
        }

        private void OnVoiceFinished(MediaPlayer player)
        {
            var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (_voicePlayer == player) StopVoice();
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
