using Windows.ApplicationModel;
using Windows.UI.Xaml.Controls;

namespace Pagegram.Views
{
    public sealed partial class AboutPage : Page
    {
        private static string Check(System.Func<string> probe)
        {
            try { probe(); return "работает"; }
            catch (System.Exception ex) { return "ошибка " + ex.GetType().Name + " 0x" + ex.HResult.ToString("X8") + ": " + ex.Message; }
        }

        public AboutPage()
        {
            InitializeComponent();
            var v = Package.Current.Id.Version;
            Version.Text = string.Format("версия {0}.{1}.{2}", v.Major, v.Minor, v.Build);
            NativeStatus.Text = "Стикеры (WebP): " + Check(() => PagegramNative.WebP.IsWebP(new byte[12]).ToString()) +
                                "\nГолосовые (Opus): " + Check(() => PagegramNative.Opus.DecodeOggToPcm(new byte[64]).Length.ToString());
        }
    }
}
