using System;
using Pagegram.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Views
{
    public sealed partial class LoginPage : Page
    {
        private TdApi.AuthorizationState _state;

        public LoginPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            TdService.Current.Update += OnUpdate;
            if (Config.ApiId == 0 || string.IsNullOrEmpty(Config.ApiHash))
            {
                Busy.Visibility = Visibility.Collapsed;
                Hint.Text = "Впиши ApiId и ApiHash в Config.cs (берутся на my.telegram.org) и пересобери.";
                return;
            }
            Show(TdService.Current.AuthState);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            TdService.Current.Update -= OnUpdate;
        }

        private void OnUpdate(TdApi.BaseObject u)
        {
            if (u is TdApi.UpdateAuthorizationState a) Show(a.AuthorizationState);
        }

        private void Show(TdApi.AuthorizationState s)
        {
            _state = s;
            Error.Text = string.Empty;
            Busy.Visibility = Visibility.Collapsed;
            Input1.Visibility = Input2.Visibility = Password.Visibility = Submit.Visibility = Visibility.Collapsed;
            Input1.Text = Input2.Text = Password.Password = string.Empty;
            Submit.IsEnabled = true;

            switch (s)
            {
                case TdApi.AuthorizationStateWaitPhoneNumber _:
                    Hint.Text = "Введи номер телефона в международном формате.";
                    Field(Input1, "+7 900 000 00 00", InputScopeNameValue.TelephoneNumber);
                    break;

                case TdApi.AuthorizationStateWaitCode _:
                    Hint.Text = "Код отправлен в Telegram на другом устройстве или по SMS.";
                    Field(Input1, "Код", InputScopeNameValue.Number);
                    break;

                case TdApi.AuthorizationStateWaitPassword p:
                    Hint.Text = "Включена двухэтапная проверка. Введи облачный пароль."
                        + (string.IsNullOrEmpty(p.PasswordHint) ? "" : "\nПодсказка: " + p.PasswordHint);
                    Password.Visibility = Visibility.Visible;
                    Password.PlaceholderText = "Пароль";
                    Submit.Visibility = Visibility.Visible;
                    Password.Focus(FocusState.Programmatic);
                    break;

                case TdApi.AuthorizationStateWaitRegistration _:
                    Hint.Text = "Номер не зарегистрирован. Как тебя зовут?";
                    Field(Input1, "Имя", InputScopeNameValue.PersonalFullName);
                    Input2.Visibility = Visibility.Visible;
                    Input2.PlaceholderText = "Фамилия (необязательно)";
                    break;

                case TdApi.AuthorizationStateWaitEmailAddress _:
                    Hint.Text = "Telegram требует привязать почту.";
                    Field(Input1, "email@example.com", InputScopeNameValue.EmailSmtpAddress);
                    break;

                case TdApi.AuthorizationStateWaitEmailCode _:
                    Hint.Text = "Введи код из письма.";
                    Field(Input1, "Код", InputScopeNameValue.Number);
                    break;

                case TdApi.AuthorizationStateWaitOtherDeviceConfirmation _:
                    Hint.Text = "Этот способ входа не поддерживается. Перезапусти приложение и войди по номеру.";
                    break;

                case TdApi.AuthorizationStateReady _:
                    Hint.Text = "Готово!";
                    break;

                case TdApi.AuthorizationStateWaitTdlibParameters _:
                case TdApi.AuthorizationStateLoggingOut _:
                case TdApi.AuthorizationStateClosing _:
                case TdApi.AuthorizationStateClosed _:
                case null:
                    Hint.Text = "Подключение…";
                    Busy.Visibility = Visibility.Visible;
                    break;

                default:
                    // новые состояния из свежих версий TDLib (например, оплата Premium при входе)
                    Hint.Text = "Этот шаг входа пока не поддерживается.";
                    break;

            }
        }

        private void Field(TextBox box, string placeholder, InputScopeNameValue scope)
        {
            box.PlaceholderText = placeholder;
            var s = new InputScope();
            s.Names.Add(new InputScopeName(scope));
            box.InputScope = s;
            box.Visibility = Visibility.Visible;
            Submit.Visibility = Visibility.Visible;
            box.Focus(FocusState.Programmatic);
        }

        private async void OnSubmit(object sender, RoutedEventArgs e)
        {
            var text = Input1.Text.Trim();
            TdApi.Function f = null;
            switch (_state)
            {
                case TdApi.AuthorizationStateWaitPhoneNumber _:
                    f = new TdApi.SetAuthenticationPhoneNumber { PhoneNumber = text };
                    break;
                case TdApi.AuthorizationStateWaitCode _:
                    f = new TdApi.CheckAuthenticationCode { Code = text };
                    break;
                case TdApi.AuthorizationStateWaitPassword _:
                    f = new TdApi.CheckAuthenticationPassword { Password = Password.Password };
                    break;
                case TdApi.AuthorizationStateWaitRegistration _:
                    f = new TdApi.RegisterUser { FirstName = text, LastName = Input2.Text.Trim() };
                    break;
                case TdApi.AuthorizationStateWaitEmailAddress _:
                    f = new TdApi.SetAuthenticationEmailAddress { EmailAddress = text };
                    break;
                case TdApi.AuthorizationStateWaitEmailCode _:
                    f = new TdApi.CheckAuthenticationEmailCode { Code = new TdApi.EmailAddressAuthenticationCode { Code = text } };
                    break;
            }
            if (f == null) return;

            Submit.IsEnabled = false;
            Busy.Visibility = Visibility.Visible;
            var r = await TdService.Current.SendAsync(f);
            Busy.Visibility = Visibility.Collapsed;
            Submit.IsEnabled = true;
            if (r is TdApi.Error err) Error.Text = err.Message;
        }
    }
}
