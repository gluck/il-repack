using System.Windows;
using System.Windows.Controls;

namespace ClassLibrary
{
    public class TemplatedDummyUserControl : Control
    {
        public bool TemplateApplied { get; private set; }

        static TemplatedDummyUserControl()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(TemplatedDummyUserControl),
                new FrameworkPropertyMetadata(typeof(TemplatedDummyUserControl)));
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            TemplateApplied = true;
        }
    }
}
