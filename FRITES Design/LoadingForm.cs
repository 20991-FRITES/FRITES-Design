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

        private void LoadingForm_Load(object sender, EventArgs e)
        {

        }
    }
}
