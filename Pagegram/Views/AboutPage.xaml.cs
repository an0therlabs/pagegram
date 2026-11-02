using Windows.ApplicationModel;
using Windows.UI.Xaml.Controls;

namespace Pagegram.Views
{
    public sealed partial class AboutPage : Page
    {
        public AboutPage()
        {
            InitializeComponent();
            var v = Package.Current.Id.Version;
            Version.Text = string.Format("версия {0}.{1}.{2}", v.Major, v.Minor, v.Build);
        }
    }
}
