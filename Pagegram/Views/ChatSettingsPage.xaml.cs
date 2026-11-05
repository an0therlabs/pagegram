using System;
using System.Collections.Generic;
using Pagegram.Services;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Navigation;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Views
{
    /// <summary>Размер текста, автозагрузка фото, размер и очистка кэша.</summary>
    public sealed partial class ChatSettingsPage : Page
    {
        private bool _ready;

        public ChatSettingsPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            FontSlider.Value = AppSettings.MessageFontSize;
            Preview.FontSize = AppSettings.MessageFontSize;
            AutoPhotos.IsOn = AppSettings.AutoLoadPhotos;
            _ready = true;
            UpdateStorage();
        }

        private void OnFontChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_ready) return;
            AppSettings.MessageFontSize = e.NewValue;
            Preview.FontSize = e.NewValue; // в открытых заново чатах — новый размер
        }

        private void OnAutoPhotosToggled(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            AppSettings.AutoLoadPhotos = AutoPhotos.IsOn;
        }

        private async void UpdateStorage()
        {
            var st = await TdService.Current.SendAsync(new TdApi.GetStorageStatisticsFast()) as TdApi.StorageStatisticsFast;
            if (st == null) { CacheText.Text = "не удалось посчитать"; return; }
            CacheText.Text = "Кэш файлов: " + Size(st.FilesSize) + " (" + st.FileCount + " файлов)";
            DbText.Text = "База сообщений: " + Size(st.DatabaseSize);
        }

        private static string Size(long b)
        {
            if (b < 1024) return b + " Б";
            if (b < 1024 * 1024) return (b / 1024) + " КБ";
            return (b / 1024.0 / 1024.0).ToString("0.0") + " МБ";
        }

        private async void OnClearCache(object sender, RoutedEventArgs e)
        {
            // size=0, ttl=0, count=0 — удалить все скачанные файлы, сообщения остаются
            var r = await TdService.Current.SendAsync(new TdApi.OptimizeStorage
            {
                Size = 0, Ttl = 0, Count = 0, ImmunityDelay = 0,
                FileTypes = new List<TdApi.FileType>(), ChatIds = new List<long>(), ExcludeChatIds = new List<long>(),
                ReturnDeletedFileStatistics = false, ChatLimit = 0
            });
            Images.Clear();
            await new MessageDialog(r is TdApi.Error err ? "Ошибка: " + err.Message : "Кэш очищен").ShowAsync();
            UpdateStorage();
        }
    }
}
