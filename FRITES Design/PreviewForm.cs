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
    public partial class PreviewForm : Form
    {
        public PreviewForm()
        {
            InitializeComponent();
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
        }

        public void SetData(Image img, string name, string sku)
        {
            pictureBox1.Image = img;
            pictureBox1.Size = new Size(200, 200);

            partName.Text = name;
            partSku.Text = sku;
        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void partSku_Click(object sender, EventArgs e)
        {

        }
    }
}
