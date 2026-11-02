using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Pagegram.Models;
using Pagegram.Services;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Views
{
    public sealed class ForwardArgs
    {
        public long FromChatId { get; set; }
        public long MessageId { get; set; }
    }

    /// <summary>Выбор чата, куда переслать сообщение. Список — те же чаты, что на главной, с фильтром по названию.</summary>
    public sealed partial class ForwardPage : Page
    {
        public ObservableCollection<ChatItem> Shown { get; } = new ObservableCollection<ChatItem>();
        private ForwardArgs _args;
        private bool _sending;

        public ForwardPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            _args = e.Parameter as ForwardArgs;
            Apply(string.Empty);
        }

        private void OnFilterChanged(object sender, TextChangedEventArgs e) { Apply(Filter.Text.Trim()); }

        private void Apply(string q)
        {
            Shown.Clear();
            IEnumerable<ChatItem> src = ChatStore.Current.Main;
            if (q.Length > 0) src = src.Where(c => c.Title.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
            foreach (var c in src.Take(100)) Shown.Add(c);
        }

        private async void OnChatClick(object sender, ItemClickEventArgs e)
        {
            var target = e.ClickedItem as ChatItem;
            if (target == null || _args == null || _sending) return;
            _sending = true;
            var r = await TdService.Current.SendAsync(new TdApi.ForwardMessages
            {
                ChatId = target.Id,
                FromChatId = _args.FromChatId,
                MessageIds = new List<long> { _args.MessageId },
                SendCopy = false,
                RemoveCaption = false
            });
            _sending = false;
            if (r is TdApi.Error err)
            {
                await new Windows.UI.Popups.MessageDialog("Не удалось переслать: " + err.Message).ShowAsync();
                return;
            }
            // открываем чат, куда переслали; сам экран выбора из истории убираем
            Frame.Navigate(typeof(ChatPage), target.Id);
            var back = Frame.BackStack;
            if (back.Count > 0 && back[back.Count - 1].SourcePageType == typeof(ForwardPage)) back.RemoveAt(back.Count - 1);
        }
    }
}
