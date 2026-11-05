using System;
using System.Collections.Generic;
using Pagegram.Models;
using Pagegram.Services;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Views
{
    public sealed partial class SettingsPage : Page
    {
        private bool _alive;

        public SettingsPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            _alive = true;
            var me = await TdService.Current.SendAsync(new TdApi.GetMe()) as TdApi.User;
            if (!_alive || me == null) return;

            var name = Formatter.FullName(me.FirstName, me.LastName);
            NameText.Text = name;
            Phone.Text = string.IsNullOrEmpty(me.PhoneNumber) ? "скрыт" : "+" + me.PhoneNumber;
            var un = me.Usernames?.ActiveUsernames;
            Username.Text = un != null && un.Count > 0 ? "@" + un[0] : "не задано";

            AvatarCircle.Fill = Palette.Avatar(me.Id);
            AvatarInitial.Text = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?";
            var fileId = me.ProfilePhoto?.Small?.Id ?? 0;
            var img = Images.Get(ImageKind.Avatar, fileId, loaded => { if (_alive) AvatarImage.ImageSource = loaded; });
            if (img != null) AvatarImage.ImageSource = img;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _alive = false;
            AvatarImage.ImageSource = null;
        }

        private void OnEditProfile(object sender, RoutedEventArgs e) { Frame.Navigate(typeof(EditProfilePage)); }
        private void OnPrivacy(object sender, RoutedEventArgs e) { Frame.Navigate(typeof(PrivacyPage)); }
        private void OnSessions(object sender, RoutedEventArgs e) { Frame.Navigate(typeof(SessionsPage)); }
        private void OnChatSettings(object sender, RoutedEventArgs e) { Frame.Navigate(typeof(ChatSettingsPage)); }
        private void OnAbout(object sender, RoutedEventArgs e) { Frame.Navigate(typeof(AboutPage)); }

        private async void OnLogout(object sender, RoutedEventArgs e)
        {
            var dlg = new MessageDialog("Выйти из аккаунта на этом устройстве?");
            dlg.Commands.Add(new UICommand("Выйти") { Id = 1 });
            dlg.Commands.Add(new UICommand("Отмена") { Id = 0 });
            var res = await dlg.ShowAsync();
            if ((int)res.Id == 1) TdService.Current.Send(new TdApi.LogOut());
        }
    }
}
