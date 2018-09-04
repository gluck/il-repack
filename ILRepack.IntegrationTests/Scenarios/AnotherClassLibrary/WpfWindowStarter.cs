using System.Windows;
using System.Windows.Threading;

namespace AnotherClassLibrary
{
    public class WpfWindowStarter
    {
        public static void ShowWindowWithControl()
        {
            Window window = new Window();
            window.Content = new ADummyUserControl();
            window.Show();
            window.Close();
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        }
    }
}
