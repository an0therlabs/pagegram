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
    public sealed partial class SearchPage : Page
    {
        public ObservableCollection<ChatItem> Results { get; } = new ObservableCollection<ChatItem>();

        private readonly DispatcherTimer _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        private int _generation;

        public SearchPage()
        {
            InitializeComponent();
            _debounce.Tick += (s, e) => { _debounce.Stop(); Run(Query.Text.Trim()); };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            Hint.Text = "Введи название чата, канала или имя";
            Query.Loaded += (s, a) => Query.Focus(FocusState.Programmatic);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _debounce.Stop();
            _generation++;
            Results.Clear();
        }

        private void OnQueryChanged(object sender, TextChangedEventArgs e)
        {
            _debounce.Stop();
            _debounce.Start();
        }

        private async void Run(string query)
        {
            int gen = ++_generation;
            Results.Clear();
            if (query.Length == 0) { Hint.Text = "Введи название чата, канала или имя"; Hint.Visibility = Visibility.Visible; return; }

            Hint.Text = "поиск…";
            Hint.Visibility = Visibility.Visible;
            var seen = new HashSet<long>();

            // 1) свои чаты — локально и мгновенно
            await AddResults(gen, seen, await TdService.Current.SendAsync(new TdApi.SearchChats { Query = query, Limit = 30 }));
            if (gen != _generation) return;

            // 2) публичные каналы/группы/люди на сервере (по @username и названию)
            if (query.Length >= 3)
                await AddResults(gen, seen, await TdService.Current.SendAsync(new TdApi.SearchPublicChats { Query = query }));
            if (gen != _generation) return;

            Hint.Text = "ничего не найдено";
            Hint.Visibility = Results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async System.Threading.Tasks.Task AddResults(int gen, HashSet<long> seen, TdApi.BaseObject r)
        {
            var ids = (r as TdApi.Chats)?.ChatIds;
            if (ids == null) return;
            foreach (var id in ids)
            {
                if (gen != _generation) return;
                if (!seen.Add(id)) continue;
                var item = await ChatStore.Current.EnsureAsync(id);
                if (item != null && gen == _generation)
                {
                    Results.Add(item);
                    Hint.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void OnResultClick(object sender, ItemClickEventArgs e)
        {
            var chat = e.ClickedItem as ChatItem;
            if (chat != null) Frame.Navigate(typeof(ChatPage), chat.Id);
        }
    }
}
