partial class AdminPanelForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }


    private void InitializeComponent()
    {
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AdminPanelForm));
            this.pnlLeft = new System.Windows.Forms.Panel();
            this.txtSymbolId = new System.Windows.Forms.TextBox();
            this.picPreview = new System.Windows.Forms.PictureBox();
            this.btnAddObject = new System.Windows.Forms.Button();
            this.btnAddSound = new System.Windows.Forms.Button();
            this.btnAddImage = new System.Windows.Forms.Button();
            this.cmbLevel = new System.Windows.Forms.ComboBox();
            this.lblSymbolId = new System.Windows.Forms.Label();
            this.lblLevel = new System.Windows.Forms.Label();
            this.txtObjectName = new System.Windows.Forms.TextBox();
            this.lblObjectName = new System.Windows.Forms.Label();
            this.lblUploadAssets = new System.Windows.Forms.Label();
            this.txtSearch = new System.Windows.Forms.TextBox();
            this.btnDelete = new System.Windows.Forms.Button();
            this.btnRefresh = new System.Windows.Forms.Button();
            this.openFileDialogImage = new System.Windows.Forms.OpenFileDialog();
            this.openFileDialogSound = new System.Windows.Forms.OpenFileDialog();
            this.dgvObjects = new System.Windows.Forms.DataGridView();
            this.ID = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ObjectName = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.SymbolID = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.LevelId = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Sound = new System.Windows.Forms.DataGridViewImageColumn();
            this.Preview = new System.Windows.Forms.DataGridViewImageColumn();
            this.CreatedAt = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.pnlLeft.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.picPreview)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvObjects)).BeginInit();
            this.SuspendLayout();
            // 
            // pnlLeft
            // 
            this.pnlLeft.BackColor = System.Drawing.Color.White;
            this.pnlLeft.Controls.Add(this.txtSymbolId);
            this.pnlLeft.Controls.Add(this.picPreview);
            this.pnlLeft.Controls.Add(this.btnAddObject);
            this.pnlLeft.Controls.Add(this.btnAddSound);
            this.pnlLeft.Controls.Add(this.btnAddImage);
            this.pnlLeft.Controls.Add(this.cmbLevel);
            this.pnlLeft.Controls.Add(this.lblSymbolId);
            this.pnlLeft.Controls.Add(this.lblLevel);
            this.pnlLeft.Controls.Add(this.txtObjectName);
            this.pnlLeft.Controls.Add(this.lblObjectName);
            this.pnlLeft.Controls.Add(this.lblUploadAssets);
            this.pnlLeft.Location = new System.Drawing.Point(20, 20);
            this.pnlLeft.Name = "pnlLeft";
            this.pnlLeft.Size = new System.Drawing.Size(260, 540);
            this.pnlLeft.TabIndex = 0;
            // 
            // txtSymbolId
            // 
            this.txtSymbolId.Location = new System.Drawing.Point(150, 175);
            this.txtSymbolId.Name = "txtSymbolId";
            this.txtSymbolId.Size = new System.Drawing.Size(90, 20);
            this.txtSymbolId.TabIndex = 11;
            this.txtSymbolId.TextChanged += new System.EventHandler(this.txtSymbolId_TextChanged);
            // 
            // picPreview
            // 
            this.picPreview.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.picPreview.Location = new System.Drawing.Point(72, 401);
            this.picPreview.Name = "picPreview";
            this.picPreview.Size = new System.Drawing.Size(100, 100);
            this.picPreview.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.picPreview.TabIndex = 10;
            this.picPreview.TabStop = false;
            // 
            // btnAddObject
            // 
            this.btnAddObject.Font = new System.Drawing.Font("Arial", 12F, System.Drawing.FontStyle.Bold);
            this.btnAddObject.Location = new System.Drawing.Point(20, 320);
            this.btnAddObject.Name = "btnAddObject";
            this.btnAddObject.Size = new System.Drawing.Size(220, 50);
            this.btnAddObject.TabIndex = 9;
            this.btnAddObject.Text = "Add Object";
            this.btnAddObject.UseVisualStyleBackColor = true;
            this.btnAddObject.Click += new System.EventHandler(this.btnAddObject_Click);
            // 
            // btnAddSound
            // 
            this.btnAddSound.Font = new System.Drawing.Font("Arial", 12F, System.Drawing.FontStyle.Bold);
            this.btnAddSound.Location = new System.Drawing.Point(20, 270);
            this.btnAddSound.Name = "btnAddSound";
            this.btnAddSound.Size = new System.Drawing.Size(220, 40);
            this.btnAddSound.TabIndex = 8;
            this.btnAddSound.Text = "Add Sound";
            this.btnAddSound.UseVisualStyleBackColor = true;
            this.btnAddSound.Click += new System.EventHandler(this.btnAddSound_Click);
            // 
            // btnAddImage
            // 
            this.btnAddImage.Font = new System.Drawing.Font("Arial", 12F, System.Drawing.FontStyle.Bold);
            this.btnAddImage.Location = new System.Drawing.Point(20, 220);
            this.btnAddImage.Name = "btnAddImage";
            this.btnAddImage.Size = new System.Drawing.Size(220, 40);
            this.btnAddImage.TabIndex = 7;
            this.btnAddImage.Text = "Add Image";
            this.btnAddImage.UseVisualStyleBackColor = true;
            this.btnAddImage.Click += new System.EventHandler(this.btnAddImage_Click);
            // 
            // cmbLevel
            // 
            this.cmbLevel.FormattingEnabled = true;
            this.cmbLevel.Location = new System.Drawing.Point(20, 175);
            this.cmbLevel.Name = "cmbLevel";
            this.cmbLevel.Size = new System.Drawing.Size(120, 21);
            this.cmbLevel.TabIndex = 5;
            this.cmbLevel.SelectedIndexChanged += new System.EventHandler(this.cmbLevel_SelectedIndexChanged);
            // 
            // lblSymbolId
            // 
            this.lblSymbolId.AutoSize = true;
            this.lblSymbolId.Location = new System.Drawing.Point(150, 150);
            this.lblSymbolId.Name = "lblSymbolId";
            this.lblSymbolId.Size = new System.Drawing.Size(55, 13);
            this.lblSymbolId.TabIndex = 4;
            this.lblSymbolId.Text = "Symbol ID";
            // 
            // lblLevel
            // 
            this.lblLevel.AutoSize = true;
            this.lblLevel.Location = new System.Drawing.Point(20, 150);
            this.lblLevel.Name = "lblLevel";
            this.lblLevel.Size = new System.Drawing.Size(33, 13);
            this.lblLevel.TabIndex = 3;
            this.lblLevel.Text = "Level";
            // 
            // txtObjectName
            // 
            this.txtObjectName.Location = new System.Drawing.Point(20, 105);
            this.txtObjectName.Name = "txtObjectName";
            this.txtObjectName.Size = new System.Drawing.Size(220, 20);
            this.txtObjectName.TabIndex = 2;
            this.txtObjectName.TextChanged += new System.EventHandler(this.txtObjectName_TextChanged);
            // 
            // lblObjectName
            // 
            this.lblObjectName.AutoSize = true;
            this.lblObjectName.Font = new System.Drawing.Font("Arial", 8F);
            this.lblObjectName.Location = new System.Drawing.Point(20, 80);
            this.lblObjectName.Name = "lblObjectName";
            this.lblObjectName.Size = new System.Drawing.Size(68, 14);
            this.lblObjectName.TabIndex = 1;
            this.lblObjectName.Text = "Object Name";
            // 
            // lblUploadAssets
            // 
            this.lblUploadAssets.AutoSize = true;
            this.lblUploadAssets.Font = new System.Drawing.Font("Arial", 12F, System.Drawing.FontStyle.Bold);
            this.lblUploadAssets.Location = new System.Drawing.Point(66, 13);
            this.lblUploadAssets.Name = "lblUploadAssets";
            this.lblUploadAssets.Size = new System.Drawing.Size(123, 57);
            this.lblUploadAssets.TabIndex = 0;
            this.lblUploadAssets.Text = "Upload Assets \r\nand \r\nLevel Setup";
            this.lblUploadAssets.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.lblUploadAssets.Click += new System.EventHandler(this.lblUploadAssets_Click);
            // 
            // txtSearch
            // 
            this.txtSearch.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.txtSearch.Location = new System.Drawing.Point(710, 20);
            this.txtSearch.Name = "txtSearch";
            this.txtSearch.Size = new System.Drawing.Size(260, 20);
            this.txtSearch.TabIndex = 1;
            this.txtSearch.Text = "Search";
            this.txtSearch.TextChanged += new System.EventHandler(this.txtSearch_TextChanged);
            // 
            // btnDelete
            // 
            this.btnDelete.Font = new System.Drawing.Font("Arial", 12F, System.Drawing.FontStyle.Bold);
            this.btnDelete.Location = new System.Drawing.Point(302, 20);
            this.btnDelete.Name = "btnDelete";
            this.btnDelete.Size = new System.Drawing.Size(150, 35);
            this.btnDelete.TabIndex = 2;
            this.btnDelete.Text = "Delete";
            this.btnDelete.UseVisualStyleBackColor = true;
            this.btnDelete.Click += new System.EventHandler(this.btnDelete_Click);
            // 
            // btnRefresh
            // 
            this.btnRefresh.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnRefresh.Font = new System.Drawing.Font("Arial", 12F, System.Drawing.FontStyle.Bold);
            this.btnRefresh.Location = new System.Drawing.Point(510, 20);
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.Size = new System.Drawing.Size(150, 35);
            this.btnRefresh.TabIndex = 3;
            this.btnRefresh.Text = "Refresh";
            this.btnRefresh.UseVisualStyleBackColor = true;
            this.btnRefresh.Click += new System.EventHandler(this.btnRefresh_Click);
            // 
            // openFileDialogImage
            // 
            this.openFileDialogImage.FileName = "openFileDialogImage";
            this.openFileDialogImage.Filter = "\"Image Files|*.png;*.jpg;*.jpeg;*.gif\"";
            this.openFileDialogImage.Title = "\"Select Object Image\"";
            // 
            // openFileDialogSound
            // 
            this.openFileDialogSound.FileName = "openFileDialogSound";
            this.openFileDialogSound.Filter = "\"Audio Files|*.mp3;*.wav\"";
            this.openFileDialogSound.Title = "\"Select Sound File\"";
            // 
            // dgvObjects
            // 
            this.dgvObjects.AllowUserToAddRows = false;
            this.dgvObjects.AllowUserToDeleteRows = false;
            this.dgvObjects.AllowUserToResizeRows = false;
            this.dgvObjects.BackgroundColor = System.Drawing.Color.White;
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle1.BackColor = System.Drawing.Color.DarkGray;
            dataGridViewCellStyle1.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            dataGridViewCellStyle1.ForeColor = System.Drawing.Color.Black;
            dataGridViewCellStyle1.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle1.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle1.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.dgvObjects.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle1;
            this.dgvObjects.ColumnHeadersHeight = 35;
            this.dgvObjects.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ID,
            this.ObjectName,
            this.SymbolID,
            this.LevelId,
            this.Sound,
            this.Preview,
            this.CreatedAt});
            this.dgvObjects.EnableHeadersVisualStyles = false;
            this.dgvObjects.Location = new System.Drawing.Point(302, 75);
            this.dgvObjects.MultiSelect = false;
            this.dgvObjects.Name = "dgvObjects";
            this.dgvObjects.ReadOnly = true;
            this.dgvObjects.RowHeadersVisible = false;
            this.dgvObjects.RowTemplate.Height = 64;
            this.dgvObjects.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvObjects.Size = new System.Drawing.Size(699, 485);
            this.dgvObjects.TabIndex = 4;
            this.dgvObjects.CellContentClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgvObjects_CellContentClick);
            // 
            // ID
            // 
            this.ID.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.ID.HeaderText = "ID";
            this.ID.Name = "ID";
            this.ID.ReadOnly = true;
            // 
            // ObjectName
            // 
            this.ObjectName.HeaderText = "Object Name";
            this.ObjectName.Name = "ObjectName";
            this.ObjectName.ReadOnly = true;
            // 
            // SymbolID
            // 
            this.SymbolID.HeaderText = "Symbol ID";
            this.SymbolID.Name = "SymbolID";
            this.SymbolID.ReadOnly = true;
            // 
            // LevelId
            // 
            this.LevelId.HeaderText = "Level ID";
            this.LevelId.Name = "LevelId";
            this.LevelId.ReadOnly = true;
            // 
            // Sound
            // 
            this.Sound.HeaderText = "Sound";
            this.Sound.ImageLayout = System.Windows.Forms.DataGridViewImageCellLayout.Zoom;
            this.Sound.Name = "Sound";
            this.Sound.ReadOnly = true;
            this.Sound.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.Sound.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Automatic;
            // 
            // Preview
            // 
            this.Preview.HeaderText = "Preview";
            this.Preview.ImageLayout = System.Windows.Forms.DataGridViewImageCellLayout.Zoom;
            this.Preview.Name = "Preview";
            this.Preview.ReadOnly = true;
            this.Preview.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.Preview.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Automatic;
            // 
            // CreatedAt
            // 
            this.CreatedAt.HeaderText = "Created At";
            this.CreatedAt.Name = "CreatedAt";
            this.CreatedAt.ReadOnly = true;
            // 
            // AdminPanelForm
            // 
            this.ClientSize = new System.Drawing.Size(1023, 589);
            this.Controls.Add(this.dgvObjects);
            this.Controls.Add(this.pnlLeft);
            this.Controls.Add(this.btnRefresh);
            this.Controls.Add(this.btnDelete);
            this.Controls.Add(this.txtSearch);
            this.Cursor = System.Windows.Forms.Cursors.Default;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "AdminPanelForm";
            this.pnlLeft.ResumeLayout(false);
            this.pnlLeft.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.picPreview)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvObjects)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

    }

    private System.Windows.Forms.DataGridView dgvObjects;
    private System.Windows.Forms.DataGridViewTextBoxColumn ID;
    private System.Windows.Forms.DataGridViewTextBoxColumn ObjectName;
    private System.Windows.Forms.DataGridViewTextBoxColumn SymbolID;
    private System.Windows.Forms.DataGridViewTextBoxColumn LevelId;
    private System.Windows.Forms.DataGridViewImageColumn Sound;
    private System.Windows.Forms.DataGridViewImageColumn Preview;
    private System.Windows.Forms.DataGridViewTextBoxColumn CreatedAt;
}