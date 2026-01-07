using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace QueenPix
{
    public class FrameSelectionDialog : Form
    {
        // Return values
        public string SelectedPath { get; private set; } = "";
        public string SelectedFilename { get; private set; } = "";
        public double SelectedFps { get; private set; } = 30.0;
        public OutputFormat SelectedFormat { get; private set; } = OutputFormat.MP4;
        public Dictionary<string, (int startFrame, int endFrame)> FrameRanges { get; private set; }
        
        private List<(string filename, int frames, double duration)> frameStats;
        private List<long> aviFileSizes;
        private List<string> aviFilePaths;
        
        private TextBox txtFps;
        private RadioButton rbMP4, rbAVI, rbBoth;
        private Label lblEstimatedSize;
        private TextBox txtLocation;
        private TextBox txtFilename;
        private Label lblFrameInfo;
        
        public enum OutputFormat
        {
            MP4,
            AVI,
            Both
        }
        
        public FrameSelectionDialog(List<(string filename, int frames, double duration)> stats, 
                                    List<long> fileSizes, 
                                    List<string> filePaths,
                                    string defaultPath, 
                                    string defaultFilename)
        {
            frameStats = stats;
            aviFileSizes = fileSizes;
            aviFilePaths = filePaths;
            FrameRanges = new Dictionary<string, (int, int)>();
            
            // Initialize frame ranges (all frames by default)
            foreach (var stat in frameStats)
            {
                FrameRanges[stat.filename] = (1, stat.frames);
            }
            
            // Calculate average FPS
            double avgFps = stats.Average(s => s.duration > 0 ? s.frames / s.duration : 0);
            
            InitializeDialog(defaultPath, defaultFilename, avgFps);
        }
        
        private void InitializeDialog(string defaultPath, string defaultFilename, double avgFps)
        {
            this.Text = "Save Recording";
            this.Size = new System.Drawing.Size(625, 530);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            
            int y = 20;
            int leftMargin = 20;
            
            // Frame Statistics header
            Label lblStats = new Label
            {
                Text = "Frame Statistics:",
                Location = new System.Drawing.Point(leftMargin, y),
                Size = new System.Drawing.Size(500, 20),
                Font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold)
            };
            this.Controls.Add(lblStats);
            y += 25;
            
            // Frame statistics list with trim info - in scrollable panel
            Panel frameInfoPanel = new Panel
            {
                Location = new System.Drawing.Point(leftMargin + 10, y),
                Size = new System.Drawing.Size(560, 100),
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(frameInfoPanel);

            lblFrameInfo = new Label
            {
                Location = new System.Drawing.Point(5, 5),
                AutoSize = true,  // Let it grow based on content
                MaximumSize = new System.Drawing.Size(530, 0),  // Limit width, unlimited height
                Font = new System.Drawing.Font("Arial", 9)
            };
            frameInfoPanel.Controls.Add(lblFrameInfo);
            UpdateFrameInfoDisplay();
            y += 110;
            
            // Trim Videos button
            Button btnTrimVideos = new Button
            {
                Text = "Trim Videos...",
                Location = new System.Drawing.Point(leftMargin, y),
                Size = new System.Drawing.Size(120, 30),
                Font = new System.Drawing.Font("Arial", 9)
            };
            btnTrimVideos.Click += BtnTrimVideos_Click;
            this.Controls.Add(btnTrimVideos);
            y += 45;
            
            // FPS input
            Label lblFps = new Label
            {
                Text = "Output Frame Rate (fps):",
                Location = new System.Drawing.Point(leftMargin, y),
                Size = new System.Drawing.Size(180, 20)
            };
            this.Controls.Add(lblFps);
            
            txtFps = new TextBox
            {
                Text = avgFps.ToString("F2"),
                Location = new System.Drawing.Point(leftMargin + 185, y - 2),
                Size = new System.Drawing.Size(100, 25)
            };
            this.Controls.Add(txtFps);
            y += 35;
            
            // Output format
            Label lblFormat = new Label
            {
                Text = "Output Format:",
                Location = new System.Drawing.Point(leftMargin, y),
                Size = new System.Drawing.Size(180, 20)
            };
            this.Controls.Add(lblFormat);
            
            rbMP4 = new RadioButton
            {
                Text = "MP4 (compressed)",
                Location = new System.Drawing.Point(leftMargin + 185, y),
                Size = new System.Drawing.Size(150, 20),
                Checked = true
            };
            rbMP4.CheckedChanged += UpdateEstimatedSize;
            this.Controls.Add(rbMP4);
            
            rbAVI = new RadioButton
            {
                Text = "AVI (uncompressed)",
                Location = new System.Drawing.Point(leftMargin + 345, y),
                Size = new System.Drawing.Size(150, 20)
            };
            rbAVI.CheckedChanged += UpdateEstimatedSize;
            this.Controls.Add(rbAVI);
            
            rbBoth = new RadioButton
            {
                Text = "Both",
                Location = new System.Drawing.Point(leftMargin + 345, y + 25),
                Size = new System.Drawing.Size(80, 20)
            };
            rbBoth.CheckedChanged += UpdateEstimatedSize;
            this.Controls.Add(rbBoth);
            y += 55;
            
            // Estimated size
            lblEstimatedSize = new Label
            {
                Text = "Calculating...",
                Location = new System.Drawing.Point(leftMargin + 185, y),
                Size = new System.Drawing.Size(400, 20),
                ForeColor = System.Drawing.Color.Gray,
                Font = new System.Drawing.Font("Arial", 9)
            };
            this.Controls.Add(lblEstimatedSize);
            y += 35;
            
            // Save location
            Label lblLocation = new Label
            {
                Text = "Save Location:",
                Location = new System.Drawing.Point(leftMargin, y),
                Size = new System.Drawing.Size(100, 20)
            };
            this.Controls.Add(lblLocation);
            
            txtLocation = new TextBox
            {
                Text = defaultPath,
                Location = new System.Drawing.Point(leftMargin + 105, y - 2),
                Size = new System.Drawing.Size(410, 25),
                ReadOnly = true
            };
            this.Controls.Add(txtLocation);
            
            Button btnBrowse = new Button
            {
                Text = "📁",
                Location = new System.Drawing.Point(leftMargin + 520, y - 4),
                Size = new System.Drawing.Size(35, 28)
            };
            btnBrowse.Click += BtnBrowse_Click;
            this.Controls.Add(btnBrowse);
            y += 35;
            
            // Filename
            Label lblFilename = new Label
            {
                Text = "Filename:",
                Location = new System.Drawing.Point(leftMargin, y),
                Size = new System.Drawing.Size(100, 20)
            };
            this.Controls.Add(lblFilename);
            
            txtFilename = new TextBox
            {
                Text = defaultFilename,
                Location = new System.Drawing.Point(leftMargin + 105, y - 2),
                Size = new System.Drawing.Size(450, 25)
            };
            this.Controls.Add(txtFilename);
            y += 50;
            
            // Buttons
            Button btnCancel = new Button
            {
                Text = "Cancel",
                Location = new System.Drawing.Point(leftMargin + 345, y),
                Size = new System.Drawing.Size(100, 35),
                DialogResult = DialogResult.Cancel
            };
            this.Controls.Add(btnCancel);
            
            Button btnSave = new Button
            {
                Text = "Save & Convert",
                Location = new System.Drawing.Point(leftMargin + 455, y),
                Size = new System.Drawing.Size(120, 35),
                DialogResult = DialogResult.OK
            };
            btnSave.Click += BtnSave_Click;
            this.Controls.Add(btnSave);
            
            this.AcceptButton = btnSave;
            this.CancelButton = btnCancel;
            
            // Initial size calculation
            UpdateEstimatedSize(null, EventArgs.Empty);
        }
        
        private void UpdateFrameInfoDisplay()
        {
            string info = "";
            foreach (var stat in frameStats)
            {
                var range = FrameRanges[stat.filename];
                int selectedFrames = range.endFrame - range.startFrame + 1;
                bool isTrimmed = range.startFrame != 1 || range.endFrame != stat.frames;
                
                if (isTrimmed)
                {
                    info += $"• {stat.filename}: {stat.frames} frames → {selectedFrames} frames (trimmed)\n";
                }
                else
                {
                    info += $"• {stat.filename}: {stat.frames} frames in {stat.duration:F2}s\n";
                }
            }
            lblFrameInfo.Text = info.TrimEnd('\n');
        }
        
        private void BtnTrimVideos_Click(object? sender, EventArgs e)
        {
            using (VideoTrimDialog dialog = new VideoTrimDialog(frameStats, aviFilePaths, FrameRanges))
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    // Update frame ranges from trim dialog
                    FrameRanges = dialog.FrameRanges;
                    
                    // Update display
                    UpdateFrameInfoDisplay();
                    UpdateEstimatedSize(null, EventArgs.Empty);
                }
            }
        }
        
        private void UpdateEstimatedSize(object? sender, EventArgs e)
        {
            try
            {
                lblEstimatedSize.Text = "Calculating...";
                Application.DoEvents();
                
                System.Threading.Tasks.Task.Run(() =>
                {
                    long totalSize = 0;
                    
                    for (int i = 0; i < aviFileSizes.Count; i++)
                    {
                        var range = FrameRanges[frameStats[i].filename];
                        int selectedFrames = range.endFrame - range.startFrame + 1;
                        int totalFrames = frameStats[i].frames;
                        
                        if (totalFrames > 0)
                        {
                            long fullSize = aviFileSizes[i];
                            long trimmedSize = (fullSize * selectedFrames) / totalFrames;
                            totalSize += trimmedSize;
                        }
                    }
                    
                    long estimatedMp4Size = totalSize / 8;
                    
                    string sizeText = "";
                    if (rbMP4.Checked)
                    {
                        sizeText = $"Estimated size: ~{estimatedMp4Size / (1024 * 1024)} MB total";
                    }
                    else if (rbAVI.Checked)
                    {
                        sizeText = $"Estimated size: ~{totalSize / (1024 * 1024)} MB total";
                    }
                    else if (rbBoth.Checked)
                    {
                        long bothSize = totalSize + estimatedMp4Size;
                        sizeText = $"Estimated size: ~{bothSize / (1024 * 1024)} MB total";
                    }
                    
                    if (lblEstimatedSize.InvokeRequired)
                    {
                        lblEstimatedSize.Invoke(new Action(() => lblEstimatedSize.Text = sizeText));
                    }
                    else
                    {
                        lblEstimatedSize.Text = sizeText;
                    }
                });
            }
            catch
            {
                lblEstimatedSize.Text = "Estimated size: N/A";
            }
        }
        
        private void BtnBrowse_Click(object? sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select Save Location";
                dialog.SelectedPath = txtLocation.Text;
                dialog.ShowNewFolderButton = true;
                
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtLocation.Text = dialog.SelectedPath;
                }
            }
        }
        
        private void BtnSave_Click(object? sender, EventArgs e)
        {
            // Validate FPS
            if (!double.TryParse(txtFps.Text, out double fps) || fps < 0.1 || fps > 240)
            {
                MessageBox.Show("Please enter a valid frame rate between 0.1 and 240 fps.",
                                "Invalid FPS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            // Validate filename
            if (string.IsNullOrWhiteSpace(txtFilename.Text))
            {
                MessageBox.Show("Please enter a filename.",
                                "Missing Filename", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            // Check for invalid characters
            char[] invalidChars = Path.GetInvalidFileNameChars();
            if (txtFilename.Text.IndexOfAny(invalidChars) >= 0)
            {
                MessageBox.Show("Filename contains invalid characters.",
                                "Invalid Filename", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            SelectedPath = txtLocation.Text;
            SelectedFilename = txtFilename.Text;
            SelectedFps = fps;
            
            if (rbMP4.Checked)
                SelectedFormat = OutputFormat.MP4;
            else if (rbAVI.Checked)
                SelectedFormat = OutputFormat.AVI;
            else
                SelectedFormat = OutputFormat.Both;
        }
    }
}