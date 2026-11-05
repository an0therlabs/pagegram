using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Pagegram.Models;
using Pagegram.Services;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Views
{
    /// <summary>Что показать в просмотрщике: список фото (file_id полноразмерных) и с какого начать.</summary>
    public sealed class PhotoViewerArgs
    {
        public PhotoViewerArgs(List<int> fileIds, int index)
        {
            FileIds = fileIds ?? new List<int>();
            Index = index;
        }
        public List<int> FileIds { get; }
        public int Index { get; }
    }

    /// <summary>
    /// Просмотрщик фото: вписано в экран, свайп между фото, зум щипком и двойным тапом,
    /// тап — спрятать/показать панель, «сохранить» — в выбранную папку.
    /// </summary>
    public sealed partial class PhotoPage : Page
    {
        public ObservableCollection<PhotoSlide> Slides { get; } = new ObservableCollection<PhotoSlide>();
        private bool _alive;

        public PhotoPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            _alive = true;
            var args = e.Parameter as PhotoViewerArgs;
            if (args == null && e.Parameter is int single) args = new PhotoViewerArgs(new List<int> { single }, 0);
            if (args == null) return;
            foreach (var id in args.FileIds) Slides.Add(new PhotoSlide(id));
            if (Slides.Count > 0) Flip.SelectedIndex = Math.Max(0, Math.Min(args.Index, Slides.Count - 1));
            UpdateCounter();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _alive = false;
            Flip.ItemsSource = null; // отпускаем декодированные картинки сразу
            Slides.Clear();
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) { UpdateCounter(); }

        private void UpdateCounter()
        {
            Counter.Text = Slides.Count > 1 ? (Math.Max(0, Flip.SelectedIndex) + 1) + " из " + Slides.Count : "Фото";
        }

        private void OnTapped(object sender, TappedRoutedEventArgs e)
        {
            Bar.Visibility = Bar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Двойной тап: приблизить в точку касания, повторный — вернуть целиком.</summary>
        private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            var sv = sender as ScrollViewer;
            if (sv == null) return;
            e.Handled = true;
            if (sv.ZoomFactor > 1.05f)
            {
                sv.ChangeView(0, 0, 1f);
                return;
            }
            const float zoom = 2.5f;
            var p = e.GetPosition(sv);
            double x = Math.Max(0, p.X * zoom - sv.ViewportWidth / 2);
            double y = Math.Max(0, p.Y * zoom - sv.ViewportHeight / 2);
            sv.ChangeView(x, y, zoom);
        }

        private async void OnSave(object sender, RoutedEventArgs e)
        {
            int i = Flip.SelectedIndex;
            if (i < 0 || i >= Slides.Count) return;
            try
            {
                // файл уже скачан для показа — TDLib отдаст путь сразу
                var r = await TdService.Current.SendAsync(new TdApi.DownloadFile
                {
                    FileId = Slides[i].FileId, Priority = 32, Offset = 0, Limit = 0, Synchronous = true
                }) as TdApi.File;
                if (!_alive || r?.Local == null || !r.Local.IsDownloadingCompleted) return;
                var src = await StorageFile.GetFileFromPathAsync(r.Local.Path);

                var picker = new Windows.Storage.Pickers.FileSavePicker
                {
                    SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
                    SuggestedFileName = "photo_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
                };
                var ext = string.IsNullOrEmpty(src.FileType) ? ".jpg" : src.FileType;
                picker.FileTypeChoices.Add("Изображение", new List<string> { ext });
                var dst = await picker.PickSaveFileAsync();
                if (dst == null) return;
                await src.CopyAndReplaceAsync(dst);
            }
            catch (Exception ex)
            {
                await new Windows.UI.Popups.MessageDialog("Не удалось сохранить: " + ex.Message).ShowAsync();
            }
        }
    }
}
