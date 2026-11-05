using Pagegram.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Views
{
    /// <summary>Имя, фамилия, «о себе», имя пользователя — setName / setBio / setUsername (только изменённое).</summary>
    public sealed partial class EditProfilePage : Page
    {
        private string _first = "", _last = "", _bio = "", _username = "";

        public EditProfilePage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            SaveButton.IsEnabled = false;
            var me = await TdService.Current.SendAsync(new TdApi.GetMe()) as TdApi.User;
            if (me == null) { Error.Text = "Не удалось загрузить профиль"; return; }
            var full = await TdService.Current.SendAsync(new TdApi.GetUserFullInfo { UserId = me.Id }) as TdApi.UserFullInfo;
            _first = me.FirstName ?? "";
            _last = me.LastName ?? "";
            _bio = full?.Bio?.Text ?? "";
            var un = me.Usernames?.EditableUsername;
            _username = un ?? "";
            FirstName.Text = _first;
            LastName.Text = _last;
            Bio.Text = _bio;
            UsernameBox.Text = _username;
            SaveButton.IsEnabled = true;
        }

        private async void OnSave(object sender, RoutedEventArgs e)
        {
            Error.Text = string.Empty;
            var first = FirstName.Text.Trim();
            var last = LastName.Text.Trim();
            var bio = Bio.Text.Trim();
            var username = UsernameBox.Text.Trim().TrimStart('@');
            if (first.Length == 0) { Error.Text = "Имя не может быть пустым"; return; }

            SaveButton.IsEnabled = false;
            string err = null;
            if (first != _first || last != _last)
                err = Fail(await TdService.Current.SendAsync(new TdApi.SetName { FirstName = first, LastName = last })) ?? err;
            if (bio != _bio)
                err = Fail(await TdService.Current.SendAsync(new TdApi.SetBio { Bio = bio })) ?? err;
            if (username != _username)
                err = Fail(await TdService.Current.SendAsync(new TdApi.SetUsername { Username = username })) ?? err;
            SaveButton.IsEnabled = true;

            if (err != null) { Error.Text = err; return; }
            if (Frame.CanGoBack) Frame.GoBack();
        }

        private static string Fail(TdApi.BaseObject r)
        {
            return (r as TdApi.Error)?.Message;
        }
    }
}
