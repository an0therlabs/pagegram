using System;
using System.Collections.Generic;
using Pagegram.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Views
{
    /// <summary>
    /// Конфиденциальность: для каждого пункта «Все / Мои контакты / Никто».
    /// Меняем только общее правило, исключения (списки людей) не трогаем.
    /// </summary>
    public sealed partial class PrivacyPage : Page
    {
        private static readonly string[] Choices = { "Все", "Мои контакты", "Никто" };

        public PrivacyPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            await AddSetting("Последний визит и статус «в сети»", () => new TdApi.UserPrivacySettingShowStatus());
            await AddSetting("Номер телефона", () => new TdApi.UserPrivacySettingShowPhoneNumber());
            await AddSetting("Фото профиля", () => new TdApi.UserPrivacySettingShowProfilePhoto());
            await AddSetting("Звонки", () => new TdApi.UserPrivacySettingAllowCalls());
            await AddSetting("Приглашения в группы", () => new TdApi.UserPrivacySettingAllowChatInvites());
        }

        private async System.Threading.Tasks.Task AddSetting(string title, Func<TdApi.UserPrivacySetting> make)
        {
            var box = new ComboBox { Header = title, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 16) };
            foreach (var c in Choices) box.Items.Add(c);
            box.IsEnabled = false;
            Root.Children.Add(box);

            var rules = await TdService.Current.SendAsync(new TdApi.GetUserPrivacySettingRules { Setting = make() }) as TdApi.UserPrivacySettingRules;
            var list = rules?.Rules != null ? new List<TdApi.UserPrivacySettingRule>(rules.Rules) : new List<TdApi.UserPrivacySettingRule>();
            box.SelectedIndex = GeneralIndex(list);
            box.IsEnabled = rules != null;

            box.SelectionChanged += async (s, a) =>
            {
                int i = box.SelectedIndex;
                if (i < 0) return;
                // заменяем общее правило, исключения оставляем как были
                var updated = new List<TdApi.UserPrivacySettingRule>();
                foreach (var r in list)
                    if (!(r is TdApi.UserPrivacySettingRuleAllowAll || r is TdApi.UserPrivacySettingRuleAllowContacts || r is TdApi.UserPrivacySettingRuleRestrictAll))
                        updated.Add(r);
                TdApi.UserPrivacySettingRule general;
                if (i == 0) general = new TdApi.UserPrivacySettingRuleAllowAll();
                else if (i == 1) general = new TdApi.UserPrivacySettingRuleAllowContacts();
                else general = new TdApi.UserPrivacySettingRuleRestrictAll();
                updated.Add(general);
                var res = await TdService.Current.SendAsync(new TdApi.SetUserPrivacySettingRules
                {
                    Setting = make(),
                    Rules = new TdApi.UserPrivacySettingRules { Rules = updated }
                });
                if (res is TdApi.Error err) await new Windows.UI.Popups.MessageDialog(err.Message).ShowAsync();
                else list = updated;
            };
        }

        private static int GeneralIndex(List<TdApi.UserPrivacySettingRule> rules)
        {
            foreach (var r in rules)
            {
                if (r is TdApi.UserPrivacySettingRuleAllowAll) return 0;
                if (r is TdApi.UserPrivacySettingRuleAllowContacts) return 1;
                if (r is TdApi.UserPrivacySettingRuleRestrictAll) return 2;
            }
            return 2; // нет общего правила — значит «никто», кроме исключений
        }
    }
}
