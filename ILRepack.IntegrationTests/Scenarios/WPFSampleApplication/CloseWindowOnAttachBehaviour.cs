using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interactivity;

namespace WPFSampleApplication
{
    public class CloseWindowOnAttachBehaviour : Behavior<Window>
    {
        protected override void OnAttached()
        {
            Task.Factory.StartNew(() =>
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));
                Dispatcher.BeginInvoke(new Action(() => AssociatedObject.Close()));
            });
        }
    }
}
