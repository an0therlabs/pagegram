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
    /// <summary>
    /// Все аватарки чата/человека, листаются свайпом.
    /// Человек — getUserProfilePhotos; группа/канал — текущее фото + история смены фото.
    /// </summary>
    public sealed partial class AvatarViewerPage : Page
    {
        public ObservableCollection<AvatarSlide> Slides { get; } = new ObservableCollection<AvatarSlide>();

        private bool _alive;

        public AvatarViewerPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            _alive = true;
            var chat = await ChatStore.Current.EnsureAsync((long)e.Parameter);
            if (!_alive || chat == null) { Hint.Text = "нет фото"; return; }
            TitleText.Text = (chat.Title ?? string.Empty).ToUpper();

            var photos = new List<TdApi.ChatPhoto>();
            if (chat.UserId != 0)
            {
                var r = await TdService.Current.SendAsync(new TdApi.GetUserProfilePhotos { UserId = chat.UserId, Offset = 0, Limit = 50 });
                var list = (r as TdApi.ChatPhotos)?.Photos;
                if (list != null) photos.AddRange(list);
            }
            else
            {
                // текущее фото группы/канала
                TdApi.ChatPhoto current = null;
                if (chat.SupergroupId != 0)
                    current = (await TdService.Current.SendAsync(new TdApi.GetSupergroupFullInfo { SupergroupId = chat.SupergroupId }) as TdApi.SupergroupFullInfo)?.Photo;
                else if (chat.BasicGroupId != 0)
                    current = (await TdService.Current.SendAsync(new TdApi.GetBasicGroupFullInfo { BasicGroupId = chat.BasicGroupId }) as TdApi.BasicGroupFullInfo)?.Photo;
                if (current != null) photos.Add(current);

                // прошлые — из сервисных сообщений «фото изменено»
                var found = await TdService.Current.SendAsync(new TdApi.SearchChatMessages
                {
                    ChatId = chat.Id,
                    Query = string.Empty,
                    FromMessageId = 0,
                    Offset = 0,
                    Limit = 50,
                    Filter = new TdApi.SearchMessagesFilterChatPhoto()
                }) as TdApi.FoundChatMessages;
                if (found?.Messages != null)
                    foreach (var m in found.Messages)
                        if (m?.Content is TdApi.MessageChatChangePhoto ch && ch.Photo != null)
                            photos.Add(ch.Photo);
            }
            if (!_alive) return;

            var seen = new HashSet<long>();
            foreach (var p in photos)
            {
                if (!seen.Add(p.Id)) continue;
                var size = PickFull(p.Sizes);
                if (size?.Photo == null) continue;
                Slides.Add(new AvatarSlide(size.Photo.Id, Formatter.Time(p.AddedDate)));
            }

            Hint.Visibility = Slides.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Hint.Text = "нет фото";
            UpdateCounter();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _alive = false;
            Flip.ItemsSource = null;
            Slides.Clear();
        }

        /// <summary>Самый большой размер, но не больше MaxPhotoSide.</summary>
        private static TdApi.PhotoSize PickFull(IList<TdApi.PhotoSize> sizes)
        {
            if (sizes == null) return null;
            TdApi.PhotoSize best = null, smallest = null;
            foreach (var s in sizes)
            {
                int side = Math.Max(s.Width, s.Height);
                if (smallest == null || side < Math.Max(smallest.Width, smallest.Height)) smallest = s;
                if (side <= Config.MaxPhotoSide && (best == null || side > Math.Max(best.Width, best.Height))) best = s;
            }
            return best ?? smallest;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) { UpdateCounter(); }

        private void UpdateCounter()
        {
            if (Slides.Count == 0) { CounterText.Text = string.Empty; return; }
            int i = Math.Max(0, Flip.SelectedIndex);
            var date = Slides[i].Date;
            CounterText.Text = (i + 1) + " из " + Slides.Count + (string.IsNullOrEmpty(date) ? "" : " · " + date);
        }
    }
}
