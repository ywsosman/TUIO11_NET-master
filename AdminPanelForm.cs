using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TuioDemo.Services;

public partial class AdminPanelForm : Form
{
    private ObjectService _objectService = new ObjectService();
    private Object _selectedObject = null;
    private LevelService _levelService = new LevelService();
    private string _selectedImagePath = null;
    private Panel pnlLeft;
    private TextBox txtSymbolId;
    private PictureBox picPreview;
    private Button btnAddObject;
    private Button btnAddSound;
    private Button btnAddImage;
    private ComboBox cmbLevel;
    private Label lblSymbolId;
    private Label lblLevel;
    private TextBox txtObjectName;
    private Label lblObjectName;
    private Label lblUploadAssets;
    private TextBox txtSearch;
    private Button btnDelete;
    private Button btnRefresh;
    private OpenFileDialog openFileDialogImage;
    private OpenFileDialog openFileDialogSound;
    private string _selectedSoundPath = null;

    //private TextBox txtObjectName;
    //private TextBox txtSearch;
    //private TextBox txtSymbolId;
    //private ComboBox cmbLevel;
    //private Button btnAddImage;
    //private Button btnAddSound;
    //private Button btnAddObject;
    //private Button btnDeleteSelected;
    //private Button btnRefresh;
    //private ListView listViewObjects;
    //private Label lblObjectName;
    //private Label lblUploadAssets;
    //private Label lblLevel;
    //private Label lblSymbolId;
    //private PictureBox picPreview;
    //private OpenFileDialog openFileDialogImage;
    //private Label label2;
    //private Label label1;
    //private Panel pnlLeft;
    //private TextBox textBox1;
    //private OpenFileDialog openFileDialogSound;

    public AdminPanelForm()
    {
        InitializeComponent();
        LoadLevels();
        RefreshObjectList();
    }



    private static string AssetsRoot => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Objects");

    private static string MakeSafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private static string CopyToAssets(string sourcePath, string subFolder, string baseName)
    {
        var ext = Path.GetExtension(sourcePath);
        var safe = MakeSafeFileName(baseName);
        var dir = Path.Combine(AssetsRoot, subFolder);
        Directory.CreateDirectory(dir);

        var fileName = safe + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ext;
        var destPath = Path.Combine(dir, fileName);

        File.Copy(sourcePath, destPath, true);

        return Path.Combine("Assets", "Objects", subFolder, fileName);
    }

    private static string ToAbsolutePath(string maybeRelative)
    {
        if (string.IsNullOrWhiteSpace(maybeRelative)) return null;
        if (Path.IsPathRooted(maybeRelative)) return maybeRelative;
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, maybeRelative);
    }




    private void RefreshObjectList(string filter = null)
    {
        dgvObjects.Rows.Clear();

        var response = _objectService.GetObjects(filter);
        if (!response.Success || response.Data == null) return;

        foreach (var obj in response.Data)
        {
            var imgAbs = ToAbsolutePath(obj.ImagePath);
            var sndAbs = ToAbsolutePath(obj.SoundPath);

            Image previewImage = null;
            if (!string.IsNullOrWhiteSpace(imgAbs) && File.Exists(imgAbs))
            {
                try
                {
                    using (var original = Image.FromFile(imgAbs))
                    {
                        previewImage = ResizeImage(original, 64, 64);
                    }
                }
                catch { previewImage = null; }
            }

            Image soundIcon = null;
            if (!string.IsNullOrWhiteSpace(sndAbs) && File.Exists(sndAbs))
            {
                soundIcon = GetSoundIcon();
            }

            int rowIndex = dgvObjects.Rows.Add();
            var row = dgvObjects.Rows[rowIndex];

            row.Cells["ID"].Value = obj.Id;
            row.Cells["ObjectName"].Value = obj.ObjectName;
            row.Cells["SymbolId"].Value = obj.SymbolId;
            row.Cells["LevelId"].Value = obj.LevelId;
            row.Cells["Sound"].Value = soundIcon;
            row.Cells["Preview"].Value = previewImage;
            row.Cells["CreatedAt"].Value = obj.CreatedAt.ToString("g");

            row.Tag = obj;
        }
    }

    private Image ResizeImage(Image image, int width, int height)
    {
        var resized = new Bitmap(width, height);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, 0, 0, width, height);
        }
        return resized;
    }

    private Image GetSoundIcon()
    {
        var bitmap = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.White);
            using (var brush = new SolidBrush(Color.FromArgb(220, 220, 220)))
            {
                g.FillEllipse(brush, 8, 12, 48, 40);
            }
            g.FillEllipse(Brushes.Gray, 18, 22, 20, 20);
            g.FillPolygon(Brushes.Black, new[]
            {
            new Point(30, 22),
            new Point(45, 32),
            new Point(30, 42)
        });
        }
        return bitmap;
    }


    private void lblUploadAssets_Click(object sender, EventArgs e)
    {

    }


    ///Textboxes
    private void txtObjectName_TextChanged(object sender, EventArgs e)
    {

    }

    private void cmbLevel_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (cmbLevel.SelectedItem != null && cmbLevel.SelectedItem.ToString() == "+ New Level")
        {
            // Show input dialog
            var form = new Form
            {
                Text = "Create New Level",
                Width = 300,
                Height = 150,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var label = new Label { Text = "Level Name:", Left = 20, Top = 20, Width = 250 };
            var textBox = new TextBox { Left = 20, Top = 50, Width = 250 };
            var okButton = new Button { Text = "OK", Left = 120, Top = 80, Width = 70, DialogResult = DialogResult.OK };
            var cancelButton = new Button { Text = "Cancel", Left = 200, Top = 80, Width = 70, DialogResult = DialogResult.Cancel };

            form.Controls.Add(label);
            form.Controls.Add(textBox);
            form.Controls.Add(okButton);
            form.Controls.Add(cancelButton);
            form.AcceptButton = okButton;
            form.CancelButton = cancelButton;

            if (form.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                var response = _levelService.CreateLevel(textBox.Text);
                if (response.Success)
                {
                    LoadLevels();
                    cmbLevel.SelectedItem = response.Data.Id;
                    MessageBox.Show($"Level created: {textBox.Text}", "Success");
                }
                else
                {
                    MessageBox.Show(string.Join("\n", response.Errors), "Error");
                    cmbLevel.SelectedIndex = 0;
                }
            }
            else
            {
                cmbLevel.SelectedIndex = 0;
            }
        }

    }

    private void txtSymbolId_TextChanged(object sender, EventArgs e)
    {

    }

    private void txtSearch_TextChanged(object sender, EventArgs e)
    {
        RefreshObjectList(txtSearch.Text);
    }


    ///Buttons

    private void btnAddImage_Click(object sender, EventArgs e)
    {
        if (openFileDialogImage.ShowDialog() == DialogResult.OK)
        {
            var chosen = openFileDialogImage.FileName;
            var baseName = string.IsNullOrWhiteSpace(txtObjectName.Text) ? "object_image" : txtObjectName.Text;

            _selectedImagePath = CopyToAssets(chosen, "Images", baseName);

            var abs = ToAbsolutePath(_selectedImagePath);
            try { picPreview.Image = Image.FromFile(abs); }
            catch { }
        }
    }

    private void btnAddSound_Click(object sender, EventArgs e)
    {
        if (openFileDialogSound.ShowDialog() == DialogResult.OK)
        {
            var chosen = openFileDialogSound.FileName;
            var baseName = string.IsNullOrWhiteSpace(txtObjectName.Text) ? "object_sound" : txtObjectName.Text;

            _selectedSoundPath = CopyToAssets(chosen, "Sounds", baseName);
        }
    }

    private void btnAddObject_Click(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtObjectName.Text))
        {
            MessageBox.Show("Object name is required.", "Input Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (cmbLevel.SelectedItem == null)
        {
            MessageBox.Show("Please select a level.", "Input Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        int levelId;
        if (!int.TryParse(cmbLevel.SelectedItem.ToString(), out levelId))
        {
            MessageBox.Show("Invalid level value.", "Input Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        int symbolId;
        if (!int.TryParse(txtSymbolId.Text, out symbolId))
        {
            MessageBox.Show("Symbol ID must be a number.", "Input Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var response = _objectService.CreateObject(
            txtObjectName.Text.Trim(),
            levelId,
            symbolId,
            _selectedImagePath,
            _selectedSoundPath
        );

        if (response.Success)
        {
            MessageBox.Show(response.Message, "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

            txtObjectName.Clear();
            txtSymbolId.Clear();
            picPreview.Image = null;
            _selectedImagePath = null;
            _selectedSoundPath = null;

            RefreshObjectList();
        }
        else
        {
            MessageBox.Show(string.Join("\n", response.Errors), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void btnDelete_Click(object sender, EventArgs e)
    {
        if (dgvObjects.SelectedRows.Count == 0)
        {
            MessageBox.Show("Please select an object to delete.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }


        var selectedItem = dgvObjects.SelectedRows[0];
        var obj = (Object)selectedItem.Tag;
        var result = MessageBox.Show($"Delete {obj.ObjectName} from Level {obj.LevelId}?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            var response = _objectService.DeleteObject(obj.Id);
            if (response.Success)
            {
                MessageBox.Show(response.Message, "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshObjectList();
            }
            else
                MessageBox.Show(string.Join("\n", response.Errors), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void btnRefresh_Click(object sender, EventArgs e)
    {
        txtSearch.Clear();
        RefreshObjectList();
    }




    private void LoadLevels()
    {
        cmbLevel.Items.Clear();
        var response = _levelService.GetAllLevels();

        if (response.Success && response.Data != null)
        {
            foreach (var level in response.Data)
            {
                cmbLevel.Items.Add(level.Id);
            }
        }

        cmbLevel.Items.Add("+ New Level");

        if (cmbLevel.Items.Count > 0)
            cmbLevel.SelectedIndex = 0;
    }

    private void dgvObjects_CellContentClick(object sender, DataGridViewCellEventArgs e)
    {

        if (dgvObjects.SelectedRows.Count > 0)
        {
            var obj = (Object)dgvObjects.SelectedRows[0].Tag;
            txtObjectName.Text = obj.ObjectName;
            cmbLevel.SelectedItem = obj.LevelId;
            txtSymbolId.Text = obj.SymbolId.ToString();
        }
    }


}