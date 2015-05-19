using System.Windows;
using System.Windows.Controls;

namespace WPFSampleApplication
{
    public class MyUserControl : Control
    {
        public bool TemplateApplied { get; private set; }

        static MyUserControl()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(MyUserControl),
                new FrameworkPropertyMetadata(typeof(MyUserControl)));
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            TemplateApplied = true;
        }
    }
}
