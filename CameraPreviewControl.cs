using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using OpenCvSharp;

namespace QueenPix
{
    // Camera preview control with video scrubbing
    public class CameraPreviewControl : UserControl
    {
        private TrackBar trackFrame;
        private TrackBar trackStart;  // Slider for start frame
        private TrackBar trackEnd;    // Slider for end frame
        private Label lblSliderInfo;
        private Label lblStartSlider;
        private Label lblEndSlider;
        private string aviFilePath;
        private (string filename, int frames, double duration) stats;
        private int cameraIndex;
        private bool isSharedMode;
        
        private PictureBox pictureBox;
        private Label lblCameraName;
        private Label lblFrameInfo;
        private NumericUpDown numStart, numEnd;
        private Label lblStart, lblEnd;
        
        private VideoCapture? videoCapture;
        private int currentFrame = 1;
        
        public event Action<int, int, int>? OnFrameRangeChanged;
        public event Action<int>? OnPreviewClicked;
        
        public CameraPreviewControl(string aviPath, (string filename, int frames, double duration) frameStats, int camIndex, bool sharedMode)
        {
            aviFilePath = aviPath;
            stats = frameStats;
            cameraIndex = camIndex;
            isSharedMode = sharedMode;
            
            InitializeControl();
            LoadVideo();
        }
        
        private void InitializeControl()
        {
            this.BorderStyle = BorderStyle.FixedSingle;
            this.BackColor = Color.White;
            
            // Camera name label
            lblCameraName = new Label
            {
                Text = $"Camera {cameraIndex + 1}",
                Location = new System.Drawing.Point(5, 5),
                AutoSize = true,
                Font = new System.Drawing.Font("Arial", 9, FontStyle.Bold)
            };
            this.Controls.Add(lblCameraName);
            
            // Frame info label
            lblFrameInfo = new Label
            {
                Text = $"{stats.frames} frames",
                Location = new System.Drawing.Point(5, 25),
                AutoSize = true,
                Font = new System.Drawing.Font("Arial", 8),
                ForeColor = Color.Gray
            };
            this.Controls.Add(lblFrameInfo);
            
            // Picture box for video preview
            pictureBox = new PictureBox
            {
                Location = new System.Drawing.Point(5, 45),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.Black,
                Cursor = Cursors.Hand
            };
            pictureBox.Click += PictureBox_Click;
            this.Controls.Add(pictureBox);

            // Frame range controls
            lblStart = new Label
            {
                Text = "Start:",
                AutoSize = true,
                Font = new System.Drawing.Font("Arial", 8)
            };
            this.Controls.Add(lblStart);

            numStart = new NumericUpDown
            {
                Minimum = 1,
                Maximum = stats.frames,
                Value = 1,
                Size = new System.Drawing.Size(60, 20)
            };
            numStart.ValueChanged += NumRange_ValueChanged;  // ← IMPORTANT: Connect the event
            this.Controls.Add(numStart);

            lblEnd = new Label
            {
                Text = "End:",
                AutoSize = true,
                Font = new System.Drawing.Font("Arial", 8)
            };
            this.Controls.Add(lblEnd);

            numEnd = new NumericUpDown
            {
                Minimum = 1,
                Maximum = stats.frames,
                Value = stats.frames,
                Size = new System.Drawing.Size(60, 20)
            };
            numEnd.ValueChanged += NumRange_ValueChanged;  // ← IMPORTANT: Connect the event
            this.Controls.Add(numEnd);

            // Start frame slider
            lblStartSlider = new Label
            {
                Text = "Trim Start:",
                AutoSize = true,
                Font = new System.Drawing.Font("Arial", 8)
            };
            this.Controls.Add(lblStartSlider);

            trackStart = new TrackBar
            {
                Minimum = 1,
                Maximum = stats.frames,
                Value = 1,
                TickStyle = TickStyle.None,
                Orientation = Orientation.Horizontal
            };
            trackStart.ValueChanged += TrackStart_ValueChanged;
            this.Controls.Add(trackStart);

            // End frame slider
            lblEndSlider = new Label
            {
                Text = "Trim End:",
                AutoSize = true,
                Font = new System.Drawing.Font("Arial", 8)
            };
            this.Controls.Add(lblEndSlider);

            trackEnd = new TrackBar
            {
                Minimum = 1,
                Maximum = stats.frames,
                Value = stats.frames,
                TickStyle = TickStyle.None,
                Orientation = Orientation.Horizontal
            };
            trackEnd.ValueChanged += TrackEnd_ValueChanged;
            this.Controls.Add(trackEnd);

            // Range info label
            lblSliderInfo = new Label
            {
                AutoSize = true,
                Font = new System.Drawing.Font("Arial", 8, FontStyle.Bold),
                ForeColor = Color.Blue
            };
            this.Controls.Add(lblSliderInfo);
            UpdateSliderInfo();
            
            this.Resize += CameraPreviewControl_Resize;
        }
        
        private void CameraPreviewControl_Resize(object? sender, EventArgs e)
        {
            // Dynamically resize and position controls based on current size
            int width = this.Width;
            int height = this.Height;
            
            // Picture box takes most of the space
            pictureBox.Size = new System.Drawing.Size(width - 10, height - 200);
            
            // Position sliders below picture box
            int sliderY = pictureBox.Bottom + 5;
            
            // Start slider
            lblStartSlider.Location = new System.Drawing.Point(5, sliderY);
            trackStart.Location = new System.Drawing.Point(75, sliderY);
            trackStart.Size = new System.Drawing.Size(width - 85, 25);
            
            // End slider
            sliderY += 50;
            lblEndSlider.Location = new System.Drawing.Point(5, sliderY);
            trackEnd.Location = new System.Drawing.Point(75, sliderY);
            trackEnd.Size = new System.Drawing.Size(width - 85, 25);
            
            // Range info
            sliderY += 50;
            lblSliderInfo.Location = new System.Drawing.Point(5, sliderY);
            
            // Position numeric range controls at bottom
            int bottomY = height - 30;
            
            if (isSharedMode)
            {
                // Hide individual controls in shared mode
                lblStart.Visible = false;
                numStart.Visible = false;
                lblEnd.Visible = false;
                numEnd.Visible = false;
                
                // Also hide sliders in shared mode
                lblStartSlider.Visible = false;
                trackStart.Visible = false;
                lblEndSlider.Visible = false;
                trackEnd.Visible = false;
                lblSliderInfo.Visible = false;
            }
            else
            {
                // Show and position individual controls
                lblStart.Visible = true;
                numStart.Visible = true;
                lblEnd.Visible = true;
                numEnd.Visible = true;
                
                lblStartSlider.Visible = true;
                trackStart.Visible = true;
                lblEndSlider.Visible = true;
                trackEnd.Visible = true;
                lblSliderInfo.Visible = true;
                
                lblStart.Location = new System.Drawing.Point(5, bottomY);
                numStart.Location = new System.Drawing.Point(45, bottomY - 2);
                lblEnd.Location = new System.Drawing.Point(115, bottomY);
                numEnd.Location = new System.Drawing.Point(145, bottomY - 2);
            }
        }
        
        private void PictureBox_Click(object? sender, EventArgs e)
        {
            OnPreviewClicked?.Invoke(cameraIndex);
        }
        
        private void LoadVideo()
        {
            try
            {
                videoCapture = new VideoCapture(aviFilePath);
                if (!videoCapture.IsOpened())
                {
                    MessageBox.Show($"Failed to open video: {Path.GetFileName(aviFilePath)}",
                                    "Video Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading video: {ex.Message}",
                                "Video Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        public void UpdatePreview(int frameNumber)
        {
            if (videoCapture == null || !videoCapture.IsOpened())
                return;
            
            try
            {
                // Frame numbers are 1-based, OpenCV is 0-based
                videoCapture.Set(VideoCaptureProperties.PosFrames, frameNumber - 1);
                
                using (Mat frame = new Mat())
                {
                    if (videoCapture.Read(frame) && !frame.Empty())
                    {
                        // Convert to bitmap and display
                        Bitmap bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(frame);

                        // Dispose old image
                        if (pictureBox.Image != null)
                        {
                            var oldImage = pictureBox.Image;
                            pictureBox.Image = null;
                            oldImage.Dispose();
                        }
                        
                        pictureBox.Image = bitmap;
                        currentFrame = frameNumber;
                    }
                }
            }
            catch (Exception ex)
            {
                // Silently handle frame read errors
                System.Diagnostics.Debug.WriteLine($"Error reading frame {frameNumber}: {ex.Message}");
            }
        }
        
        public void SetSharedMode(bool shared)
        {
            isSharedMode = shared;
            CameraPreviewControl_Resize(null, EventArgs.Empty);
        }
        
        public void SetFrameRange(int start, int end)
        {
            // Clamp to valid range for this video
            start = Math.Max(1, Math.Min(start, stats.frames));
            end = Math.Max(1, Math.Min(end, stats.frames));
            
            if (start > end)
                start = end;
            
            numStart.Value = start;
            numEnd.Value = end;
            
            // Update sliders
            trackStart.Value = start;
            trackEnd.Value = end;
            
            // Update preview to start frame
            UpdatePreview(start);
            UpdateSliderInfo();
        }
        
        private void NumRange_ValueChanged(object? sender, EventArgs e)
        {
            int start = (int)numStart.Value;
            int end = (int)numEnd.Value;
            
            // Validate range
            if (start > end)
            {
                if (sender == numStart)
                    numEnd.Value = start;
                else
                    numStart.Value = end;
                
                start = (int)numStart.Value;
                end = (int)numEnd.Value;
            }
            
            // Sync sliders
            trackStart.Value = start;
            trackEnd.Value = end;
            
            // Update preview based on which control changed
            if (sender == numStart)
                UpdatePreview(start);
            else if (sender == numEnd)
                UpdatePreview(end);
            
            // Notify parent
            OnFrameRangeChanged?.Invoke(cameraIndex, start, end);
            UpdateSliderInfo();
        }
        
        private void TrackStart_ValueChanged(object? sender, EventArgs e)
        {
            int start = trackStart.Value;
            int end = trackEnd.Value;
            
            // Ensure start doesn't exceed end
            if (start > end)
            {
                trackStart.Value = end;
                start = end;
            }
            
            // Update numeric control and preview
            numStart.Value = start;
            UpdatePreview(start);
            UpdateSliderInfo();
        }

        private void TrackEnd_ValueChanged(object? sender, EventArgs e)
        {
            int start = trackStart.Value;
            int end = trackEnd.Value;
            
            // Ensure end doesn't go below start
            if (end < start)
            {
                trackEnd.Value = start;
                end = start;
            }
            
            // Update numeric control and preview
            numEnd.Value = end;
            UpdatePreview(end);
            UpdateSliderInfo();
        }

        private void UpdateSliderInfo()
        {
            int start = trackStart.Value;
            int end = trackEnd.Value;
            int selectedFrames = end - start + 1;
            double duration = stats.duration > 0 ? (selectedFrames * stats.duration / stats.frames) : 0;
            lblSliderInfo.Text = $"Selected: {selectedFrames} frames ({duration:F2}s)";
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (videoCapture != null)
                {
                    videoCapture.Release();
                    videoCapture.Dispose();
                    videoCapture = null;
                }
                
                if (pictureBox.Image != null)
                {
                    var oldImage = pictureBox.Image;
                    pictureBox.Image = null;
                    oldImage.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}