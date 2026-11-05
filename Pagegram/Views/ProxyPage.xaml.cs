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
    /// <summary>
    /// Прокси через TDLib (addProxy / enableProxy / pingProxy): MTProto, SOCKS5, HTTP.
    /// Работает и до входа в аккаунт — открывается с экрана входа.
    /// Параметр навигации string — ссылка tg://proxy / t.me/proxy, предложим её добавить.
    /// </summary>
    public sealed partial class ProxyPage : Page
    {
        private bool _alive;

        public ProxyPage()
        {
            InitializeComponent();
            TypeBox.SelectedIndex = 0;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            _alive = true;
            Load();
            var link = e.Parameter as string;
            if (!string.IsNullOrEmpty(link))
            {
                LinkBox.Text = link;
                var dlg = new Windows.UI.Popups.MessageDialog("Добавить и включить прокси из ссылки?");
                dlg.Commands.Add(new Windows.UI.Popups.UICommand("добавить") { Id = 1 });
                dlg.Commands.Add(new Windows.UI.Popups.UICommand("отмена") { Id = 0 });
                if ((int)(await dlg.ShowAsync()).Id == 1) await AddFromLink(link);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e) { _alive = false; }

        // ---- список ----

        private async void Load()
        {
            var r = await TdService.Current.SendAsync(new TdApi.GetProxies()) as TdApi.AddedProxies;
            if (!_alive) return;
            List.Children.Clear();
            bool anyEnabled = false;
            if (r?.Proxies != null)
                foreach (var p in r.Proxies)
                {
                    if (p?.Proxy == null) continue;
                    anyEnabled |= p.IsEnabled;
                    List.Children.Add(Row(p));
                }
            Empty.Visibility = List.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            DisableButton.Visibility = anyEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        private FrameworkElement Row(TdApi.AddedProxy p)
        {
            var title = new TextBlock
            {
                Text = p.Proxy.Server + ":" + p.Proxy.Port + (p.IsEnabled ? "   ✓ включён" : ""),
                FontSize = 17, TextWrapping = TextWrapping.Wrap,
                Foreground = p.IsEnabled ? (Brush)Application.Current.Resources["SystemControlForegroundAccentBrush"] : new SolidColorBrush(Colors.White)
            };
            var status = Gray(TypeName(p.Proxy.Type));
            var info = new StackPanel();
            info.Children.Add(title);
            info.Children.Add(status);

            var main = new Button
            {
                Content = info, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(0, 8, 0, 8)
            };
            int id = p.Id;
            bool enabled = p.IsEnabled;
            main.Click += async (s, a) =>
            {
                var res = enabled
                    ? await TdService.Current.SendAsync(new TdApi.DisableProxy())
                    : await TdService.Current.SendAsync(new TdApi.EnableProxy { ProxyId = id });
                if (res is TdApi.Error err) Error.Text = err.Message;
                Load();
            };

            var ping = SmallButton("пинг");
            var proxy = p.Proxy;
            ping.Click += async (s, a) =>
            {
                status.Text = TypeName(proxy.Type) + " · проверка…";
                var res = await TdService.Current.SendAsync(new TdApi.PingProxy { Proxy = proxy });
                if (!_alive) return;
                status.Text = TypeName(proxy.Type) + " · " + (res is TdApi.Seconds sec
                    ? (int)(sec.SecondsValue * 1000) + " мс"
                    : "недоступен: " + ((res as TdApi.Error)?.Message ?? "?"));
            };
            var remove = SmallButton("удалить");
            remove.Click += async (s, a) =>
            {
                var res = await TdService.Current.SendAsync(new TdApi.RemoveProxy { ProxyId = id });
                if (res is TdApi.Error err) Error.Text = err.Message;
                Load();
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(main);
            Grid.SetColumn(ping, 1);
            Grid.SetColumn(remove, 2);
            grid.Children.Add(ping);
            grid.Children.Add(remove);
            return grid;
        }

        private static Button SmallButton(string text)
        {
            return new Button
            {
                Content = text, FontSize = 13, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static TextBlock Gray(string t)
        {
            return new TextBlock { Text = t, FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(255, 0x8A, 0x8A, 0x8A)) };
        }

        private static string TypeName(TdApi.ProxyType t)
        {
            if (t is TdApi.ProxyTypeMtproto) return "MTProto";
            if (t is TdApi.ProxyTypeSocks5) return "SOCKS5";
            if (t is TdApi.ProxyTypeHttp) return "HTTP";
            return "прокси";
        }

        private async void OnDisable(object sender, RoutedEventArgs e)
        {
            await TdService.Current.SendAsync(new TdApi.DisableProxy());
            Load();
        }

        // ---- добавление ----

        private async void OnAddLink(object sender, RoutedEventArgs e) { await AddFromLink(LinkBox.Text.Trim()); }

        private async System.Threading.Tasks.Task AddFromLink(string link)
        {
            Error.Text = string.Empty;
            if (link.Length == 0) return;
            // TDLib сам разбирает tg://proxy, tg://socks, t.me/proxy, t.me/socks
            var r = await TdService.Current.SendAsync(new TdApi.GetInternalLinkType { Link = link });
            var p = (r as TdApi.InternalLinkTypeProxy)?.Proxy;
            if (p == null) { Error.Text = "Это не ссылка на прокси"; return; }
            await Add(p);
        }

        private void OnPresetWs(object sender, RoutedEventArgs e)
        {
            TypeBox.SelectedIndex = 0; // MTProto
            PortBox.Text = "1443";
            if (ServerBox.Text.Trim().Length == 0) ServerBox.PlaceholderText = "IP ПК или роутера, где запущен tg-ws-proxy";
            ServerBox.Focus(FocusState.Programmatic);
        }

        private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            bool mt = TypeBox.SelectedIndex == 0;
            SecretBox.Visibility = mt ? Visibility.Visible : Visibility.Collapsed;
            UserBox.Visibility = mt ? Visibility.Collapsed : Visibility.Visible;
            PassBox.Visibility = mt ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void OnAddManual(object sender, RoutedEventArgs e)
        {
            Error.Text = string.Empty;
            var server = ServerBox.Text.Trim();
            int port;
            if (server.Length == 0) { Error.Text = "Укажите сервер"; return; }
            if (!int.TryParse(PortBox.Text.Trim(), out port) || port <= 0 || port > 65535) { Error.Text = "Неверный порт"; return; }

            TdApi.ProxyType type;
            switch (TypeBox.SelectedIndex)
            {
                case 1: type = new TdApi.ProxyTypeSocks5 { Username = UserBox.Text.Trim(), Password = PassBox.Password }; break;
                case 2: type = new TdApi.ProxyTypeHttp { Username = UserBox.Text.Trim(), Password = PassBox.Password, HttpOnly = false }; break;
                default:
                    var secret = SecretBox.Text.Trim();
                    if (secret.Length == 0) { Error.Text = "Укажите секрет"; return; }
                    type = new TdApi.ProxyTypeMtproto { Secret = secret };
                    break;
            }
            await Add(new TdApi.Proxy { Server = server, Port = port, Type = type });
        }

        private async System.Threading.Tasks.Task Add(TdApi.Proxy p)
        {
            var r = await TdService.Current.SendAsync(new TdApi.AddProxy { Proxy = p, Enable = true, Comment = string.Empty });
            if (r is TdApi.Error err) { Error.Text = "Не удалось добавить: " + err.Message; return; }
            Load();
        }
    }
}
