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
        public Brush Background => IsOutgoing ? Palette.Outgoing : Palette.Incoming;
        // Хвостики снизу, вровень с краем пузыря: у входящих слева (рядом с аватаркой), у исходящих справа
        public Visibility InTailVisibility => IsOutgoing ? Visibility.Collapsed : Visibility.Visible;
        public Visibility OutTailVisibility => IsOutgoing ? Visibility.Visible : Visibility.Collapsed;

        // ---- аватарка отправителя (только у входящих) ----
        private long _senderKey;          // id пользователя/чата — для цвета кружка
        private string _senderInitial = "?";
        private int _avatarFileId;
        private ImageSource _avatar;
        private bool _avatarRequested;

        public Visibility AvatarVisibility => IsOutgoing ? Visibility.Collapsed : Visibility.Visible;
        public Brush AvatarBrush => Palette.Avatar(_senderKey);
        public string AvatarInitial => _senderInitial;

        public ImageSource Avatar
        {
            get
            {
                if (_avatar == null && !_avatarRequested && _avatarFileId != 0)
                {
                    _avatarRequested = true;
                    _avatar = Avatars.Get(_avatarFileId, img => { _avatar = img; Raise(nameof(Avatar)); });
                }
                return _avatar;
            }
        }
        public Visibility SenderVisibility => string.IsNullOrEmpty(Sender) ? Visibility.Collapsed : Visibility.Visible;

        private string _text;
        public string Text
        {
            get { return _text; }
            set { if (_text != value) { _text = value; Raise(nameof(Text)); } }
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

            if (m.IsOutgoing)
            {
                if (m.SendingState is TdApi.MessageSendingStatePending) item._status = "⏱";
                else if (m.SendingState is TdApi.MessageSendingStateFailed) item._status = "!";
            }
            else
            {
                var name = SenderName(m.SenderId);
                if (showSender) item.Sender = name;
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
