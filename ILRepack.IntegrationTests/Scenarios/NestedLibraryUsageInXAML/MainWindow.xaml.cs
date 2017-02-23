using System.Windows;
using AnotherClassLibrary;

namespace NestedLibraryUsageInXAML
{
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void MainWindowLoaded(object sender, RoutedEventArgs e)
        {
            await new BclAsyncUsage().GetNumber();
            Close();
        }
    }
}
