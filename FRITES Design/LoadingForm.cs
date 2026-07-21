using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FRITES_Design
{
    public partial class LoadingForm : Form
    {
        public LoadingForm()
        {
            InitializeComponent();

            progressBar1.Minimum = 0;
            progressBar1.Maximum = 100;
        }

        public void SetProgress(int value)
        {
            if (value < 0)
            {
                value = 0;
            } 
            else if (value > 100)
            {
                value = 100;
            }

            if (InvokeRequired)
            {
                Invoke(new MethodInvoker(delegate
                {
                    progressBar1.Value = value;
                }));            
            }
            else
            {
                progressBar1.Value = value;
            }
        }

        public void SetLabel(string label)
        {
            label1.Text = label;
        }

        private void LoadingForm_Load(object sender, EventArgs e)
        {

        }
    }
}
