using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace WPFPackUrisInClrStringsApplicationCore
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            ApplicationThemeManager.Changed += ApplicationThemeManager_Changed;

            ApplicationThemeManager.ApplySystemTheme(true);
        }

        private void ApplicationThemeManager_Changed(ApplicationTheme currentApplicationTheme, System.Windows.Media.Color systemAccent)
        {
            var frameworkElement = this;
            ApplicationThemeManager.Apply(frameworkElement);
            if (frameworkElement is Window window)
            {
                if (window != UiApplication.Current.MainWindow)
                {
                    WindowBackgroundManager.UpdateBackground(
                        window,
                        currentApplicationTheme,
                        WindowBackdropType.None
                    );
                }
            }
        }

        private async void FluentWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await Task.Delay(300);
            Close();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            ChangeTheme();
        }

        public static void ChangeTheme()
        {
            var currentApplicationTheme = ApplicationThemeManager.GetAppTheme();
            var applicationTheme =
                currentApplicationTheme == ApplicationTheme.Light
                    ? ApplicationTheme.Dark
                    : ApplicationTheme.Light;

            ApplicationThemeManager.Apply(applicationTheme, backgroundEffect: WindowBackdropType.Auto, updateAccent: false);
        }
    }
}