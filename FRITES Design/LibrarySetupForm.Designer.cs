namespace FRITES_Design
{
    partial class LibrarySetupForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(LibrarySetupForm));
            this.label1 = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            this.label2 = new System.Windows.Forms.Label();
            this.columnButtonRenderer1 = new BrightIdeasSoftware.ColumnButtonRenderer();
            this.buildCacheButton = new System.Windows.Forms.Button();
            this.skipButton = new System.Windows.Forms.Button();
            this.multithreadingCheckbox = new System.Windows.Forms.CheckBox();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.label1.Location = new System.Drawing.Point(12, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(752, 205);
            this.label1.TabIndex = 0;
            this.label1.Text = resources.GetString("label1.Text");
            // 
            // panel1
            // 
            this.panel1.BackColor = System.Drawing.SystemColors.ControlLightLight;
            this.panel1.Controls.Add(this.label2);
            this.panel1.Location = new System.Drawing.Point(18, 254);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(746, 142);
            this.panel1.TabIndex = 1;
            this.panel1.Paint += new System.Windows.Forms.PaintEventHandler(this.panel1_Paint);
            // 
            // label2
            // 
            this.label2.Font = new System.Drawing.Font("Segoe UI", 10.125F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.Location = new System.Drawing.Point(3, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(740, 142);
            this.label2.TabIndex = 0;
            this.label2.Text = "This process may take 15-20 minutes based on your computer\'s performance";
            this.label2.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.label2.Click += new System.EventHandler(this.label2_Click);
            // 
            // columnButtonRenderer1
            // 
            this.columnButtonRenderer1.ButtonPadding = new System.Drawing.Size(10, 10);
            // 
            // buildCacheButton
            // 
            this.buildCacheButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.buildCacheButton.Location = new System.Drawing.Point(473, 479);
            this.buildCacheButton.Name = "buildCacheButton";
            this.buildCacheButton.Size = new System.Drawing.Size(288, 50);
            this.buildCacheButton.TabIndex = 2;
            this.buildCacheButton.Text = "Build cache (15-20 min)";
            this.buildCacheButton.UseVisualStyleBackColor = true;
            this.buildCacheButton.Click += new System.EventHandler(this.buildCacheButton_Click);
            // 
            // skipButton
            // 
            this.skipButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.skipButton.Location = new System.Drawing.Point(365, 479);
            this.skipButton.Name = "skipButton";
            this.skipButton.Size = new System.Drawing.Size(102, 50);
            this.skipButton.TabIndex = 3;
            this.skipButton.Text = "Skip";
            this.skipButton.UseVisualStyleBackColor = true;
            this.skipButton.Click += new System.EventHandler(this.skipButton_Click);
            // 
            // multithreadingCheckbox
            // 
            this.multithreadingCheckbox.AutoSize = true;
            this.multithreadingCheckbox.Location = new System.Drawing.Point(17, 423);
            this.multithreadingCheckbox.Name = "multithreadingCheckbox";
            this.multithreadingCheckbox.Size = new System.Drawing.Size(504, 34);
            this.multithreadingCheckbox.TabIndex = 4;
            this.multithreadingCheckbox.Text = "Use multithreading (faster, but requires beefy cpu)";
            this.multithreadingCheckbox.UseVisualStyleBackColor = true;
            // 
            // LibrarySetupForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(12F, 30F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(776, 541);
            this.ControlBox = false;
            this.Controls.Add(this.multithreadingCheckbox);
            this.Controls.Add(this.skipButton);
            this.Controls.Add(this.buildCacheButton);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.label1);
            this.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.Name = "LibrarySetupForm";
            this.Text = "FTC Parts Cache";
            this.panel1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Label label2;
        private BrightIdeasSoftware.ColumnButtonRenderer columnButtonRenderer1;
        private System.Windows.Forms.Button buildCacheButton;
        private System.Windows.Forms.Button skipButton;
        private System.Windows.Forms.CheckBox multithreadingCheckbox;
    }
}