using System;
using System.Collections.ObjectModel;
using Pagegram.Models;
using Pagegram.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Views
{
    /// <summary>Информация о чате/канале/человеке + сетка фото из чата (подгружается порциями).</summary>
    public sealed partial class ChatInfoPage : Page
    {
        public ObservableCollection<MediaThumb> Photos { get; } = new ObservableCollection<MediaThumb>();
        public ChatItem Chat { get; private set; }

        private long _chatId;
        private long _nextFromId;
        private bool _loading;
        private bool _end;
        private bool _alive;
        private ScrollViewer _scroll;

        public ChatInfoPage()
        {
            InitializeComponent();
            PhotoGrid.Loaded += (s, e) =>
            {
                if (_scroll != null) return;
                _scroll = ChatsPage.FindScrollViewer(PhotoGrid);
                if (_scroll != null) _scroll.ViewChanged += OnViewChanged;
            };
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            _alive = true;
            _chatId = (long)e.Parameter;
            var chat = await ChatStore.Current.EnsureAsync(_chatId);
            if (!_alive) return;
            Chat = chat;
            Bindings.Update();
            TitleText.Text = chat?.Title ?? string.Empty;

            LoadPhotos();
            if (chat == null) return;
            if (chat.UserId != 0) await FillUser(chat.UserId);
            else if (chat.SupergroupId != 0) await FillSupergroup(chat);
            else if (chat.BasicGroupId != 0) await FillBasicGroup(chat.BasicGroupId);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _alive = false;
            if (_scroll != null) _scroll.ViewChanged -= OnViewChanged;
            PhotoGrid.ItemsSource = null;
            Photos.Clear();
        }

        // ---- информация ----

        private async System.Threading.Tasks.Task FillUser(long userId)
        {
            var u = await TdService.Current.SendAsync(new TdApi.GetUser { UserId = userId }) as TdApi.User;
            if (!_alive || u == null) return;
            SubtitleText.Text = Formatter.UserStatus(u.Status);
            SetRow(UsernameRow, UsernameText, FirstUsername(u.Usernames));
            SetRow(PhoneRow, PhoneText, string.IsNullOrEmpty(u.PhoneNumber) ? null : "+" + u.PhoneNumber);

            var full = await TdService.Current.SendAsync(new TdApi.GetUserFullInfo { UserId = userId }) as TdApi.UserFullInfo;
            if (!_alive || full == null) return;
            AboutLabel.Text = "О себе";
            SetRow(AboutRow, AboutText, full.Bio?.Text);
        }

        private async System.Threading.Tasks.Task FillSupergroup(ChatItem chat)
        {
            var sg = await TdService.Current.SendAsync(new TdApi.GetSupergroup { SupergroupId = chat.SupergroupId }) as TdApi.Supergroup;
            if (!_alive || sg == null) return;
            SetMembers(sg.MemberCount, chat.IsChannel);
            SetRow(UsernameRow, UsernameText, FirstUsername(sg.Usernames));

            var full = await TdService.Current.SendAsync(new TdApi.GetSupergroupFullInfo { SupergroupId = chat.SupergroupId }) as TdApi.SupergroupFullInfo;
            if (!_alive || full == null) return;
            if (full.MemberCount > 0) SetMembers(full.MemberCount, chat.IsChannel);
            SetRow(AboutRow, AboutText, full.Description);
        }

        private async System.Threading.Tasks.Task FillBasicGroup(long basicGroupId)
        {
            var bg = await TdService.Current.SendAsync(new TdApi.GetBasicGroup { BasicGroupId = basicGroupId }) as TdApi.BasicGroup;
            if (!_alive || bg == null) return;
            SetMembers(bg.MemberCount, false);

            var full = await TdService.Current.SendAsync(new TdApi.GetBasicGroupFullInfo { BasicGroupId = basicGroupId }) as TdApi.BasicGroupFullInfo;
            if (!_alive || full == null) return;
            SetRow(AboutRow, AboutText, full.Description);
        }

        private void SetMembers(int count, bool channel)
        {
            if (count <= 0) return;
            SubtitleText.Text = channel
                ? Formatter.Plural(count, "подписчик", "подписчика", "подписчиков")
                : Formatter.Plural(count, "участник", "участника", "участников");
        }

        private static string FirstUsername(TdApi.Usernames u)
        {
            var a = u?.ActiveUsernames;
            return a != null && a.Count > 0 ? "@" + a[0] : null;
        }

        private static void SetRow(FrameworkElement row, TextBlock text, string value)
        {
            text.Text = value ?? string.Empty;
            row.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
        }

        // ---- фото из чата ----

        private async void LoadPhotos()
        {
            if (_loading || _end) return;
            _loading = true;
            var r = await TdService.Current.SendAsync(new TdApi.SearchChatMessages
            {
                ChatId = _chatId,
                Query = string.Empty,
                FromMessageId = _nextFromId,
                Offset = 0,
                Limit = 30,
                Filter = new TdApi.SearchMessagesFilterPhoto()
            });
            if (!_alive) { _loading = false; return; }

            var found = r as TdApi.FoundChatMessages;
            var msgs = found?.Messages;
            if (found == null || msgs == null || msgs.Count == 0 || found.NextFromMessageId == 0) _end = true;
            if (found != null) _nextFromId = found.NextFromMessageId;

            if (msgs != null)
            {
                foreach (var m in msgs)
                {
                    var p = m?.Content as TdApi.MessagePhoto;
                    if (p?.Photo == null) continue;
                    var thumb = PickThumb(p.Photo);
                    var full = Formatter.PickPhotoSize(p.Photo);
                    if (thumb?.Photo == null) continue;
                    Photos.Add(new MediaThumb(m.Id, thumb.Photo.Id, full?.Photo?.Id ?? thumb.Photo.Id));
                }
            }

            PhotosHint.Text = r is TdApi.Error err ? err.Message : "фото пока нет";
            PhotosHint.Visibility = Photos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _loading = false;
        }

        /// <summary>Для плитки: самый маленький размер не меньше 150px, иначе самый большой.</summary>
        private static TdApi.PhotoSize PickThumb(TdApi.Photo photo)
        {
            TdApi.PhotoSize best = null, largest = null;
            foreach (var s in photo.Sizes)
            {
                int side = Math.Max(s.Width, s.Height);
                if (largest == null || side > Math.Max(largest.Width, largest.Height)) largest = s;
                if (side >= 150 && (best == null || side < Math.Max(best.Width, best.Height))) best = s;
            }
            return best ?? largest;
        }

        private void OnViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_scroll.VerticalOffset > _scroll.ScrollableHeight - _scroll.ViewportHeight) LoadPhotos();
        }

        private void OnGridSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 3 квадратные плитки в ряд
            var wrap = PhotoGrid.ItemsPanelRoot as ItemsWrapGrid;
            if (wrap == null) return;
            double side = Math.Floor((e.NewSize.Width - PhotoGrid.Padding.Left - PhotoGrid.Padding.Right) / 3);
            if (side > 0) { wrap.ItemWidth = side; wrap.ItemHeight = side; }
        }

        private void OnAvatarTapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (Chat != null && Chat.PhotoFileId != 0) Frame.Navigate(typeof(AvatarViewerPage), _chatId);
        }

        private void OnPhotoClick(object sender, ItemClickEventArgs e)
        {
            var t = e.ClickedItem as MediaThumb;
            if (t != null) Frame.Navigate(typeof(PhotoPage), t.FullFileId);
        }
    }
}
