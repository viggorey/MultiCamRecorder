using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace QueenPix
{
    /// <summary>
    /// Centrifuge Analysis dialog: load a video + JSON timestamp file,
    /// navigate frames, calibrate scale, measure insect radius at detachment,
    /// and compute centripetal acceleration / velocity.
    /// </summary>
    public class CentrifugeAnalysisDialog : Form
    {
        // ── UI controls ──────────────────────────────────────────────────────
        private PictureBox _pictureBox = null!;
        private PictureBox _chartBox = null!;

        private Button _btnLoadVideo = null!;
        private Button _btnLoadJson = null!;
        private Label _lblVideoFile = null!;
        private Label _lblJsonFile = null!;

        private TextBox _txtMmPerPixel = null!;
        private Button _btnDrawScalebar = null!;

        private TrackBar _slider = null!;
        private Button _btnPrev = null!;
        private Button _btnNext = null!;
        private TextBox _txtFrame = null!;
        private Label _lblFrameInfo = null!;

        private Button _btnMarkDetach = null!;
        private Label _lblDetachFrame = null!;

        private Button _btnClickInsect = null!;
        private TextBox _txtRadius = null!;

        private TextBox _txtFps = null!;
        private TextBox _txtOmega = null!;
        private TextBox _txtVelocity = null!;
        private TextBox _txtAcceleration = null!;

        private Button _btnSave = null!;

        // ── State ─────────────────────────────────────────────────────────────
        private VideoCapture? _videoCapture;
        private Bitmap? _currentFrame;
        private Bitmap? _overlayBitmap;
        private int _totalFrames;
        private int _frameWidth;
        private int _frameHeight;
        private int _currentFrameNumber = 1;
        private string _videoPath = "";
        private string _jsonPath = "";

        // JSON per-frame seconds-from-start (index = frameNumber - 1)
        private List<double> _frameSeconds = new List<double>();

        // Detachment frame
        private int _detachmentFrame = 0;

        // Scalebar mode
        private bool _drawingScalebar;
        private bool _scalebarFirstClick;
        private System.Drawing.Point _scalebarPt1;
        private System.Drawing.Point _scalebarPt2;

        // Insect-click mode (2 clicks: centrifuge centre, then insect position)
        private bool _clickingInsect;
        private int _clickCount;
        private System.Drawing.Point[] _clickPts = new System.Drawing.Point[2];

        // Measurement results
        private double _radiusMm;

        // ─────────────────────────────────────────────────────────────────────
        public CentrifugeAnalysisDialog()
        {
            InitializeLayout();
        }

        // =====================================================================
        // UI construction
        // =====================================================================
        private void InitializeLayout()
        {
            this.Text = "Centrifuge Analysis";
            this.Size = new System.Drawing.Size(1160, 830);
            this.MinimumSize = new System.Drawing.Size(900, 650);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;

            // ── Left panel: video display ─────────────────────────────────
            _pictureBox = new PictureBox
            {
                Location = new System.Drawing.Point(10, 10),
                Size = new System.Drawing.Size(690, 770),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Cursor = Cursors.Default
            };
            _pictureBox.MouseDown += PictureBox_MouseDown;
            _pictureBox.MouseUp   += PictureBox_MouseUp;
            _pictureBox.MouseMove += PictureBox_MouseMove;
            this.Controls.Add(_pictureBox);

            // ── Right panel: controls ────────────────────────────────────
            Panel rightPanel = new Panel
            {
                Location = new System.Drawing.Point(710, 5),
                Size = new System.Drawing.Size(420, 790),
                AutoScroll = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right
            };
            this.Controls.Add(rightPanel);

            int y = 8;
            int lw = 110; // label width
            int x = 5;

            // ── Section: Files ───────────────────────────────────────────
            AddSectionLabel(rightPanel, "Files", ref y);

            _btnLoadVideo = new Button { Text = "Load Video…", Location = new System.Drawing.Point(x, y), Size = new System.Drawing.Size(100, 26) };
            _btnLoadVideo.Click += BtnLoadVideo_Click;
            rightPanel.Controls.Add(_btnLoadVideo);

            _lblVideoFile = new Label
            {
                Text = "(no video)",
                Location = new System.Drawing.Point(x + 105, y + 4),
                Size = new System.Drawing.Size(295, 18),
                ForeColor = Color.Gray,
                Font = new Font("Arial", 7.5f)
            };
            rightPanel.Controls.Add(_lblVideoFile);
            y += 32;

            _btnLoadJson = new Button { Text = "Load JSON…", Location = new System.Drawing.Point(x, y), Size = new System.Drawing.Size(100, 26) };
            _btnLoadJson.Click += BtnLoadJson_Click;
            rightPanel.Controls.Add(_btnLoadJson);

            _lblJsonFile = new Label
            {
                Text = "(no JSON)",
                Location = new System.Drawing.Point(x + 105, y + 4),
                Size = new System.Drawing.Size(295, 18),
                ForeColor = Color.Gray,
                Font = new Font("Arial", 7.5f)
            };
            rightPanel.Controls.Add(_lblJsonFile);
            y += 38;

            // ── Section: Calibration ──────────────────────────────────────
            AddSectionLabel(rightPanel, "Scale Calibration", ref y);

            AddLabel(rightPanel, "mm / pixel:", x, y, lw);
            _txtMmPerPixel = new TextBox { Location = new System.Drawing.Point(x + lw, y - 2), Size = new System.Drawing.Size(90, 22), Text = "1.0" };
            rightPanel.Controls.Add(_txtMmPerPixel);

            _btnDrawScalebar = new Button { Text = "Draw Scalebar", Location = new System.Drawing.Point(x + lw + 96, y - 2), Size = new System.Drawing.Size(105, 24) };
            _btnDrawScalebar.Click += BtnDrawScalebar_Click;
            rightPanel.Controls.Add(_btnDrawScalebar);
            y += 38;

            // ── Section: Frame Navigation ─────────────────────────────────
            AddSectionLabel(rightPanel, "Frame Navigation", ref y);

            _slider = new TrackBar
            {
                Location = new System.Drawing.Point(x, y),
                Size = new System.Drawing.Size(400, 40),
                Minimum = 1,
                Maximum = 1,
                Value = 1,
                TickFrequency = 1,
                SmallChange = 1,
                LargeChange = 10
            };
            _slider.ValueChanged += Slider_ValueChanged;
            rightPanel.Controls.Add(_slider);
            y += 42;

            _btnPrev = new Button { Text = "◀", Location = new System.Drawing.Point(x, y), Size = new System.Drawing.Size(36, 26) };
            _btnPrev.Click += (s, e) => GoToFrame(_currentFrameNumber - 1);
            rightPanel.Controls.Add(_btnPrev);

            _txtFrame = new TextBox { Location = new System.Drawing.Point(x + 42, y), Size = new System.Drawing.Size(60, 22), Text = "1", TextAlign = HorizontalAlignment.Center };
            _txtFrame.KeyDown += TxtFrame_KeyDown;
            rightPanel.Controls.Add(_txtFrame);

            _btnNext = new Button { Text = "▶", Location = new System.Drawing.Point(x + 108, y), Size = new System.Drawing.Size(36, 26) };
            _btnNext.Click += (s, e) => GoToFrame(_currentFrameNumber + 1);
            rightPanel.Controls.Add(_btnNext);

            _lblFrameInfo = new Label
            {
                Text = "—",
                Location = new System.Drawing.Point(x + 150, y + 4),
                Size = new System.Drawing.Size(245, 18),
                Font = new Font("Arial", 7.5f),
                ForeColor = Color.DimGray
            };
            rightPanel.Controls.Add(_lblFrameInfo);
            y += 38;

            // ── Section: Detachment Frame ─────────────────────────────────
            AddSectionLabel(rightPanel, "Detachment Frame", ref y);

            _btnMarkDetach = new Button { Text = "Mark Current Frame", Location = new System.Drawing.Point(x, y), Size = new System.Drawing.Size(150, 26) };
            _btnMarkDetach.Click += BtnMarkDetach_Click;
            rightPanel.Controls.Add(_btnMarkDetach);

            _lblDetachFrame = new Label
            {
                Text = "Not set",
                Location = new System.Drawing.Point(x + 158, y + 4),
                Size = new System.Drawing.Size(230, 18),
                ForeColor = Color.DarkRed,
                Font = new Font("Arial", 8.5f, FontStyle.Bold)
            };
            rightPanel.Controls.Add(_lblDetachFrame);
            y += 38;

            // ── Section: Insect Measurement ───────────────────────────────
            AddSectionLabel(rightPanel, "Insect Measurement", ref y);

            _btnClickInsect = new Button { Text = "Click Centre + Insect (2 pts)", Location = new System.Drawing.Point(x, y), Size = new System.Drawing.Size(200, 26) };
            _btnClickInsect.Click += BtnClickInsect_Click;
            rightPanel.Controls.Add(_btnClickInsect);
            y += 34;

            AddLabel(rightPanel, "Radius (mm):", x, y, lw);
            _txtRadius = new TextBox { Location = new System.Drawing.Point(x + lw, y - 2), Size = new System.Drawing.Size(90, 22), ReadOnly = true, BackColor = Color.WhiteSmoke };
            rightPanel.Controls.Add(_txtRadius);
            y += 38;

            // ── Section: Results ──────────────────────────────────────────
            AddSectionLabel(rightPanel, "Results at Detachment", ref y);

            AddResultRow(rightPanel, "FPS (rev/s):", ref y, out _txtFps);
            AddResultRow(rightPanel, "ω (rad/s):", ref y, out _txtOmega);
            AddResultRow(rightPanel, "Velocity (m/s):", ref y, out _txtVelocity);
            AddResultRow(rightPanel, "Acceleration (m/s²):", ref y, out _txtAcceleration);
            y += 4;

            // ── Section: Acceleration Curve ───────────────────────────────
            AddSectionLabel(rightPanel, "Acceleration Curve (frame 1 → detachment)", ref y);

            _chartBox = new PictureBox
            {
                Location = new System.Drawing.Point(x, y),
                Size = new System.Drawing.Size(400, 190),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            rightPanel.Controls.Add(_chartBox);
            y += 200;

            // ── Save button ───────────────────────────────────────────────
            _btnSave = new Button
            {
                Text = "Save Results",
                Location = new System.Drawing.Point(x, y),
                Size = new System.Drawing.Size(120, 30),
                BackColor = Color.SteelBlue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnSave.Click += BtnSave_Click;
            rightPanel.Controls.Add(_btnSave);
            y += 40;

            // Set scrollable height
            rightPanel.AutoScrollMinSize = new System.Drawing.Size(0, y + 10);
        }

        // =====================================================================
        // Layout helpers
        // =====================================================================
        private static void AddSectionLabel(Panel parent, string text, ref int y)
        {
            Label lbl = new Label
            {
                Text = text,
                Location = new System.Drawing.Point(5, y),
                Size = new System.Drawing.Size(400, 18),
                Font = new Font("Arial", 8.5f, FontStyle.Bold),
                ForeColor = Color.DarkSlateGray
            };
            parent.Controls.Add(lbl);

            // Divider line via a thin panel
            Panel line = new Panel
            {
                Location = new System.Drawing.Point(5, y + 19),
                Size = new System.Drawing.Size(400, 1),
                BackColor = Color.LightGray
            };
            parent.Controls.Add(line);
            y += 26;
        }

        private static void AddLabel(Panel parent, string text, int x, int y, int width)
        {
            parent.Controls.Add(new Label
            {
                Text = text,
                Location = new System.Drawing.Point(x, y + 2),
                Size = new System.Drawing.Size(width, 18),
                Font = new Font("Arial", 8f)
            });
        }

        private static void AddResultRow(Panel parent, string label, ref int y, out TextBox textBox)
        {
            AddLabel(parent, label, 5, y, 140);
            textBox = new TextBox
            {
                Location = new System.Drawing.Point(150, y - 2),
                Size = new System.Drawing.Size(120, 22),
                ReadOnly = true,
                BackColor = Color.WhiteSmoke,
                Font = new Font("Courier New", 8.5f)
            };
            parent.Controls.Add(textBox);
            y += 28;
        }

        // =====================================================================
        // File loading
        // =====================================================================
        private void BtnLoadVideo_Click(object? sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Select Video File",
                Filter = "Video files|*.avi;*.mp4|All files|*.*"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            _videoPath = dlg.FileName;
            _lblVideoFile.Text = Path.GetFileName(_videoPath);
            _lblVideoFile.ForeColor = Color.DarkGreen;

            _videoCapture?.Dispose();
            _videoCapture = new VideoCapture(_videoPath);
            if (!_videoCapture.IsOpened())
            {
                MessageBox.Show("Could not open video file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _totalFrames = (int)_videoCapture.Get(VideoCaptureProperties.FrameCount);
            _frameWidth  = (int)_videoCapture.Get(VideoCaptureProperties.FrameWidth);
            _frameHeight = (int)_videoCapture.Get(VideoCaptureProperties.FrameHeight);

            if (_totalFrames < 1) _totalFrames = 1;

            _slider.Minimum = 1;
            _slider.Maximum = _totalFrames;
            _slider.Value   = 1;
            _slider.TickFrequency = Math.Max(1, _totalFrames / 20);

            _overlayBitmap = new Bitmap(_frameWidth, _frameHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            GoToFrame(1);

            // Auto-load matching JSON
            string jsonCandidate = Path.ChangeExtension(_videoPath, ".json");
            if (File.Exists(jsonCandidate))
                LoadJson(jsonCandidate);
        }

        private void BtnLoadJson_Click(object? sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Select JSON Timestamp File",
                Filter = "JSON files|*.json|All files|*.*",
                InitialDirectory = string.IsNullOrEmpty(_videoPath) ? "" : Path.GetDirectoryName(_videoPath) ?? ""
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            LoadJson(dlg.FileName);
        }

        private void LoadJson(string path)
        {
            try
            {
                _jsonPath = path;
                _frameSeconds.Clear();

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var frame in doc.RootElement.GetProperty("frames").EnumerateArray())
                    _frameSeconds.Add(frame.GetProperty("secondsFromStart").GetDouble());

                _lblJsonFile.Text = Path.GetFileName(path);
                _lblJsonFile.ForeColor = Color.DarkGreen;

                UpdateFrameInfoLabel();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not load JSON file:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // =====================================================================
        // Frame navigation
        // =====================================================================
        private void GoToFrame(int frameNumber)
        {
            if (_videoCapture == null || !_videoCapture.IsOpened()) return;

            frameNumber = Math.Max(1, Math.Min(_totalFrames, frameNumber));
            _currentFrameNumber = frameNumber;

            _videoCapture.Set(VideoCaptureProperties.PosFrames, frameNumber - 1);
            using var mat = new Mat();
            if (_videoCapture.Read(mat) && !mat.Empty())
            {
                _currentFrame?.Dispose();
                _currentFrame = BitmapConverter.ToBitmap(mat);
            }

            // Sync slider without re-triggering event
            if (_slider.Value != frameNumber)
            {
                _slider.ValueChanged -= Slider_ValueChanged;
                _slider.Value = frameNumber;
                _slider.ValueChanged += Slider_ValueChanged;
            }

            _txtFrame.Text = frameNumber.ToString();
            UpdateFrameInfoLabel();
            RedrawPictureBox();
        }

        private void Slider_ValueChanged(object? sender, EventArgs e)
        {
            GoToFrame(_slider.Value);
        }

        private void TxtFrame_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && int.TryParse(_txtFrame.Text, out int n))
                GoToFrame(n);
        }

        private void UpdateFrameInfoLabel()
        {
            if (_totalFrames == 0) { _lblFrameInfo.Text = "—"; return; }

            string ts = "";
            if (_frameSeconds.Count >= _currentFrameNumber)
                ts = $"  t={_frameSeconds[_currentFrameNumber - 1]:F3}s";

            _lblFrameInfo.Text = $"Frame {_currentFrameNumber} / {_totalFrames}{ts}";
        }

        // =====================================================================
        // Detachment frame
        // =====================================================================
        private void BtnMarkDetach_Click(object? sender, EventArgs e)
        {
            if (_videoCapture == null) { MessageBox.Show("Load a video first.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            _detachmentFrame = _currentFrameNumber;
            _lblDetachFrame.Text = $"Frame {_detachmentFrame}";
            UpdateResults();
            RedrawChart();
        }

        // =====================================================================
        // Scalebar drawing
        // =====================================================================
        private void BtnDrawScalebar_Click(object? sender, EventArgs e)
        {
            if (_videoCapture == null) { MessageBox.Show("Load a video first.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            _drawingScalebar = true;
            _scalebarFirstClick = true;
            _clickingInsect = false;
            _pictureBox.Cursor = Cursors.Cross;
            _btnDrawScalebar.BackColor = Color.LightYellow;
            _btnDrawScalebar.Text = "Click start point…";
        }

        // =====================================================================
        // Insect 3-click measurement
        // =====================================================================
        private void BtnClickInsect_Click(object? sender, EventArgs e)
        {
            if (_videoCapture == null) { MessageBox.Show("Load a video first.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (_detachmentFrame == 0) { MessageBox.Show("Mark the detachment frame first.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (!double.TryParse(_txtMmPerPixel.Text, out double mmpp) || mmpp <= 0)
            { MessageBox.Show("Enter a valid mm/pixel value first.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

            // Navigate to detachment frame so user measures in the right frame
            GoToFrame(_detachmentFrame);

            _clickingInsect = true;
            _clickCount = 0;
            _drawingScalebar = false;
            _pictureBox.Cursor = Cursors.Cross;
            _btnClickInsect.BackColor = Color.LightYellow;
            _btnClickInsect.Text = "Click 1: centrifuge centre";
        }

        // =====================================================================
        // PictureBox mouse events
        // =====================================================================
        private void PictureBox_MouseDown(object? sender, MouseEventArgs e)
        {
            System.Drawing.Point imgPt = PictureBoxToImageCoords(e.Location);

            if (_drawingScalebar)
            {
                if (_scalebarFirstClick)
                {
                    _scalebarPt1 = imgPt;
                    _scalebarFirstClick = false;
                    _btnDrawScalebar.Text = "Click end point…";
                }
            }
            else if (_clickingInsect)
            {
                if (_clickCount < 2)
                {
                    _clickPts[_clickCount] = imgPt;
                    _clickCount++;

                    DrawInsectClickOverlay();

                    if (_clickCount == 1)
                        _btnClickInsect.Text = "Click 2: insect position";
                    else
                        FinishInsectMeasurement();
                }
            }
        }

        private void PictureBox_MouseUp(object? sender, MouseEventArgs e)
        {
            if (_drawingScalebar && !_scalebarFirstClick)
            {
                System.Drawing.Point imgPt = PictureBoxToImageCoords(e.Location);
                _scalebarPt2 = imgPt;

                double pixelDist = Distance(_scalebarPt1, _scalebarPt2);
                if (pixelDist < 2) { ResetScalebarMode(); return; }

                // Draw the scalebar line on overlay
                using var g = Graphics.FromImage(_overlayBitmap!);
                using var pen = new Pen(Color.Yellow, 2);
                g.DrawLine(pen, _scalebarPt1, _scalebarPt2);
                // Small end ticks
                DrawTick(g, pen, _scalebarPt1);
                DrawTick(g, pen, _scalebarPt2);
                RedrawPictureBox();

                // Ask for real-world length
                string? input = PromptDialog("Enter scalebar length (mm):", "Scalebar Length", "127");
                if (input != null && double.TryParse(input, out double mm) && mm > 0)
                {
                    double factor = mm / pixelDist;
                    _txtMmPerPixel.Text = factor.ToString("F6");
                }

                ResetScalebarMode();
            }
        }

        private void PictureBox_MouseMove(object? sender, MouseEventArgs e)
        {
            // Could draw a live preview line while drawing scalebar — keep simple for now
        }

        private void ResetScalebarMode()
        {
            _drawingScalebar = false;
            _scalebarFirstClick = true;
            _pictureBox.Cursor = Cursors.Default;
            _btnDrawScalebar.BackColor = SystemColors.Control;
            _btnDrawScalebar.Text = "Draw Scalebar";
        }

        private void DrawTick(Graphics g, Pen pen, System.Drawing.Point pt)
        {
            g.DrawLine(pen, pt.X - 4, pt.Y - 4, pt.X + 4, pt.Y + 4);
            g.DrawLine(pen, pt.X + 4, pt.Y - 4, pt.X - 4, pt.Y + 4);
        }

        private void DrawInsectClickOverlay()
        {
            if (_overlayBitmap == null) return;
            using var g = Graphics.FromImage(_overlayBitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Red cross at centrifuge centre (pt 0)
            if (_clickCount >= 1)
            {
                using var pen = new Pen(Color.Red, 2);
                var p = _clickPts[0];
                g.DrawLine(pen, p.X - 10, p.Y, p.X + 10, p.Y);
                g.DrawLine(pen, p.X, p.Y - 10, p.X, p.Y + 10);
            }
            // Green dot at insect position + cyan radius line (pt 1)
            if (_clickCount >= 2)
            {
                using var brush = new SolidBrush(Color.LimeGreen);
                var p = _clickPts[1];
                g.FillEllipse(brush, p.X - 5, p.Y - 5, 10, 10);
                using var radPen = new Pen(Color.Cyan, 1.5f) { DashStyle = DashStyle.Dot };
                g.DrawLine(radPen, _clickPts[0], _clickPts[1]);
            }

            RedrawPictureBox();
        }

        private void FinishInsectMeasurement()
        {
            _clickingInsect = false;
            _pictureBox.Cursor = Cursors.Default;
            _btnClickInsect.BackColor = SystemColors.Control;
            _btnClickInsect.Text = "Click Centre + Insect (2 pts)";

            if (!double.TryParse(_txtMmPerPixel.Text, out double mmpp) || mmpp <= 0) return;

            _radiusMm = Distance(_clickPts[0], _clickPts[1]) * mmpp;
            _txtRadius.Text = _radiusMm.ToString("F3");

            UpdateResults();
            RedrawChart();
        }

        // =====================================================================
        // Results calculation
        // =====================================================================
        private void UpdateResults()
        {
            if (_detachmentFrame == 0 || _radiusMm <= 0 || _frameSeconds.Count < 2) return;

            double fps = GetInstantaneousFps(_detachmentFrame);
            double omega = fps * 2 * Math.PI;
            double r = _radiusMm / 1000.0; // metres
            double v = omega * r;
            double a = omega * omega * r;

            _txtFps.Text          = fps.ToString("F3");
            _txtOmega.Text        = omega.ToString("F3");
            _txtVelocity.Text     = v.ToString("F3");
            _txtAcceleration.Text = a.ToString("F3");
        }

        /// <summary>
        /// Returns instantaneous FPS at frame N using the interval between
        /// consecutive JSON timestamps (1 frame = 1 rotation).
        /// </summary>
        private double GetInstantaneousFps(int frameNumber)
        {
            int idx = frameNumber - 1; // 0-based
            if (_frameSeconds.Count < 2) return 0;

            if (idx > 0 && idx < _frameSeconds.Count)
            {
                double dt = _frameSeconds[idx] - _frameSeconds[idx - 1];
                if (dt > 0) return 1.0 / dt;
            }
            else if (idx == 0 && _frameSeconds.Count >= 2)
            {
                double dt = _frameSeconds[1] - _frameSeconds[0];
                if (dt > 0) return 1.0 / dt;
            }
            return 0;
        }

        // =====================================================================
        // Acceleration chart
        // =====================================================================
        private void RedrawChart()
        {
            if (_detachmentFrame == 0 || _radiusMm <= 0 || _frameSeconds.Count < 2)
            {
                _chartBox.Image = null;
                return;
            }

            int w = _chartBox.Width;
            int h = _chartBox.Height;
            if (w < 10 || h < 10) return;

            int padL = 55, padR = 15, padT = 15, padB = 35;
            int chartW = w - padL - padR;
            int chartH = h - padT - padB;

            // Compute per-frame acceleration up to detachment
            int endFrame = Math.Min(_detachmentFrame, _frameSeconds.Count);
            var accels = new List<double>();
            double r = _radiusMm / 1000.0;

            for (int f = 1; f <= endFrame; f++)
            {
                double fps = GetInstantaneousFps(f);
                double omega = fps * 2 * Math.PI;
                accels.Add(omega * omega * r);
            }

            if (accels.Count == 0) return;

            double maxA = 0;
            foreach (var a in accels) if (a > maxA) maxA = a;
            if (maxA <= 0) maxA = 1;
            maxA *= 1.1; // 10% headroom

            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Axes
            using var axisPen = new Pen(Color.Black, 1.5f);
            g.DrawRectangle(axisPen, padL, padT, chartW, chartH);

            // Y-axis ticks and labels
            using var tickFont = new Font("Arial", 6.5f);
            using var tickBrush = new SolidBrush(Color.Black);
            using var gridPen = new Pen(Color.LightGray, 1f) { DashStyle = DashStyle.Dash };
            int yTicks = 5;
            for (int i = 0; i <= yTicks; i++)
            {
                double val = maxA * i / yTicks;
                int py = padT + chartH - (int)(chartH * i / yTicks);
                g.DrawLine(gridPen, padL, py, padL + chartW, py);
                g.DrawLine(axisPen, padL - 4, py, padL, py);
                string label = val >= 1000 ? $"{val / 1000:F1}k" : $"{val:F0}";
                g.DrawString(label, tickFont, tickBrush, padL - 50, py - 6);
            }

            // X-axis label
            using var axisFont = new Font("Arial", 7f);
            g.DrawString("Frame", axisFont, tickBrush, padL + chartW / 2 - 15, h - padB + 18);
            g.DrawString("a (m/s²)", axisFont, tickBrush, 2, padT + chartH / 2 - 20,
                new StringFormat { FormatFlags = StringFormatFlags.DirectionVertical });

            // Data line
            if (accels.Count >= 2)
            {
                using var dataPen = new Pen(Color.SteelBlue, 2f);
                var pts = new PointF[accels.Count];
                for (int i = 0; i < accels.Count; i++)
                {
                    float px = padL + (float)i / (accels.Count - 1) * chartW;
                    float py = padT + chartH - (float)(accels[i] / maxA * chartH);
                    pts[i] = new PointF(px, py);
                }
                g.DrawLines(dataPen, pts);

                // Red dashed vertical line at detachment frame
                float detachX = padL + (float)(accels.Count - 1) / Math.Max(accels.Count - 1, 1) * chartW;
                using var detachPen = new Pen(Color.Red, 1.5f) { DashStyle = DashStyle.Dash };
                g.DrawLine(detachPen, detachX, padT, detachX, padT + chartH);
                g.DrawString("detach", new Font("Arial", 6f), new SolidBrush(Color.Red), detachX + 2, padT + 2);
            }

            _chartBox.Image?.Dispose();
            _chartBox.Image = bmp;
        }

        // =====================================================================
        // Drawing helper — compose frame + overlay
        // =====================================================================
        private void RedrawPictureBox()
        {
            if (_currentFrame == null) return;

            Bitmap composite = new Bitmap(_frameWidth, _frameHeight);
            using var g = Graphics.FromImage(composite);
            g.DrawImage(_currentFrame, 0, 0);
            if (_overlayBitmap != null)
                g.DrawImage(_overlayBitmap, 0, 0);

            var old = _pictureBox.Image;
            _pictureBox.Image = composite;
            old?.Dispose();
        }

        // =====================================================================
        // Save results
        // =====================================================================
        private void BtnSave_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_videoPath)) { MessageBox.Show("Load a video first.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (_detachmentFrame == 0) { MessageBox.Show("Mark the detachment frame first.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (_radiusMm <= 0) { MessageBox.Show("Measure the insect radius first.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

            if (!double.TryParse(_txtMmPerPixel.Text, out double mmpp)) mmpp = 0;
            if (!double.TryParse(_txtFps.Text,          out double fps))  fps  = 0;
            if (!double.TryParse(_txtAcceleration.Text,  out double acc))  acc  = 0;
            if (!double.TryParse(_txtVelocity.Text,      out double vel))  vel  = 0;

            string outFile = Path.Combine(Path.GetDirectoryName(_videoPath)!, "results_centrifuge.txt");

            // Write header if file does not exist
            if (!File.Exists(outFile))
            {
                File.WriteAllText(outFile,
                    "filename\tframe\tfps\tmm_per_pixel\tradius(mm)\tacceleration(m/s2)\tvelocity(m/s)\r\n");
            }

            string line = string.Format("{0}\t{1}\t{2:F4}\t{3:F6}\t{4:F4}\t{5:F4}\t{6:F4}\r\n",
                Path.GetFileNameWithoutExtension(_videoPath),
                _detachmentFrame,
                fps, mmpp,
                _radiusMm,
                acc, vel);

            File.AppendAllText(outFile, line);

            MessageBox.Show($"Result saved to:\n{outFile}", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // =====================================================================
        // Coordinate helpers
        // =====================================================================
        private System.Drawing.Point PictureBoxToImageCoords(System.Drawing.Point pt)
        {
            if (_frameWidth <= 0 || _frameHeight <= 0) return pt;

            float scale = Math.Min((float)_pictureBox.Width  / _frameWidth,
                                   (float)_pictureBox.Height / _frameHeight);
            int dispW   = (int)(_frameWidth  * scale);
            int dispH   = (int)(_frameHeight * scale);
            int offsetX = (_pictureBox.Width  - dispW) / 2;
            int offsetY = (_pictureBox.Height - dispH) / 2;

            int ix = (int)((pt.X - offsetX) / scale);
            int iy = (int)((pt.Y - offsetY) / scale);
            ix = Math.Max(0, Math.Min(_frameWidth  - 1, ix));
            iy = Math.Max(0, Math.Min(_frameHeight - 1, iy));
            return new System.Drawing.Point(ix, iy);
        }

        private static double Distance(System.Drawing.Point a, System.Drawing.Point b)
            => Math.Sqrt((double)(a.X - b.X) * (a.X - b.X) + (double)(a.Y - b.Y) * (a.Y - b.Y));

        // =====================================================================
        // Simple input prompt (replaces MATLAB inputdlg)
        // =====================================================================
        private static string? PromptDialog(string prompt, string title, string defaultValue)
        {
            Form dlg = new Form
            {
                Text = title,
                Size = new System.Drawing.Size(320, 130),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            Label lbl = new Label { Text = prompt, Location = new System.Drawing.Point(12, 12), Size = new System.Drawing.Size(290, 20) };
            TextBox txt = new TextBox { Text = defaultValue, Location = new System.Drawing.Point(12, 36), Size = new System.Drawing.Size(288, 22), SelectionStart = 0, SelectionLength = defaultValue.Length };
            Button ok  = new Button { Text = "OK",     DialogResult = DialogResult.OK,     Location = new System.Drawing.Point(130, 66), Size = new System.Drawing.Size(75, 26) };
            Button can = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new System.Drawing.Point(215, 66), Size = new System.Drawing.Size(75, 26) };
            dlg.Controls.AddRange(new Control[] { lbl, txt, ok, can });
            dlg.AcceptButton = ok;
            dlg.CancelButton = can;
            dlg.ActiveControl = txt;

            return dlg.ShowDialog() == DialogResult.OK ? txt.Text.Trim() : null;
        }

        // =====================================================================
        // Cleanup
        // =====================================================================
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _videoCapture?.Dispose();
            _currentFrame?.Dispose();
            _overlayBitmap?.Dispose();
        }
    }
}
