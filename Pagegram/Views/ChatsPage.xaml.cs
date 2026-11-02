using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Pagegram.Models;
using Pagegram.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Views
{
    public sealed partial class ChatsPage : Page
    {
        public ObservableCollection<ChatItem> Chats => ChatStore.Current.Main;
        public ObservableCollection<PersonItem> Contacts { get; } = new ObservableCollection<PersonItem>();

        private bool _loading;
        private bool _allLoaded;
        private bool _contactsLoaded;
        private ScrollViewer _scroll;

        public ChatsPage()
        {
            InitializeComponent();
            ChatList.Loaded += (s, e) =>
            {
                if (_scroll != null) return;
                _scroll = FindScrollViewer(ChatList);
                if (_scroll != null) _scroll.ViewChanged += OnViewChanged;
            };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            TdService.Current.Update += OnUpdate;
            if (e.NavigationMode == NavigationMode.New)
            {
                _allLoaded = false;
                _contactsLoaded = false;
                Contacts.Clear();
                Tabs.SelectedIndex = 0;
                LoadMore();
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            TdService.Current.Update -= OnUpdate;
        }

        private void OnUpdate(TdApi.BaseObject u)
        {
            if (u is TdApi.UpdateConnectionState c)
            {
                string text = null;
                switch (c.State)
                {
                    case TdApi.ConnectionStateReady _: break;
                    case TdApi.ConnectionStateWaitingForNetwork _: text = "ожидание сети…"; break;
                    case TdApi.ConnectionStateUpdating _: text = "обновление…"; break;
                    default: text = "соединение…"; break;
                }
                Status.Text = text ?? string.Empty;
                Status.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        // ---- чаты ----

        private async void LoadMore()
        {
            if (_loading || _allLoaded) return;
            _loading = true;
            // Сами чаты прилетят апдейтами updateNewChat/updateChatPosition
            var r = await TdService.Current.SendAsync(new TdApi.LoadChats
            {
                ChatList = new TdApi.ChatListMain(),
                Limit = Config.ChatsPageSize
            });
            if (r is TdApi.Error err && err.Code == 404) _allLoaded = true;
            _loading = false;
        }

        private void OnViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            var sv = _scroll;
            if (sv.VerticalOffset > sv.ScrollableHeight - sv.ViewportHeight) LoadMore();
        }

        private void OnChatClick(object sender, ItemClickEventArgs e)
        {
            var chat = e.ClickedItem as ChatItem;
            if (chat != null) Frame.Navigate(typeof(ChatPage), chat.Id);
        }

        // ---- контакты: грузим только при первом открытии вкладки ----

        private void OnTabChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Tabs.SelectedIndex == 1 && !_contactsLoaded) LoadContacts();
        }

        private async void LoadContacts()
        {
            _contactsLoaded = true;
            ContactsHint.Text = "загрузка…";
            ContactsHint.Visibility = Visibility.Visible;

            var r = await TdService.Current.SendAsync(new TdApi.GetContacts());
            var ids = (r as TdApi.Users)?.UserIds;
            if (ids == null)
            {
                ContactsHint.Text = (r as TdApi.Error)?.Message ?? "не удалось загрузить контакты";
                _contactsLoaded = false;
                return;
            }

            var people = new List<PersonItem>(ids.Count);
            foreach (var id in ids)
            {
                // getUser отвечает из локального кэша TDLib — быстро
                var u = await TdService.Current.SendAsync(new TdApi.GetUser { UserId = id }) as TdApi.User;
                if (u == null) continue;
                people.Add(new PersonItem(u.Id, Formatter.FullName(u.FirstName, u.LastName),
                    Formatter.UserStatus(u.Status), u.ProfilePhoto?.Small?.Id ?? 0));
            }

            Contacts.Clear();
            foreach (var p in people.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)) Contacts.Add(p);

            ContactsHint.Text = "контактов пока нет";
            ContactsHint.Visibility = Contacts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void OnContactClick(object sender, ItemClickEventArgs e)
        {
            var person = e.ClickedItem as PersonItem;
            if (person == null) return;
            var r = await TdService.Current.SendAsync(new TdApi.CreatePrivateChat { UserId = person.UserId, Force = false });
            if (r is TdApi.Chat chat)
            {
                await ChatStore.Current.EnsureAsync(chat.Id);
                Frame.Navigate(typeof(ChatPage), chat.Id);
            }
        }

        // ---- нижняя панель ----

        private void OnNewChat(object sender, RoutedEventArgs e)
        {
            // Новый чат = выбрать собеседника из контактов
            Tabs.SelectedIndex = 1;
        }

        private void OnSearch(object sender, RoutedEventArgs e) { Frame.Navigate(typeof(SearchPage)); }
        private void OnSettings(object sender, RoutedEventArgs e) { Frame.Navigate(typeof(SettingsPage)); }
        private void OnAbout(object sender, RoutedEventArgs e) { Frame.Navigate(typeof(AboutPage)); }

        internal static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root is ScrollViewer sv) return sv;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var r = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (r != null) return r;
            }
            return null;
        }
    }
}
