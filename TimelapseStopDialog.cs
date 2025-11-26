using System;
using System.Drawing;
using System.Windows.Forms;

namespace MultiCameraRecorder
{
    public class TimelapseStopDialog : Form
    {
        private NumericUpDown numOutputFps;
        private CheckBox chkKeepImages;
        private ComboBox cmbVideoFormat;
        private Label lblInfo;
        private Label lblOutputFps;
        private Label lblVideoFormat;
        private Button btnOK;
        private Button btnCancel;

        public double OutputFps { get; private set; }
        public bool KeepImages { get; private set; }
        public string VideoFormat { get; private set; }

        public TimelapseStopDialog(int totalFrames)
        {
            InitializeComponent(totalFrames);
        }

        private void InitializeComponent(int totalFrames)
        {
            this.Text = "Timelapse Recording Complete";
            this.Size = new Size(450, 320);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            int y = 20;

            // Info label
            lblInfo = new Label
            {
                Text = $"Timelapse recording complete!\n\n" +
                       $"Total frames captured: {totalFrames:N0}\n\n" +
                       $"Configure output video settings:",
                Location = new Point(20, y),
                Size = new Size(400, 80),
                Font = new Font("Arial", 9)
            };
            this.Controls.Add(lblInfo);
            y += 90;

            // Output FPS
            lblOutputFps = new Label
            {
                Text = "Output Frame Rate (fps):",
                Location = new Point(20, y + 2),
                Size = new Size(180, 20),
                Font = new Font("Arial", 9)
            };
            this.Controls.Add(lblOutputFps);

            numOutputFps = new NumericUpDown
            {
                Location = new Point(210, y),
                Size = new Size(100, 25),
                Minimum = 1,
                Maximum = 120,
                DecimalPlaces = 1,
                Value = 30,
                Font = new Font("Arial", 9)
            };
            this.Controls.Add(numOutputFps);

            Label lblFpsHelp = new Label
            {
                Text = "(Higher = faster playback)",
                Location = new Point(320, y + 2),
                Size = new Size(150, 20),
                Font = new Font("Arial", 8),
                ForeColor = Color.Gray
            };
            this.Controls.Add(lblFpsHelp);
            y += 35;

            // Video format
            lblVideoFormat = new Label
            {
                Text = "Output Video Format:",
                Location = new Point(20, y + 2),
                Size = new Size(180, 20),
                Font = new Font("Arial", 9)
            };
            this.Controls.Add(lblVideoFormat);

            cmbVideoFormat = new ComboBox
            {
                Location = new Point(210, y),
                Size = new Size(100, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Arial", 9)
            };
            cmbVideoFormat.Items.AddRange(new object[] { "AVI", "MP4" });
            cmbVideoFormat.SelectedIndex = 0;
            this.Controls.Add(cmbVideoFormat);

            Label lblFormatHelp = new Label
            {
                Text = "(AVI = raw, MP4 = compressed)",
                Location = new Point(320, y + 2),
                Size = new Size(180, 20),
                Font = new Font("Arial", 8),
                ForeColor = Color.Gray
            };
            this.Controls.Add(lblFormatHelp);
            y += 35;

            // Keep images checkbox
            chkKeepImages = new CheckBox
            {
                Text = "Keep source images after compilation",
                Location = new Point(20, y),
                Size = new Size(300, 25),
                Checked = false,
                Font = new Font("Arial", 9)
            };
            this.Controls.Add(chkKeepImages);
            y += 35;

            // Separator
            Panel separator = new Panel
            {
                Location = new Point(20, y),
                Size = new Size(400, 1),
                BackColor = Color.LightGray
            };
            this.Controls.Add(separator);
            y += 15;

            // Preview calculation
            Label lblPreview = new Label
            {
                Text = "",
                Location = new Point(20, y),
                Size = new Size(400, 40),
                Font = new Font("Arial", 9),
                ForeColor = Color.Blue
            };
            this.Controls.Add(lblPreview);

            // Update preview when FPS changes
            numOutputFps.ValueChanged += (s, e) =>
            {
                double duration = totalFrames / (double)numOutputFps.Value;
                lblPreview.Text = $"Output video duration: {duration:F1} seconds\n" +
                                $"({duration / 60:F1} minutes)";
            };

            // Trigger initial calculation
            numOutputFps.Value = 30;
            y += 50;

            // Buttons
            btnOK = new Button
            {
                Text = "Compile Video",
                Location = new Point(160, y),
                Size = new Size(120, 35),
                Font = new Font("Arial", 9, FontStyle.Bold),
                DialogResult = DialogResult.OK
            };
            btnOK.Click += BtnOK_Click;
            this.Controls.Add(btnOK);

            btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(290, y),
                Size = new Size(120, 35),
                Font = new Font("Arial", 9),
                DialogResult = DialogResult.Cancel
            };
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            OutputFps = (double)numOutputFps.Value;
            KeepImages = chkKeepImages.Checked;
            VideoFormat = cmbVideoFormat.SelectedItem.ToString();

            // Validate
            if (OutputFps < 1)
            {
                MessageBox.Show("Output frame rate must be at least 1 fps.", "Invalid Input",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                this.DialogResult = DialogResult.None;
                return;
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}