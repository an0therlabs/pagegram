using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Security.ExchangeActiveSyncProvisioning;
using Windows.Storage;
using Windows.UI.Core;
using Td = Telegram.Td;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Services
{
    /// <summary>
    /// Обёртка над TDLib. Апдейты фильтруются на фоновом потоке, а в UI
    /// прилетают пачками через одну задачу диспетчера — чтобы слабый проц
    /// не захлёбывался сотнями RunAsync при старте.
    /// </summary>
    public sealed class TdService : Td.ClientResultHandler
    {
        public static TdService Current { get; } = new TdService();

        /// <summary>Вызывается на UI-потоке.</summary>
        public event Action<TdApi.BaseObject> Update;

        public TdApi.AuthorizationState AuthState { get; private set; }

        /// <summary>Кэш имён пользователей (только строки, без лишних объектов).</summary>
        public ConcurrentDictionary<long, string> UserNames { get; } = new ConcurrentDictionary<long, string>();

        /// <summary>file_id маленькой аватарки пользователя (0 — нет фото).</summary>
        public ConcurrentDictionary<long, int> UserPhotos { get; } = new ConcurrentDictionary<long, int>();

        public long MyUserId { get; private set; }

        private static readonly Handler Ignore = new Handler(null);
        private static bool _runStarted;

        private readonly ConcurrentQueue<TdApi.BaseObject> _queue = new ConcurrentQueue<TdApi.BaseObject>();
        private int _drainScheduled;
        private CoreDispatcher _dispatcher;
        private Td.Client _client;

        private TdService() { }

        public void Start(CoreDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            if (_client != null) return;

            if (!_runStarted)
            {
                _runStarted = true;
                // 1 = только ошибки. Логи не пишем — экономим флеш и CPU.
                Td.Client.Execute(new TdApi.SetLogVerbosityLevel(1));
                Task.Factory.StartNew(() => Td.Client.Run(), TaskCreationOptions.LongRunning);
            }
            _client = Td.Client.Create(this);
        }

        public void Send(TdApi.Function f)
        {
            _client?.Send(f, Ignore);
        }

        public Task<TdApi.BaseObject> SendAsync(TdApi.Function f)
        {
            var tcs = new TaskCompletionSource<TdApi.BaseObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            var c = _client;
            if (c == null) tcs.SetResult(new TdApi.Error { Code = 500, Message = "Client is not started" });
            else c.Send(f, new Handler(tcs));
            return tcs.Task;
        }

        public void SetOption(string name, bool value)
        {
            Send(new TdApi.SetOption { Name = name, Value = new TdApi.OptionValueBoolean { Value = value } });
        }

        // ---- приём апдейтов (фоновый поток TDLib) ----

        public void OnResult(TdApi.BaseObject obj)
        {
            switch (obj)
            {
                case TdApi.UpdateAuthorizationState a:
                    OnAuthState(a.AuthorizationState);
                    Enqueue(obj);
                    return;

                case TdApi.UpdateUser u:
                    var user = u.User;
                    UserNames[user.Id] = Formatter.FullName(user.FirstName, user.LastName);
                    UserPhotos[user.Id] = user.ProfilePhoto?.Small?.Id ?? 0;
                    return; // в UI не гоняем вообще

                case TdApi.UpdateOption o:
                    if (o.Name == "my_id" && o.Value is TdApi.OptionValueInteger id) MyUserId = id.Value;
                    return;

                // То, что реально нужно интерфейсу
                case TdApi.UpdateNewChat _:
                case TdApi.UpdateChatTitle _:
                case TdApi.UpdateChatPhoto _:
                case TdApi.UpdateChatNotificationSettings _:
                case TdApi.UpdateChatLastMessage _:
                case TdApi.UpdateChatPosition _:
                case TdApi.UpdateChatReadInbox _:
                case TdApi.UpdateChatDraftMessage _:
                case TdApi.UpdateNewMessage _:
                case TdApi.UpdateMessageSendSucceeded _:
                case TdApi.UpdateMessageSendFailed _:
                case TdApi.UpdateMessageContent _:
                case TdApi.UpdateMessageInteractionInfo _:
                case TdApi.UpdateDeleteMessages _:
                case TdApi.UpdateConnectionState _:
                    Enqueue(obj);
                    return;

                default:
                    return; // остальное (стикеры, реакции, сторис и т.д.) выкидываем сразу
            }
        }

        private void Enqueue(TdApi.BaseObject obj)
        {
            _queue.Enqueue(obj);
            if (Interlocked.Exchange(ref _drainScheduled, 1) == 0)
            {
                var d = _dispatcher;
                if (d == null) { _drainScheduled = 0; return; }
                var _ = d.RunAsync(CoreDispatcherPriority.Normal, Drain);
            }
        }

        private void Drain()
        {
            // не больше 100 апдейтов за один заход, чтобы UI оставался отзывчивым
            int n = 0;
            TdApi.BaseObject obj;
            while (n++ < 100 && _queue.TryDequeue(out obj))
            {
                try { Update?.Invoke(obj); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }

            Interlocked.Exchange(ref _drainScheduled, 0);
            if (!_queue.IsEmpty && Interlocked.Exchange(ref _drainScheduled, 1) == 0)
            {
                var _ = _dispatcher.RunAsync(CoreDispatcherPriority.Low, Drain);
            }
        }

        private void OnAuthState(TdApi.AuthorizationState state)
        {
            AuthState = state;

            if (state is TdApi.AuthorizationStateWaitTdlibParameters)
            {
                var root = ApplicationData.Current.LocalFolder.Path;
                string model = "Windows Phone";
                try { model = new EasClientDeviceInformation().SystemProductName; } catch { }

                Send(new TdApi.SetTdlibParameters
                {
                    UseTestDc = false,
                    DatabaseDirectory = Path.Combine(root, "td"),
                    FilesDirectory = Path.Combine(root, "td_files"),
                    DatabaseEncryptionKey = new byte[0],
                    // всё держим в базе на диске, а не в RAM
                    UseFileDatabase = true,
                    UseChatInfoDatabase = true,
                    UseMessageDatabase = true,
                    UseSecretChats = false,
                    ApiId = Config.ApiId,
                    ApiHash = Config.ApiHash,
                    SystemLanguageCode = "ru",
                    DeviceModel = string.IsNullOrEmpty(model) ? "Lumia" : model,
                    SystemVersion = "Windows 10 Mobile",
                    ApplicationVersion = Config.AppVersion
                });
            }
            else if (state is TdApi.AuthorizationStateReady)
            {
                // Чистить старые файлы автоматически и не собирать статистику "топ чатов"
                SetOption("use_storage_optimizer", true);
                SetOption("disable_top_chats", true);
                SetOption("online", true);
            }
            else if (state is TdApi.AuthorizationStateClosed)
            {
                // После выхода из аккаунта создаём новый клиент, чтобы можно было войти заново
                _client = null;
                UserNames.Clear();
                UserPhotos.Clear();
                var d = _dispatcher;
                if (d != null)
                {
                    var _ = d.RunAsync(CoreDispatcherPriority.Normal, () => Start(d));
                }
            }
        }

        private sealed class Handler : Td.ClientResultHandler
        {
            private readonly TaskCompletionSource<TdApi.BaseObject> _tcs;
            public Handler(TaskCompletionSource<TdApi.BaseObject> tcs) { _tcs = tcs; }
            public void OnResult(TdApi.BaseObject obj) { _tcs?.TrySetResult(obj); }
        }
    }
}
