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
    public partial class LibrarySetupForm : Form
    {
        public LibrarySetupForm()
        {
            InitializeComponent();
        }

        private void panel1_Paint(object sender, PaintEventArgs e)
        {

        }

        public bool multithreadingEnabled
        {
            get { return multithreadingCheckbox.Checked; }
        }

        private void buildCacheButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            Close();
        }

        private void skipButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void label2_Click(object sender, EventArgs e)
        {

        }
    }
}
