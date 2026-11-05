using System;
using Pagegram.Services;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Views
{
    /// <summary>Устройства, где открыт аккаунт. Тап по чужому сеансу — завершить его.</summary>
    public sealed partial class SessionsPage : Page
    {
        public SessionsPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e) { Load(); }

        private async void Load()
        {
            List.Children.Clear();
            var r = await TdService.Current.SendAsync(new TdApi.GetActiveSessions()) as TdApi.Sessions;
            if (r?.Sessions == null) { Hint.Text = "не удалось загрузить сеансы"; return; }
            Hint.Visibility = Visibility.Collapsed;

            int others = 0;
            foreach (var s in r.Sessions)
            {
                if (s == null) continue;
                if (!s.IsCurrent) others++;
                List.Children.Add(Row(s));
            }
            TerminateAll.Visibility = others > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private FrameworkElement Row(TdApi.Session s)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
            panel.Children.Add(new TextBlock
            {
                Text = (s.ApplicationName ?? "") + " " + (s.ApplicationVersion ?? "") + (s.IsCurrent ? "  · это устройство" : ""),
                FontSize = 17, Foreground = new SolidColorBrush(Colors.White), TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(Gray((s.DeviceModel ?? "") + ", " + (s.Platform ?? "") + " " + (s.SystemVersion ?? "")));
            panel.Children.Add(Gray((s.IpAddress ?? "") + (string.IsNullOrEmpty(s.Location) ? "" : " · " + s.Location) +
                                    (s.IsCurrent ? "" : " · " + Formatter.Time(s.LastActiveDate))));

            var b = new Button
            {
                Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(0, 8, 0, 8),
                IsEnabled = !s.IsCurrent
            };
            var id = s.Id;
            var name = s.ApplicationName + ", " + s.DeviceModel;
            b.Click += async (o, a) =>
            {
                var dlg = new Windows.UI.Popups.MessageDialog("Завершить сеанс «" + name + "»?");
                dlg.Commands.Add(new Windows.UI.Popups.UICommand("завершить") { Id = 1 });
                dlg.Commands.Add(new Windows.UI.Popups.UICommand("отмена") { Id = 0 });
                if ((int)(await dlg.ShowAsync()).Id != 1) return;
                var res = await TdService.Current.SendAsync(new TdApi.TerminateSession { SessionId = id });
                if (res is TdApi.Error err) await new Windows.UI.Popups.MessageDialog(err.Message).ShowAsync();
                Load();
            };
            return b;
        }

        private static TextBlock Gray(string t)
        {
            return new TextBlock { Text = t, FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(255, 0x8A, 0x8A, 0x8A)), TextWrapping = TextWrapping.Wrap };
        }

        private async void OnTerminateAll(object sender, RoutedEventArgs e)
        {
            var dlg = new Windows.UI.Popups.MessageDialog("Завершить все сеансы, кроме этого устройства?");
            dlg.Commands.Add(new Windows.UI.Popups.UICommand("завершить") { Id = 1 });
            dlg.Commands.Add(new Windows.UI.Popups.UICommand("отмена") { Id = 0 });
            if ((int)(await dlg.ShowAsync()).Id != 1) return;
            var res = await TdService.Current.SendAsync(new TdApi.TerminateAllOtherSessions());
            if (res is TdApi.Error err) await new Windows.UI.Popups.MessageDialog(err.Message).ShowAsync();
            Load();
        }
    }
}
