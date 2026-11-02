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
    /// <summary>Открыть тему форума.</summary>
    public sealed class TopicArgs
    {
        public long ChatId { get; set; }
        public int TopicId { get; set; }
        public string Name { get; set; }
        public bool IsGeneral { get; set; }
    }

    /// <summary>Список тем супергруппы-форума. Подгружается порциями по 40 при прокрутке.</summary>
    public sealed partial class TopicsPage : Page
    {
        public ObservableCollection<TopicItem> Topics { get; } = new ObservableCollection<TopicItem>();
        public ChatItem Chat { get; private set; }

        private long _chatId;
        private int _offDate;
        private long _offMsg;
        private int _offTopic;
        private bool _loading, _end, _alive;
        private readonly HashSet<int> _seen = new HashSet<int>();
        private ScrollViewer _scroll;

        public TopicsPage()
        {
            InitializeComponent();
            TopicList.Loaded += (s, e) =>
            {
                if (_scroll != null) return;
                _scroll = ChatsPage.FindScrollViewer(TopicList);
                if (_scroll != null) _scroll.ViewChanged += OnViewChanged;
            };
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            _alive = true;
            _chatId = (long)e.Parameter;
            Chat = await ChatStore.Current.EnsureAsync(_chatId);
            if (!_alive) return;
            Bindings.Update();
            TitleText.Text = (Chat?.Title ?? string.Empty).ToUpper();
            LoadMore();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _alive = false;
            if (_scroll != null) _scroll.ViewChanged -= OnViewChanged;
        }

        private async void LoadMore()
        {
            if (_loading || _end) return;
            _loading = true;
            var r = await TdService.Current.SendAsync(new TdApi.GetForumTopics
            {
                ChatId = _chatId,
                Query = string.Empty,
                OffsetDate = _offDate,
                OffsetMessageId = _offMsg,
                OffsetForumTopicId = _offTopic,
                Limit = 40
            });
            if (!_alive) return;

            var topics = r as TdApi.ForumTopics;
            if (topics?.Topics == null || topics.Topics.Count == 0) _end = true;
            if (topics != null)
            {
                foreach (var t in topics.Topics)
                    if (t?.Info != null && _seen.Add(t.Info.ForumTopicId)) Topics.Add(new TopicItem(t));
                if (topics.NextOffsetForumTopicId == 0 && topics.NextOffsetMessageId == 0) _end = true;
                _offDate = topics.NextOffsetDate;
                _offMsg = topics.NextOffsetMessageId;
                _offTopic = topics.NextOffsetForumTopicId;
            }

            Hint.Text = r is TdApi.Error err ? err.Message : "тем нет";
            Hint.Visibility = Topics.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _loading = false;
        }

        private void OnViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_scroll.VerticalOffset > _scroll.ScrollableHeight - _scroll.ViewportHeight) LoadMore();
        }

        private void OnTopicClick(object sender, ItemClickEventArgs e)
        {
            var t = e.ClickedItem as TopicItem;
            if (t == null) return;
            Frame.Navigate(typeof(ChatPage), new TopicArgs { ChatId = _chatId, TopicId = t.Id, Name = t.Name, IsGeneral = t.IsGeneral });
        }

        private void OnHeaderTapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            Frame.Navigate(typeof(ChatInfoPage), _chatId);
        }
    }
}
