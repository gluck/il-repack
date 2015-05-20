using System.Windows;

namespace LibraryClassUsageInXAML
{
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void MainWindowLoaded(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
