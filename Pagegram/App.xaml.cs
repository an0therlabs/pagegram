using System;
using Pagegram.Services;
using Pagegram.Views;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using TdApi = Telegram.Td.Api;

namespace Pagegram
{
    sealed partial class App : Application
    {
        private Frame _frame;

        public App()
        {
            InitializeComponent();
            Suspending += OnSuspending;
            Resuming += OnResuming;
            EnteredBackground += (s, e) => LowMemory.Raise(); // в фоне лимит сильно ниже — ужимаемся заранее
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            if (_frame == null)
            {
                _frame = new Frame();
                // Без анимаций переходов — на слабом GPU они тормозят и жрут память
                _frame.ContentTransitions = null;
                Window.Current.Content = _frame;

                var dispatcher = Window.Current.Dispatcher;
                LowMemory.Init(dispatcher);
                SystemNavigationManager.GetForCurrentView().BackRequested += OnBackRequested;

                TdService.Current.Update += OnUpdate;
                TdService.Current.Start(dispatcher);

                _frame.Navigate(typeof(LoginPage), null, new SuppressNavigationTransitionInfo());
            }
            Window.Current.Activate();
        }

        private void OnUpdate(TdApi.BaseObject update)
        {
            ChatStore.Current.Handle(update);

            if (update is TdApi.UpdateAuthorizationState a)
            {
                var s = a.AuthorizationState;
                if (s is TdApi.AuthorizationStateReady)
                {
                    if (!(_frame.Content is ChatsPage))
                    {
                        _frame.Navigate(typeof(ChatsPage), null, new SuppressNavigationTransitionInfo());
                        _frame.BackStack.Clear();
                    }
                }
                else if (s is TdApi.AuthorizationStateLoggingOut || s is TdApi.AuthorizationStateClosed)
                {
                    ChatStore.Current.Clear();
                    GoToLogin();
                }
                else if (!(s is TdApi.AuthorizationStateClosing))
                {
                    GoToLogin();
                }
            }
        }

        private void GoToLogin()
        {
            if (_frame.Content is LoginPage) return;
            _frame.Navigate(typeof(LoginPage), null, new SuppressNavigationTransitionInfo());
            _frame.BackStack.Clear();
        }

        private void OnBackRequested(object sender, BackRequestedEventArgs e)
        {
            if (_frame != null && _frame.CanGoBack)
            {
                e.Handled = true;
                _frame.GoBack(new SuppressNavigationTransitionInfo());
            }
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            // База TDLib на диске, так что если систему прибьёт процесс — ничего не потеряем
            TdService.Current.SetOption("online", false);
        }

        private void OnResuming(object sender, object e)
        {
            TdService.Current.SetOption("online", true);
        }
    }
}
