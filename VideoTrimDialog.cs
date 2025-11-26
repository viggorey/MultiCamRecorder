using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OpenCvSharp;

namespace MultiCamRecorder
{
    public class VideoTrimDialog : Form
    {
        public Dictionary<string, (int startFrame, int endFrame)> FrameRanges { get; private set; }
        private TrackBar trackSharedStart;
        private TrackBar trackSharedEnd;
        private Label lblSharedSliderInfo;
        private List<(string filename, int frames, double duration)> frameStats;
        private List<string> aviFilePaths;
        
        private CheckBox chkApplyToAll;
        private NumericUpDown numSharedStart, numSharedEnd;
        private Label lblSharedRange;
        private Panel gridPanel;
        private List<CameraPreviewControl> cameraControls;
        
        private int expandedCameraIndex = -1;
        
        public VideoTrimDialog(List<(string filename, int frames, double duration)> stats,
                              List<string> filePaths,
                              Dictionary<string, (int startFrame, int endFrame)> currentRanges)
        {
            frameStats = stats;
            aviFilePaths = filePaths;
            
            // Clone current ranges
            FrameRanges = new Dictionary<string, (int, int)>(currentRanges);
            
            InitializeDialog();
        }
        
        private void InitializeDialog()
        {
            this.Text = "Trim Videos";
            this.Size = new System.Drawing.Size(1400, 1020);  // ← Increased from 750 to 850
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new System.Drawing.Size(1400, 1020);  // ← Increased from 750 to 850  // ← Increased from 680 to 750
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            
            int y = 20;
            int leftMargin = 20;
            
            // Title
            Label lblTitle = new Label
            {
                Text = "Select frame ranges to keep:",
                Location = new System.Drawing.Point(leftMargin, y),
                Size = new System.Drawing.Size(400, 20),
                Font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold)
            };
            this.Controls.Add(lblTitle);
            y += 30;
            
            // Apply to all checkbox
            chkApplyToAll = new CheckBox
            {
                Text = "Apply same frame range to all cameras",
                Location = new System.Drawing.Point(leftMargin, y),
                Size = new System.Drawing.Size(300, 20),
                Checked = false,
                Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold)
            };
            chkApplyToAll.CheckedChanged += ChkApplyToAll_CheckedChanged;
            this.Controls.Add(chkApplyToAll);
            y += 30;
            
            // Shared range controls (initially hidden)
            lblSharedRange = new Label
            {
                Text = "Shared Range:",
                Location = new System.Drawing.Point(leftMargin + 20, y),
                Size = new System.Drawing.Size(100, 20),
                Visible = false
            };
            this.Controls.Add(lblSharedRange);

            Label lblSharedStart = new Label
            {
                Text = "Start:",
                Location = new System.Drawing.Point(leftMargin + 130, y + 2),
                Size = new System.Drawing.Size(40, 20),
                Visible = false
            };
            this.Controls.Add(lblSharedStart);

            int minFrames = frameStats.Min(s => s.frames);
            numSharedStart = new NumericUpDown
            {
                Location = new System.Drawing.Point(leftMargin + 175, y),
                Size = new System.Drawing.Size(70, 25),
                Minimum = 1,
                Maximum = minFrames,
                Value = FrameRanges[frameStats[0].filename].startFrame,
                Visible = false
            };
            numSharedStart.ValueChanged += NumSharedRange_ValueChanged;
            this.Controls.Add(numSharedStart);

            Label lblSharedEnd = new Label
            {
                Text = "End:",
                Location = new System.Drawing.Point(leftMargin + 260, y + 2),
                Size = new System.Drawing.Size(35, 20),
                Visible = false
            };
            this.Controls.Add(lblSharedEnd);

            numSharedEnd = new NumericUpDown
            {
                Location = new System.Drawing.Point(leftMargin + 300, y),
                Size = new System.Drawing.Size(70, 25),
                Minimum = 1,
                Maximum = minFrames,
                Value = Math.Min(FrameRanges[frameStats[0].filename].endFrame, minFrames),
                Visible = false
            };
            numSharedEnd.ValueChanged += NumSharedRange_ValueChanged;
            this.Controls.Add(numSharedEnd);

            Label lblSharedNote = new Label
            {
                Text = $"(limited to shortest: {minFrames} frames)",
                Location = new System.Drawing.Point(leftMargin + 380, y + 2),
                Size = new System.Drawing.Size(250, 20),
                ForeColor = System.Drawing.Color.Gray,
                Font = new System.Drawing.Font("Arial", 8),
                Visible = false
            };
            this.Controls.Add(lblSharedNote);

            // Store references for visibility toggling
            lblSharedStart.Tag = "shared";
            lblSharedEnd.Tag = "shared";
            lblSharedNote.Tag = "shared";

            y += 50;  // ← Increased from 35 to 40

            // Add shared sliders
            Label lblSharedStartSlider = new Label
            {
                Text = "Start:",
                Location = new System.Drawing.Point(leftMargin + 20, y + 2),
                Size = new System.Drawing.Size(40, 20),
                Visible = false,
                Tag = "shared"
            };
            this.Controls.Add(lblSharedStartSlider);

            trackSharedStart = new TrackBar
            {
                Location = new System.Drawing.Point(leftMargin + 65, y),
                Size = new System.Drawing.Size(500, 25),
                Minimum = 1,
                Maximum = minFrames,
                Value = FrameRanges[frameStats[0].filename].startFrame,
                TickStyle = TickStyle.None,
                Visible = false,
                Tag = "shared"
            };
            trackSharedStart.ValueChanged += TrackSharedStart_ValueChanged;
            this.Controls.Add(trackSharedStart);

            y += 50;  // ← Increased from 35 to 40

            Label lblSharedEndSlider = new Label
            {
                Text = "End:",
                Location = new System.Drawing.Point(leftMargin + 20, y + 2),
                Size = new System.Drawing.Size(40, 20),
                Visible = false,
                Tag = "shared"
            };
            this.Controls.Add(lblSharedEndSlider);

            trackSharedEnd = new TrackBar
            {
                Location = new System.Drawing.Point(leftMargin + 65, y),
                Size = new System.Drawing.Size(500, 25),
                Minimum = 1,
                Maximum = minFrames,
                Value = Math.Min(FrameRanges[frameStats[0].filename].endFrame, minFrames),
                TickStyle = TickStyle.None,
                Visible = false,
                Tag = "shared"
            };
            trackSharedEnd.ValueChanged += TrackSharedEnd_ValueChanged;
            this.Controls.Add(trackSharedEnd);

            y += 50;  // ← Increased from 35 to 40

            lblSharedSliderInfo = new Label
            {
                Location = new System.Drawing.Point(leftMargin + 65, y),
                Size = new System.Drawing.Size(400, 20),
                Font = new System.Drawing.Font("Arial", 8, FontStyle.Bold),
                ForeColor = Color.Blue,
                Visible = false,
                Tag = "shared"
            };
            this.Controls.Add(lblSharedSliderInfo);
            UpdateSharedSliderInfo();

            y += 30;
            
            // Camera preview grid panel - larger to show recording size
            gridPanel = new Panel
            {
                Location = new System.Drawing.Point(leftMargin, y),
                Size = new System.Drawing.Size(1340, 660),  // ← Increased from 480 to 580
                BorderStyle = BorderStyle.FixedSingle,
                AutoScroll = true
            };
            this.Controls.Add(gridPanel);
            
            // Create camera preview controls
            cameraControls = new List<CameraPreviewControl>();
            CreateCameraGrid();
            
            y += 670;
            
            // Buttons
            Button btnCancel = new Button
            {
                Text = "Cancel",
                Location = new System.Drawing.Point(1150, y),
                Size = new System.Drawing.Size(100, 35),
                DialogResult = DialogResult.Cancel
            };
            this.Controls.Add(btnCancel);
            
            Button btnApply = new Button
            {
                Text = "Apply",
                Location = new System.Drawing.Point(1260, y),
                Size = new System.Drawing.Size(100, 35),
                DialogResult = DialogResult.OK
            };
            this.Controls.Add(btnApply);
            
            this.AcceptButton = btnApply;
            this.CancelButton = btnCancel;
        }
        
        private void CreateCameraGrid()
        {
            gridPanel.Controls.Clear();
            cameraControls.Clear();
            
            if (expandedCameraIndex >= 0 && expandedCameraIndex < aviFilePaths.Count)
            {
                // Expanded view - show only one camera (full panel size)
                var control = new CameraPreviewControl(
                    aviFilePaths[expandedCameraIndex],
                    frameStats[expandedCameraIndex],
                    expandedCameraIndex,
                    chkApplyToAll.Checked);
                
                control.Location = new System.Drawing.Point(0, 0);
                control.Size = new System.Drawing.Size(gridPanel.Width - 2, gridPanel.Height - 2);
                control.OnFrameRangeChanged += CameraControl_OnFrameRangeChanged;
                control.OnPreviewClicked += CameraControl_OnPreviewClicked;
                
                gridPanel.Controls.Add(control);
                cameraControls.Add(control);
                
                // Load saved frame range
                var range = FrameRanges[frameStats[expandedCameraIndex].filename];
                control.SetFrameRange(range.startFrame, range.endFrame);
                control.UpdatePreview(range.startFrame);
            }
            else
            {
                // Grid view - show all cameras at larger size
                int previewWidth = 320;
                int previewHeight = 560; // Increased space for video and controls
                
                for (int i = 0; i < aviFilePaths.Count; i++)
                {
                    var control = new CameraPreviewControl(
                        aviFilePaths[i],
                        frameStats[i],
                        i,
                        chkApplyToAll.Checked);
                    
                    int col = i % 4; // 4 cameras in a row
                    int row = i / 4;
                    
                    control.Location = new System.Drawing.Point(
                        col * (previewWidth + 15) + 10,
                        row * (previewHeight + 15) + 10);
                    control.Size = new System.Drawing.Size(previewWidth, previewHeight);
                    control.OnFrameRangeChanged += CameraControl_OnFrameRangeChanged;
                    control.OnPreviewClicked += CameraControl_OnPreviewClicked;
                    
                    gridPanel.Controls.Add(control);
                    cameraControls.Add(control);
                    
                    // Load saved frame range
                    var range = FrameRanges[frameStats[i].filename];
                    control.SetFrameRange(range.startFrame, range.endFrame);
                    control.UpdatePreview(range.startFrame);
                }
            }
        }
        
        private void CameraControl_OnPreviewClicked(int cameraIndex)
        {
            if (expandedCameraIndex == cameraIndex)
            {
                // Already expanded, collapse back to grid
                expandedCameraIndex = -1;
            }
            else
            {
                // Expand this camera
                expandedCameraIndex = cameraIndex;
            }
            
            CreateCameraGrid();
        }
        
        private void CameraControl_OnFrameRangeChanged(int cameraIndex, int startFrame, int endFrame)
        {
            string filename = frameStats[cameraIndex].filename;
            FrameRanges[filename] = (startFrame, endFrame);
        }
        
        private void ChkApplyToAll_CheckedChanged(object? sender, EventArgs e)
        {
            bool applyToAll = chkApplyToAll.Checked;
            
            // Show/hide shared controls
            lblSharedRange.Visible = applyToAll;
            numSharedStart.Visible = applyToAll;
            numSharedEnd.Visible = applyToAll;
            
            // Show/hide shared control labels
            foreach (Control control in this.Controls)
            {
                if (control.Tag?.ToString() == "shared")
                {
                    control.Visible = applyToAll;
                }
            }
            
            // Update camera controls
            foreach (var control in cameraControls)
            {
                control.SetSharedMode(applyToAll);
            }
            
            // If enabling shared mode, apply current shared values
            if (applyToAll)
            {
                NumSharedRange_ValueChanged(null, EventArgs.Empty);
            }
        }
        
        private void NumSharedRange_ValueChanged(object? sender, EventArgs e)
        {
            if (!chkApplyToAll.Checked)
                return;
            
            int start = (int)numSharedStart.Value;
            int end = (int)numSharedEnd.Value;
            
            // Validate
            if (start > end)
            {
                start = end;
                numSharedStart.Value = start;
            }
            
            // Sync sliders
            trackSharedStart.Value = start;
            trackSharedEnd.Value = end;
            UpdateSharedSliderInfo();
            
            // Apply to all cameras
            for (int i = 0; i < cameraControls.Count; i++)
            {
                cameraControls[i].SetFrameRange(start, end);
                
                // Update stored ranges
                string filename = frameStats[i].filename;
                int maxFrame = frameStats[i].frames;
                int actualEnd = Math.Min(end, maxFrame);
                int actualStart = Math.Min(start, maxFrame);
                FrameRanges[filename] = (actualStart, actualEnd);
            }
        }
        
        private void TrackSharedStart_ValueChanged(object? sender, EventArgs e)
        {
            if (!chkApplyToAll.Checked)
                return;
            
            int start = trackSharedStart.Value;
            int end = trackSharedEnd.Value;
            
            // Ensure start doesn't exceed end
            if (start > end)
            {
                trackSharedStart.Value = end;
                start = end;
            }
            
            // Update numeric control
            numSharedStart.Value = start;
            UpdateSharedSliderInfo();
            
            // Apply to all cameras (will be handled by NumSharedRange_ValueChanged)
        }

        private void TrackSharedEnd_ValueChanged(object? sender, EventArgs e)
        {
            if (!chkApplyToAll.Checked)
                return;
            
            int start = trackSharedStart.Value;
            int end = trackSharedEnd.Value;
            
            // Ensure end doesn't go below start
            if (end < start)
            {
                trackSharedEnd.Value = start;
                end = start;
            }
            
            // Update numeric control
            numSharedEnd.Value = end;
            UpdateSharedSliderInfo();
            
            // Apply to all cameras (will be handled by NumSharedRange_ValueChanged)
        }

        private void UpdateSharedSliderInfo()
        {
            if (chkApplyToAll.Checked && frameStats.Count > 0)
            {
                int start = trackSharedStart.Value;
                int end = trackSharedEnd.Value;
                int selectedFrames = end - start + 1;
                
                // Calculate approximate duration based on first camera
                double avgFps = frameStats[0].frames / frameStats[0].duration;
                double duration = selectedFrames / avgFps;
                
                lblSharedSliderInfo.Text = $"Selected: {selectedFrames} frames (~{duration:F2}s)";
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Dispose all video captures before closing
            foreach (var control in cameraControls)
            {
                control.Dispose();
            }
            
            base.OnFormClosing(e);
        }
    }
}