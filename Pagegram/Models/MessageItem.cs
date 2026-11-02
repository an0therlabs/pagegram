using System;
using System.ComponentModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Pagegram.Services;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Models
{
    /// <summary>
    /// Сообщение для UI. Исходный объект TDLib НЕ храним — только нужные строки,
    /// чтобы GC мог выкинуть весь граф (реакции, entities, форварды и т.п.).
    /// </summary>
    public sealed class MessageItem : INotifyPropertyChanged
    {
        public long Id { get; set; }
        public bool IsOutgoing { get; private set; }
        public string Sender { get; private set; }
        public string Time { get; private set; }

        /// <summary>file_id фото для просмотра, 0 если не фото.</summary>
        public int PhotoFileId { get; private set; }

        public HorizontalAlignment Alignment => IsOutgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        // Стикер рисуется без пузыря и без хвостика, как в официальных клиентах
        public Brush Background => IsSticker || IsVideoNote ? Palette.Transparent : IsOutgoing ? Palette.Outgoing : Palette.Incoming;
        // Хвостики снизу, вровень с краем пузыря: у входящих слева (рядом с аватаркой), у исходящих справа
        public Visibility InTailVisibility => IsOutgoing || IsSticker || IsVideoNote ? Visibility.Collapsed : Visibility.Visible;
        public Visibility OutTailVisibility => IsOutgoing && !IsSticker && !IsVideoNote ? Visibility.Visible : Visibility.Collapsed;
        public Visibility TextVisibility => string.IsNullOrEmpty(_text) ? Visibility.Collapsed : Visibility.Visible;
        public Brush FooterForeground => IsSticker || IsVideoNote ? Palette.Secondary : Palette.Footer;

        // ---- кружок (видеосообщение) ----
        public bool IsVideoNote { get; private set; }
        public int VideoNoteFileId { get; private set; }
        public string VideoNoteDuration { get; private set; }
        public Visibility NoteVisibility => IsVideoNote ? Visibility.Visible : Visibility.Collapsed;
        // растёт от угла со стороны отправителя: входящий — вправо-вверх, исходящий — влево-вверх
        public Windows.Foundation.Point NoteOrigin => IsOutgoing ? new Windows.Foundation.Point(1, 1) : new Windows.Foundation.Point(0, 1);
        // Имя отправителя в группе — цветом этого человека (тот же цвет, что у его кружка)
        public Brush SenderBrush => Palette.Avatar(_senderKey);

        // ---- медиа в пузыре: фото или стикер ----
        public bool IsSticker { get; private set; }

        // ---- комментарии к посту канала (есть, если у канала подключено обсуждение) ----
        public long ChatId { get; private set; }
        private bool _hasComments;
        private int _commentCount;

        public Visibility CommentsVisibility => _hasComments ? Visibility.Visible : Visibility.Collapsed;
        public string CommentsText => _commentCount == 0 ? "Оставить комментарий" : Formatter.Plural(_commentCount, "комментарий", "комментария", "комментариев");

        public bool IsChannelPost { get; private set; }

        /// <summary>У канала есть группа обсуждения — комментировать можно любой пост, даже если TDLib не прислал reply_info.</summary>
        public void EnableComments()
        {
            if (!IsChannelPost || _hasComments) return;
            _hasComments = true;
            Raise(nameof(CommentsVisibility));
            Raise(nameof(CommentsText));
        }

        public void SetReplyInfo(TdApi.MessageInteractionInfo info)
        {
            var ri = info?.ReplyInfo;
            bool has = IsChannelPost && (ri != null || _hasComments);
            int count = ri?.ReplyCount ?? 0;
            if (has == _hasComments && count == _commentCount) return;
            _hasComments = has;
            _commentCount = count;
            Raise(nameof(CommentsVisibility));
            Raise(nameof(CommentsText));
        }
        private ImageKind _mediaKind;
        private int _mediaFileId;
        // Слабая ссылка: пока строка на экране, картинку держит Image; ушла за экран — GC может забрать,
        // при возврате возьмём из кэша или заново с диска (без повторного скачивания).
        private WeakReference<ImageSource> _media;
        private bool _mediaLoading;

        public double MediaWidth { get; private set; }
        public double MediaHeight { get; private set; }
        public Visibility MediaVisibility => _mediaFileId != 0 && !IsVideoNote ? Visibility.Visible : Visibility.Collapsed;

        public ImageSource Media
        {
            get
            {
                if (_mediaFileId <= 0) return null;
                ImageSource img;
                if (_media != null && _media.TryGetTarget(out img)) return img;
                if (_mediaLoading) return null;
                _mediaLoading = true;
                img = Images.Get(_mediaKind, _mediaFileId, loaded =>
                {
                    _mediaLoading = false;
                    _media = new WeakReference<ImageSource>(loaded);
                    Raise(nameof(Media));
                });
                if (img != null)
                {
                    _mediaLoading = false;
                    _media = new WeakReference<ImageSource>(img);
                }
                return img;
            }
        }

        // ---- аватарка отправителя (только у входящих) ----
        private long _senderKey;          // id пользователя/чата — для цвета кружка
        private string _senderInitial = "?";
        private int _avatarFileId;
        private ImageSource _avatar;
        private bool _avatarRequested;

        private bool _hideAvatar;
        public Visibility AvatarVisibility => IsOutgoing || _hideAvatar ? Visibility.Collapsed : Visibility.Visible;
        public Brush AvatarBrush => Palette.Avatar(_senderKey);
        public string AvatarInitial => _senderInitial;

        public ImageSource Avatar
        {
            get
            {
                if (_avatar == null && !_avatarRequested && _avatarFileId != 0)
                {
                    _avatarRequested = true;
                    _avatar = Images.Get(ImageKind.Avatar, _avatarFileId, img => { _avatar = img; Raise(nameof(Avatar)); });
                }
                return _avatar;
            }
        }
        public Visibility SenderVisibility => string.IsNullOrEmpty(Sender) ? Visibility.Collapsed : Visibility.Visible;

        private string _text;
        public string Text
        {
            get { return _text; }
            set
            {
                if (_text == value) return;
                _text = value;
                Raise(nameof(Text));
                Raise(nameof(TextVisibility));
            }
        }

        /// <summary>Сообщение отредактировали: у фото меняется только подпись.</summary>
        public void UpdateContent(TdApi.MessageContent c)
        {
            if (c is TdApi.MessagePhoto p) Text = p.Caption?.Text ?? string.Empty;
            else if (c is TdApi.MessageSticker) { }
            else Text = Formatter.Content(c);
        }

        private void SetMedia(TdApi.MessageContent c)
        {
            if (c is TdApi.MessagePhoto p)
            {
                var size = PickInlinePhoto(p.Photo);
                if (size?.Photo == null || size.Width <= 0 || size.Height <= 0) return;
                _mediaKind = ImageKind.Photo;
                _mediaFileId = size.Photo.Id;
                MediaWidth = Images.PhotoWidth;
                MediaHeight = Math.Max(80, Math.Min(340, Images.PhotoWidth * (double)size.Height / size.Width));
                _text = p.Caption?.Text ?? string.Empty;
            }
            else if (c is TdApi.MessageVideoNote vn && vn.VideoNote?.Video != null)
            {
                var note = vn.VideoNote;
                IsVideoNote = true;
                VideoNoteFileId = note.Video.Id;
                VideoNoteDuration = (note.Duration / 60) + ":" + (note.Duration % 60).ToString("00");
                _mediaKind = ImageKind.Photo; // превью кружка — обычный jpeg
                _mediaFileId = note.Thumbnail?.File?.Id ?? 0;
                _text = string.Empty;
                if (_mediaFileId == 0) _mediaFileId = -1; // превью нет — покажем пустой кружок, видео всё равно играет
            }
            else if (c is TdApi.MessageSticker ms && ms.Sticker != null)
            {
                var st = ms.Sticker;
                int fileId = 0;
                // Статичный WebP — сам стикер; анимированные (TGS/WebM) — их превью, если оно картинкой
                if (st.Format is TdApi.StickerFormatWebp) fileId = st.StickerValue?.Id ?? 0;
                if (fileId == 0 && st.Thumbnail?.File != null &&
                    (st.Thumbnail.Format is TdApi.ThumbnailFormatWebp || st.Thumbnail.Format is TdApi.ThumbnailFormatPng ||
                     st.Thumbnail.Format is TdApi.ThumbnailFormatJpeg))
                    fileId = st.Thumbnail.File.Id;
                if (fileId == 0) return; // нечем показать — останется текст «😀 Стикер»

                IsSticker = true;
                _mediaKind = ImageKind.Sticker;
                _mediaFileId = fileId;
                double w = st.Width > 0 ? st.Width : 512, h = st.Height > 0 ? st.Height : 512;
                double k = Images.StickerSize / Math.Max(w, h);
                MediaWidth = Math.Round(w * k);
                MediaHeight = Math.Round(h * k);
                _text = string.Empty;
            }
        }

        /// <summary>Для ленты: самый маленький размер не меньше 320px (это ~20-40 КБ), иначе самый большой.</summary>
        private static TdApi.PhotoSize PickInlinePhoto(TdApi.Photo photo)
        {
            if (photo?.Sizes == null) return null;
            TdApi.PhotoSize best = null, largest = null;
            foreach (var s in photo.Sizes)
            {
                int side = Math.Max(s.Width, s.Height);
                if (largest == null || side > Math.Max(largest.Width, largest.Height)) largest = s;
                if (side >= 320 && (best == null || side < Math.Max(best.Width, best.Height))) best = s;
            }
            return best ?? largest;
        }

        private string _status = string.Empty;
        /// <summary>⏱ — отправляется, ✓ — отправлено, ! — ошибка.</summary>
        public string Status
        {
            get { return _status; }
            set { if (_status != value) { _status = value; Raise(nameof(Status)); Raise(nameof(Footer)); } }
        }

        public string Footer => string.IsNullOrEmpty(_status) ? Time : Time + " " + _status;

        public static MessageItem From(TdApi.Message m, bool showSender)
        {
            var item = new MessageItem
            {
                Id = m.Id,
                IsOutgoing = m.IsOutgoing,
                Time = Formatter.Time(m.Date),
                _text = Formatter.Content(m.Content)
            };
            item.SetMedia(m.Content);
            item.ChatId = m.ChatId;
            item.IsChannelPost = m.IsChannelPost;
            item._hasComments = m.IsChannelPost && m.InteractionInfo?.ReplyInfo != null;
            item._commentCount = m.InteractionInfo?.ReplyInfo?.ReplyCount ?? 0;

            if (m.IsOutgoing)
            {
                if (m.SendingState is TdApi.MessageSendingStatePending) item._status = "⏱";
                else if (m.SendingState is TdApi.MessageSendingStateFailed) item._status = "!";
            }
            else
            {
                var name = SenderName(m.SenderId);
                var fromChatItself = m.SenderId is TdApi.MessageSenderChat own && own.ChatId == m.ChatId;
                if (fromChatItself)
                {
                    // Пост от имени канала (или анонимного админа): аватарку канала не показываем,
                    // а если включена подпись авторов — пишем имя автора над постом
                    item._hideAvatar = true;
                    if (!string.IsNullOrEmpty(m.AuthorSignature)) item.Sender = m.AuthorSignature;
                }
                else if (showSender || m.IsChannelPost)
                {
                    // группа, или канал с включённым «показывать профили авторов»
                    item.Sender = name;
                }
                item._senderInitial = InitialOf(name);
                if (m.SenderId is TdApi.MessageSenderUser su)
                {
                    item._senderKey = su.UserId;
                    int fid;
                    if (TdService.Current.UserPhotos.TryGetValue(su.UserId, out fid)) item._avatarFileId = fid;
                }
                else if (m.SenderId is TdApi.MessageSenderChat sc)
                {
                    item._senderKey = sc.ChatId;
                    item._avatarFileId = ChatStore.Current.Get(sc.ChatId)?.PhotoFileId ?? 0;
                }
            }

            if (m.Content is TdApi.MessagePhoto p)
            {
                var size = Formatter.PickPhotoSize(p.Photo);
                if (size?.Photo != null) item.PhotoFileId = size.Photo.Id;
            }

            return item;
        }

        private static string InitialOf(string name)
        {
            if (!string.IsNullOrEmpty(name))
                foreach (var ch in name)
                    if (char.IsLetterOrDigit(ch)) return char.ToUpperInvariant(ch).ToString();
            return "?";
        }

        public static string SenderName(TdApi.MessageSender s)
        {
            if (s is TdApi.MessageSenderUser u)
            {
                string name;
                return TdService.Current.UserNames.TryGetValue(u.UserId, out name) ? name : string.Empty;
            }
            if (s is TdApi.MessageSenderChat c)
            {
                return ChatStore.Current.Get(c.ChatId)?.Title ?? string.Empty;
            }
            return string.Empty;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string name) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
    }
}
