using System.Windows;

namespace AnotherClassLibrary
{
    public class WpfWindowStarter
    {
        public static void ShowWindowWithControl()
        {
            Window window = new Window();
            window.Content = new ADummyUserControl();
            window.Show();
        }
    }
}
