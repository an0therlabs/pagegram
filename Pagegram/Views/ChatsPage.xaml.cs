using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Pagegram.Models;
using Pagegram.Services;
using Windows.UI.Popups;
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

        private bool _loading;
        private bool _allLoaded;
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
                switch (c.State)
                {
                    case TdApi.ConnectionStateReady _: Header.Text = "ЧАТЫ"; break;
                    case TdApi.ConnectionStateWaitingForNetwork _: Header.Text = "ОЖИДАНИЕ СЕТИ…"; break;
                    case TdApi.ConnectionStateUpdating _: Header.Text = "ОБНОВЛЕНИЕ…"; break;
                    default: Header.Text = "СОЕДИНЕНИЕ…"; break;
                }
            }
        }

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

        private async void OnClearCache(object sender, RoutedEventArgs e)
        {
            // size=0, ttl=0, count=0 — удалить все скачанные файлы (фото и т.д.), сообщения остаются
            var r = await TdService.Current.SendAsync(new TdApi.OptimizeStorage
            {
                Size = 0,
                Ttl = 0,
                Count = 0,
                ImmunityDelay = 0,
                FileTypes = new List<TdApi.FileType>(),
                ChatIds = new List<long>(),
                ExcludeChatIds = new List<long>(),
                ReturnDeletedFileStatistics = false,
                ChatLimit = 0
            });
            var msg = r is TdApi.Error err ? "Ошибка: " + err.Message : "Кэш очищен";
            await new MessageDialog(msg).ShowAsync();
        }

        private async void OnLogout(object sender, RoutedEventArgs e)
        {
            var dlg = new MessageDialog("Выйти из аккаунта на этом устройстве?");
            dlg.Commands.Add(new UICommand("Выйти") { Id = 1 });
            dlg.Commands.Add(new UICommand("Отмена") { Id = 0 });
            var res = await dlg.ShowAsync();
            if ((int)res.Id == 1) TdService.Current.Send(new TdApi.LogOut());
        }

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
