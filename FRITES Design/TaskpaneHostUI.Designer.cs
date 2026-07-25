using System.Windows.Forms;

namespace FRITES_Design
{
    partial class TaskpaneHostUI
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();

            base.Dispose(disposing);
        }

        #region Component Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(TaskpaneHostUI));
            this.sqLiteCommand1 = new System.Data.SQLite.SQLiteCommand();
            this.imageList1 = new System.Windows.Forms.ImageList(this.components);
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.updatePartListButton = new System.Windows.Forms.ToolStripButton();
            this.refreshButton = new System.Windows.Forms.ToolStripButton();
            this.downloadButton = new System.Windows.Forms.ToolStripButton();
            this.deleteButton = new System.Windows.Forms.ToolStripButton();
            this.openInBrowserButton = new System.Windows.Forms.ToolStripButton();
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.searchTextBox = new System.Windows.Forms.TextBox();
            this.treeListView1 = new BrightIdeasSoftware.TreeListView();
            this.PartName = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.SKU = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.downloaded = ((BrightIdeasSoftware.OLVColumn)(new BrightIdeasSoftware.OLVColumn()));
            this.toolStrip1.SuspendLayout();
            this.tableLayoutPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.treeListView1)).BeginInit();
            this.SuspendLayout();
            // 
            // sqLiteCommand1
            // 
            this.sqLiteCommand1.CommandText = null;
            // 
            // imageList1
            // 
            this.imageList1.ColorDepth = System.Windows.Forms.ColorDepth.Depth32Bit;
            this.imageList1.ImageSize = new System.Drawing.Size(48, 48);
            this.imageList1.TransparentColor = System.Drawing.Color.Transparent;
            // 
            // toolStrip1
            // 
            this.toolStrip1.AutoSize = false;
            this.toolStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.updatePartListButton,
            this.refreshButton,
            this.downloadButton,
            this.deleteButton,
            this.openInBrowserButton});
            this.toolStrip1.Location = new System.Drawing.Point(0, 0);
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.Size = new System.Drawing.Size(907, 78);
            this.toolStrip1.TabIndex = 0;
            // 
            // updatePartListButton
            // 
            this.updatePartListButton.AutoSize = false;
            this.updatePartListButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.updatePartListButton.Image = global::FRITES_Design.Properties.Resources.Downloadwebsettings_10413;
            this.updatePartListButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.updatePartListButton.Name = "updatePartListButton";
            this.updatePartListButton.Size = new System.Drawing.Size(60, 60);
            this.updatePartListButton.Text = "Update part list";
            this.updatePartListButton.Click += new System.EventHandler(this.updateButton_Click);
            // 
            // refreshButton
            // 
            this.refreshButton.AutoSize = false;
            this.refreshButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.refreshButton.Image = ((System.Drawing.Image)(resources.GetObject("refreshButton.Image")));
            this.refreshButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.refreshButton.Name = "refreshButton";
            this.refreshButton.Size = new System.Drawing.Size(60, 60);
            this.refreshButton.Text = "refresh";
            this.refreshButton.Click += new System.EventHandler(this.refreshButton_Click);
            // 
            // downloadButton
            // 
            this.downloadButton.AutoSize = false;
            this.downloadButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.downloadButton.Image = ((System.Drawing.Image)(resources.GetObject("downloadButton.Image")));
            this.downloadButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.downloadButton.Name = "downloadButton";
            this.downloadButton.Size = new System.Drawing.Size(60, 60);
            this.downloadButton.Text = "Download";
            this.downloadButton.Click += new System.EventHandler(this.downloadButton_ClickAsync);
            // 
            // deleteButton
            // 
            this.deleteButton.AutoSize = false;
            this.deleteButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.deleteButton.Image = ((System.Drawing.Image)(resources.GetObject("deleteButton.Image")));
            this.deleteButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.deleteButton.Name = "deleteButton";
            this.deleteButton.Size = new System.Drawing.Size(60, 60);
            this.deleteButton.Text = "Delete";
            this.deleteButton.Click += new System.EventHandler(this.deleteButton_Click);
            // 
            // openInBrowserButton
            // 
            this.openInBrowserButton.AutoSize = false;
            this.openInBrowserButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.openInBrowserButton.Image = ((System.Drawing.Image)(resources.GetObject("openInBrowserButton.Image")));
            this.openInBrowserButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.openInBrowserButton.Name = "openInBrowserButton";
            this.openInBrowserButton.Size = new System.Drawing.Size(60, 60);
            this.openInBrowserButton.Text = "Open in browser";
            this.openInBrowserButton.Click += new System.EventHandler(this.openInBrowserButton_Click);
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.BackColor = System.Drawing.SystemColors.Window;
            this.tableLayoutPanel1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.tableLayoutPanel1.ColumnCount = 1;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.Controls.Add(this.searchTextBox, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.treeListView1, 0, 1);
            this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanel1.Location = new System.Drawing.Point(0, 78);
            this.tableLayoutPanel1.Margin = new System.Windows.Forms.Padding(0);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 2;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 49F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 23F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 23F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(907, 1897);
            this.tableLayoutPanel1.TabIndex = 0;
            // 
            // searchTextBox
            // 
            this.searchTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.searchTextBox.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.searchTextBox.Location = new System.Drawing.Point(4, 3);
            this.searchTextBox.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.searchTextBox.Name = "searchTextBox";
            this.searchTextBox.Size = new System.Drawing.Size(899, 39);
            this.searchTextBox.TabIndex = 0;
            this.searchTextBox.TextChanged += new System.EventHandler(this.searchTextBox_TextChanged);
            // 
            // treeListView1
            // 
            this.treeListView1.AllColumns.Add(this.PartName);
            this.treeListView1.AllColumns.Add(this.SKU);
            this.treeListView1.AllColumns.Add(this.downloaded);
            this.treeListView1.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.treeListView1.CellEditUseWholeCell = false;
            this.treeListView1.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.PartName,
            this.SKU,
            this.downloaded});
            this.treeListView1.Cursor = System.Windows.Forms.Cursors.Default;
            this.treeListView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.treeListView1.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.treeListView1.HideSelection = false;
            this.treeListView1.Location = new System.Drawing.Point(3, 52);
            this.treeListView1.Name = "treeListView1";
            this.treeListView1.ShowGroups = false;
            this.treeListView1.Size = new System.Drawing.Size(901, 1842);
            this.treeListView1.SmallImageList = this.imageList1;
            this.treeListView1.TabIndex = 1;
            this.treeListView1.UseCompatibleStateImageBehavior = false;
            this.treeListView1.View = System.Windows.Forms.View.Details;
            this.treeListView1.VirtualMode = true;
            this.treeListView1.ItemDrag += new System.Windows.Forms.ItemDragEventHandler(this.treeListView1_ItemDrag);
            this.treeListView1.SelectedIndexChanged += new System.EventHandler(this.treeListView1_SelectedIndexChanged);
            this.treeListView1.MouseLeave += new System.EventHandler(this.treeListView1_MouseLeave);
            this.treeListView1.MouseMove += new System.Windows.Forms.MouseEventHandler(this.treeListView1_MouseMove);
            // 
            // PartName
            // 
            this.PartName.Text = "Part Name";
            this.PartName.Width = 540;
            // 
            // SKU
            // 
            this.SKU.Text = "SKU";
            this.SKU.Width = 276;
            // 
            // downloaded
            // 
            this.downloaded.Text = "Downloaded";
            this.downloaded.Width = 90;
            // 
            // TaskpaneHostUI
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(14F, 29F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.tableLayoutPanel1);
            this.Controls.Add(this.toolStrip1);
            this.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.Name = "TaskpaneHostUI";
            this.Size = new System.Drawing.Size(907, 1975);
            this.Load += new System.EventHandler(this.TaskpaneHostUI_Load);
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this.tableLayoutPanel1.ResumeLayout(false);
            this.tableLayoutPanel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.treeListView1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Data.SQLite.SQLiteCommand sqLiteCommand1;
        private System.Windows.Forms.ImageList imageList1;
        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.ToolStripButton updatePartListButton;

        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.TextBox searchTextBox;
        private ToolStripButton refreshButton;
        private BrightIdeasSoftware.TreeListView treeListView1;
        private BrightIdeasSoftware.OLVColumn SKU;
        private BrightIdeasSoftware.OLVColumn PartName;
        private BrightIdeasSoftware.OLVColumn downloaded;
        private ToolStripButton downloadButton;
        private ToolStripButton deleteButton;
        private ToolStripButton openInBrowserButton;
    }
}