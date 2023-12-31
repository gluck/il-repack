using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace WPFSampleApplicationCore
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.CurrentDispatcher.BeginInvoke((Action)delegate
            {
                Thread.Sleep(100);
                Close();
            });
        }
    }
}
