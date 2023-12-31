using ClassLibrary;
using System;
using System.Windows.Forms;

namespace WindowsFormsTestNetCoreApp
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();

            this.Load += Form_OnLoad;

            button1.Image = Properties.Resources.checkbox_checked;
            new DummyConverter();
        }

        private void Form_OnLoad(object sender, EventArgs e)
        {
            var timer = new Timer() { Enabled = true, Interval = 300 };
            timer.Tick += delegate
            {
                this.Close();
            };
        }

        private void button1_Click(object sender, EventArgs e)
        {
        }
    }
}
