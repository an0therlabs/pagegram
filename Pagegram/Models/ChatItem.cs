using System.ComponentModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Pagegram.Models
{
    /// <summary>
    /// Минимальная модель чата для списка. Вместо фото — цветной кружок с буквой:
    /// аватарки сотни чатов съели бы десятки мегабайт.
    /// </summary>
    public sealed class ChatItem : INotifyPropertyChanged
    {
        public ChatItem(long id) { Id = id; }

        public long Id { get; }
        public bool IsGroup { get; set; }      // показывать отправителя в сообщениях
        public bool IsChannel { get; set; }
        public long Order { get; set; }
        public bool InMain { get; set; }

        private string _title = string.Empty;
        public string Title
        {
            get { return _title; }
            set
            {
                if (_title == value) return;
                _title = value ?? string.Empty;
                Raise(nameof(Title));
                Raise(nameof(Initial));
            }
        }

        public string Initial
        {
            get
            {
                foreach (var ch in _title)
                    if (char.IsLetterOrDigit(ch)) return char.ToUpperInvariant(ch).ToString();
                return "?";
            }
        }

        public Brush AvatarBrush => Palette.Avatar(Id);

        private string _preview = string.Empty;
        public string Preview
        {
            get { return _preview; }
            set { if (_preview != value) { _preview = value ?? string.Empty; Raise(nameof(Preview)); } }
        }

        private string _time = string.Empty;
        public string Time
        {
            get { return _time; }
            set { if (_time != value) { _time = value ?? string.Empty; Raise(nameof(Time)); } }
        }

        private int _unread;
        public int Unread
        {
            get { return _unread; }
            set
            {
                if (_unread == value) return;
                _unread = value;
                Raise(nameof(UnreadText));
                Raise(nameof(UnreadVisibility));
            }
        }

        public string UnreadText => _unread > 999 ? "999+" : _unread.ToString();
        public Visibility UnreadVisibility => _unread > 0 ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string name) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
    }
}
