using System;
using Pagegram.Services;
using Windows.Graphics.Display;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using TdApi = Telegram.Td.Api;

namespace Pagegram.Views
{
    /// <summary>
    /// Фото открываются только по тапу и только здесь. Декодируем ровно под ширину экрана,
    /// так что 1280px картинка на 720p экране занимает ~3.5 МБ вместо ~6.5 МБ.
    /// </summary>
    public sealed partial class PhotoPage : Page
    {
        private bool _alive;

        public PhotoPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            _alive = true;
            var fileId = (int)e.Parameter;

            var r = await TdService.Current.SendAsync(new TdApi.DownloadFile
            {
                FileId = fileId,
                Priority = 32,
                Offset = 0,
                Limit = 0,
                Synchronous = true
            });
            if (!_alive) return;

            var file = r as TdApi.File;
            if (file?.Local == null || !file.Local.IsDownloadingCompleted)
            {
                Ring.IsActive = false;
                Error.Text = (r as TdApi.Error)?.Message ?? "Не удалось загрузить фото";
                return;
            }

            try
            {
                var sf = await StorageFile.GetFileFromPathAsync(file.Local.Path);
                using (var stream = await sf.OpenReadAsync())
                {
                    var bmp = new BitmapImage();
                    double scale = DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
                    bmp.DecodePixelType = DecodePixelType.Physical;
                    bmp.DecodePixelWidth = (int)(Window.Current.Bounds.Width * scale);
                    await bmp.SetSourceAsync(stream);
                    if (_alive) Img.Source = bmp;
                }
            }
            catch (Exception ex)
            {
                Error.Text = ex.Message;
            }
            Ring.IsActive = false;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _alive = false;
            Img.Source = null; // сразу отдаём декодированный битмап
        }
    }
}
