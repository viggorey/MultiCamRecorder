using System;
using System.Collections.Generic;
using System.Windows.Forms;
using TIS.Imaging;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using OpenCvSharp;  // ← ADD THIS LINE
using OpenCvSharp.Extensions;  // ← ADD THIS LINE TOO
using System.Runtime.InteropServices;


namespace MultiCamRecorder
{
    // User settings class for persistent storage
    public class UserSettings
    {
        public string WorkingFolder { get; set; } = "";
        public string FfmpegPath { get; set; } = "";
        
        public static string GetSettingsPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appData, "MultiCamRecorder");
            Directory.CreateDirectory(appFolder);
            return Path.Combine(appFolder, "settings.json");
        }
        public Dictionary<string, CameraSettings> CameraSettingsByDevice { get; set; } = new Dictionary<string, CameraSettings>();
        public List<CameraNameProfile> NameProfiles { get; set; } = new List<CameraNameProfile>();
        public string LastUsedProfile { get; set; } = "";
        public static UserSettings Load()
        {
            try
            {
                string path = GetSettingsPath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
                }
            }
            catch { }
            return new UserSettings();
        }
        
        public void Save()
        {
            try
            {
                string path = GetSettingsPath();
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { }
        }
    }

    public class CameraNameProfile
    {
        public string ProfileName { get; set; } = "";
        public Dictionary<string, string> CameraNames { get; set; } = new Dictionary<string, string>();
    }

    public partial class Form1 : Form
    {
        // Class to hold information about each camera
        private class CameraControl
        {
            public ICImagingControl ImagingControl { get; set; }
            public Label NameLabel { get; set; }
            public Label FpsLabel { get; set; }
            public BaseSink? OriginalSink { get; set; }
            public Button SettingsButton { get; set; }
            public CameraSettings Settings { get; set; }
            public int FrameCount { get; set; }
            public DateTime LastFpsUpdate { get; set; }
            public string DeviceName { get; set; }
            public string? RecordingFilePath { get; set; }
            public int TotalRecordedFrames { get; set; }
            public DateTime? RecordingStartTime { get; set; }
            public DateTime? RecordingStopTime { get; set; } 
            public TextBox NameTextBox { get; set; }  // ← ADD THIS LINE
            public string CustomName { get; set; }
            public Queue<System.Drawing.Bitmap>? LoopBuffer { get; set; }
            public int MaxLoopFrames { get; set; }
            public object LoopBufferLock { get; set; }
            public Task? LoopCaptureTask { get; set; }
            public CancellationTokenSource? LoopCancelToken { get; set; }
            public long LastFrameNumber { get; set; } = -1;
            public int DroppedFrames { get; set; }
            public long ExpectedFrameCount { get; set; }
            // Timelapse fields
            public bool IsTimelapseMode { get; set; }
            public DateTime? LastTimelapseCapture { get; set; }
            public int TimelapseFrameCount { get; set; }
            public List<string>? TimelapseImagePaths { get; set; }
            public double TimelapseIntervalSeconds { get; set; }
            
            public CameraControl(string deviceName)
            {
                ImagingControl = new ICImagingControl();
                NameLabel = new Label();
                FpsLabel = new Label();
                OriginalSink = null;
                FrameCount = 0;
                LastFpsUpdate = DateTime.Now;
                DeviceName = deviceName;
                RecordingFilePath = null;
                TotalRecordedFrames = 0;
                RecordingStartTime = null;
                RecordingStopTime = null; 
                SettingsButton = new Button();
                Settings = new CameraSettings();
                NameTextBox = new TextBox();           // ← ADD THIS LINE
                CustomName = "";    
                LoopBuffer = null;
                MaxLoopFrames = 0;
                LoopBufferLock = new object();
            }
        }
        private class SystemInfo
        {
            public double TotalRamGB { get; private set; }
            public double AvailableRamGB { get; private set; }
            public int LogicalCores { get; private set; }
            public int PhysicalCores { get; private set; }
            
            public SystemInfo()
            {
                try
                {
                    // Get RAM info
                    var computerInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();
                    TotalRamGB = computerInfo.TotalPhysicalMemory / (1024.0 * 1024.0 * 1024.0);
                    AvailableRamGB = computerInfo.AvailablePhysicalMemory / (1024.0 * 1024.0 * 1024.0);
                    
                    // Get CPU info
                    LogicalCores = Environment.ProcessorCount;
                    
                    // Estimate physical cores (usually half of logical cores due to hyperthreading)
                    PhysicalCores = LogicalCores / 2;
                    if (PhysicalCores < 1) PhysicalCores = LogicalCores;
                }
                catch
                {
                    // Fallback values if detection fails
                    TotalRamGB = 8.0;
                    AvailableRamGB = 4.0;
                    LogicalCores = 4;
                    PhysicalCores = 2;
                }
            }
            
            public string GetSummary()
            {
                return $"RAM: {AvailableRamGB:F1}/{TotalRamGB:F1} GB available | CPU: {PhysicalCores} cores ({LogicalCores} logical)";
            }
        }

        // List to hold all camera controls
        private List<CameraControl> cameras = new List<CameraControl>();
        
        // Control buttons
        private Button btnRefreshCameras = null!;
        private Button btnStartLive = null!;
        private Button btnStopLive = null!;
        private Button btnStartRecording = null!;
        private Button btnStopRecording = null!;
        private Button btnScreenshot = null!;
        private NumericUpDown numTimelapseHours = null!;
        private NumericUpDown numTimelapseMinutes = null!;
        private NumericUpDown numTimelapseSeconds = null!;
        private System.Windows.Forms.Timer? timelapseTimer = null;
        private CheckBox chkLoopRecording = null!;
        
        // Working folder controls
        private Label lblWorkingFolder = null!;
        private TextBox txtWorkingFolder = null!;
        private Button btnBrowseWorkingFolder = null!;
        private ComboBox cmbRecordingMode = null!;
        private Label lblLoopDuration = null!;
        private NumericUpDown numLoopDuration = null!;
        private Label lblExternalFps = null!;
        private NumericUpDown numExternalTriggerFps = null!;
        private Label lblTimelapseDuration = null!;
        private Label lblTimelapseOutputFps = null!;
        private CheckBox chkKeepTimelapseImages = null!;
        private Label lblRamEstimate = null!;
        private CheckBox chkMaxDuration = null!;
        private NumericUpDown numMaxMinutes = null!;
        private UserSettings settings = null!;     
        private SystemInfo systemInfo = null!;
        
        // Recording state
        private bool isRecording = false;
        private string currentRecordingBaseName = ""; // Store the timestamp-based name
        private bool wasLiveBeforeSettings = false;
        private bool isBenchmarkMode = false;
        private ComboBox cmbMaxDurationUnit = null!;


        private bool IsOneDrivePath(string path)
        {
            return path.Contains("OneDrive", StringComparison.OrdinalIgnoreCase);
        }

        // Timer for FPS calculation
        private System.Windows.Forms.Timer fpsTimer = null!;
        
        // FFmpeg path (configurable)
        private string FFMPEG_PATH
        {
            get
            {
                // Use saved path if available and valid
                if (!string.IsNullOrEmpty(settings.FfmpegPath) && File.Exists(settings.FfmpegPath))
                {
                    return settings.FfmpegPath;
                }
                
                // Try to auto-detect
                string? detectedPath = DetectFfmpegPath();
                if (!string.IsNullOrEmpty(detectedPath) && File.Exists(detectedPath))
                {
                    // Save the detected path for future use
                    settings.FfmpegPath = detectedPath;
                    settings.Save();
                    return detectedPath;
                }
                
                // Fallback to default location
                return @"C:\Program Files\The Imaging Source Europe GmbH\ffmpeg-8.0-essentials_build\bin\ffmpeg.exe";
            }
        }
        
        // Layout constants
        private const int CAMERA_WIDTH = 320;
        private const int CAMERA_HEIGHT = 240;
        private const int CAMERA_SPACING = 10;
        private const int TOP_MARGIN = 145; // Increased for menu + buttons + working folder
        private const int SIDE_MARGIN = 10;
        private int expandedCameraIndex = -1;
        private int currentPreviewWidth = 320;
        private int currentPreviewHeight = 240;
        private ComboBox cmbPreviewSize = null!;
        private ComboBox cmbLayoutMode = null!;

        private System.Windows.Forms.Timer diskMonitorTimer = null!;
        // Add near line 160, with other private fields
        private HashSet<string> excludedCameraDevices = new HashSet<string>();
        private long lastTotalBytesWritten = 0;

        public Form1()
        {
            InitializeComponent();
            this.Resize += (s, e) =>
            {
                if (expandedCameraIndex >= 0)
                {
                    UpdateExpandedLayout();
                }
            };
            settings = UserSettings.Load();
            systemInfo = new SystemInfo();  // ← ADD THIS LINE
            SetupMainWindow();
            SetupMenu();
            SetupFpsTimer();
            DetectAndSetupCameras();
            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown; // ← ADD THIS LINE
        }

        private void Form1_KeyDown(object? sender, KeyEventArgs e)
        {
            // Ctrl+R - Start/Stop Recording
            if (e.Control && e.KeyCode == Keys.R)
            {
                if (btnStartRecording.Enabled)
                    BtnStartRecording_Click(null, EventArgs.Empty);
                else if (btnStopRecording.Enabled)
                    BtnStopRecording_Click(null, EventArgs.Empty);
                e.Handled = true;
            }
            // Ctrl+L - Start/Stop Live
            else if (e.Control && e.KeyCode == Keys.L)
            {
                if (btnStartLive.Enabled)
                    BtnStartLive_Click(null, EventArgs.Empty);
                else if (btnStopLive.Enabled)
                    BtnStopLive_Click(null, EventArgs.Empty);
                e.Handled = true;
            }
            // F5 - Refresh Cameras
            else if (e.KeyCode == Keys.F5)
            {
                BtnRefreshCameras_Click(null, EventArgs.Empty);
                e.Handled = true;
            }
            // Ctrl+B - Run Frame Drop Tester
            else if (e.Control && e.KeyCode == Keys.B)
            {
                RunRecordingTest();
                e.Handled = true;
            }
            // Escape - Collapse expanded camera
            else if (e.KeyCode == Keys.Escape)
            {
                if (expandedCameraIndex >= 0)
                {
                    CollapseExpandedCamera();
                    e.Handled = true;
                }
            }
            // Ctrl+S - Screenshot
            else if (e.Control && e.KeyCode == Keys.S)
            {
                if (btnScreenshot.Enabled)
                    BtnScreenshot_Click(null, EventArgs.Empty);
                e.Handled = true;
            }
        } 

        private void SetupMenu()
        {
            MenuStrip menuStrip = new MenuStrip();
            
            // File menu
            ToolStripMenuItem fileMenu = new ToolStripMenuItem("File");
            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => Application.Exit();
            fileMenu.DropDownItems.Add(exitItem);
            
            // Tools menu
            ToolStripMenuItem toolsMenu = new ToolStripMenuItem("Tools");
            ToolStripMenuItem convertItem = new ToolStripMenuItem("Convert AVIs to MP4...");
            convertItem.Click += ConvertAvisMenuItem_Click;
            toolsMenu.DropDownItems.Add(convertItem);
            ToolStripMenuItem trimItem = new ToolStripMenuItem("Trim Videos...");
            trimItem.Click += TrimVideosMenuItem_Click;
            toolsMenu.DropDownItems.Add(trimItem);

            toolsMenu.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem profilesItem = new ToolStripMenuItem("Camera Name Profiles...");
            profilesItem.Click += (s, e) => ManageCameraProfiles();
            toolsMenu.DropDownItems.Add(profilesItem);

            toolsMenu.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem capacityCalcItem = new ToolStripMenuItem("Capacity Calculator...");
            capacityCalcItem.Click += (s, e) => ShowCapacityCalculator();
            toolsMenu.DropDownItems.Add(capacityCalcItem);
            ToolStripMenuItem recordingTestItem = new ToolStripMenuItem("Recording Test...");
            recordingTestItem.Click += (s, e) => RunRecordingTest();
            toolsMenu.DropDownItems.Add(recordingTestItem);

            toolsMenu.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem ffmpegSettingsItem = new ToolStripMenuItem("FFmpeg Settings...");
            ffmpegSettingsItem.Click += (s, e) => ConfigureFfmpegPath();
            toolsMenu.DropDownItems.Add(ffmpegSettingsItem);
            
            // Help menu
            ToolStripMenuItem helpMenu = new ToolStripMenuItem("Help");

            ToolStripMenuItem aboutItem = new ToolStripMenuItem("About");
            aboutItem.Click += (s, e) => MessageBox.Show(
                "Multi-Camera Recorder\n\n" +
                "Version 2.0\n\n" +
                "Synchronized multi-camera recording with accurate frame rate control.\n\n" +
                "KEYBOARD SHORTCUTS:\n" +
                "• Ctrl+L - Start/Stop Live Preview\n" +
                "• Ctrl+R - Start/Stop Recording\n" +
                "• Ctrl+B - Recording Test\n" +
                "• Ctrl+S - Screenshot\n" +
                "• F5 - Refresh Cameras\n",
                "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
            helpMenu.DropDownItems.Add(aboutItem);

            menuStrip.Items.Add(fileMenu);
            menuStrip.Items.Add(toolsMenu);
            menuStrip.Items.Add(helpMenu);
            
            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);
        }

        /// <summary>
        /// Auto-detects FFmpeg path by checking common installation locations and PATH environment variable
        /// </summary>
        private string? DetectFfmpegPath()
        {
            // Common installation paths to check
            string[] commonPaths = new[]
            {
                @"C:\Program Files\The Imaging Source Europe GmbH\ffmpeg-8.0-essentials_build\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
                @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ffmpeg", "bin", "ffmpeg.exe"),
            };

            // Check common paths
            foreach (string path in commonPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            // Check PATH environment variable
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                string[] paths = pathEnv.Split(Path.PathSeparator);
                foreach (string dir in paths)
                {
                    if (!string.IsNullOrEmpty(dir))
                    {
                        string ffmpegPath = Path.Combine(dir, "ffmpeg.exe");
                        if (File.Exists(ffmpegPath))
                        {
                            return ffmpegPath;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Opens a dialog to configure the FFmpeg path
        /// </summary>
        private void ConfigureFfmpegPath()
        {
            using (Form configForm = new Form())
            {
                configForm.Text = "FFmpeg Settings";
                configForm.Size = new System.Drawing.Size(600, 200);
                configForm.StartPosition = FormStartPosition.CenterParent;
                configForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                configForm.MaximizeBox = false;
                configForm.MinimizeBox = false;

                Label lblInfo = new Label
                {
                    Text = "FFmpeg Path:",
                    Location = new System.Drawing.Point(10, 15),
                    Size = new System.Drawing.Size(100, 20),
                    AutoSize = true
                };
                configForm.Controls.Add(lblInfo);

                TextBox txtPath = new TextBox
                {
                    Text = settings.FfmpegPath,
                    Location = new System.Drawing.Point(10, 40),
                    Size = new System.Drawing.Size(450, 25),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
                };
                configForm.Controls.Add(txtPath);

                Button btnBrowse = new Button
                {
                    Text = "Browse...",
                    Location = new System.Drawing.Point(470, 38),
                    Size = new System.Drawing.Size(80, 25),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right
                };
                btnBrowse.Click += (s, e) =>
                {
                    using (OpenFileDialog dlg = new OpenFileDialog())
                    {
                        dlg.Filter = "FFmpeg executable|ffmpeg.exe|All files|*.*";
                        dlg.FileName = "ffmpeg.exe";
                        if (dlg.ShowDialog() == DialogResult.OK)
                        {
                            txtPath.Text = dlg.FileName;
                        }
                    }
                };
                configForm.Controls.Add(btnBrowse);

                Button btnAutoDetect = new Button
                {
                    Text = "Auto-Detect",
                    Location = new System.Drawing.Point(10, 75),
                    Size = new System.Drawing.Size(100, 25)
                };
                btnAutoDetect.Click += (s, e) =>
                {
                    string? detected = DetectFfmpegPath();
                    if (!string.IsNullOrEmpty(detected))
                    {
                        txtPath.Text = detected;
                        MessageBox.Show($"FFmpeg found at:\n{detected}", "Auto-Detect", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show("FFmpeg not found in common locations or PATH.\nPlease browse to the location manually.", "Auto-Detect", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                };
                configForm.Controls.Add(btnAutoDetect);

                Label lblCurrent = new Label
                {
                    Text = $"Current: {FFMPEG_PATH}",
                    Location = new System.Drawing.Point(10, 110),
                    Size = new System.Drawing.Size(550, 30),
                    AutoSize = false
                };
                if (!File.Exists(FFMPEG_PATH))
                {
                    lblCurrent.ForeColor = System.Drawing.Color.Red;
                    lblCurrent.Text += " (NOT FOUND)";
                }
                configForm.Controls.Add(lblCurrent);

                Button btnOK = new Button
                {
                    Text = "OK",
                    DialogResult = DialogResult.OK,
                    Location = new System.Drawing.Point(400, 130),
                    Size = new System.Drawing.Size(75, 25),
                    Anchor = AnchorStyles.Bottom | AnchorStyles.Right
                };
                configForm.Controls.Add(btnOK);

                Button btnCancel = new Button
                {
                    Text = "Cancel",
                    DialogResult = DialogResult.Cancel,
                    Location = new System.Drawing.Point(480, 130),
                    Size = new System.Drawing.Size(75, 25),
                    Anchor = AnchorStyles.Bottom | AnchorStyles.Right
                };
                configForm.Controls.Add(btnCancel);

                configForm.AcceptButton = btnOK;
                configForm.CancelButton = btnCancel;

                if (configForm.ShowDialog() == DialogResult.OK)
                {
                    string newPath = txtPath.Text.Trim();
                    if (string.IsNullOrEmpty(newPath))
                    {
                        // Clear the setting to use auto-detect
                        settings.FfmpegPath = "";
                        settings.Save();
                        MessageBox.Show("FFmpeg path cleared. Auto-detection will be used on next startup.", "Settings Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else if (File.Exists(newPath))
                    {
                        settings.FfmpegPath = newPath;
                        settings.Save();
                        MessageBox.Show($"FFmpeg path saved:\n{newPath}", "Settings Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show($"The specified path does not exist:\n{newPath}\n\nPlease verify the path and try again.", "Invalid Path", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        private void SetupMainWindow()
        {
            this.Text = "Multi-Camera Recorder";
            this.Size = new System.Drawing.Size(1400, 485);
            this.AutoScroll = true;

            // Create control buttons at the top (below menu bar)
            int buttonY = 30; // Moved down for menu bar
            int buttonX = 10;
            int buttonWidth = 120;
            int buttonSpacing = 130;

            btnRefreshCameras = new Button
            {
                Text = "Refresh Cameras",
                Location = new System.Drawing.Point(buttonX, buttonY),
                Size = new System.Drawing.Size(buttonWidth, 30)
            };
            btnRefreshCameras.Click += BtnRefreshCameras_Click;
            this.Controls.Add(btnRefreshCameras);

            btnStartLive = new Button
            {
                Text = "Start All Live",
                Location = new System.Drawing.Point(buttonX + buttonSpacing, buttonY),
                Size = new System.Drawing.Size(buttonWidth, 30),
                Enabled = false
            };
            btnStartLive.Click += BtnStartLive_Click;
            this.Controls.Add(btnStartLive);

            btnStopLive = new Button
            {
                Text = "Stop All Live",
                Location = new System.Drawing.Point(buttonX + buttonSpacing * 2, buttonY),
                Size = new System.Drawing.Size(buttonWidth, 30),
                Enabled = false
            };
            btnStopLive.Click += BtnStopLive_Click;
            this.Controls.Add(btnStopLive);

            btnStartRecording = new Button
            {
                Text = "Start Recording",
                Location = new System.Drawing.Point(buttonX + buttonSpacing * 3, buttonY),
                Size = new System.Drawing.Size(buttonWidth, 30),
                Enabled = false
            };
            btnStartRecording.Click += BtnStartRecording_Click;
            this.Controls.Add(btnStartRecording);

            btnStopRecording = new Button
            {
                Text = "Stop Recording",
                Location = new System.Drawing.Point(buttonX + buttonSpacing * 4, buttonY),
                Size = new System.Drawing.Size(buttonWidth, 30),
                Enabled = false
            };
            btnStopRecording.Click += BtnStopRecording_Click;
            this.Controls.Add(btnStopRecording);

            // Add after btnStopRecording setup (around line 258)
            Button btnManageCameras = new Button
            {
                Text = "Manage Cameras",
                Location = new System.Drawing.Point(buttonX + buttonSpacing * 5, buttonY),
                Size = new System.Drawing.Size(buttonWidth, 30)
            };
            btnManageCameras.Click += BtnManageCameras_Click;
            this.Controls.Add(btnManageCameras);

            // Add Screenshot button
            btnScreenshot = new Button  // ← Changed from "Button btnScreenshot" to "btnScreenshot"
            {
                Text = "📷 Screenshot",
                Location = new System.Drawing.Point(buttonX + buttonSpacing * 6, buttonY),
                Size = new System.Drawing.Size(buttonWidth, 30),
                Enabled = false
            };
            btnScreenshot.Click += BtnScreenshot_Click;
            this.Controls.Add(btnScreenshot);
            
            // Working folder controls
            int folderY = buttonY + 40;
            
            lblWorkingFolder = new Label
            {
                Text = "Working Folder:",
                Location = new System.Drawing.Point(buttonX, folderY + 5),
                Size = new System.Drawing.Size(100, 20),
                Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold)
            };
            this.Controls.Add(lblWorkingFolder);
            
            txtWorkingFolder = new TextBox
            {
                Location = new System.Drawing.Point(buttonX + 105, folderY),
                Size = new System.Drawing.Size(400, 25),
                Text = string.IsNullOrEmpty(settings.WorkingFolder) ? @"C:\CameraRecordings" : settings.WorkingFolder,
                ReadOnly = true
            };
            this.Controls.Add(txtWorkingFolder);
            
            btnBrowseWorkingFolder = new Button
            {
                Text = "📁",
                Location = new System.Drawing.Point(buttonX + 510, folderY - 2),
                Size = new System.Drawing.Size(35, 28),
                Font = new System.Drawing.Font("Segoe UI", 10)
            };
            btnBrowseWorkingFolder.Click += BtnBrowseWorkingFolder_Click;
            this.Controls.Add(btnBrowseWorkingFolder);

            int loopY = folderY + 35;

            // Recording Mode Dropdown
            Label lblRecordingMode = new Label
            {
                Text = "Recording Mode:",
                Location = new System.Drawing.Point(buttonX, loopY + 4),
                Size = new System.Drawing.Size(110, 20),
                Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold)
            };
            this.Controls.Add(lblRecordingMode);

            ComboBox cmbRecordingMode = new ComboBox
            {
                Location = new System.Drawing.Point(buttonX + 115, loopY),
                Size = new System.Drawing.Size(140, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new System.Drawing.Font("Arial", 9)
            };
            cmbRecordingMode.Items.AddRange(new object[] {
                "Normal Recording",
                "Timelapse",
                "Loop Recording"
            });
            cmbRecordingMode.SelectedIndex = 0; // Default to Normal
            this.Controls.Add(cmbRecordingMode);

            // Event handler to show/hide controls based on recording mode
            cmbRecordingMode.SelectedIndexChanged += (s, e) =>
            {
                string selectedMode = cmbRecordingMode.SelectedItem?.ToString() ?? "";
                
                // Hide ALL mode-specific controls first
                lblLoopDuration.Visible = false;
                numLoopDuration.Visible = false;
                lblExternalFps.Visible = false;
                numExternalTriggerFps.Visible = false;
                lblRamEstimate.Visible = false;  // ← ADD THIS
                
                // Hide all timelapse controls
                foreach (Control ctrl in this.Controls)
                {
                    if (ctrl.Tag?.ToString() == "timelapse")
                    {
                        ctrl.Visible = false;
                    }
                }
                
                // Show max duration for all modes
                chkMaxDuration.Visible = true;
                numMaxMinutes.Visible = chkMaxDuration.Checked;
                cmbMaxDurationUnit.Visible = chkMaxDuration.Checked;
                
                // Show mode-specific controls
                if (selectedMode == "Loop Recording")
                {
                    lblLoopDuration.Visible = true;
                    numLoopDuration.Visible = true;
                    lblExternalFps.Visible = true;
                    numExternalTriggerFps.Visible = true;
                    lblRamEstimate.Visible = true;  // ← ADD THIS
                    UpdateRamEstimate();
                }
                else if (selectedMode == "Timelapse")
                {
                    // Show timelapse controls
                    foreach (Control ctrl in this.Controls)
                    {
                        if (ctrl.Tag?.ToString() == "timelapse")
                        {
                            ctrl.Visible = true;
                        }
                    }
                }
                // Normal Recording shows nothing extra
            };

            // Store reference for later use
            cmbRecordingMode.Tag = "recordingMode";

            // Loop Recording Controls (hidden by default)
            lblLoopDuration = new Label
            {
                Text = "Duration (sec):",
                Location = new System.Drawing.Point(buttonX + 265, loopY + 4),
                Size = new System.Drawing.Size(90, 20),
                Font = new System.Drawing.Font("Arial", 8),
                Visible = false
            };
            this.Controls.Add(lblLoopDuration);

            numLoopDuration = new NumericUpDown
            {
                Location = new System.Drawing.Point(buttonX + 360, loopY),
                Size = new System.Drawing.Size(60, 25),
                Minimum = 2,
                Maximum = 60,
                Value = 10,
                Visible = false
            };
            this.Controls.Add(numLoopDuration);

            chkLoopRecording = new CheckBox
            {
                Text = "Loop Recording",
                Location = new System.Drawing.Point(buttonX + 130, loopY),
                Size = new System.Drawing.Size(120, 25),
                Checked = false,
                Visible = false
            };
            this.Controls.Add(chkLoopRecording);

            // Timelapse Controls (hidden by default)
            Label lblTimelapseInterval = new Label
            {
                Text = "Frame every:",
                Location = new System.Drawing.Point(buttonX + 265, loopY + 4),
                Size = new System.Drawing.Size(80, 20),
                Font = new System.Drawing.Font("Arial", 8),
                Visible = false,
                Tag = "timelapse"
            };
            this.Controls.Add(lblTimelapseInterval);

            numTimelapseHours = new NumericUpDown
            {
                Location = new System.Drawing.Point(buttonX + 350, loopY),
                Size = new System.Drawing.Size(50, 25),
                Minimum = 0,
                Maximum = 23,
                Value = 0,
                Visible = false,
                Tag = "timelapse"
            };
            this.Controls.Add(numTimelapseHours);

            Label lblHours = new Label
            {
                Text = "h",
                Location = new System.Drawing.Point(buttonX + 405, loopY + 4),
                Size = new System.Drawing.Size(15, 20),
                Font = new System.Drawing.Font("Arial", 8),
                Visible = false,
                Tag = "timelapse"
            };
            this.Controls.Add(lblHours);

            numTimelapseMinutes = new NumericUpDown
            {
                Location = new System.Drawing.Point(buttonX + 425, loopY),
                Size = new System.Drawing.Size(50, 25),
                Minimum = 0,
                Maximum = 59,
                Value = 1,
                Visible = false,
                Tag = "timelapse"
            };
            this.Controls.Add(numTimelapseMinutes);

            Label lblMinutes = new Label
            {
                Text = "m",
                Location = new System.Drawing.Point(buttonX + 480, loopY + 4),
                Size = new System.Drawing.Size(15, 20),
                Font = new System.Drawing.Font("Arial", 8),
                Visible = false,
                Tag = "timelapse"
            };
            this.Controls.Add(lblMinutes);

            numTimelapseSeconds = new NumericUpDown
            {
                Location = new System.Drawing.Point(buttonX + 500, loopY),
                Size = new System.Drawing.Size(50, 25),
                Minimum = 0,
                Maximum = 59,
                Value = 0,
                Visible = false,
                Tag = "timelapse"
            };
            this.Controls.Add(numTimelapseSeconds);

            Label lblSeconds = new Label
            {
                Text = "s",
                Location = new System.Drawing.Point(buttonX + 555, loopY + 4),
                Size = new System.Drawing.Size(15, 20),
                Font = new System.Drawing.Font("Arial", 8),
                Visible = false,
                Tag = "timelapse"
            };
            this.Controls.Add(lblSeconds);

            lblExternalFps = new Label
            {
                Text = "Expected FPS:",
                Location = new System.Drawing.Point(buttonX + 290, loopY + 4),
                Size = new System.Drawing.Size(100, 20),
                Font = new System.Drawing.Font("Arial", 8),
                Visible = false  // ← ADD THIS LINE
            };
            this.Controls.Add(lblExternalFps);

            numExternalTriggerFps = new NumericUpDown
            {
                Location = new System.Drawing.Point(buttonX + 395, loopY),
                Size = new System.Drawing.Size(60, 25),
                Minimum = 1,
                Maximum = 240,
                DecimalPlaces = 1,
                Value = 30,
                Enabled = false,
                Visible = false  // ← ADD THIS LINE
            };
            this.Controls.Add(numExternalTriggerFps);

            // ✅ MAX DURATION CONTROLS (after loop controls):
            chkMaxDuration = new CheckBox
            {
                Text = "Max Duration:",
                Location = new System.Drawing.Point(buttonX + 600, loopY + 2),
                Size = new System.Drawing.Size(110, 20),
                Font = new System.Drawing.Font("Arial", 8),
                Visible = false  // Hidden until recording mode selected
            };
            this.Controls.Add(chkMaxDuration);

            numMaxMinutes = new NumericUpDown
            {
                Location = new System.Drawing.Point(buttonX + 715, loopY),
                Size = new System.Drawing.Size(70, 25),
                Minimum = 1,
                Maximum = 525600, // 365 days in minutes
                Value = 60,
                Enabled = false,
                Visible = false
            };
            this.Controls.Add(numMaxMinutes);

            cmbMaxDurationUnit = new ComboBox
            {
                Location = new System.Drawing.Point(buttonX + 790, loopY),
                Size = new System.Drawing.Size(70, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = false,
                Visible = false
            };
            cmbMaxDurationUnit.Items.AddRange(new object[] { "minutes", "hours", "days" });
            cmbMaxDurationUnit.SelectedIndex = 0;
            this.Controls.Add(cmbMaxDurationUnit);

            chkMaxDuration.CheckedChanged += (s, e) =>
            {
                numMaxMinutes.Enabled = chkMaxDuration.Checked;
                cmbMaxDurationUnit.Enabled = chkMaxDuration.Checked;
                numMaxMinutes.Visible = chkMaxDuration.Checked;
                cmbMaxDurationUnit.Visible = chkMaxDuration.Checked;
            };

            // Store references for later use
            chkMaxDuration.Tag = "maxDurationCheckbox";
            numMaxMinutes.Tag = "maxDurationMinutes";

            // Add after the max duration controls (around line 380)
            int layoutY = loopY + 35;

            Label lblLayout = new Label
            {
                Text = "Preview Size:",
                Location = new System.Drawing.Point(buttonX + 870, loopY + 4),
                Size = new System.Drawing.Size(85, 20),
                Font = new System.Drawing.Font("Arial", 8)
            };
            this.Controls.Add(lblLayout);

            cmbPreviewSize = new ComboBox
            {
                Location = new System.Drawing.Point(buttonX + 960, loopY),
                Size = new System.Drawing.Size(100, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new System.Drawing.Font("Arial", 8)
            };
            cmbPreviewSize.Items.AddRange(new object[] {
                "Small (240x180)",
                "Medium (320x240)",
                "Large (400x300)",
                "XLarge (480x360)"
            });
            cmbPreviewSize.SelectedIndex = 1; // Default to Medium
            cmbPreviewSize.SelectedIndexChanged += (s, e) => UpdateCameraLayout();
            cmbPreviewSize.Tag = "previewSize";
            this.Controls.Add(cmbPreviewSize);

            Label lblLayoutMode = new Label
            {
                Text = "Layout:",
                Location = new System.Drawing.Point(buttonX + 1070, loopY + 4),
                Size = new System.Drawing.Size(45, 20),
                Font = new System.Drawing.Font("Arial", 8)
            };
            this.Controls.Add(lblLayoutMode);

            cmbLayoutMode = new ComboBox
            {
                Location = new System.Drawing.Point(buttonX + 1120, loopY),
                Size = new System.Drawing.Size(90, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new System.Drawing.Font("Arial", 8)
            };
            cmbLayoutMode.Items.AddRange(new object[] {
                "Auto Grid",
                "1 Row",
                "2 Rows",
                "3 Rows",
                "4 Rows"
            });
            cmbLayoutMode.SelectedIndex = 0; // Default to Auto
            cmbLayoutMode.SelectedIndexChanged += (s, e) => UpdateCameraLayout();
            cmbLayoutMode.Tag = "layoutMode";
            this.Controls.Add(cmbLayoutMode);

            lblRamEstimate = new Label
            {
                Text = "Est. RAM: 0 MB",
                Location = new System.Drawing.Point(buttonX + 465, loopY + 4),
                Size = new System.Drawing.Size(150, 20),
                Font = new System.Drawing.Font("Arial", 8),
                ForeColor = System.Drawing.Color.Blue,
                Visible = false  // ← ADD THIS LINE
            };
            this.Controls.Add(lblRamEstimate);

            // Enable/disable controls based on checkbox
            chkLoopRecording.CheckedChanged += (s, e) =>
            {
                if (isBenchmarkMode) return;
                numLoopDuration.Enabled = chkLoopRecording.Checked;
                numExternalTriggerFps.Enabled = chkLoopRecording.Checked;
                UpdateRamEstimate();
            };

            // Update RAM estimate when values change
            numLoopDuration.ValueChanged += (s, e) => UpdateRamEstimate();
            numExternalTriggerFps.ValueChanged += (s, e) => UpdateRamEstimate();

            // Store references
            numExternalTriggerFps.Tag = "externalTriggerFps";
            numLoopDuration.Tag = "loopDuration";
            this.cmbRecordingMode = cmbRecordingMode;
            this.cmbMaxDurationUnit = cmbMaxDurationUnit;
        }
        private System.Drawing.Bitmap DeepCloneBitmap(System.Drawing.Bitmap source)
        {
            // Create a new bitmap with RGB format (safer and more compatible)
            System.Drawing.Bitmap clone = new System.Drawing.Bitmap(
                source.Width, 
                source.Height, 
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            
            // Use Graphics to draw the source onto the clone
            // This is slower but much more reliable
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(clone))
            {
                g.DrawImage(source, 0, 0, source.Width, source.Height);
            }
            
            return clone;
        }
        private void ShowCapacityCalculator()
        {
            Form calcForm = new Form
            {
                Text = "Capacity Calculator",
                Size = new System.Drawing.Size(750, 650),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                AutoScroll = true
            };

            int y = 20;

            // System Information Display
            GroupBox grpSystemInfo = new GroupBox
            {
                Text = "Your System",
                Location = new System.Drawing.Point(20, y),
                Size = new System.Drawing.Size(690, 80),
                Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold)
            };

            Label lblSystemInfo = new Label
            {
                Text = $"Total RAM: {systemInfo.TotalRamGB:F1} GB\n" +
                    $"Available RAM: {systemInfo.AvailableRamGB:F1} GB\n" +
                    $"CPU Cores: {systemInfo.PhysicalCores} physical ({systemInfo.LogicalCores} logical)",
                Location = new System.Drawing.Point(15, 25),
                Size = new System.Drawing.Size(660, 50),
                Font = new System.Drawing.Font("Arial", 9)
            };
            grpSystemInfo.Controls.Add(lblSystemInfo);
            calcForm.Controls.Add(grpSystemInfo);
            y += 90;

            // CALCULATOR SECTION
            GroupBox grpCalc = new GroupBox
            {
                Text = "Camera Configuration",
                Location = new System.Drawing.Point(20, y),
                Size = new System.Drawing.Size(690, 400),
                Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold)
            };

            int calcY = 30;

            // Number of cameras
            Label lblNumCameras = new Label
            {
                Text = "Number of Cameras:",
                Location = new System.Drawing.Point(20, calcY),
                Size = new System.Drawing.Size(150, 20)
            };
            grpCalc.Controls.Add(lblNumCameras);

            NumericUpDown numCameras = new NumericUpDown
            {
                Location = new System.Drawing.Point(180, calcY - 2),
                Size = new System.Drawing.Size(80, 25),
                Minimum = 1,
                Maximum = 12,
                Value = 4
            };
            grpCalc.Controls.Add(numCameras);

            // Loop duration (shared by all cameras)
            Label lblDuration = new Label
            {
                Text = "Loop Duration (sec):",
                Location = new System.Drawing.Point(300, calcY),
                Size = new System.Drawing.Size(150, 20)
            };
            grpCalc.Controls.Add(lblDuration);

            NumericUpDown numDuration = new NumericUpDown
            {
                Location = new System.Drawing.Point(460, calcY - 2),
                Size = new System.Drawing.Size(80, 25),
                Minimum = 1,
                Maximum = 60,
                Value = 5
            };
            grpCalc.Controls.Add(numDuration);
            calcY += 40;

            // Apply to All checkbox
            CheckBox chkApplyAll = new CheckBox
            {
                Text = "Apply Camera 1 settings to all cameras",
                Location = new System.Drawing.Point(20, calcY),
                Size = new System.Drawing.Size(300, 20),
                Checked = true
            };
            grpCalc.Controls.Add(chkApplyAll);
            calcY += 30;

            // Scrollable panel for camera settings
            Panel cameraPanel = new Panel
            {
                Location = new System.Drawing.Point(20, calcY),
                Size = new System.Drawing.Size(650, 200),
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle
            };
            grpCalc.Controls.Add(cameraPanel);

            // Store camera controls
            List<ComboBox> resolutionCombos = new List<ComboBox>();
            List<NumericUpDown> fpsNumerics = new List<NumericUpDown>();

            // Function to rebuild camera controls
            Action rebuildCameraControls = () =>
            {
                cameraPanel.Controls.Clear();
                resolutionCombos.Clear();
                fpsNumerics.Clear();

                int camY = 10;
                int numCams = (int)numCameras.Value;

                for (int i = 0; i < numCams; i++)
                {
                    Label lblCam = new Label
                    {
                        Text = $"Camera {i + 1}:",
                        Location = new System.Drawing.Point(10, camY + 2),
                        Size = new System.Drawing.Size(80, 20),
                        Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold)
                    };
                    cameraPanel.Controls.Add(lblCam);

                    ComboBox cmbRes = new ComboBox
                    {
                        Location = new System.Drawing.Point(100, camY),
                        Size = new System.Drawing.Size(180, 25),
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        Enabled = (i == 0 || !chkApplyAll.Checked)
                    };
                    cmbRes.Items.AddRange(new object[] {
                        "640x480 (Grayscale)",
                        "640x480 (Color)",
                        "1280x720 (Grayscale)",
                        "1280x720 (Color)",
                        "1920x1080 (Color)"
                    });
                    cmbRes.SelectedIndex = 0;
                    cameraPanel.Controls.Add(cmbRes);
                    resolutionCombos.Add(cmbRes);

                    Label lblFpsLabel = new Label
                    {
                        Text = "FPS:",
                        Location = new System.Drawing.Point(300, camY + 2),
                        Size = new System.Drawing.Size(40, 20)
                    };
                    cameraPanel.Controls.Add(lblFpsLabel);

                    NumericUpDown numFps = new NumericUpDown
                    {
                        Location = new System.Drawing.Point(345, camY),
                        Size = new System.Drawing.Size(80, 25),
                        Minimum = 1,
                        Maximum = 240,
                        DecimalPlaces = 1,
                        Value = 108,
                        Enabled = (i == 0 || !chkApplyAll.Checked)
                    };
                    cameraPanel.Controls.Add(numFps);
                    fpsNumerics.Add(numFps);

                    camY += 35;
                }

                // Wire up Apply to All logic
                if (resolutionCombos.Count > 0 && fpsNumerics.Count > 0)
                {
                    resolutionCombos[0].SelectedIndexChanged += (s, e) =>
                    {
                        if (chkApplyAll.Checked)
                        {
                            for (int j = 1; j < resolutionCombos.Count; j++)
                            {
                                resolutionCombos[j].SelectedIndex = resolutionCombos[0].SelectedIndex;
                            }
                        }
                    };

                    fpsNumerics[0].ValueChanged += (s, e) =>
                    {
                        if (chkApplyAll.Checked)
                        {
                            for (int j = 1; j < fpsNumerics.Count; j++)
                            {
                                fpsNumerics[j].Value = fpsNumerics[0].Value;
                            }
                        }
                    };
                }
            };

            // Rebuild when number of cameras changes
            numCameras.ValueChanged += (s, e) => rebuildCameraControls();

            // Handle Apply to All checkbox
            chkApplyAll.CheckedChanged += (s, e) =>
            {
                if (chkApplyAll.Checked && resolutionCombos.Count > 0)
                {
                    // Apply first camera settings to all
                    for (int j = 1; j < resolutionCombos.Count; j++)
                    {
                        resolutionCombos[j].SelectedIndex = resolutionCombos[0].SelectedIndex;
                        resolutionCombos[j].Enabled = false;
                        fpsNumerics[j].Value = fpsNumerics[0].Value;
                        fpsNumerics[j].Enabled = false;
                    }
                }
                else
                {
                    // Enable all controls
                    for (int j = 1; j < resolutionCombos.Count; j++)
                    {
                        resolutionCombos[j].Enabled = true;
                        fpsNumerics[j].Enabled = true;
                    }
                }
            };

            // Initial build
            rebuildCameraControls();

            calcY += 210;

            // Result Label
            Label lblResult = new Label
            {
                Text = "Click Calculate to check compatibility",
                Location = new System.Drawing.Point(20, calcY),
                Size = new System.Drawing.Size(650, 60),
                Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold),
                ForeColor = System.Drawing.Color.Blue
            };
            grpCalc.Controls.Add(lblResult);

            // Calculate Button
            Button btnCalculate = new Button
            {
                Text = "Calculate",
                Location = new System.Drawing.Point(550, 25),
                Size = new System.Drawing.Size(120, 35),
                Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold)
            };

            btnCalculate.Click += (s, e) =>
            {
                int duration = (int)numDuration.Value;
                double totalRamGB = 0;
                double totalProcessingLoad = 0;
                int totalFrames = 0;

                for (int i = 0; i < resolutionCombos.Count; i++)
                {
                    double fps = (double)fpsNumerics[i].Value;
                    string resolution = resolutionCombos[i].SelectedItem.ToString();

                    // Parse resolution and color
                    int width = 640, height = 480;
                    bool isColor = resolution.Contains("Color");

                    if (resolution.Contains("1280x720"))
                    {
                        width = 1280;
                        height = 720;
                    }
                    else if (resolution.Contains("1920x1080"))
                    {
                        width = 1920;
                        height = 1080;
                    }

                    // Calculate RAM usage
                    int bytesPerPixel = isColor ? 3 : 1;
                    long bytesPerFrame = width * height * bytesPerPixel;
                    long bitmapOverhead = (long)(bytesPerFrame * 1.2);
                    int framesPerCamera = (int)(fps * duration);
                    long cameraRamBytes = framesPerCamera * bitmapOverhead;
                    totalRamGB += cameraRamBytes / (1024.0 * 1024.0 * 1024.0);
                    totalFrames += framesPerCamera;

                    // Calculate processing time per frame
                    double msPerFrame = 1000.0 / fps;
                    double copyTimeMs = 0;

                    if (width == 640 && height == 480)
                        copyTimeMs = isColor ? 1.5 : 1.0;
                    else if (width == 1280 && height == 720)
                        copyTimeMs = isColor ? 3.5 : 2.5;
                    else if (width == 1920 && height == 1080)
                        copyTimeMs = isColor ? 7.0 : 5.0;

                    double processingLoad = (copyTimeMs / msPerFrame) * 100;
                    totalProcessingLoad += processingLoad;
                }

                // Adjust for actual hardware
                double effectiveCores = systemInfo.PhysicalCores * 0.8;
                double adjustedCpuLoad = totalProcessingLoad / effectiveCores;
                double ramPercentage = (totalRamGB / systemInfo.AvailableRamGB) * 100;

                // Determine compatibility
                string verdict = "";
                System.Drawing.Color color = System.Drawing.Color.Green;

                if (ramPercentage > 80 || adjustedCpuLoad > 80)
                {
                    verdict = "❌ NOT RECOMMENDED - Exceeds system capacity";
                    color = System.Drawing.Color.Red;
                }
                else if (ramPercentage > 50 || adjustedCpuLoad > 60)
                {
                    verdict = "⚠️  BORDERLINE - May work but will stress system";
                    color = System.Drawing.Color.Orange;
                }
                else
                {
                    verdict = "✅ COMPATIBLE - Should work well on your system";
                    color = System.Drawing.Color.Green;
                }

                lblResult.Text = $"{verdict}\n" +
                                $"RAM: {totalRamGB:F2} GB ({ramPercentage:F0}% of available) | " +
                                $"CPU: ~{adjustedCpuLoad:F0}% | Total frames: {totalFrames:N0}";
                lblResult.ForeColor = color;
            };

            grpCalc.Controls.Add(btnCalculate);
            calcForm.Controls.Add(grpCalc);
            y += 410;

            // Close Button
            Button btnClose = new Button
            {
                Text = "Close",
                Location = new System.Drawing.Point(310, y),
                Size = new System.Drawing.Size(120, 35),
                DialogResult = DialogResult.OK
            };
            calcForm.Controls.Add(btnClose);

            calcForm.AcceptButton = btnClose;
            calcForm.ShowDialog();
        }
        private void RunRecordingTest()
        {
            if (cameras.Count == 0)
            {
                MessageBox.Show("Please connect and detect cameras first!\n\nClick 'Refresh Cameras' to detect cameras.",
                                "No Cameras", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            if (isRecording)
            {
                MessageBox.Show("Cannot run frame drop tester while recording!",
                                "Busy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            // STEP 1: Choose test type
            Form testTypeForm = new Form
            {
                Text = "Recording Test",
                Size = new System.Drawing.Size(450, 200),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            
            Label lblQuestion = new Label
            {
                Text = "What type of recording do you want to test?",
                Location = new System.Drawing.Point(20, 20),
                Size = new System.Drawing.Size(400, 30),
                Font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold)
            };
            testTypeForm.Controls.Add(lblQuestion);
            
            Button btnLoopTest = new Button
            {
                Text = "Loop Recording Test",
                Location = new System.Drawing.Point(40, 70),
                Size = new System.Drawing.Size(160, 50),
                Font = new System.Drawing.Font("Arial", 10)
            };
            testTypeForm.Controls.Add(btnLoopTest);
            
            Button btnVideoTest = new Button
            {
                Text = "Video Recording Test",
                Location = new System.Drawing.Point(230, 70),
                Size = new System.Drawing.Size(160, 50),
                Font = new System.Drawing.Font("Arial", 10)
            };
            testTypeForm.Controls.Add(btnVideoTest);
            
            Button btnCancel = new Button
            {
                Text = "Cancel",
                Location = new System.Drawing.Point(165, 130),
                Size = new System.Drawing.Size(100, 30),
                DialogResult = DialogResult.Cancel
            };
            testTypeForm.Controls.Add(btnCancel);
            testTypeForm.CancelButton = btnCancel;
            
            bool isLoopTest = false;
            bool testChosen = false;
            
            btnLoopTest.Click += (s, e) =>
            {
                isLoopTest = true;
                testChosen = true;
                testTypeForm.Close();
            };
            
            btnVideoTest.Click += (s, e) =>
            {
                isLoopTest = false;
                testChosen = true;
                testTypeForm.Close();
            };
            
            testTypeForm.ShowDialog();
            
            if (!testChosen)
                return;
            
            // STEP 2: Configure test parameters
            Dictionary<int, double> cameraExpectedFps = new Dictionary<int, double>();
            double testDuration = 0;
            
            if (isLoopTest)
            {
                // Configure Loop Test
                if (!ConfigureLoopTest(out cameraExpectedFps, out testDuration))
                    return;
            }
            else
            {
                // Configure Video Test
                if (!ConfigureVideoTest(out cameraExpectedFps, out testDuration))
                    return;
            }
            
            // STEP 3: Run the test
            if (isLoopTest)
            {
                RunLoopRecordingTest(cameraExpectedFps, testDuration);
            }
            else
            {
                RunVideoRecordingTest(cameraExpectedFps, testDuration);
            }
        }

        private bool ConfigureLoopTest(out Dictionary<int, double> cameraExpectedFps, out double loopDuration)
        {
            cameraExpectedFps = new Dictionary<int, double>();
            loopDuration = 5.0;
            
            Form configForm = new Form
            {
                Text = "Configure Loop Recording Test",
                Size = new System.Drawing.Size(500, 200 + (cameras.Count * 40)),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            
            int y = 20;
            
            Label lblInfo = new Label
            {
                Text = "Set expected FPS for each camera:",
                Location = new System.Drawing.Point(20, y),
                Size = new System.Drawing.Size(450, 20),
                Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold)
            };
            configForm.Controls.Add(lblInfo);
            y += 30;
            
            // FPS controls for each camera
            List<NumericUpDown> fpsControls = new List<NumericUpDown>();
            CheckBox chkApplyAll = null;
            
            for (int i = 0; i < cameras.Count; i++)
            {
                Label lblCamera = new Label
                {
                    Text = $"{cameras[i].CustomName}:",
                    Location = new System.Drawing.Point(40, y + 2),
                    Size = new System.Drawing.Size(150, 20)
                };
                configForm.Controls.Add(lblCamera);
                
                NumericUpDown numFps = new NumericUpDown
                {
                    Location = new System.Drawing.Point(200, y),
                    Size = new System.Drawing.Size(80, 25),
                    Minimum = 1,
                    Maximum = 240,
                    DecimalPlaces = 1,
                    Value = cameras[i].Settings.UseExternalTrigger ? (decimal)numExternalTriggerFps.Value : (decimal)cameras[i].Settings.SoftwareFrameRate  // ← FIXED
                };
                configForm.Controls.Add(numFps);
                fpsControls.Add(numFps);
                
                Label lblFpsUnit = new Label
                {
                    Text = "fps",
                    Location = new System.Drawing.Point(290, y + 2),
                    Size = new System.Drawing.Size(30, 20)
                };
                configForm.Controls.Add(lblFpsUnit);
                
                // Add "Apply to All" checkbox after first camera
                if (i == 0)
                {
                    chkApplyAll = new CheckBox
                    {
                        Text = "Apply to All",
                        Location = new System.Drawing.Point(330, y + 2),
                        Size = new System.Drawing.Size(120, 20)
                    };
                    
                    chkApplyAll.CheckedChanged += (s, e) =>
                    {
                        if (chkApplyAll.Checked)
                        {
                            double firstFps = (double)fpsControls[0].Value;
                            for (int j = 1; j < fpsControls.Count; j++)
                            {
                                fpsControls[j].Value = (decimal)firstFps;
                                fpsControls[j].Enabled = false;
                            }
                        }
                        else
                        {
                            for (int j = 1; j < fpsControls.Count; j++)
                            {
                                fpsControls[j].Enabled = true;
                            }
                        }
                    };
                    
                    // Also apply when first camera FPS changes and "Apply to All" is checked
                    fpsControls[0].ValueChanged += (s, e) =>
                    {
                        if (chkApplyAll.Checked)
                        {
                            double firstFps = (double)fpsControls[0].Value;
                            for (int j = 1; j < fpsControls.Count; j++)
                            {
                                fpsControls[j].Value = (decimal)firstFps;
                            }
                        }
                    };
                    
                    configForm.Controls.Add(chkApplyAll);
                }
                
                y += 35;
            }
            
            // Loop duration
            y += 10;
            Label lblDuration = new Label
            {
                Text = "Loop Duration:",
                Location = new System.Drawing.Point(40, y + 2),
                Size = new System.Drawing.Size(150, 20),
                Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold)
            };
            configForm.Controls.Add(lblDuration);
            
            NumericUpDown numDuration = new NumericUpDown
            {
                Location = new System.Drawing.Point(200, y),
                Size = new System.Drawing.Size(80, 25),
                Minimum = 1,
                Maximum = 60,
                Value = 5
            };
            configForm.Controls.Add(numDuration);
            
            Label lblDurationUnit = new Label
            {
                Text = "seconds",
                Location = new System.Drawing.Point(290, y + 2),
                Size = new System.Drawing.Size(60, 20)
            };
            configForm.Controls.Add(lblDurationUnit);
            
            y += 40;
            
            Label lblNote = new Label
            {
                Text = $"Test will record for {3}× loop duration (total: 15 seconds)",
                Location = new System.Drawing.Point(40, y),
                Size = new System.Drawing.Size(420, 20),
                ForeColor = System.Drawing.Color.Blue,
                Font = new System.Drawing.Font("Arial", 8)
            };
            configForm.Controls.Add(lblNote);
            
            // Update note dynamically
            numDuration.ValueChanged += (s, e) =>
            {
                int totalSeconds = (int)numDuration.Value * 3;
                lblNote.Text = $"Test will record for 3× loop duration (total: {totalSeconds} seconds)";
            };
            
            y += 30;
            
            // Buttons
            Button btnCancelConfig = new Button
            {
                Text = "Cancel",
                Location = new System.Drawing.Point(220, y),
                Size = new System.Drawing.Size(100, 35),
                DialogResult = DialogResult.Cancel
            };
            configForm.Controls.Add(btnCancelConfig);
            
            Button btnStart = new Button
            {
                Text = "Start Test",
                Location = new System.Drawing.Point(330, y),
                Size = new System.Drawing.Size(120, 35),
                DialogResult = DialogResult.OK
            };
            configForm.Controls.Add(btnStart);
            
            configForm.AcceptButton = btnStart;
            configForm.CancelButton = btnCancelConfig;
            
            if (configForm.ShowDialog() != DialogResult.OK)
                return false;
            
            // Collect settings
            for (int i = 0; i < cameras.Count; i++)
            {
                cameraExpectedFps[i] = (double)fpsControls[i].Value;
            }
            loopDuration = (double)numDuration.Value;
            
            return true;
        }

        private bool ConfigureVideoTest(out Dictionary<int, double> cameraExpectedFps, out double testDuration)
        {
            cameraExpectedFps = new Dictionary<int, double>();
            testDuration = 15.0;
            
            Form configForm = new Form
            {
                Text = "Configure Video Recording Test",
                Size = new System.Drawing.Size(500, 200 + (cameras.Count * 40)),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            
            int y = 20;
            
            Label lblInfo = new Label
            {
                Text = "Set expected FPS for each camera:",
                Location = new System.Drawing.Point(20, y),
                Size = new System.Drawing.Size(450, 20),
                Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold)
            };
            configForm.Controls.Add(lblInfo);
            y += 30;
            
            // FPS controls for each camera
            List<NumericUpDown> fpsControls = new List<NumericUpDown>();
            CheckBox chkApplyAll = null;
            
            for (int i = 0; i < cameras.Count; i++)
            {
                Label lblCamera = new Label
                {
                    Text = $"{cameras[i].CustomName}:",
                    Location = new System.Drawing.Point(40, y + 2),
                    Size = new System.Drawing.Size(150, 20)
                };
                configForm.Controls.Add(lblCamera);
                
                NumericUpDown numFps = new NumericUpDown
                {
                    Location = new System.Drawing.Point(200, y),
                    Size = new System.Drawing.Size(80, 25),
                    Minimum = 1,
                    Maximum = 240,
                    DecimalPlaces = 1,
                    Value = cameras[i].Settings.UseExternalTrigger ? (decimal)numExternalTriggerFps.Value : (decimal)cameras[i].Settings.SoftwareFrameRate  // ← FIXED
                };
                configForm.Controls.Add(numFps);
                fpsControls.Add(numFps);
                
                Label lblFpsUnit = new Label
                {
                    Text = "fps",
                    Location = new System.Drawing.Point(290, y + 2),
                    Size = new System.Drawing.Size(30, 20)
                };
                configForm.Controls.Add(lblFpsUnit);
                
                // Add "Apply to All" checkbox after first camera
                if (i == 0)
                {
                    chkApplyAll = new CheckBox
                    {
                        Text = "Apply to All",
                        Location = new System.Drawing.Point(330, y + 2),
                        Size = new System.Drawing.Size(120, 20)
                    };
                    
                    chkApplyAll.CheckedChanged += (s, e) =>
                    {
                        if (chkApplyAll.Checked)
                        {
                            double firstFps = (double)fpsControls[0].Value;
                            for (int j = 1; j < fpsControls.Count; j++)
                            {
                                fpsControls[j].Value = (decimal)firstFps;
                                fpsControls[j].Enabled = false;
                            }
                        }
                        else
                        {
                            for (int j = 1; j < fpsControls.Count; j++)
                            {
                                fpsControls[j].Enabled = true;
                            }
                        }
                    };
                    
                    // Also apply when first camera FPS changes and "Apply to All" is checked
                    fpsControls[0].ValueChanged += (s, e) =>
                    {
                        if (chkApplyAll.Checked)
                        {
                            double firstFps = (double)fpsControls[0].Value;
                            for (int j = 1; j < fpsControls.Count; j++)
                            {
                                fpsControls[j].Value = (decimal)firstFps;
                            }
                        }
                    };
                    
                    configForm.Controls.Add(chkApplyAll);
                }
                
                y += 35;
            }
            
            // Test recording length
            y += 10;
            Label lblDuration = new Label
            {
                Text = "Test Recording Length:",
                Location = new System.Drawing.Point(40, y + 2),
                Size = new System.Drawing.Size(150, 20),
                Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold)
            };
            configForm.Controls.Add(lblDuration);
            
            NumericUpDown numDuration = new NumericUpDown
            {
                Location = new System.Drawing.Point(200, y),
                Size = new System.Drawing.Size(80, 25),
                Minimum = 5,
                Maximum = 300,
                Value = 15
            };
            configForm.Controls.Add(numDuration);
            
            Label lblDurationUnit = new Label
            {
                Text = "seconds",
                Location = new System.Drawing.Point(290, y + 2),
                Size = new System.Drawing.Size(60, 20)
            };
            configForm.Controls.Add(lblDurationUnit);
            
            y += 40;
            
            Label lblNote = new Label
            {
                Text = "Videos will be saved to your working folder for analysis",
                Location = new System.Drawing.Point(40, y),
                Size = new System.Drawing.Size(420, 20),
                ForeColor = System.Drawing.Color.Blue,
                Font = new System.Drawing.Font("Arial", 8)
            };
            configForm.Controls.Add(lblNote);
            
            y += 30;
            
            // Buttons
            Button btnCancelConfig = new Button
            {
                Text = "Cancel",
                Location = new System.Drawing.Point(220, y),
                Size = new System.Drawing.Size(100, 35),
                DialogResult = DialogResult.Cancel
            };
            configForm.Controls.Add(btnCancelConfig);
            
            Button btnStart = new Button
            {
                Text = "Start Test",
                Location = new System.Drawing.Point(330, y),
                Size = new System.Drawing.Size(120, 35),
                DialogResult = DialogResult.OK
            };
            configForm.Controls.Add(btnStart);
            
            configForm.AcceptButton = btnStart;
            configForm.CancelButton = btnCancelConfig;
            
            if (configForm.ShowDialog() != DialogResult.OK)
                return false;
            
            // Collect settings
            for (int i = 0; i < cameras.Count; i++)
            {
                cameraExpectedFps[i] = (double)fpsControls[i].Value;
            }
            testDuration = (double)numDuration.Value;
            
            return true;
        }

        private void RunLoopRecordingTest(Dictionary<int, double> cameraExpectedFps, double loopDuration)
        {
            // Save current loop settings
            bool wasLoopEnabled = chkLoopRecording.Checked;
            int oldDuration = (int)numLoopDuration.Value;
            double oldFps = (double)numExternalTriggerFps.Value;
            
            try
            {
                isBenchmarkMode = true;
                
                // Configure loop recording for test
                chkLoopRecording.Checked = true;
                numLoopDuration.Value = (decimal)loopDuration;
                numExternalTriggerFps.Enabled = true;
                
                double totalTestDuration = loopDuration * 3;
                
                LogCameraInfo($"=== LOOP RECORDING TEST START ===");
                LogCameraInfo($"Loop duration: {loopDuration}s, Total test: {totalTestDuration}s");
                
                // Create progress dialog
                Form testForm = new Form
                {
                    Text = "Running Loop Recording Test...",
                    Size = new System.Drawing.Size(550, 320),
                    StartPosition = FormStartPosition.CenterParent,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    ControlBox = false
                };
                
                Label lblStatus = new Label
                {
                    Text = "Initializing test...",
                    Location = new System.Drawing.Point(20, 20),
                    Size = new System.Drawing.Size(460, 30),
                    Font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold)
                };
                testForm.Controls.Add(lblStatus);
                
                ProgressBar progressBar = new ProgressBar
                {
                    Location = new System.Drawing.Point(20, 60),
                    Size = new System.Drawing.Size(360, 25),
                    Minimum = 0,
                    Maximum = (int)(totalTestDuration * 10),
                    Value = 0
                };
                testForm.Controls.Add(progressBar);
                
                Label lblDetails = new Label
                {
                    Text = "",
                    Location = new System.Drawing.Point(20, 100),
                    Size = new System.Drawing.Size(460, 150),
                    Font = new System.Drawing.Font("Consolas", 9)
                };
                testForm.Controls.Add(lblDetails);
                
                testForm.Show();
                Application.DoEvents();
                
                
                // Start cameras if not already live
                bool needToStartLive = !cameras.Any(c => c.ImagingControl.LiveVideoRunning);
                if (needToStartLive)
                {
                    foreach (var camera in cameras)
                    {
                        try
                        {
                            camera.ImagingControl.LiveStart();
                            camera.LastFpsUpdate = DateTime.Now;
                            camera.FrameCount = 0;
                        }
                        catch { }
                    }
                    System.Threading.Thread.Sleep(500);
                }

                // Reset counters
                foreach (var camera in cameras)
                {
                    camera.DroppedFrames = 0;
                    camera.ExpectedFrameCount = 0;
                    camera.LastFrameNumber = -1;
                }

                // Start recording
                lblStatus.Text = "Starting loop recording...";
                Application.DoEvents();
                BtnStartRecording_Click(null, EventArgs.Empty);

                // ✅ Set testStart AFTER recording has actually started!
                DateTime testStart = DateTime.Now;
                
                // Monitor during test
                System.Windows.Forms.Timer testTimer = new System.Windows.Forms.Timer();
                testTimer.Interval = 100;
                
                testTimer.Tick += (s, e) =>
                {
                    double elapsed = (DateTime.Now - testStart).TotalSeconds;
                    
                    int progressValue = Math.Max(0, Math.Min((int)(totalTestDuration * 10), (int)(elapsed * 10)));
                    progressBar.Value = progressValue;
                    
                    lblStatus.Text = $"Test running... {elapsed:F1}s / {totalTestDuration:F1}s";
                    
                    // Show current stats
                    string stats = "";
                    int totalCaptured = 0;
                    int totalDropped = 0;
                    
                    foreach (var camera in cameras)
                    {
                        int bufferSize = 0;
                        if (camera.LoopBuffer != null)
                        {
                            lock (camera.LoopBufferLock)
                            {
                                bufferSize = camera.LoopBuffer.Count;
                            }
                        }
                        
                        int cameraIndex = cameras.IndexOf(camera);
                        double expectedFps = cameraExpectedFps[cameraIndex];

                        // Calculate expected frames based on THIS camera's actual recording time
                        double cameraElapsed = elapsed;
                        if (camera.RecordingStartTime.HasValue)
                        {
                            cameraElapsed = (DateTime.Now - camera.RecordingStartTime.Value).TotalSeconds;
                        }
                        int expectedFrames = (int)(cameraElapsed * expectedFps);  // ← Uses per-camera elapsed time
                        int capturedFrames = (int)camera.ExpectedFrameCount;
                        
                        totalCaptured += capturedFrames;
                        totalDropped += camera.DroppedFrames;
                        
                        stats += $"{camera.CustomName}:\n";
                        stats += $"  Buffer: {bufferSize} frames\n";
                        stats += $"  Expected: {expectedFrames} | Captured: {capturedFrames}\n";
                        stats += $"  Dropped: {camera.DroppedFrames}\n\n";
                    }
                    
                    lblDetails.Text = stats;
                    
                    if (elapsed >= totalTestDuration)
                    {
                        testTimer.Stop();
                        testTimer.Dispose();
                        
                        // Stop recording without saving
                        if (isRecording)
                        {
                            LogCameraInfo("=== LOOP TEST STOP (no file save) ===");
                            
                            foreach (var camera in cameras)
                            {
                                try
                                {
                                    camera.RecordingStopTime = DateTime.Now;
                                    camera.ImagingControl.LiveStop();
                                    
                                    if (camera.LoopCancelToken != null)
                                    {
                                        camera.LoopCancelToken.Cancel();
                                        camera.LoopCaptureTask?.Wait(1000);
                                        camera.LoopCancelToken.Dispose();
                                        camera.LoopCancelToken = null;
                                        camera.LoopCaptureTask = null;
                                    }
                                    
                                    // Clear loop buffer without saving
                                    lock (camera.LoopBufferLock)
                                    {
                                        if (camera.LoopBuffer != null)
                                        {
                                            while (camera.LoopBuffer.Count > 0)
                                            {
                                                var frame = camera.LoopBuffer.Dequeue();
                                                frame.Dispose();
                                            }
                                            camera.LoopBuffer = null;
                                        }
                                    }
                                    
                                    // Restore original sink and restart live
                                    camera.ImagingControl.Sink = camera.OriginalSink;
                                    camera.ImagingControl.LiveStart();
                                }
                                catch (Exception ex)
                                {
                                    LogCameraInfo($"Test cleanup error: {ex.Message}");
                                }
                            }

                            // ✅ ADD THESE LINES HERE - Update button states after restarting cameras
                            btnStartLive.Enabled = false;
                            btnStopLive.Enabled = true;
                            btnStartRecording.Enabled = true;
                            btnScreenshot.Enabled = true;

                            isRecording = false;
                            btnStartRecording.Enabled = true;
                            btnStopRecording.Enabled = false;
                            chkLoopRecording.Enabled = true;
                            numLoopDuration.Enabled = chkLoopRecording.Checked;
                        }
                        
                        testForm.Close();
                        
                        // Calculate and show results
                        ShowLoopTestResults(cameraExpectedFps, totalTestDuration);
                    }
                };
                
                testTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Test failed: {ex.Message}",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                LogCameraInfo($"Loop test ERROR: {ex.Message}");
            }
            finally
            {
                // Restore settings
                chkLoopRecording.Checked = wasLoopEnabled;
                numLoopDuration.Value = oldDuration;
                numExternalTriggerFps.Value = (decimal)oldFps;
                isBenchmarkMode = false;
            }
        }

        private void ShowLoopTestResults(Dictionary<int, double> cameraExpectedFps, double totalTestDuration)
        {
            Form resultsForm = new Form
            {
                Text = "Loop Recording Test Results",
                Size = new System.Drawing.Size(750, 700),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.Sizable,
                MinimumSize = new System.Drawing.Size(750, 700)
            };
            
            int y = 20;
            
            // Calculate overall stats first
            int totalExpected = 0;
            int totalCaptured = 0;
            int totalDropped = 0;
            bool allGood = true;
            
            for (int i = 0; i < cameras.Count; i++)
            {
                var camera = cameras[i];
                double expectedFps = cameraExpectedFps[i];
                
                double cameraDuration = totalTestDuration;
                if (camera.RecordingStartTime.HasValue && camera.RecordingStopTime.HasValue)
                {
                    cameraDuration = (camera.RecordingStopTime.Value - camera.RecordingStartTime.Value).TotalSeconds;
                }
                
                int expectedFrames = (int)(cameraDuration * expectedFps);
                int capturedFrames = (int)camera.ExpectedFrameCount;
                int droppedFrames = camera.DroppedFrames;
                double dropRate = expectedFrames > 0 ? (droppedFrames / (double)expectedFrames) * 100 : 0;
                
                totalExpected += expectedFrames;
                totalCaptured += capturedFrames;
                totalDropped += droppedFrames;
                
                if (dropRate >= 5) allGood = false;
            }
            
            // Overall verdict
            double overallDropRate = totalExpected > 0 ? (totalDropped / (double)totalExpected) * 100 : 0;
            double overallCaptureRate = totalExpected > 0 ? (totalCaptured / (double)totalExpected) * 100 : 0;
            
            string overallVerdict = "";
            System.Drawing.Color overallVerdictColor;
            
            if (allGood && overallDropRate == 0 && overallCaptureRate >= 99)
            {
                overallVerdict = "✅ EXCELLENT - Perfect capture!";
                overallVerdictColor = System.Drawing.Color.Green;
            }
            else if (overallDropRate < 1 && overallCaptureRate >= 95)
            {
                overallVerdict = "✅ GOOD - Minimal frame loss";
                overallVerdictColor = System.Drawing.Color.Green;
            }
            else if (overallDropRate < 5)
            {
                overallVerdict = "⚠️ ACCEPTABLE - Some frame drops detected";
                overallVerdictColor = System.Drawing.Color.Orange;
            }
            else
            {
                overallVerdict = "❌ POOR - High frame drop rate";
                overallVerdictColor = System.Drawing.Color.Red;
            }
            
            Label lblVerdict = new Label
            {
                Text = overallVerdict,
                Location = new System.Drawing.Point(20, y),
                Size = new System.Drawing.Size(660, 40),
                Font = new System.Drawing.Font("Arial", 14, System.Drawing.FontStyle.Bold),
                ForeColor = overallVerdictColor
            };
            resultsForm.Controls.Add(lblVerdict);
            y += 50;
            
            // Summary stats in a GroupBox
            GroupBox grpSummary = new GroupBox
            {
                Text = "Test Summary",
                Location = new System.Drawing.Point(20, y),
                Size = new System.Drawing.Size(660, 140),
                Font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold)
            };

            Label lblSummaryContent = new Label
            {
                Text = $"Test Duration: {totalTestDuration:F1} seconds\n" +
                    $"Cameras Tested: {cameras.Count}\n" +
                    $"Total Expected Frames: {totalExpected:N0}\n" +
                    $"Total Captured Frames: {totalCaptured:N0}\n" +
                    $"Total Dropped Frames: {totalDropped:N0}\n" +
                    $"Overall Capture Rate: {overallCaptureRate:F1}%",
                Location = new System.Drawing.Point(15, 25),
                Size = new System.Drawing.Size(630, 105),
                Font = new System.Drawing.Font("Arial", 9)
            };
            grpSummary.Controls.Add(lblSummaryContent);
            resultsForm.Controls.Add(grpSummary);
            y += 150;

            // Per-camera results in a GroupBox with scrollable panel
            GroupBox grpDetails = new GroupBox
            {
                Text = "Per-Camera Results",
                Location = new System.Drawing.Point(20, y),
                Size = new System.Drawing.Size(660, 380),
                Font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold)
            };

            Panel detailsPanel = new Panel
            {
                Location = new System.Drawing.Point(15, 25),
                Size = new System.Drawing.Size(630, 345),
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle
            };

            // Build per-camera results
            int detailY = 10;
            for (int i = 0; i < cameras.Count; i++)
            {
                var camera = cameras[i];
                double expectedFps = cameraExpectedFps[i];
                
                double cameraDuration = totalTestDuration;
                if (camera.RecordingStartTime.HasValue && camera.RecordingStopTime.HasValue)
                {
                    cameraDuration = (camera.RecordingStopTime.Value - camera.RecordingStartTime.Value).TotalSeconds;
                }
                
                int expectedFrames = (int)(cameraDuration * expectedFps);
                int capturedFrames = (int)camera.ExpectedFrameCount;
                int droppedFrames = camera.DroppedFrames;
                double dropRate = expectedFrames > 0 ? (droppedFrames / (double)expectedFrames) * 100 : 0;
                double captureRate = expectedFrames > 0 ? (capturedFrames / (double)expectedFrames) * 100 : 0;
                double actualFps = cameraDuration > 0 ? capturedFrames / cameraDuration : 0;
                
                string verdict = "";
                System.Drawing.Color verdictColor;
                if (dropRate == 0 && captureRate >= 99)
                {
                    verdict = "✅ EXCELLENT";
                    verdictColor = System.Drawing.Color.Green;
                }
                else if (dropRate < 1 && captureRate >= 95)
                {
                    verdict = "✅ GOOD";
                    verdictColor = System.Drawing.Color.Green;
                }
                else if (dropRate < 5)
                {
                    verdict = "⚠️ ACCEPTABLE";
                    verdictColor = System.Drawing.Color.Orange;
                }
                else
                {
                    verdict = "❌ POOR";
                    verdictColor = System.Drawing.Color.Red;
                }
                
                // Camera header
                Label lblCameraHeader = new Label
                {
                    Text = $"{verdict} - {camera.CustomName}",
                    Location = new System.Drawing.Point(10, detailY),
                    Size = new System.Drawing.Size(600, 25),
                    Font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold),
                    ForeColor = verdictColor,
                    BackColor = System.Drawing.Color.FromArgb(240, 240, 240)
                };
                detailsPanel.Controls.Add(lblCameraHeader);
                detailY += 30;
                
                // Expected values
                Label lblExpected = new Label
                {
                    Text = $"Expected:  {expectedFrames:N0} frames @ {expectedFps:F1} fps",
                    Location = new System.Drawing.Point(25, detailY),
                    Size = new System.Drawing.Size(590, 20),
                    Font = new System.Drawing.Font("Consolas", 9)
                };
                detailsPanel.Controls.Add(lblExpected);
                detailY += 22;
                
                // Actual values
                Label lblActual = new Label
                {
                    Text = $"Actual:    {capturedFrames:N0} frames in {cameraDuration:F2}s = {actualFps:F2} fps",
                    Location = new System.Drawing.Point(25, detailY),
                    Size = new System.Drawing.Size(590, 20),
                    Font = new System.Drawing.Font("Consolas", 9),
                    ForeColor = captureRate >= 95 ? System.Drawing.Color.Green : System.Drawing.Color.Red
                };
                detailsPanel.Controls.Add(lblActual);
                detailY += 22;
                
                // Accuracy metrics
                int frameDifference = capturedFrames - expectedFrames;
                string accuracyText = $"Capture Rate: {captureRate:F1}% ({frameDifference:+0;-0} frames)  |  " +
                                    $"Dropped: {droppedFrames:N0} ({dropRate:F2}%)";
                Label lblAccuracy = new Label
                {
                    Text = accuracyText,
                    Location = new System.Drawing.Point(25, detailY),
                    Size = new System.Drawing.Size(590, 20),
                    Font = new System.Drawing.Font("Arial", 8),
                    ForeColor = System.Drawing.Color.Gray
                };
                detailsPanel.Controls.Add(lblAccuracy);
                detailY += 22;
                
                // Recording info
                Label lblRecording = new Label
                {
                    Text = $"Recording Duration: {cameraDuration:F2}s",
                    Location = new System.Drawing.Point(25, detailY),
                    Size = new System.Drawing.Size(590, 20),
                    Font = new System.Drawing.Font("Arial", 7),
                    ForeColor = System.Drawing.Color.DarkBlue
                };
                detailsPanel.Controls.Add(lblRecording);
                detailY += 35;
            }

            grpDetails.Controls.Add(detailsPanel);
            resultsForm.Controls.Add(grpDetails);
            y += 390;

            // Close button
            Button btnClose = new Button
            {
                Text = "Close",
                Location = new System.Drawing.Point(290, y),
                Size = new System.Drawing.Size(100, 35),
                DialogResult = DialogResult.OK,
                Font = new System.Drawing.Font("Arial", 10)
            };
            resultsForm.Controls.Add(btnClose);
            resultsForm.AcceptButton = btnClose;

            LogCameraInfo($"=== LOOP TEST RESULTS ===");
            LogCameraInfo($"Overall: {overallCaptureRate:F1}% capture rate, {overallDropRate:F2}% drop rate");

            resultsForm.ShowDialog();
        }

        private void RunVideoRecordingTest(Dictionary<int, double> cameraExpectedFps, double testDuration)
        {
            // Save current loop setting
            bool wasLoopEnabled = chkLoopRecording.Checked;
            
            try
            {
                isBenchmarkMode = true;
                
                // Disable loop recording for video test
                chkLoopRecording.Checked = false;
                
                LogCameraInfo($"=== VIDEO RECORDING TEST START ===");
                LogCameraInfo($"Test duration: {testDuration}s");
                
                // Create progress dialog
                Form testForm = new Form
                {
                    Text = "Running Video Recording Test...",
                    Size = new System.Drawing.Size(550, 250),
                    StartPosition = FormStartPosition.CenterParent,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    ControlBox = false
                };
                
                Label lblStatus = new Label
                {
                    Text = "Starting video recording...",
                    Location = new System.Drawing.Point(20, 20),
                    Size = new System.Drawing.Size(460, 30),
                    Font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold)
                };
                testForm.Controls.Add(lblStatus);
                
                ProgressBar progressBar = new ProgressBar
                {
                    Location = new System.Drawing.Point(20, 60),
                    Size = new System.Drawing.Size(360, 25),
                    Minimum = 0,
                    Maximum = (int)(testDuration * 10),
                    Value = 0
                };
                testForm.Controls.Add(progressBar);
                
                Label lblDetails = new Label
                {
                    Text = "Recording in progress...",
                    Location = new System.Drawing.Point(20, 100),
                    Size = new System.Drawing.Size(460, 100),
                    Font = new System.Drawing.Font("Consolas", 9)
                };
                testForm.Controls.Add(lblDetails);
                
                testForm.Show();
                Application.DoEvents();
                
                // Start cameras if not already live
                bool needToStartLive = !cameras.Any(c => c.ImagingControl.LiveVideoRunning);
                if (needToStartLive)
                {
                    foreach (var camera in cameras)
                    {
                        try
                        {
                            camera.ImagingControl.LiveStart();
                            camera.LastFpsUpdate = DateTime.Now;
                            camera.FrameCount = 0;
                        }
                        catch { }
                    }
                    System.Threading.Thread.Sleep(500);
                }
                
                // Setup recording for each camera manually (NOT using BtnStartRecording_Click)
                List<string> testVideoFiles = new List<string>();
                List<MediaStreamSink> sinks = new List<MediaStreamSink>();
                
                string workingFolder = txtWorkingFolder.Text;
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                
                for (int i = 0; i < cameras.Count; i++)
                {
                    var camera = cameras[i];
                    
                    try
                    {
                        // Stop camera
                        if (camera.ImagingControl.LiveVideoRunning)
                        {
                            camera.ImagingControl.LiveStop();
                        }
                        
                        // Save original sink
                        camera.OriginalSink = camera.ImagingControl.Sink;
                        
                        // Create AVI file path
                        string safeName = string.Join("_", camera.CustomName.Split(Path.GetInvalidFileNameChars()));
                        if (string.IsNullOrWhiteSpace(safeName))
                            safeName = $"Camera{i + 1}";
                        
                        string aviFile = Path.Combine(workingFolder, $"Test_{timestamp}_{safeName}.avi");
                        testVideoFiles.Add(aviFile);
                        
                        // Create MediaStreamSink with NO compression
                        MediaStreamSink sink = new MediaStreamSink((AviCompressor?)null, aviFile);
                        sinks.Add(sink);
                        
                        // Set sink
                        camera.ImagingControl.Sink = sink;
                        
                        LogCameraInfo($"{camera.CustomName}: Recording to {aviFile}");
                    }
                    catch (Exception ex)
                    {
                        LogCameraInfo($"Setup error for {camera.CustomName}: {ex.Message}");
                    }
                }
                
                // Start all cameras SYNCHRONOUSLY
                DateTime recordingStart = DateTime.Now;
                foreach (var camera in cameras)
                {
                    try
                    {
                        camera.ImagingControl.LiveStart();
                        camera.RecordingStartTime = DateTime.Now;
                        System.Threading.Thread.Sleep(10);  // Small delay between cameras
                    }
                    catch (Exception ex)
                    {
                        LogCameraInfo($"Start error for {camera.CustomName}: {ex.Message}");
                    }
                }
                
                LogCameraInfo($"All cameras started at {recordingStart:HH:mm:ss.fff}");
                
                // Simple blocking wait with progress updates
                while ((DateTime.Now - recordingStart).TotalSeconds < testDuration)
                {
                    double elapsed = (DateTime.Now - recordingStart).TotalSeconds;
                    
                    progressBar.Value = Math.Min((int)(elapsed * 10), progressBar.Maximum);
                    lblStatus.Text = $"Recording... {elapsed:F1}s / {testDuration:F1}s";
                    
                    string stats = $"Recording to disk...\n\n";
                    stats += $"Cameras: {cameras.Count}\n";
                    stats += $"Time elapsed: {elapsed:F1}s\n";
                    stats += $"Time remaining: {(testDuration - elapsed):F1}s";
                    
                    lblDetails.Text = stats;
                    
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(100);
                }
                
                DateTime recordingStop = DateTime.Now;
                double actualDuration = (recordingStop - recordingStart).TotalSeconds;
                
                LogCameraInfo($"=== VIDEO TEST STOP ===");
                LogCameraInfo($"Actual recording duration: {actualDuration:F2}s");
                
                lblStatus.Text = "Stopping and flushing files...";
                Application.DoEvents();
                
                // CRITICAL: Stop cameras and properly close sinks
                for (int i = 0; i < cameras.Count; i++)
                {
                    var camera = cameras[i];
                    
                    try
                    {
                        camera.RecordingStopTime = DateTime.Now;
                        
                        // Stop camera
                        camera.ImagingControl.LiveStop();
                        
                        LogCameraInfo($"{camera.CustomName}: Stopped at {camera.RecordingStopTime.Value:HH:mm:ss.fff}");
                    }
                    catch (Exception ex)
                    {
                        LogCameraInfo($"Stop error for {camera.CustomName}: {ex.Message}");
                    }
                }
                
                // Wait for files to flush
                System.Threading.Thread.Sleep(1000);
                
                // CRITICAL: Dispose all MediaStreamSinks to force file close
                LogCameraInfo("Disposing MediaStreamSinks to flush files...");
                foreach (var sink in sinks)
                {
                    try
                    {
                        sink.Dispose();
                    }
                    catch (Exception ex)
                    {
                        LogCameraInfo($"Sink dispose error: {ex.Message}");
                    }
                }
                
                // Wait for file system to catch up
                System.Threading.Thread.Sleep(1000);
                
                // Restore original sinks and restart cameras
                for (int i = 0; i < cameras.Count; i++)
                {
                    var camera = cameras[i];
                    
                    try
                    {
                        camera.ImagingControl.Sink = camera.OriginalSink;
                        camera.ImagingControl.LiveStart();
                    }
                    catch (Exception ex)
                    {
                        LogCameraInfo($"Restart error for {camera.CustomName}: {ex.Message}");
                    }
                }

                // ✅ UPDATE BUTTON STATES TO REFLECT LIVE MODE
                btnStartLive.Enabled = false;
                btnStopLive.Enabled = true;
                btnStartRecording.Enabled = true;
                btnScreenshot.Enabled = true;
                
                testForm.Close();
                
                LogCameraInfo("Waiting 2 seconds for final disk flush...");
                System.Threading.Thread.Sleep(2000);
                
                // Calculate per-camera actual durations
                Dictionary<int, double> actualCameraDurations = new Dictionary<int, double>();
                for (int i = 0; i < cameras.Count; i++)
                {
                    if (cameras[i].RecordingStartTime.HasValue && cameras[i].RecordingStopTime.HasValue)
                    {
                        double cameraDuration = (cameras[i].RecordingStopTime.Value - cameras[i].RecordingStartTime.Value).TotalSeconds;
                        actualCameraDurations[i] = cameraDuration;
                        LogCameraInfo($"{cameras[i].CustomName}: Actual recording duration = {cameraDuration:F3}s");
                    }
                    else
                    {
                        // Fallback to test duration if timestamps missing
                        actualCameraDurations[i] = actualDuration;
                    }
                }

                // Analyze the recorded videos using per-camera actual durations
                AnalyzeVideoTestResults(testVideoFiles, cameraExpectedFps, testDuration, actualCameraDurations);

                // Clean up test video files after analysis
                LogCameraInfo("Cleaning up test video files...");
                foreach (string videoFile in testVideoFiles)
                {
                    try
                    {
                        if (File.Exists(videoFile))
                        {
                            File.Delete(videoFile);
                            LogCameraInfo($"Deleted test file: {Path.GetFileName(videoFile)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogCameraInfo($"Failed to delete {Path.GetFileName(videoFile)}: {ex.Message}");
                    }
                }
                LogCameraInfo("Test cleanup complete");
           
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Test failed: {ex.Message}",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                LogCameraInfo($"Video test ERROR: {ex.Message}");
            }
            finally
            {
                // Restore settings
                chkLoopRecording.Checked = wasLoopEnabled;
                isBenchmarkMode = false;
            }
        }

        private void AnalyzeVideoTestResults(List<string> videoFiles, Dictionary<int, double> cameraExpectedFps, double testDuration, Dictionary<int, double> actualCameraDurations)
        {
            // Create analysis progress form
            Form analysisForm = new Form
            {
                Text = "Analyzing Videos...",
                Size = new System.Drawing.Size(400, 150),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ControlBox = false
            };
            
            Label lblAnalyzing = new Label
            {
                Text = "Analyzing recorded videos, please wait...",
                Location = new System.Drawing.Point(20, 50),
                Size = new System.Drawing.Size(360, 40),
                Font = new System.Drawing.Font("Arial", 10),
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter
            };
            analysisForm.Controls.Add(lblAnalyzing);
            
            analysisForm.Show();
            Application.DoEvents();
            
            // Analyze each video
            string detailedStats = "PER-CAMERA RESULTS:\n\n";
            bool allGood = true;
            int analyzedCount = 0;
            
            for (int i = 0; i < videoFiles.Count && i < cameras.Count; i++)
            {
                string videoFile = videoFiles[i];
                
                if (!File.Exists(videoFile))
                {
                    detailedStats += $"❌ {cameras[i].CustomName}:\n";
                    detailedStats += $"   ERROR: Video file not found\n\n";
                    allGood = false;
                    continue;
                }
                
                try
                {
                    int actualFrames = GetVideoFrameCount(videoFile);
                    double storedFps = GetVideoFrameRate(videoFile);
                    
                    // Use the ACTUAL test duration, not the AVI metadata duration
                    double actualDuration = testDuration;  // ← FIXED! Use actual recording time
                    double calculatedFps = actualDuration > 0 ? actualFrames / actualDuration : 0;
                    
                    double expectedFps = cameraExpectedFps[i];
                    int expectedFrames = (int)(testDuration * expectedFps);
                    
                    double fpsAccuracy = expectedFps > 0 ? (calculatedFps / expectedFps) * 100 : 0;
                    int frameDifference = actualFrames - expectedFrames;
                    double frameAccuracy = expectedFrames > 0 ? (actualFrames / (double)expectedFrames) * 100 : 0;
                    
                    string verdict = "";
                    if (frameAccuracy >= 99 && Math.Abs(fpsAccuracy - 100) < 5)
                        verdict = "✅";
                    else if (frameAccuracy >= 95 && Math.Abs(fpsAccuracy - 100) < 10)
                        verdict = "✅";
                    else if (frameAccuracy >= 90)
                        verdict = "⚠️";
                    else
                    {
                        verdict = "❌";
                        allGood = false;
                    }
                    
                    detailedStats += $"{verdict} {cameras[i].CustomName}:\n";
                    detailedStats += $"   Expected: {expectedFrames:N0} frames @ {expectedFps:F1} fps\n";
                    detailedStats += $"   Actual: {actualFrames:N0} frames @ {calculatedFps:F1} fps\n";
                    detailedStats += $"   Stored FPS: {storedFps:F1}\n";
                    detailedStats += $"   Frame Accuracy: {frameAccuracy:F1}% ({frameDifference:+0;-0} frames)\n";
                    detailedStats += $"   FPS Accuracy: {fpsAccuracy:F1}%\n";
                    detailedStats += $"   Duration: {actualDuration:F2}s\n";
                    detailedStats += $"   File: {Path.GetFileName(videoFile)}\n\n";
                    
                    analyzedCount++;
                }
                catch (Exception ex)
                {
                    detailedStats += $"❌ {cameras[i].CustomName}:\n";
                    detailedStats += $"   ERROR: {ex.Message}\n\n";
                    allGood = false;
                }
            }
            
            // Overall verdict
            string overallVerdict = "";
            System.Drawing.Color overallVerdictColor;  // ← RENAMED

            if (allGood && analyzedCount == cameras.Count)
            {
                overallVerdict = "✅ EXCELLENT - All cameras recorded accurately!";
                overallVerdictColor = System.Drawing.Color.Green;
            }
            else if (analyzedCount == cameras.Count)
            {
                overallVerdict = "⚠️ ACCEPTABLE - Some accuracy issues detected";
                overallVerdictColor = System.Drawing.Color.Orange;
            }
            else
            {
                overallVerdict = "❌ POOR - Recording failures detected";
                overallVerdictColor = System.Drawing.Color.Red;
            }
            
            // Close analysis form
            analysisForm.Close();
            
            // Create results form
            Form resultsForm = new Form
            {
                Text = "Video Recording Test Results",
                Size = new System.Drawing.Size(750, 600),  // ← INCREASED SIZE
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.Sizable,
                MinimumSize = new System.Drawing.Size(750, 600)  // ← INCREASED MINIMUM SIZE
            };
            
            int y = 20;
            
            Label lblVerdict = new Label
            {
                Text = overallVerdict,
                Location = new System.Drawing.Point(20, y),
                Size = new System.Drawing.Size(660, 40),
                Font = new System.Drawing.Font("Arial", 14, System.Drawing.FontStyle.Bold),
                ForeColor = overallVerdictColor  // ← UPDATED
            };
            resultsForm.Controls.Add(lblVerdict);
            y += 50;
            
            // Summary stats in a GroupBox
            GroupBox grpSummary = new GroupBox
            {
                Text = "Test Summary",
                Location = new System.Drawing.Point(20, y),
                Size = new System.Drawing.Size(660, 120),
                Font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold)
            };

            Label lblSummaryContent = new Label
            {
                Text = $"Test Duration: {testDuration:F1} seconds\n" +
                    $"Cameras Tested: {cameras.Count}\n" +
                    $"Successfully Analyzed: {analyzedCount} / {cameras.Count}\n" +
                    $"Videos Location: {Path.GetDirectoryName(videoFiles[0])}",
                Location = new System.Drawing.Point(15, 25),
                Size = new System.Drawing.Size(630, 85),
                Font = new System.Drawing.Font("Arial", 9)
            };
            grpSummary.Controls.Add(lblSummaryContent);
            resultsForm.Controls.Add(grpSummary);
            y += 130;

            // Per-camera results in a GroupBox with scrollable panel
            GroupBox grpDetails = new GroupBox
            {
                Text = "Per-Camera Results",
                Location = new System.Drawing.Point(20, y),
                Size = new System.Drawing.Size(660, 280),
                Font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold)
            };

            Panel detailsPanel = new Panel
            {
                Location = new System.Drawing.Point(15, 25),
                Size = new System.Drawing.Size(630, 245),
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle
            };

            // Build structured HTML-like formatted text
            int detailY = 10;
            foreach (int i in Enumerable.Range(0, videoFiles.Count).Where(idx => idx < cameras.Count))
            {
                string videoFile = videoFiles[i];
                
                if (!File.Exists(videoFile))
                    continue;
                
                try
                {
                    // Get frame count from file
                    int actualFrames = GetVideoFrameCount(videoFile);
                    
                    // Get stored metadata (just for reference, not for calculation)
                    double storedFps = GetVideoFrameRate(videoFile);
                    double aviMetadataDuration = GetVideoDuration(videoFile);  // Just for display
                    
                    // CRITICAL: Use THIS CAMERA's actual recording duration
                    double actualRecordingDuration = actualCameraDurations.ContainsKey(i) ? actualCameraDurations[i] : testDuration;

                    // Calculate FPS from actual recording time
                    double calculatedFps = actualRecordingDuration > 0 ? actualFrames / actualRecordingDuration : 0;

                    double expectedFps = cameraExpectedFps[i];
                    // Calculate expected frames based on THIS camera's actual recording time
                    int expectedFrames = (int)(actualRecordingDuration * expectedFps);
                    
                    double fpsAccuracy = expectedFps > 0 ? (calculatedFps / expectedFps) * 100 : 0;
                    int frameDifference = actualFrames - expectedFrames;
                    double frameAccuracy = expectedFrames > 0 ? (actualFrames / (double)expectedFrames) * 100 : 0;
                    
                    string verdict = "";
                    System.Drawing.Color verdictColor;
                    if (frameAccuracy >= 99 && Math.Abs(fpsAccuracy - 100) < 5)
                    {
                        verdict = "✅ EXCELLENT";
                        verdictColor = System.Drawing.Color.Green;
                    }
                    else if (frameAccuracy >= 95 && Math.Abs(fpsAccuracy - 100) < 10)
                    {
                        verdict = "✅ GOOD";
                        verdictColor = System.Drawing.Color.Green;
                    }
                    else if (frameAccuracy >= 90)
                    {
                        verdict = "⚠️ ACCEPTABLE";
                        verdictColor = System.Drawing.Color.Orange;
                    }
                    else
                    {
                        verdict = "❌ POOR";
                        verdictColor = System.Drawing.Color.Red;
                    }
                    
                    // Camera header
                    Label lblCameraHeader = new Label
                    {
                        Text = $"{verdict} - {cameras[i].CustomName}",
                        Location = new System.Drawing.Point(10, detailY),
                        Size = new System.Drawing.Size(600, 25),  // ← INCREASED to 600
                        Font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold),
                        ForeColor = verdictColor,
                        BackColor = System.Drawing.Color.FromArgb(240, 240, 240)
                    };

                    detailsPanel.Controls.Add(lblCameraHeader);
                    detailY += 30;
                    
                    // Expected values
                    Label lblExpected = new Label
                    {
                        Text = $"Expected:  {expectedFrames:N0} frames @ {expectedFps:F1} fps",
                        Location = new System.Drawing.Point(25, detailY),
                        Size = new System.Drawing.Size(590, 20),  // ← INCREASED to 590
                        Font = new System.Drawing.Font("Consolas", 9)
                    };
                    detailsPanel.Controls.Add(lblExpected);
                    detailY += 22;

                    // Actual values - USE ACTUAL RECORDING TIME
                    Label lblActual = new Label
                    {
                        Text = $"Actual:    {actualFrames:N0} frames in {actualRecordingDuration:F2}s = {calculatedFps:F2} fps",
                        Location = new System.Drawing.Point(25, detailY),
                        Size = new System.Drawing.Size(590, 20),  // ← INCREASED to 590
                        Font = new System.Drawing.Font("Consolas", 9),
                        ForeColor = frameAccuracy >= 95 ? System.Drawing.Color.Green : System.Drawing.Color.Red
                    };
                    detailsPanel.Controls.Add(lblActual);
                    detailY += 22;

                    // Accuracy metrics
                    string accuracyText = $"Frame Accuracy: {frameAccuracy:F1}% ({frameDifference:+0;-0} frames)  |  " +
                                        $"FPS Accuracy: {fpsAccuracy:F1}%";
                    Label lblAccuracy = new Label
                    {
                        Text = accuracyText,
                        Location = new System.Drawing.Point(25, detailY),
                        Size = new System.Drawing.Size(590, 20),  // ← INCREASED to 590
                        Font = new System.Drawing.Font("Arial", 8),
                        ForeColor = System.Drawing.Color.Gray
                    };
                    detailsPanel.Controls.Add(lblAccuracy);
                    detailY += 22;

                    // File info - show BOTH durations for comparison
                    Label lblFile = new Label
                    {
                        Text = $"Recording: {actualRecordingDuration:F2}s  |  AVI metadata: {aviMetadataDuration:F2}s (stored: {storedFps:F1} fps)\nFile: {Path.GetFileName(videoFile)}",
                        Location = new System.Drawing.Point(25, detailY),
                        Size = new System.Drawing.Size(590, 35),  // ← INCREASED to 590
                        Font = new System.Drawing.Font("Arial", 7),
                        ForeColor = System.Drawing.Color.DarkBlue
                    };
                    detailsPanel.Controls.Add(lblFile);
                    detailY += 40;
                }
                catch (Exception ex)
                {
                    Label lblError = new Label
                    {
                        Text = $"❌ {cameras[i].CustomName}: ERROR - {ex.Message}",
                        Location = new System.Drawing.Point(10, detailY),
                        Size = new System.Drawing.Size(590, 40),
                        Font = new System.Drawing.Font("Arial", 9),
                        ForeColor = System.Drawing.Color.Red
                    };
                    detailsPanel.Controls.Add(lblError);
                    detailY += 45;
                }
            }


            grpDetails.Controls.Add(detailsPanel);
            resultsForm.Controls.Add(grpDetails);
            y += 290;

            // Close button
            Button btnClose = new Button
            {
                Text = "Close",
                Location = new System.Drawing.Point(290, y),
                Size = new System.Drawing.Size(100, 35),
                DialogResult = DialogResult.OK,
                Font = new System.Drawing.Font("Arial", 10)
            };
            resultsForm.Controls.Add(btnClose);
            resultsForm.AcceptButton = btnClose;

            LogCameraInfo($"=== VIDEO TEST RESULTS ===");
            LogCameraInfo($"Analyzed: {analyzedCount}/{cameras.Count} cameras");

            resultsForm.ShowDialog();
        }

        private void ManageCameraProfiles()
        {
            Form profileForm = new Form
            {
                Text = "Camera Name Profiles",
                Size = new System.Drawing.Size(500, 400),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            
            Label lblInfo = new Label
            {
                Text = "Save and load camera naming configurations:",
                Location = new System.Drawing.Point(20, 20),
                Size = new System.Drawing.Size(460, 20),
                Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold)
            };
            profileForm.Controls.Add(lblInfo);
            
            ListBox lstProfiles = new ListBox
            {
                Location = new System.Drawing.Point(20, 50),
                Size = new System.Drawing.Size(300, 250)
            };
            
            // Load existing profiles
            foreach (var profile in settings.NameProfiles)
            {
                lstProfiles.Items.Add(profile.ProfileName);
            }
            
            profileForm.Controls.Add(lstProfiles);
            
            // Save Current button
            Button btnSaveCurrent = new Button
            {
                Text = "Save Current Names",
                Location = new System.Drawing.Point(340, 50),
                Size = new System.Drawing.Size(140, 30)
            };
            btnSaveCurrent.Click += (s, e) =>
            {
                if (cameras.Count == 0)
                {
                    MessageBox.Show("No cameras detected!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                
                string profileName = Microsoft.VisualBasic.Interaction.InputBox(
                    "Enter profile name:",
                    "Save Profile",
                    $"Profile {settings.NameProfiles.Count + 1}");
                
                if (string.IsNullOrWhiteSpace(profileName))
                    return;
                
                // Create new profile
                var newProfile = new CameraNameProfile
                {
                    ProfileName = profileName,
                    CameraNames = new Dictionary<string, string>()
                };
                
                foreach (var camera in cameras)
                {
                    newProfile.CameraNames[camera.DeviceName] = camera.CustomName;
                }
                
                settings.NameProfiles.Add(newProfile);
                settings.Save();
                
                lstProfiles.Items.Add(profileName);
                MessageBox.Show($"Profile '{profileName}' saved!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            profileForm.Controls.Add(btnSaveCurrent);
            
            // Load Profile button
            Button btnLoad = new Button
            {
                Text = "Load Selected",
                Location = new System.Drawing.Point(340, 90),
                Size = new System.Drawing.Size(140, 30)
            };
            btnLoad.Click += (s, e) =>
            {
                if (lstProfiles.SelectedIndex < 0)
                {
                    MessageBox.Show("Please select a profile!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                
                var profile = settings.NameProfiles[lstProfiles.SelectedIndex];
                
                // Apply names
                int applied = 0;
                foreach (var camera in cameras)
                {
                    if (profile.CameraNames.ContainsKey(camera.DeviceName))
                    {
                        camera.CustomName = profile.CameraNames[camera.DeviceName];
                        camera.NameTextBox.Text = camera.CustomName;
                        applied++;
                    }
                }
                
                settings.LastUsedProfile = profile.ProfileName;
                settings.Save();
                
                MessageBox.Show($"Applied names to {applied} camera(s)!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            profileForm.Controls.Add(btnLoad);
            
            // Delete Profile button
            Button btnDelete = new Button
            {
                Text = "Delete Selected",
                Location = new System.Drawing.Point(340, 130),
                Size = new System.Drawing.Size(140, 30)
            };
            btnDelete.Click += (s, e) =>
            {
                if (lstProfiles.SelectedIndex < 0)
                {
                    MessageBox.Show("Please select a profile!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                
                var result = MessageBox.Show("Delete this profile?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    settings.NameProfiles.RemoveAt(lstProfiles.SelectedIndex);
                    lstProfiles.Items.RemoveAt(lstProfiles.SelectedIndex);
                    settings.Save();
                }
            };
            profileForm.Controls.Add(btnDelete);
            
            // Close button
            Button btnClose = new Button
            {
                Text = "Close",
                Location = new System.Drawing.Point(200, 320),
                Size = new System.Drawing.Size(100, 35),
                DialogResult = DialogResult.OK
            };
            profileForm.Controls.Add(btnClose);
            profileForm.AcceptButton = btnClose;
            
            profileForm.ShowDialog();
        }
        private void UpdateRamEstimate()
        {
            try
            {
                // Check if we even have the controls
                if (cameras == null || cameras.Count == 0 || lblRamEstimate == null)
                {
                    if (lblRamEstimate != null)
                    {
                        lblRamEstimate.Text = "";
                        lblRamEstimate.ForeColor = System.Drawing.Color.Blue;
                    }
                    return;
                }
                
                // Only show RAM estimate for loop recording mode
                string selectedMode = cmbRecordingMode?.SelectedItem?.ToString() ?? "";
                if (selectedMode != "Loop Recording")
                {
                    lblRamEstimate.Text = "";
                    lblRamEstimate.ForeColor = System.Drawing.Color.Gray;
                    return;
                }
                
                // Check if cameras are ready
                bool allCamerasReady = cameras.All(c => c.ImagingControl != null && c.ImagingControl.VideoFormatCurrent != null);
                if (!allCamerasReady)
                {
                    lblRamEstimate.Text = "⏳ Cameras loading...";
                    lblRamEstimate.ForeColor = System.Drawing.Color.Gray;
                    return;
                }
                
                int loopSeconds = (int)numLoopDuration.Value;
                double externalTriggerFps = (double)numExternalTriggerFps.Value;
                
                long totalRamBytes = 0;
                double totalProcessingLoad = 0;
                
                foreach (var camera in cameras)
                {
                    // Determine FPS for this camera
                    double fps = camera.Settings.UseExternalTrigger ? externalTriggerFps : camera.Settings.SoftwareFrameRate;
                    
                    // Calculate number of frames
                    int maxFrames = (int)(loopSeconds * fps);
                    
                    // Estimate frame size based on camera format
                    int width = 640;
                    int height = 480;
                    int bytesPerPixel = 1;
                    
                    if (camera.ImagingControl.VideoFormatCurrent != null)
                    {
                        string format = camera.ImagingControl.VideoFormatCurrent.ToString();
                        
                        var match = System.Text.RegularExpressions.Regex.Match(format, @"(\d+)x(\d+)");
                        if (match.Success)
                        {
                            width = int.Parse(match.Groups[1].Value);
                            height = int.Parse(match.Groups[2].Value);
                        }
                        
                        if (format.Contains("RGB") || format.Contains("BGR"))
                        {
                            bytesPerPixel = 3;
                        }
                        else if (format.Contains("Y800") || format.Contains("Mono8"))
                        {
                            bytesPerPixel = 1;
                        }
                    }
                    
                    // Calculate RAM for this camera
                    long bytesPerFrame = width * height * bytesPerPixel;
                    long bitmapOverhead = (long)(bytesPerFrame * 1.2);
                    long cameraRamBytes = maxFrames * bitmapOverhead;
                    totalRamBytes += cameraRamBytes;
                    
                    // Calculate processing load per physical core
                    double msPerFrame = 1000.0 / fps;
                    double copyTimeMs = 0;
                    
                    if (width <= 640 && height <= 480)
                    {
                        copyTimeMs = bytesPerPixel == 3 ? 1.5 : 1.0;
                    }
                    else if (width <= 1280 && height <= 720)
                    {
                        copyTimeMs = bytesPerPixel == 3 ? 3.5 : 2.5;
                    }
                    else if (width <= 1920 && height <= 1080)
                    {
                        copyTimeMs = bytesPerPixel == 3 ? 7.0 : 5.0;
                    }
                    else
                    {
                        double pixelRatio = (width * height) / (1920.0 * 1080.0);
                        copyTimeMs = (bytesPerPixel == 3 ? 7.0 : 5.0) * pixelRatio;
                    }
                    
                    double processingLoad = (copyTimeMs / msPerFrame) * 100;
                    totalProcessingLoad += processingLoad;
                }
                
                // Convert to GB
                double totalRamGB = totalRamBytes / (1024.0 * 1024.0 * 1024.0);
                
                // Calculate percentage of available system RAM
                double ramPercentage = (totalRamGB / systemInfo.AvailableRamGB) * 100;
                
                // Adjust CPU load based on actual core count
                // Assume we can use 80% of physical cores efficiently
                double effectiveCores = systemInfo.PhysicalCores * 0.8;
                double adjustedCpuLoad = totalProcessingLoad / effectiveCores;
                
                // Determine status based on ACTUAL hardware
                string statusIcon = "";
                System.Drawing.Color statusColor = System.Drawing.Color.Green;
                string warningText = "";
                
                // RAM thresholds based on actual available RAM
                bool ramCritical = ramPercentage > 80;  // Using >80% of available RAM
                bool ramHigh = ramPercentage > 50;      // Using >50% of available RAM
                bool ramModerate = ramPercentage > 30;  // Using >30% of available RAM
                
                // CPU thresholds
                bool cpuCritical = adjustedCpuLoad > 80;
                bool cpuHigh = adjustedCpuLoad > 60;
                bool cpuModerate = adjustedCpuLoad > 40;
                
                if (ramCritical || cpuCritical)
                {
                    statusIcon = "❌";
                    statusColor = System.Drawing.Color.Red;
                    warningText = " NOT RECOMMENDED";
                }
                else if (ramHigh || cpuHigh)
                {
                    statusIcon = "⚠️";
                    statusColor = System.Drawing.Color.Orange;
                    warningText = " BORDERLINE";
                }
                else if (ramModerate || cpuModerate)
                {
                    statusIcon = "⚠️";
                    statusColor = System.Drawing.Color.DarkOrange;
                    warningText = " HIGH";
                }
                else
                {
                    statusIcon = "✅";
                    statusColor = System.Drawing.Color.Green;
                    warningText = " OK";
                }
                
                // Display
                lblRamEstimate.Text = $"{statusIcon} RAM: {totalRamGB:F2} GB ({ramPercentage:F0}%)";
                lblRamEstimate.ForeColor = statusColor;
                
                // Enhanced tooltip with actual hardware info
                string tooltipText = $"SYSTEM HARDWARE:\n" +
                                    $"Total RAM: {systemInfo.TotalRamGB:F1} GB\n" +
                                    $"Available RAM: {systemInfo.AvailableRamGB:F1} GB\n" +
                                    $"CPU Cores: {systemInfo.PhysicalCores} ({systemInfo.LogicalCores} logical)\n" +
                                    $"\n" +
                                    $"ESTIMATED LOAD:\n" +
                                    $"RAM Needed: {totalRamGB:F2} GB ({ramPercentage:F0}% of available)\n" +
                                    $"CPU Load: ~{adjustedCpuLoad:F0}% (across {effectiveCores:F1} effective cores)\n" +
                                    $"Cameras: {cameras.Count}";
                
                if (ramPercentage > 50)
                    tooltipText += $"\n\n⚠️ Using {ramPercentage:F0}% of available RAM!";
                if (adjustedCpuLoad > 60)
                    tooltipText += $"\n⚠️ High CPU load - may drop frames!";
                
                // Manage tooltip
                System.Windows.Forms.ToolTip? tooltip = null;
                
                if (lblRamEstimate.Tag is System.Windows.Forms.ToolTip existingTooltip)
                {
                    tooltip = existingTooltip;
                }
                else
                {
                    tooltip = new System.Windows.Forms.ToolTip();
                    lblRamEstimate.Tag = tooltip;
                }
                
                tooltip.SetToolTip(lblRamEstimate, tooltipText);
            }
            catch (Exception ex)
            {
                LogCameraInfo($"UpdateRamEstimate ERROR: {ex.Message}");
                if (lblRamEstimate != null)
                {
                    lblRamEstimate.Text = "❓ Error";
                    lblRamEstimate.ForeColor = System.Drawing.Color.Gray;
                }
            }
        }
        private void SetupFpsTimer()
        {
            fpsTimer = new System.Windows.Forms.Timer();
            fpsTimer.Interval = 1000; // Update every second
            fpsTimer.Tick += FpsTimer_Tick;
            fpsTimer.Start();

            // Add disk monitoring timer
            diskMonitorTimer = new System.Windows.Forms.Timer();
            diskMonitorTimer.Interval = 1000; // Check every second
            diskMonitorTimer.Tick += (s, e) => MonitorDiskWriteSpeed();
            diskMonitorTimer.Start();
        }
        
        private void DetectAndSetupCameras(bool suppressMessage = false)
        {
            // Clear existing cameras
            foreach (var cam in cameras)
            {
                try
                {
                    if (cam.ImagingControl.LiveVideoRunning)
                    {
                        cam.ImagingControl.LiveStop();
                    }
                }
                catch { }
                
                this.Controls.Remove(cam.ImagingControl);
                this.Controls.Remove(cam.NameLabel);
                this.Controls.Remove(cam.FpsLabel);
                this.Controls.Remove(cam.NameTextBox);      // ADD THIS LINE
                this.Controls.Remove(cam.SettingsButton);   // ADD THIS LINE
                cam.ImagingControl.Dispose();
            }
            cameras.Clear();

            // Create a temporary ICImagingControl to get available devices
            string[] deviceNames;
            using (var tempControl = new ICImagingControl())
            {
                var devices = tempControl.Devices;
                
                if (devices == null || devices.Length == 0)
                {
                    MessageBox.Show("No cameras detected! Please connect cameras and click 'Refresh Cameras'.",
                                    "No Cameras", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                deviceNames = devices.Select(d => d.Name).ToArray();
            }

            System.Threading.Thread.Sleep(200);


            // Now create a camera control for each device
            for (int i = 0; i < deviceNames.Length; i++)
            {
                string deviceName = deviceNames[i];
                var camera = new CameraControl(deviceName);
                
                int xPosition = SIDE_MARGIN + (i * (CAMERA_WIDTH + CAMERA_SPACING));
                int yPosition = TOP_MARGIN;

                // Editable camera name textbox
                camera.NameTextBox = new TextBox
                {
                    Text = $"Camera{i + 1}",  // This is already correct!
                    Location = new System.Drawing.Point(xPosition, yPosition),
                    Size = new System.Drawing.Size(CAMERA_WIDTH - 10, 20),
                    Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold),
                    BorderStyle = BorderStyle.FixedSingle
                };
                camera.CustomName = $"Camera{i + 1}";  // ADD THIS LINE to sync CustomName with TextBox
                int capturedIndex = i; // Capture for event handler
                camera.NameTextBox.TextChanged += (s, e) => 
                {
                    cameras[capturedIndex].CustomName = camera.NameTextBox.Text;
                };
                this.Controls.Add(camera.NameTextBox);

                // Device name label (smaller, below textbox)
                camera.NameLabel.Text = deviceName;
                camera.NameLabel.Location = new System.Drawing.Point(xPosition, yPosition + 22);
                camera.NameLabel.AutoSize = false;
                camera.NameLabel.Size = new System.Drawing.Size(CAMERA_WIDTH, 18);
                camera.NameLabel.Font = new System.Drawing.Font("Arial", 7);
                camera.NameLabel.ForeColor = System.Drawing.Color.Gray;
                this.Controls.Add(camera.NameLabel);

                camera.SettingsButton = new Button
                {
                    Text = "⚙️ Settings",
                    Location = new System.Drawing.Point(xPosition + CAMERA_WIDTH - 80, yPosition + 42), 
                    Size = new System.Drawing.Size(75, 22),
                    Font = new System.Drawing.Font("Arial", 7)
                };
                int cameraIndex = i; // Capture index for event handler
                camera.SettingsButton.Click += (s, e) => ShowCameraSettings(cameraIndex);
                this.Controls.Add(camera.SettingsButton);
                camera.SettingsButton.BringToFront();

                // Load saved settings for this camera if available
                if (settings.CameraSettingsByDevice.ContainsKey(deviceName))
                {
                    camera.Settings = settings.CameraSettingsByDevice[deviceName];
                }

                camera.FpsLabel.Text = "Ready";
                camera.FpsLabel.Location = new System.Drawing.Point(xPosition, yPosition + 45);
                camera.FpsLabel.Size = new System.Drawing.Size(CAMERA_WIDTH, 20);
                camera.FpsLabel.ForeColor = System.Drawing.Color.Green;
                camera.FpsLabel.Font = new System.Drawing.Font("Arial", 8, System.Drawing.FontStyle.Bold);
                this.Controls.Add(camera.FpsLabel);

                camera.ImagingControl.Location = new System.Drawing.Point(xPosition, yPosition + 68);
                camera.ImagingControl.Size = new System.Drawing.Size(CAMERA_WIDTH, CAMERA_HEIGHT);
                camera.ImagingControl.Name = $"icImagingControl{i + 1}";
                camera.ImagingControl.LiveDisplay = true;
                
                try
                {
                    camera.ImagingControl.Device = deviceName;
                    
                    // Prefer Y800/Mono8 for all cameras, but use whatever is available if not
                    bool formatSet = false;

                    if (camera.ImagingControl.VideoFormats != null && camera.ImagingControl.VideoFormats.Length > 0)
                    {
                        // Try to find any Y800/Mono8 format (at any resolution)
                        var y800Format = camera.ImagingControl.VideoFormats
                            .FirstOrDefault(f => (f.ToString().Contains("Y800") || f.ToString().Contains("Mono8")) &&
                                                f.ToString().Contains("640") &&
                                                f.ToString().Contains("480"));
                        
                        if (y800Format != null)
                        {
                            camera.ImagingControl.VideoFormat = y800Format;
                            formatSet = true;
                        }
                        else
                        {
                            camera.ImagingControl.VideoFormat = camera.ImagingControl.VideoFormats[0];
                            formatSet = true;
                            Console.WriteLine($"Warning: Camera {i + 1} using {camera.ImagingControl.VideoFormats[0]} (Y800 not available)");
                        }
                    }

                    if (!formatSet)
                    {
                        MessageBox.Show($"ERROR: Could not set video format for Camera {i + 1}",
                                      "Format Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }

                   if (camera.Settings != null && !string.IsNullOrEmpty(camera.Settings.Format))
                    {
                        // Try to apply saved format
                        var savedFormat = camera.ImagingControl.VideoFormats
                            .FirstOrDefault(f => f.ToString() == camera.Settings.Format);
                        
                        if (savedFormat != null)
                        {
                            try
                            {
                                camera.ImagingControl.VideoFormat = savedFormat;
                                LogCameraInfo($"Camera {i + 1} loaded saved format: {camera.Settings.Format}");
                            }
                            catch { }
                        }
                        
                        // Apply other saved properties (exposure, gain, etc.)
                        ApplyCameraSettings(camera, camera.Settings);
                    } 

                    // Just show device name in the small label
                    camera.NameLabel.Text = deviceName;

                    camera.ImagingControl.ImageAvailable += (s, e) => 
                    {
                        if (cameraIndex < cameras.Count)  // Use the existing cameraIndex variable
                        {
                            cameras[cameraIndex].FrameCount++;
                            // Debug: Log first 5 frames to verify event is firing
                            if (cameras[cameraIndex].FrameCount <= 5)
                            {
                                Console.WriteLine($"Camera {cameraIndex + 1} frame {cameras[cameraIndex].FrameCount}");
                            }
                        }
                    };

                    // Add error handler to detect when camera stops unexpectedly
                    camera.ImagingControl.DeviceLost += (s, e) =>
                    {
                        LogCameraInfo($"Camera {cameraIndex + 1} DEVICE LOST during recording!");
                        if (isRecording)
                        {
                            MessageBox.Show($"WARNING: Camera {cameraIndex + 1} disconnected during recording!",
                                        "Camera Lost", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    };
                    // Add click handler for expanding preview
                    int clickIndex = i;  // Capture for click handler
                    camera.ImagingControl.MouseClick += (s, e) => ToggleCameraExpansion(clickIndex); 
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error setting up camera {i + 1} ({deviceName}): {ex.Message}",
                                    "Camera Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                this.Controls.Add(camera.ImagingControl);
                cameras.Add(camera);
            }



            if (cameras.Count > 0)
            {
                btnStartLive.Enabled = true;
                if (!suppressMessage)
                {
                    MessageBox.Show($"Detected {cameras.Count} camera(s)!", 
                                    "Cameras Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                
                // Update RAM estimate with new camera info
                UpdateRamEstimate();  // ← ADD THIS LINE
            }

            int requiredWidth = SIDE_MARGIN * 2 + (cameras.Count * (CAMERA_WIDTH + CAMERA_SPACING));
            if (requiredWidth > this.Width)
            {
                this.Width = Math.Min(requiredWidth, 1920);
            }
            UpdateRamEstimate(); 

            if (cmbPreviewSize != null && cmbLayoutMode != null)
            {
                UpdateCameraLayout();
            }
        }

        private void BtnManageCameras_Click(object? sender, EventArgs e)
        {
            // Stop live preview if running (we'll restart after)
            bool wasLive = cameras.Any(c => c.ImagingControl.LiveVideoRunning);
            
            if (isRecording)
            {
                MessageBox.Show("Cannot manage cameras while recording!\n\nStop recording first.",
                                "Recording Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            // Get all available devices
            List<string> allDevices = new List<string>();
            using (var tempControl = new ICImagingControl())
            {
                var devices = tempControl.Devices;
                if (devices != null)
                {
                    allDevices = devices.Select(d => d.Name).ToList();
                }
            }
            
            if (allDevices.Count == 0)
            {
                MessageBox.Show("No cameras detected!", "No Cameras", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            // Create dialog
            Form manageForm = new Form
            {
                Text = "Manage Cameras",
                Size = new System.Drawing.Size(500, 400),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            
            int y = 20;
            
            Label lblInfo = new Label
            {
                Text = "Select which cameras to use:",
                Location = new System.Drawing.Point(20, y),
                Size = new System.Drawing.Size(450, 20),
                Font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold)
            };
            manageForm.Controls.Add(lblInfo);
            y += 30;
            
            Label lblNote = new Label
            {
                Text = "Unchecked cameras will be removed from the session.\nCheck to re-add previously removed cameras.",
                Location = new System.Drawing.Point(20, y),
                Size = new System.Drawing.Size(450, 35),
                ForeColor = System.Drawing.Color.Gray,
                Font = new System.Drawing.Font("Arial", 8)
            };
            manageForm.Controls.Add(lblNote);
            y += 45;
            
            // Create checkboxes for each detected device
            List<CheckBox> deviceCheckboxes = new List<CheckBox>();
            
            foreach (string device in allDevices)
            {
                bool isCurrentlyActive = cameras.Any(c => c.DeviceName == device);
                bool isExcluded = excludedCameraDevices.Contains(device);
                
                CheckBox chkDevice = new CheckBox
                {
                    Text = device,
                    Location = new System.Drawing.Point(40, y),
                    Size = new System.Drawing.Size(400, 25),
                    Checked = isCurrentlyActive && !isExcluded,
                    Font = new System.Drawing.Font("Arial", 9)
                };
                
                // Add status indicator
                if (isCurrentlyActive)
                {
                    chkDevice.Text += " (active)";
                    chkDevice.ForeColor = System.Drawing.Color.Green;
                }
                else if (isExcluded)
                {
                    chkDevice.Text += " (excluded)";
                    chkDevice.ForeColor = System.Drawing.Color.Gray;
                }
                else
                {
                    chkDevice.Text += " (new)";
                    chkDevice.ForeColor = System.Drawing.Color.Blue;
                }
                
                manageForm.Controls.Add(chkDevice);
                deviceCheckboxes.Add(chkDevice);
                y += 30;
            }
            
            y += 20;
            
            // Select All / Deselect All buttons
            Button btnSelectAll = new Button
            {
                Text = "Select All",
                Location = new System.Drawing.Point(120, y),
                Size = new System.Drawing.Size(100, 30)
            };
            btnSelectAll.Click += (s, ev) =>
            {
                foreach (var chk in deviceCheckboxes)
                    chk.Checked = true;
            };
            manageForm.Controls.Add(btnSelectAll);
            
            Button btnDeselectAll = new Button
            {
                Text = "Deselect All",
                Location = new System.Drawing.Point(230, y),
                Size = new System.Drawing.Size(100, 30)
            };
            btnDeselectAll.Click += (s, ev) =>
            {
                foreach (var chk in deviceCheckboxes)
                    chk.Checked = false;
            };
            manageForm.Controls.Add(btnDeselectAll);
            
            y += 50;
            
            // OK/Cancel buttons
            Button btnCancel = new Button
            {
                Text = "Cancel",
                Location = new System.Drawing.Point(260, y),
                Size = new System.Drawing.Size(100, 35),
                DialogResult = DialogResult.Cancel
            };
            manageForm.Controls.Add(btnCancel);
            
            Button btnApply = new Button
            {
                Text = "Apply",
                Location = new System.Drawing.Point(370, y),
                Size = new System.Drawing.Size(100, 35),
                DialogResult = DialogResult.OK
            };
            manageForm.Controls.Add(btnApply);
            
            manageForm.AcceptButton = btnApply;
            manageForm.CancelButton = btnCancel;
            
            // Adjust form height based on number of devices
            manageForm.Height = Math.Max(400, y + 80);
            
            if (manageForm.ShowDialog() == DialogResult.OK)
            {
                // Update excluded devices list
                excludedCameraDevices.Clear();
                
                for (int i = 0; i < allDevices.Count; i++)
                {
                    if (!deviceCheckboxes[i].Checked)
                    {
                        excludedCameraDevices.Add(allDevices[i]);
                    }
                }
                
                // Check if anything actually changed
                var currentDevices = cameras.Select(c => c.DeviceName).ToHashSet();
                var newActiveDevices = allDevices.Where(d => !excludedCameraDevices.Contains(d)).ToHashSet();
                
                if (!currentDevices.SetEquals(newActiveDevices))
                {
                    // Stop live if running
                    if (wasLive)
                    {
                        foreach (var camera in cameras)
                        {
                            try { camera.ImagingControl.LiveStop(); } catch { }
                        }
                    }
                    
                    // Rebuild camera list
                    RebuildCameraList();
                    
                    // Restart live if it was running
                    if (wasLive && cameras.Count > 0)
                    {
                        foreach (var camera in cameras)
                        {
                            try
                            {
                                camera.ImagingControl.LiveStart();
                                camera.LastFpsUpdate = DateTime.Now;
                                camera.FrameCount = 0;
                            }
                            catch { }
                        }
                        
                        btnStartLive.Enabled = false;
                        btnStopLive.Enabled = true;
                        btnStartRecording.Enabled = true;
                    }
                    
                    MessageBox.Show($"Camera configuration updated!\n\nActive cameras: {cameras.Count}",
                                    "Cameras Updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
        private void RebuildCameraList()
        {
            // Save current custom names before clearing
            Dictionary<string, string> savedNames = new Dictionary<string, string>();
            foreach (var cam in cameras)
            {
                savedNames[cam.DeviceName] = cam.CustomName;
            }
            
            // Clear existing cameras from UI
            foreach (var cam in cameras)
            {
                try
                {
                    if (cam.ImagingControl.LiveVideoRunning)
                    {
                        cam.ImagingControl.LiveStop();
                    }
                }
                catch { }
                
                this.Controls.Remove(cam.ImagingControl);
                this.Controls.Remove(cam.NameLabel);
                this.Controls.Remove(cam.FpsLabel);
                this.Controls.Remove(cam.NameTextBox);
                this.Controls.Remove(cam.SettingsButton);
                cam.ImagingControl.Dispose();
            }
            cameras.Clear();
            
            // Get all available devices
            string[] deviceNames;
            using (var tempControl = new ICImagingControl())
            {
                var devices = tempControl.Devices;
                if (devices == null || devices.Length == 0)
                {
                    btnStartLive.Enabled = false;
                    btnStopLive.Enabled = false;
                    btnStartRecording.Enabled = false;
                    UpdateRamEstimate();
                    return;
                }
                deviceNames = devices.Select(d => d.Name).ToArray();
            }
            
            System.Threading.Thread.Sleep(200);
            
            // Filter out excluded devices
            var activeDevices = deviceNames.Where(d => !excludedCameraDevices.Contains(d)).ToList();
            
            // Create camera controls for active devices only
            for (int i = 0; i < activeDevices.Count; i++)
            {
                string deviceName = activeDevices[i];
                var camera = new CameraControl(deviceName);
                
                int xPosition = SIDE_MARGIN + (i * (CAMERA_WIDTH + CAMERA_SPACING));
                int yPosition = TOP_MARGIN;
                
                // Restore custom name if we had one, otherwise use Camera1, Camera2, etc.
                string customName = savedNames.ContainsKey(deviceName) ? savedNames[deviceName] : $"Camera{i + 1}";
                camera.CustomName = customName;
                
                // Editable camera name textbox
                camera.NameTextBox = new TextBox
                {
                    Text = customName,
                    Location = new System.Drawing.Point(xPosition, yPosition),
                    Size = new System.Drawing.Size(CAMERA_WIDTH - 10, 20),
                    Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold),
                    BorderStyle = BorderStyle.FixedSingle
                };
                int capturedIndex = i;
                camera.NameTextBox.TextChanged += (s, e) =>
                {
                    cameras[capturedIndex].CustomName = camera.NameTextBox.Text;
                };
                this.Controls.Add(camera.NameTextBox);
                
                // Device name label
                camera.NameLabel.Text = deviceName;
                camera.NameLabel.Location = new System.Drawing.Point(xPosition, yPosition + 22);
                camera.NameLabel.AutoSize = false;
                camera.NameLabel.Size = new System.Drawing.Size(CAMERA_WIDTH, 18);
                camera.NameLabel.Font = new System.Drawing.Font("Arial", 7);
                camera.NameLabel.ForeColor = System.Drawing.Color.Gray;
                this.Controls.Add(camera.NameLabel);
                
                camera.SettingsButton = new Button
                {
                    Text = "⚙️ Settings",
                    Location = new System.Drawing.Point(xPosition + CAMERA_WIDTH - 80, yPosition + 42),
                    Size = new System.Drawing.Size(75, 22),
                    Font = new System.Drawing.Font("Arial", 7)
                };
                int cameraIndex = i;
                camera.SettingsButton.Click += (s, e) => ShowCameraSettings(cameraIndex);
                this.Controls.Add(camera.SettingsButton);
                camera.SettingsButton.BringToFront();
                
                // Load saved settings for this camera if available
                if (settings.CameraSettingsByDevice.ContainsKey(deviceName))
                {
                    camera.Settings = settings.CameraSettingsByDevice[deviceName];
                }
                
                camera.FpsLabel.Text = "Ready";
                camera.FpsLabel.Location = new System.Drawing.Point(xPosition, yPosition + 45);
                camera.FpsLabel.Size = new System.Drawing.Size(CAMERA_WIDTH, 20);
                camera.FpsLabel.ForeColor = System.Drawing.Color.Green;
                camera.FpsLabel.Font = new System.Drawing.Font("Arial", 8, System.Drawing.FontStyle.Bold);
                this.Controls.Add(camera.FpsLabel);
                
                camera.ImagingControl.Location = new System.Drawing.Point(xPosition, yPosition + 68);
                camera.ImagingControl.Size = new System.Drawing.Size(CAMERA_WIDTH, CAMERA_HEIGHT);
                camera.ImagingControl.Name = $"icImagingControl{i + 1}";
                camera.ImagingControl.LiveDisplay = true;
                
                try
                {
                    camera.ImagingControl.Device = deviceName;
                    
                    // Set video format
                    if (camera.ImagingControl.VideoFormats != null && camera.ImagingControl.VideoFormats.Length > 0)
                    {
                        var y800Format = camera.ImagingControl.VideoFormats
                            .FirstOrDefault(f => (f.ToString().Contains("Y800") || f.ToString().Contains("Mono8")) &&
                                                f.ToString().Contains("640") &&
                                                f.ToString().Contains("480"));
                        
                        if (y800Format != null)
                        {
                            camera.ImagingControl.VideoFormat = y800Format;
                        }
                        else
                        {
                            camera.ImagingControl.VideoFormat = camera.ImagingControl.VideoFormats[0];
                        }
                    }
                    
                    // Apply saved format if available
                    if (camera.Settings != null && !string.IsNullOrEmpty(camera.Settings.Format))
                    {
                        var savedFormat = camera.ImagingControl.VideoFormats
                            .FirstOrDefault(f => f.ToString() == camera.Settings.Format);
                        
                        if (savedFormat != null)
                        {
                            try
                            {
                                camera.ImagingControl.VideoFormat = savedFormat;
                            }
                            catch { }
                        }
                        
                        ApplyCameraSettings(camera, camera.Settings);
                    }
                    
                    camera.ImagingControl.ImageAvailable += (s, e) =>
                    {
                        if (cameraIndex < cameras.Count)
                        {
                            cameras[cameraIndex].FrameCount++;
                        }
                    };
                    
                    camera.ImagingControl.DeviceLost += (s, e) =>
                    {
                        LogCameraInfo($"Camera {cameraIndex + 1} DEVICE LOST!");
                        if (isRecording)
                        {
                            MessageBox.Show($"WARNING: Camera {cameraIndex + 1} disconnected during recording!",
                                        "Camera Lost", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    };
                    // Add click handler for expanding preview
                    int clickIndex = i;  // Capture for click handler
                    camera.ImagingControl.Click += (s, e) => ToggleCameraExpansion(clickIndex);
                }
                catch (Exception ex)
                {
                    LogCameraInfo($"Error setting up camera {i + 1} ({deviceName}): {ex.Message}");
                }
                
                this.Controls.Add(camera.ImagingControl);
                cameras.Add(camera);
            }
            
            if (cameras.Count > 0)
            {
                btnStartLive.Enabled = true;
                UpdateRamEstimate();
            }
            else
            {
                btnStartLive.Enabled = false;
                btnStopLive.Enabled = false;
                btnStartRecording.Enabled = false;
            }
            
            // Adjust window width
            int requiredWidth = SIDE_MARGIN * 2 + (cameras.Count * (CAMERA_WIDTH + CAMERA_SPACING));
            if (requiredWidth > this.Width)
            {
                this.Width = Math.Min(requiredWidth, 1920);
            }
            
            UpdateRamEstimate();
            if (cmbPreviewSize != null && cmbLayoutMode != null)
            {
                UpdateCameraLayout();
            }
        }
        private void ShowCameraSettings(int cameraIndex)
        {
            if (cameraIndex >= cameras.Count)
                return;
            
            var camera = cameras[cameraIndex];
            
            // Check if camera is busy
            if (isRecording)
            {
                MessageBox.Show("Cannot change settings while recording!",
                                "Camera Busy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            // Track if ANY camera is currently live
            wasLiveBeforeSettings = cameras.Any(c => c.ImagingControl.LiveVideoRunning);

            bool wasLive = camera.ImagingControl.LiveVideoRunning;

            // Store current settings to detect what changed
            string oldFormat = camera.Settings.Format;
            bool oldUseExternalTrigger = camera.Settings.UseExternalTrigger;
            float oldSoftwareFrameRate = camera.Settings.SoftwareFrameRate;

            // DON'T stop camera - let it run so Update button works in property dialog

            // Show settings dialog
            using (CameraSettingsDialog dialog = new CameraSettingsDialog(camera.ImagingControl, camera.Settings, cameraIndex + 1))
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    // Check what changed
                    bool formatChanged = !string.IsNullOrEmpty(dialog.Settings.Format) && 
                                        dialog.Settings.Format != oldFormat;
                    bool triggerModeChanged = dialog.Settings.UseExternalTrigger != oldUseExternalTrigger;
                    bool fpsChanged = dialog.Settings.SoftwareFrameRate != oldSoftwareFrameRate;
                    
                    bool needsRefresh = formatChanged || triggerModeChanged || fpsChanged;
                    
                    // Save settings
                    camera.Settings = dialog.Settings;
                    settings.CameraSettingsByDevice[camera.DeviceName] = dialog.Settings.Clone();
                    settings.Save();

                    if (needsRefresh)
                    {
                        // Format, trigger mode, or FPS changed - need full refresh
                        LogCameraInfo($"Camera {cameraIndex + 1}: Settings changed (Format: {formatChanged}, Trigger: {triggerModeChanged}, FPS: {fpsChanged}) - refreshing cameras");
                        
                        // Refresh all cameras to apply settings properly (suppress the detection message)
                        DetectAndSetupCameras(suppressMessage: true);

                        // If cameras were live before, restart them automatically
                        if (wasLiveBeforeSettings)
                        {
                            try
                            {
                                int successCount = 0;
                                for (int i = 0; i < cameras.Count; i++)
                                {
                                    try
                                    {
                                        cameras[i].ImagingControl.LiveStart();
                                        cameras[i].LastFpsUpdate = DateTime.Now;
                                        cameras[i].FrameCount = 0;
                                        successCount++;
                                    }
                                    catch { }
                                }

                                btnStartLive.Enabled = false;
                                btnStopLive.Enabled = true;
                                btnStartRecording.Enabled = true;
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        // Only camera properties changed (exposure, gain, etc.)
                        // SDK already applied them via Update/OK button - no refresh needed!
                        LogCameraInfo($"Camera {cameraIndex + 1}: Only camera properties changed - no refresh needed");
                        
                        // Update device name label to show device name only
                        camera.NameLabel.Text = camera.DeviceName;
                    }
                }
            }
        }

        private void ApplyCameraSettings(CameraControl camera, CameraSettings settings)
        {
            try
            {
                // Apply VCD properties from saved XML string
                if (!string.IsNullOrEmpty(settings.VCDPropertiesXml) && camera.ImagingControl.VCDPropertyItems != null)
                {
                    try
                    {
                        camera.ImagingControl.VCDPropertyItems.Load(settings.VCDPropertiesXml);
                        LogCameraInfo($"Loaded VCD properties for {camera.DeviceName}");
                    }
                    catch (Exception ex)
                    {
                        LogCameraInfo($"Could not load VCD properties: {ex.Message}");
                    }
                }
                
                // Apply saved software frame rate if available (only if NOT using external trigger)
                try
                {
                    if (!settings.UseExternalTrigger)
                    {
                        // Software-controlled frame rate
                        if (camera.ImagingControl.DeviceFrameRateAvailable)
                        {
                            // Check if the saved rate is valid
                            if (camera.ImagingControl.DeviceFrameRates != null && 
                                camera.ImagingControl.DeviceFrameRates.Length > 0)
                            {
                                float[] validRates = camera.ImagingControl.DeviceFrameRates;
                                float requestedRate = settings.SoftwareFrameRate;
                                
                                // Find closest valid rate (nearest neighbor)
                                float closestRate;
                                var lowerRates = validRates.Where(r => r <= requestedRate).ToArray();
                                var higherRates = validRates.Where(r => r > requestedRate).ToArray();

                                if (lowerRates.Length == 0)
                                {
                                    // All rates are higher, use minimum
                                    closestRate = validRates.Min();
                                }
                                else if (higherRates.Length == 0)
                                {
                                    // All rates are lower, use maximum
                                    closestRate = validRates.Max();
                                }
                                else
                                {
                                    // Find closest from both sides
                                    float lower = lowerRates.Max();
                                    float higher = higherRates.Min();
                                    
                                    // Pick whichever is closer
                                    closestRate = (requestedRate - lower) < (higher - requestedRate) ? lower : higher;
                                }
                                
                                camera.ImagingControl.DeviceFrameRate = closestRate;
                                LogCameraInfo($"Camera {camera.DeviceName}: Set SOFTWARE frame rate to {closestRate:F1} fps");
                            }
                            else
                            {
                                // Try to set it anyway
                                camera.ImagingControl.DeviceFrameRate = settings.SoftwareFrameRate;
                                LogCameraInfo($"Camera {camera.DeviceName}: Attempted to set SOFTWARE frame rate to {settings.SoftwareFrameRate:F1} fps");
                            }
                        }
                        else
                        {
                            LogCameraInfo($"Camera {camera.DeviceName}: Frame rate control not supported");
                        }
                    }
                    else
                    {
                        // External trigger mode - don't set software frame rate
                        LogCameraInfo($"Camera {camera.DeviceName}: Using EXTERNAL TRIGGER (software frame rate not applied)");
                    }
                }
                catch (Exception ex)
                {
                    LogCameraInfo($"Camera {camera.DeviceName}: Failed to set frame rate - {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                LogCameraInfo($"Error applying camera settings: {ex.Message}");
            }
        }
        private string GetCurrentFormat(ICImagingControl control)
        {
            try
            {
                if (control.VideoFormatCurrent != null)
                {
                    return control.VideoFormatCurrent.ToString();
                }
                return "Unknown";
            }
            catch
            {
                return "Error";
            }
        }
        
        private void FpsTimer_Tick(object? sender, EventArgs e)
        {
            foreach (var camera in cameras)
            {
                DateTime now = DateTime.Now;
                double elapsedSeconds = (now - camera.LastFpsUpdate).TotalSeconds;
                
                if (elapsedSeconds > 0)
                {
                    double fps = camera.FrameCount / elapsedSeconds;
                    
                    if (isRecording && camera.RecordingStartTime.HasValue)
                    {
                        if (camera.IsTimelapseMode)
                        {
                            // Timelapse mode - show frame count and next capture countdown
                            int nextCaptureSeconds = 0;
                            if (camera.LastTimelapseCapture != null)
                            {
                                double elapsed = (DateTime.Now - camera.LastTimelapseCapture.Value).TotalSeconds;
                                nextCaptureSeconds = (int)(camera.TimelapseIntervalSeconds - elapsed);
                                if (nextCaptureSeconds < 0) nextCaptureSeconds = 0;
                            }
                            
                            camera.FpsLabel.Text = $"⏱️ TIMELAPSE | {camera.TimelapseFrameCount} frames | Next in {nextCaptureSeconds}s";
                            camera.FpsLabel.ForeColor = System.Drawing.Color.Purple;
                        }
                        else if (cmbRecordingMode.SelectedItem?.ToString() == "Loop Recording" && camera.LoopBuffer != null)
                        {
                            // Loop mode - show buffer status
                            int bufferFrames = 0;
                            lock (camera.LoopBufferLock)
                            {
                                bufferFrames = camera.LoopBuffer.Count;
                            }
                            
                            double expectedFps;
                            if (camera.Settings.UseExternalTrigger)
                            {
                                expectedFps = (double)numExternalTriggerFps.Value;
                            }
                            else
                            {
                                expectedFps = camera.Settings.SoftwareFrameRate;
                            }
                            
                            double bufferSeconds = bufferFrames / expectedFps;
                            int loopDuration = (int)numLoopDuration.Value;
                            
                            camera.FpsLabel.Text = $"🔴 LOOP {loopDuration}s | {bufferFrames} frames ({bufferSeconds:F1}s)";
                            camera.FpsLabel.ForeColor = System.Drawing.Color.Orange;
                        }
                        else
                        {
                            // Normal recording
                            double recordingDuration = (now - camera.RecordingStartTime.Value).TotalSeconds;
                            
                            // Get file size
                            long fileSize = 0;
                            if (camera.RecordingFilePath != null && File.Exists(camera.RecordingFilePath))
                            {
                                try
                                {
                                    FileInfo fileInfo = new FileInfo(camera.RecordingFilePath);
                                    fileSize = fileInfo.Length / (1024 * 1024); // MB
                                }
                                catch { }
                            }
                            
                            camera.FpsLabel.Text = $"🔴 REC | {fileSize}MB | {recordingDuration:F1}s";
                            camera.FpsLabel.ForeColor = System.Drawing.Color.Red;
                        }
                    }
                    else
                    {
                        // During live preview - show configured/expected FPS
                        if (camera.Settings.UseExternalTrigger)
                        {
                            camera.FpsLabel.Text = "LIVE | External trigger";
                        }
                        else
                        {
                            camera.FpsLabel.Text = $"LIVE | {camera.Settings.SoftwareFrameRate:F1} fps (configured)";
                        }
                        camera.FpsLabel.ForeColor = System.Drawing.Color.Green;
                    }
                    
                    // Reset counter
                    camera.FrameCount = 0;
                    camera.LastFpsUpdate = now;
                }
            }
        }
        
        private void UpdateCameraLayout()
        {
            if (cameras.Count == 0)
                return;
            
            // Check if a camera is expanded
            if (expandedCameraIndex >= 0 && expandedCameraIndex < cameras.Count)
            {
                // EXPANDED VIEW - Show only one camera large
                UpdateExpandedLayout();
            }
            else
            {
                // GRID VIEW - Show all cameras
                UpdateGridLayout();
            }
        }

        private void UpdateExpandedLayout()
        {
            var expandedCamera = cameras[expandedCameraIndex];
            
            // Hide all other cameras
            for (int i = 0; i < cameras.Count; i++)
            {
                if (i != expandedCameraIndex)
                {
                    cameras[i].NameTextBox.Visible = false;
                    cameras[i].NameLabel.Visible = false;
                    cameras[i].FpsLabel.Visible = false;
                    cameras[i].SettingsButton.Visible = false;
                    cameras[i].ImagingControl.Visible = false;
                }
            }
            
            // Calculate available space for expanded preview
            int availableWidth = this.ClientSize.Width - (SIDE_MARGIN * 2);
            int availableHeight = this.ClientSize.Height - TOP_MARGIN;

            // Use actual camera aspect ratio if available, otherwise 4:3
            float aspectRatio = 4.0f / 3.0f; // Default 4:3
            try
            {
                if (expandedCamera.ImagingControl.VideoFormatCurrent != null)
                {
                    string format = expandedCamera.ImagingControl.VideoFormatCurrent.ToString();
                    var match = System.Text.RegularExpressions.Regex.Match(format, @"(\d+)x(\d+)");
                    if (match.Success)
                    {
                        int camWidth = int.Parse(match.Groups[1].Value);
                        int camHeight = int.Parse(match.Groups[2].Value);
                        aspectRatio = (float)camWidth / camHeight;
                    }
                }
            }
            catch { }

            int expandedWidth, expandedHeight;

            // Space needed above the preview (name, device label, fps label, etc.)
            int controlsHeight = 75;

            // Calculate maximum size that fits while maintaining aspect ratio
            int maxHeight = availableHeight - controlsHeight - 10; // 10px bottom margin
            int maxWidth = availableWidth - 10;   // Minimal side margins

            if ((float)maxWidth / maxHeight > aspectRatio)
            {
                // Height is limiting factor - use full height
                expandedHeight = maxHeight;
                expandedWidth = (int)(expandedHeight * aspectRatio);
            }
            else
            {
                // Width is limiting factor - use full width
                expandedWidth = maxWidth;
                expandedHeight = (int)(expandedWidth / aspectRatio);
            }

            // Center horizontally
            int xPosition = SIDE_MARGIN + (availableWidth - expandedWidth) / 2;
            int yPosition = TOP_MARGIN;
            
            // Position expanded camera controls
            expandedCamera.NameTextBox.Location = new System.Drawing.Point(xPosition, yPosition);
            expandedCamera.NameTextBox.Size = new System.Drawing.Size(Math.Min(expandedWidth, 400), 25);
            expandedCamera.NameTextBox.Visible = true;
            
            expandedCamera.NameLabel.Location = new System.Drawing.Point(xPosition, yPosition + 28);
            expandedCamera.NameLabel.Size = new System.Drawing.Size(expandedWidth, 18);
            expandedCamera.NameLabel.Visible = true;
            
            expandedCamera.SettingsButton.Location = new System.Drawing.Point(xPosition + expandedWidth - 80, yPosition + 48);
            expandedCamera.SettingsButton.Visible = true;
            
            expandedCamera.FpsLabel.Location = new System.Drawing.Point(xPosition, yPosition + 50);
            expandedCamera.FpsLabel.Size = new System.Drawing.Size(expandedWidth - 90, 20);
            expandedCamera.FpsLabel.Visible = true;
            
            expandedCamera.ImagingControl.Location = new System.Drawing.Point(xPosition, yPosition + 75);
            expandedCamera.ImagingControl.Size = new System.Drawing.Size(expandedWidth, expandedHeight);
            expandedCamera.ImagingControl.Visible = true;
            
            // Add "Back to Grid" label if not already present
            Label? backLabel = this.Controls.OfType<Label>().FirstOrDefault(l => l.Tag?.ToString() == "backToGrid");
            if (backLabel == null)
            {
                backLabel = new Label
                {
                    Text = "← Back to Grid (Esc)",
                    Location = new System.Drawing.Point(SIDE_MARGIN, TOP_MARGIN + 2),
                    Size = new System.Drawing.Size(250, 20),  // Reduced width
                    Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold),
                    ForeColor = System.Drawing.Color.Blue,
                    Cursor = Cursors.Hand,
                    Tag = "backToGrid",
                    BackColor = System.Drawing.Color.Transparent  // Make background transparent
                };
                backLabel.Click += (s, e) => CollapseExpandedCamera();
                this.Controls.Add(backLabel);
            }
            backLabel.Visible = true;
        }

        private void UpdateGridLayout()
        {
            // Hide the "Back to Grid" label
            Label? backLabel = this.Controls.OfType<Label>().FirstOrDefault(l => l.Tag?.ToString() == "backToGrid");
            if (backLabel != null)
            {
                backLabel.Visible = false;
            }
            
            // Get selected preview size
            if (cmbPreviewSize != null)
            {
                switch (cmbPreviewSize.SelectedIndex)
                {
                    case 0: // Small
                        currentPreviewWidth = 240;
                        currentPreviewHeight = 180;
                        break;
                    case 1: // Medium
                        currentPreviewWidth = 320;
                        currentPreviewHeight = 240;
                        break;
                    case 2: // Large
                        currentPreviewWidth = 400;
                        currentPreviewHeight = 300;
                        break;
                    case 3: // XLarge
                        currentPreviewWidth = 480;
                        currentPreviewHeight = 360;
                        break;
                    default:
                        currentPreviewWidth = 320;
                        currentPreviewHeight = 240;
                        break;
                }
            }
            
            // Calculate grid layout
            int camerasPerRow;
            int numRows;
            
            if (cmbLayoutMode == null || cmbLayoutMode.SelectedIndex == 0) // Auto Grid
            {
                // Calculate optimal grid based on available width
                int availableWidth = this.ClientSize.Width - (SIDE_MARGIN * 2);
                camerasPerRow = Math.Max(1, availableWidth / (currentPreviewWidth + CAMERA_SPACING));
                camerasPerRow = Math.Min(camerasPerRow, cameras.Count); // Don't exceed camera count
                numRows = (int)Math.Ceiling((double)cameras.Count / camerasPerRow);
            }
            else // Manual row count
            {
                numRows = cmbLayoutMode.SelectedIndex; // 1, 2, 3, or 4 rows
                camerasPerRow = (int)Math.Ceiling((double)cameras.Count / numRows);
            }
            
            // Reposition all camera controls and make them visible
            for (int i = 0; i < cameras.Count; i++)
            {
                var camera = cameras[i];
                
                int col = i % camerasPerRow;
                int row = i / camerasPerRow;
                
                int xPosition = SIDE_MARGIN + (col * (currentPreviewWidth + CAMERA_SPACING));
                int yPosition = TOP_MARGIN + (row * (currentPreviewHeight + 90)); // 90 = space for labels and controls
                
                // Update positions and make visible
                camera.NameTextBox.Location = new System.Drawing.Point(xPosition, yPosition);
                camera.NameTextBox.Size = new System.Drawing.Size(currentPreviewWidth - 10, 20);
                camera.NameTextBox.Visible = true;
                
                camera.NameLabel.Location = new System.Drawing.Point(xPosition, yPosition + 22);
                camera.NameLabel.Size = new System.Drawing.Size(currentPreviewWidth, 18);
                camera.NameLabel.Visible = true;
                
                camera.SettingsButton.Location = new System.Drawing.Point(xPosition + currentPreviewWidth - 80, yPosition + 42);
                camera.SettingsButton.Visible = true;
                
                camera.FpsLabel.Location = new System.Drawing.Point(xPosition, yPosition + 45);
                camera.FpsLabel.Size = new System.Drawing.Size(currentPreviewWidth, 20);
                camera.FpsLabel.Visible = true;
                
                camera.ImagingControl.Location = new System.Drawing.Point(xPosition, yPosition + 68);
                camera.ImagingControl.Size = new System.Drawing.Size(currentPreviewWidth, currentPreviewHeight);
                camera.ImagingControl.Visible = true;
            }
            
            // Adjust form size to fit all cameras
            int requiredWidth = SIDE_MARGIN * 2 + (camerasPerRow * (currentPreviewWidth + CAMERA_SPACING));
            int requiredHeight = TOP_MARGIN + (numRows * (currentPreviewHeight + 90)) + 50;
            
            // Set minimum size but don't force it larger than screen
            this.MinimumSize = new System.Drawing.Size(
                Math.Min(requiredWidth, Screen.PrimaryScreen.WorkingArea.Width),
                Math.Min(requiredHeight, Screen.PrimaryScreen.WorkingArea.Height)
            );
            
            // Resize form if needed
            if (this.Width < requiredWidth)
                this.Width = Math.Min(requiredWidth, Screen.PrimaryScreen.WorkingArea.Width);
            if (this.Height < requiredHeight)
                this.Height = Math.Min(requiredHeight, Screen.PrimaryScreen.WorkingArea.Height);
            
            // Update RAM estimate
            UpdateRamEstimate();
        }

        private void BtnBrowseWorkingFolder_Click(object? sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select Working Folder for Recordings";
                dialog.SelectedPath = txtWorkingFolder.Text;
                dialog.ShowNewFolderButton = true;
                
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string selectedPath = dialog.SelectedPath;
                    
                    // Check if folder is writable
                    try
                    {
                        string testFile = Path.Combine(selectedPath, $"test_write_{Guid.NewGuid()}.tmp");
                        File.WriteAllText(testFile, "test");
                        File.Delete(testFile);
                    }
                    catch
                    {
                        MessageBox.Show($"Cannot write to this folder:\n{selectedPath}\n\nPlease choose a different location.",
                                        "Folder Not Writable", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    
                    // Check for OneDrive
                    if (IsOneDrivePath(selectedPath))
                    {
                        var result = MessageBox.Show(
                            "⚠️ WARNING: This appears to be a OneDrive folder!\n\n" +
                            $"Path: {selectedPath}\n\n" +
                            "OneDrive sync can interfere with recording.\n\n" +
                            "Are you sure you want to use this folder?",
                            "OneDrive Detected",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);
                        
                        if (result == DialogResult.No)
                            return;
                    }
                    
                    txtWorkingFolder.Text = selectedPath;
                    settings.WorkingFolder = selectedPath;
                    settings.Save();
                }
            }
        }

        private void BtnRefreshCameras_Click(object? sender, EventArgs e)
        {
            DetectAndSetupCameras();
        }

        private void BtnStartLive_Click(object? sender, EventArgs e)
        {
            try
            {
                int successCount = 0;
                List<string> errors = new List<string>();

                for (int i = 0; i < cameras.Count; i++)
                {
                    var camera = cameras[i];
                    try
                    {
                        camera.ImagingControl.LiveStart();
                        camera.LastFpsUpdate = DateTime.Now;
                        camera.FrameCount = 0;
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Camera {i + 1}: {ex.Message}");
                    }
                }

                btnStartLive.Enabled = false;
                btnStopLive.Enabled = true;
                btnScreenshot.Enabled = true;
                btnStartRecording.Enabled = true;

                // Only show popup if there were errors
                if (errors.Count > 0)
                {
                    MessageBox.Show($"Started {successCount}/{cameras.Count} cameras.\n\nErrors:\n{string.Join("\n", errors)}",
                                    "Partial Success", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting cameras: {ex.Message}",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnStopLive_Click(object? sender, EventArgs e)
        {
            try
            {
                if (isRecording)
                {
                    StopRecording();
                }

                foreach (var camera in cameras)
                {
                    try
                    {
                        camera.ImagingControl.LiveStop();
                    }
                    catch { }
                }

                btnStartLive.Enabled = true;
                btnStopLive.Enabled = false;
                btnStartRecording.Enabled = false;

                MessageBox.Show("All cameras stopped.", "Success", 
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error stopping cameras: {ex.Message}",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnScreenshot_Click(object? sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(txtWorkingFolder.Text))
                {
                    MessageBox.Show("Please set a working folder first!",
                                "No Working Folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Create Screenshots subfolder
                string screenshotsFolder = Path.Combine(txtWorkingFolder.Text, "Screenshots");
                Directory.CreateDirectory(screenshotsFolder);

                // Generate timestamp for filenames
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
                
                int savedCount = 0;
                List<string> errors = new List<string>();

                foreach (var camera in cameras)
                {
                    try
                    {
                        if (camera.ImagingControl.LiveVideoRunning)
                        {
                            ImageBuffer? imageBuffer = null;
                            
                            // Try to get image buffer from appropriate source
                            if (camera.ImagingControl.Sink is FrameHandlerSink frameHandlerSink)
                            {
                                // Preview mode - using FrameHandlerSink
                                imageBuffer = frameHandlerSink.LastAcquiredBuffer;
                            }
                            else if (camera.OriginalSink is FrameHandlerSink originalFrameHandlerSink)
                            {
                                // Recording mode - the original sink was stored before recording
                                imageBuffer = originalFrameHandlerSink.LastAcquiredBuffer;
                            }
                            
                            if (imageBuffer != null)
                            {
                                // Generate filename with camera name or number
                                string cameraName = string.IsNullOrWhiteSpace(camera.CustomName) 
                                    ? $"Camera{cameras.IndexOf(camera) + 1}" 
                                    : camera.CustomName.Replace(" ", "_");
                                
                                string filename = $"{timestamp}_{cameraName}.png";
                                string filepath = Path.Combine(screenshotsFolder, filename);
                                
                                // Create bitmap and save as PNG
                                var bitmap = imageBuffer.CreateBitmapWrap();
                                using (System.Drawing.Bitmap clone = new System.Drawing.Bitmap(bitmap))
                                {
                                    clone.Save(filepath, System.Drawing.Imaging.ImageFormat.Png);
                                }
                                
                                savedCount++;
                            }
                            else
                            {
                                errors.Add($"Camera {cameras.IndexOf(camera) + 1}: No image buffer available");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Camera {cameras.IndexOf(camera) + 1}: {ex.Message}");
                    }
                }

                // Show result
                if (savedCount > 0)
                {
                    string message = $"✅ Saved {savedCount} screenshot(s) to:\n{screenshotsFolder}";
                    if (errors.Count > 0)
                    {
                        message += $"\n\n⚠️ Errors:\n{string.Join("\n", errors)}";
                    }
                    MessageBox.Show(message, "Screenshots Saved", 
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("❌ No screenshots captured!\n\n" +
                                (errors.Count > 0 ? string.Join("\n", errors) : "Make sure cameras are running."),
                                "Screenshot Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error taking screenshots: {ex.Message}",
                            "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnStartRecording_Click(object? sender, EventArgs e)
        {
            try
            {
                // Ensure working folder exists
                string workingFolder = txtWorkingFolder.Text;
                if (string.IsNullOrEmpty(workingFolder))
                {
                    MessageBox.Show("Please select a Working Folder first!",
                                    "No Working Folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                
                if (!Directory.Exists(workingFolder))
                {
                    try
                    {
                        Directory.CreateDirectory(workingFolder);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Cannot create working folder:\n{ex.Message}",
                                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                cmbRecordingMode.Enabled = false;
                
                int successCount = 0;
                List<string> errors = new List<string>();

                // Generate timestamp-based base name
                currentRecordingBaseName = $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}";

                LogCameraInfo("=== RECORDING START ===");
                
                // Check recording mode
                string recordingMode = cmbRecordingMode.SelectedItem?.ToString() ?? "Normal Recording";
                bool isLoopMode = recordingMode == "Loop Recording";
                bool isTimelapseMode = recordingMode == "Timelapse";
                int loopDurationSeconds = (int)numLoopDuration.Value;

                if (isLoopMode)
                {
                    LogCameraInfo($"Loop mode ENABLED - {loopDurationSeconds}s buffer");
                }
                else if (isTimelapseMode)
                {
                    int hours = (int)numTimelapseHours.Value;
                    int minutes = (int)numTimelapseMinutes.Value;
                    int seconds = (int)numTimelapseSeconds.Value;
                    double intervalSeconds = (hours * 3600) + (minutes * 60) + seconds;
                    
                    if (intervalSeconds < 1)
                    {
                        MessageBox.Show("Timelapse interval must be at least 1 second!",
                                    "Invalid Interval", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        cmbRecordingMode.Enabled = true;
                        return;
                    }
                    
                    LogCameraInfo($"Timelapse mode ENABLED - frame every {intervalSeconds}s");
                }
                // PHASE 1: Stop all cameras first (synchronized)
                foreach (var camera in cameras)
                {
                    try
                    {
                        camera.ImagingControl.LiveStop();
                        camera.TotalRecordedFrames = 0;
                        camera.FrameCount = 0;

                        // Initialize timelapse if needed
                        if (isTimelapseMode)
                        {
                            int hours = (int)numTimelapseHours.Value;
                            int minutes = (int)numTimelapseMinutes.Value;
                            int seconds = (int)numTimelapseSeconds.Value;
                            double intervalSeconds = (hours * 3600) + (minutes * 60) + seconds;
                            
                            camera.IsTimelapseMode = true;
                            camera.TimelapseIntervalSeconds = intervalSeconds;
                            camera.TimelapseFrameCount = 0;
                            camera.LastTimelapseCapture = null;
                            camera.TimelapseImagePaths = new List<string>();
                            
                            LogCameraInfo($"{camera.CustomName}: Timelapse initialized - interval {intervalSeconds}s");
                        }
                        else
                        {
                            camera.IsTimelapseMode = false;
                        }
                        
                        // Initialize loop buffer if in loop mode
                        if (isLoopMode)
                        {
                            // Calculate expected FPS
                            double expectedFps;
                            if (camera.Settings.UseExternalTrigger)
                            {
                                expectedFps = (double)numExternalTriggerFps.Value;  // ← Use user-specified value
                            }
                            else
                            {
                                expectedFps = camera.Settings.SoftwareFrameRate;
                            }
                            
                            camera.MaxLoopFrames = (int)(loopDurationSeconds * expectedFps);
                            // ✅ Pre-allocate capacity to avoid resizing
                            camera.LoopBuffer = new Queue<System.Drawing.Bitmap>(camera.MaxLoopFrames + 10);
                            LogCameraInfo($"{camera.CustomName}: Loop buffer initialized for {camera.MaxLoopFrames} frames ({expectedFps:F1} fps)");
                        }
                    }
                    catch { }
                }

                System.Threading.Thread.Sleep(50);

                // PHASE 2: Configure sinks for all cameras
                for (int i = 0; i < cameras.Count; i++)
                {
                    var camera = cameras[i];
                    try
                    {
                        camera.OriginalSink = camera.ImagingControl.Sink;

                        if (isLoopMode)
                        {
                            // Loop mode - use FrameHandlerSink in grab mode
                            var frameHandlerSink = new FrameHandlerSink();
                            frameHandlerSink.SnapMode = false; // Grab mode (continuous capture)
                            camera.ImagingControl.Sink = frameHandlerSink;
                            camera.RecordingFilePath = null;
                            
                            // Start background polling thread
                            camera.LoopCancelToken = new CancellationTokenSource();
                            camera.LastFrameNumber = -1;
                            
                            int cameraIndex = i;
                            camera.LoopCaptureTask = Task.Run(() => 
                            {
                                LogCameraInfo($"{cameras[cameraIndex].CustomName}: Loop capture thread started");
                                
                                while (!camera.LoopCancelToken.Token.IsCancellationRequested)
                                {
                                    try
                                    {
                                        var sink = cameras[cameraIndex].ImagingControl.Sink as FrameHandlerSink;
                                        if (sink == null) continue;
                                        
                                        // Check if a new frame arrived using FrameCount
                                        long currentFrameCount = sink.FrameCount;
                                        if (currentFrameCount != cameras[cameraIndex].LastFrameNumber)
                                        {
                                            // ✅ Check for dropped frames
                                            if (cameras[cameraIndex].LastFrameNumber >= 0)
                                            {
                                                long frameDelta = currentFrameCount - cameras[cameraIndex].LastFrameNumber;
                                                if (frameDelta > 1)
                                                {
                                                    cameras[cameraIndex].DroppedFrames += (int)(frameDelta - 1);
                                                    if (cameras[cameraIndex].DroppedFrames <= 10 || cameras[cameraIndex].DroppedFrames % 50 == 0)
                                                    {
                                                        LogCameraInfo($"⚠️ {cameras[cameraIndex].CustomName}: Dropped {frameDelta - 1} frames! Total dropped: {cameras[cameraIndex].DroppedFrames}");
                                                    }
                                                }
                                            }
                                            
                                            cameras[cameraIndex].ExpectedFrameCount++;
                                            cameras[cameraIndex].LastFrameNumber = currentFrameCount;
                                            
                                            // Get the frame from LastAcquiredBuffer (NOT ImageActiveBuffer!)
                                            var imageBuffer = sink.LastAcquiredBuffer;
                                            var bitmap = imageBuffer?.Bitmap;
                                            
                                            if (bitmap != null)
                                            {
                                                lock (cameras[cameraIndex].LoopBufferLock)
                                                {
                                                    // DEEP clone bitmap with complete memory independence
                                                    System.Drawing.Bitmap clonedFrame = DeepCloneBitmap(bitmap);
                                                    
                                                    // Add to buffer
                                                    cameras[cameraIndex].LoopBuffer!.Enqueue(clonedFrame);
                                                    
                                                    // Log progress
                                                    if (cameras[cameraIndex].LoopBuffer.Count <= 5 || 
                                                        cameras[cameraIndex].LoopBuffer.Count % 100 == 0)
                                                    {
                                                        LogCameraInfo($"Loop: {cameras[cameraIndex].CustomName} buffer = {cameras[cameraIndex].LoopBuffer.Count} frames (total captured: {currentFrameCount})");
                                                    }
                                                    
                                                    // Remove oldest if over limit
                                                    if (cameras[cameraIndex].LoopBuffer.Count > cameras[cameraIndex].MaxLoopFrames)
                                                    {
                                                        var oldFrame = cameras[cameraIndex].LoopBuffer.Dequeue();
                                                        oldFrame.Dispose();
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        LogCameraInfo($"Loop polling error for {cameras[cameraIndex].CustomName}: {ex.Message}");
                                    }
                                    
                                    // Poll every 5ms (fast enough for 200fps)
                                    Thread.Sleep(5);
                                }
                                
                                LogCameraInfo($"{cameras[cameraIndex].CustomName}: Loop capture thread stopped");
                            }, camera.LoopCancelToken.Token);
                            
                            LogCameraInfo($"Camera {i + 1} loop mode - polling thread with FrameHandlerSink started");
                        }
                        else if (isTimelapseMode)
                        {
                            // Timelapse mode - use FrameHandlerSink for frame capture
                            var frameHandlerSink = new FrameHandlerSink();
                            frameHandlerSink.SnapMode = false;
                            camera.ImagingControl.Sink = frameHandlerSink;
                            camera.RecordingFilePath = null;
                            
                            // Create shared timelapse folder with camera subfolder
                            string timelapseMainFolder = Path.Combine(workingFolder, $"Timelapse_Frames_{currentRecordingBaseName}");
                            string cameraName = string.Join("_", camera.CustomName.Split(Path.GetInvalidFileNameChars()));
                            string cameraFolder = Path.Combine(timelapseMainFolder, cameraName);
                            Directory.CreateDirectory(cameraFolder);
                            
                            LogCameraInfo($"Camera {i + 1} timelapse mode - folder: {cameraFolder}");
                        }
                        else
                        {
                            // Normal recording - use file sink
                            string safeName = string.Join("_", camera.CustomName.Split(Path.GetInvalidFileNameChars()));
                            if (string.IsNullOrWhiteSpace(safeName))
                                safeName = $"Camera{i + 1}";
                                
                            string aviFilename = Path.Combine(workingFolder, 
                                $"{currentRecordingBaseName}_{safeName}.avi");
                            
                            camera.RecordingFilePath = aviFilename;
                            camera.ImagingControl.Sink = new MediaStreamSink((AviCompressor?)null, aviFilename);
                            
                            LogCameraInfo($"Camera {i + 1} sink configured: {aviFilename}");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Camera {i + 1} setup: {ex.Message}");
                        LogCameraInfo($"Camera {i + 1} setup ERROR: {ex.Message}");
                    }
                }

                // PHASE 3: Start all cameras (simple now - polling handles frame capture)
                for (int i = 0; i < cameras.Count; i++)
                {
                    var camera = cameras[i];
                    
                    try
                    {
                        camera.ImagingControl.LiveStart();
                        System.Threading.Thread.Sleep(10);
                        camera.RecordingStartTime = DateTime.Now;
                        successCount++;
                        LogCameraInfo($"Camera {i + 1} started at {camera.RecordingStartTime.Value:HH:mm:ss.fff}");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Camera {i + 1} start: {ex.Message}");
                        LogCameraInfo($"Camera {i + 1} start ERROR: {ex.Message}");
                    }
                }
                if (successCount > 0)
                {
                    isRecording = true;
                    btnStartRecording.Enabled = false;
                    btnStopRecording.Enabled = true;
                    btnScreenshot.Enabled = true;

                    // Start timelapse capture timer if in timelapse mode
                    if (isTimelapseMode)
                    {
                        timelapseTimer = new System.Windows.Forms.Timer();
                        timelapseTimer.Interval = 100;
                        
                        timelapseTimer.Tick += (s, e) =>
                        {
                            if (!isRecording)
                            {
                                timelapseTimer.Stop();
                                timelapseTimer.Dispose();
                                return;
                            }
                            
                            DateTime now = DateTime.Now;
                            
                            foreach (var camera in cameras)
                            {
                                if (!camera.IsTimelapseMode) continue;
                                
                                // Check if it's time to capture
                                bool shouldCapture = false;
                                if (camera.LastTimelapseCapture == null)
                                {
                                    shouldCapture = true; // First capture
                                }
                                else
                                {
                                    double elapsed = (now - camera.LastTimelapseCapture.Value).TotalSeconds;
                                    if (elapsed >= camera.TimelapseIntervalSeconds)
                                    {
                                        shouldCapture = true;
                                    }
                                }
                                
                                if (shouldCapture)
                                {
                                    try
                                    {
                                        if (camera.ImagingControl.Sink is FrameHandlerSink tlSink)
                                        {
                                            var imageBuffer = tlSink.LastAcquiredBuffer;
                                            if (imageBuffer != null)
                                            {
                                                var bitmap = imageBuffer.CreateBitmapWrap();
                                                using (System.Drawing.Bitmap clone = new System.Drawing.Bitmap(bitmap))
                                                {
                                                    string cameraName = string.Join("_", camera.CustomName.Split(Path.GetInvalidFileNameChars()));
                                                    string timelapseMainFolder = Path.Combine(workingFolder, $"Timelapse_Frames_{currentRecordingBaseName}");
                                                    string cameraFolder = Path.Combine(timelapseMainFolder, cameraName);
                                                    string frameNumber = camera.TimelapseFrameCount.ToString("D6");
                                                    string imagePath = Path.Combine(cameraFolder, $"{currentRecordingBaseName}_{cameraName}_{frameNumber}.png");
                                                    
                                                    clone.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
                                                    camera.TimelapseImagePaths!.Add(imagePath);
                                                    camera.TimelapseFrameCount++;
                                                    camera.LastTimelapseCapture = now;
                                                    
                                                    LogCameraInfo($"{camera.CustomName}: Captured timelapse frame {camera.TimelapseFrameCount}");
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        LogCameraInfo($"{camera.CustomName}: Timelapse capture error - {ex.Message}");
                                    }
                                }
                            }
                        };
                        
                        timelapseTimer.Start();
                        LogCameraInfo("Timelapse capture timer started");
                    }

                    if (chkMaxDuration.Checked)
                    {
                        // Convert max duration to minutes based on selected unit
                        int maxMinutes = (int)numMaxMinutes.Value;
                        string durationUnit = cmbMaxDurationUnit.SelectedItem?.ToString() ?? "minutes";
                        
                        if (durationUnit == "hours")
                            maxMinutes = maxMinutes * 60;
                        else if (durationUnit == "days")
                            maxMinutes = maxMinutes * 60 * 24;
                        
                        DateTime recordingStart = DateTime.Now;
                        
                        System.Windows.Forms.Timer maxDurationTimer = new System.Windows.Forms.Timer();
                        maxDurationTimer.Interval = 10000; // Check every 10 seconds
                        maxDurationTimer.Tick += (s, e) =>
                        {
                            if (!isRecording)
                            {
                                maxDurationTimer.Stop();
                                maxDurationTimer.Dispose();
                                return;
                            }
                            
                            double elapsedMinutes = (DateTime.Now - recordingStart).TotalMinutes;
                            if (elapsedMinutes >= maxMinutes)
                            {
                                maxDurationTimer.Stop();
                                LogCameraInfo($"Max duration reached ({maxMinutes} minutes) - stopping recording automatically");
                                
                                // Auto-stop recording
                                this.Invoke(new Action(() =>
                                {
                                    BtnStopRecording_Click(null, EventArgs.Empty);
                                    MessageBox.Show($"Recording automatically stopped after {maxMinutes} minute(s).",
                                                    "Max Duration Reached", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                }));
                                
                                maxDurationTimer.Dispose();
                            }
                        };
                        maxDurationTimer.Start();
                        
                        LogCameraInfo($"Max duration monitoring enabled: {maxMinutes} minutes");
                    }
                }
                
                
            // Only show popup if there were errors
            if (errors.Count > 0)
            {
                MessageBox.Show($"Recording started on {successCount}/{cameras.Count} cameras.\n\nErrors:\n{string.Join("\n", errors)}",
                                "Partial Success", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting recording: {ex.Message}\n\nStack trace: {ex.StackTrace}",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                LogCameraInfo($"CRITICAL ERROR: {ex.Message}");
            }
        }

        private void BtnStopRecording_Click(object? sender, EventArgs e)
        {
            btnScreenshot.Enabled = btnStopLive.Enabled;
            StopRecording();
        }

        private int GetVideoFrameCount(string videoFile)
        {
            try
            {
                string ffprobePath = Path.Combine(Path.GetDirectoryName(FFMPEG_PATH)!, "ffprobe.exe");
                
                if (!File.Exists(ffprobePath))
                    return 0;

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v error -select_streams v:0 -count_packets -show_entries stream=nb_read_packets -of csv=p=0 \"{videoFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using (Process process = Process.Start(startInfo)!)
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    if (int.TryParse(output.Trim(), out int frameCount))
                    {
                        return frameCount;
                    }
                }
            }
            catch { }

            return 0;
        }

        private double GetVideoFrameRate(string videoFile)
        {
            try
            {
                string ffprobePath = Path.Combine(Path.GetDirectoryName(FFMPEG_PATH)!, "ffprobe.exe");
                
                if (!File.Exists(ffprobePath))
                    return 0;

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v error -select_streams v:0 -show_entries stream=r_frame_rate -of default=noprint_wrappers=1:nokey=1 \"{videoFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using (Process process = Process.Start(startInfo)!)
                {
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();

                    // Frame rate often comes as "90/1" or "11500/1000"
                    if (output.Contains("/"))
                    {
                        string[] parts = output.Split('/');
                        if (parts.Length == 2 && 
                            double.TryParse(parts[0], out double numerator) && 
                            double.TryParse(parts[1], out double denominator) && 
                            denominator > 0)
                        {
                            return numerator / denominator;
                        }
                    }
                    else if (double.TryParse(output, out double fps))
                    {
                        return fps;
                    }
                }
            }
            catch { }

            return 0;
        }

        private void StopRecording()
        {
            try
            {
                if (isRecording)
                {
                    LogCameraInfo("=== RECORDING STOP REQUESTED ===");
                    
                    // Re-enable recording mode controls
                    cmbRecordingMode.Enabled = true;

                    string recordingMode = cmbRecordingMode.SelectedItem?.ToString() ?? "Normal Recording";
                    bool isLoopMode = recordingMode == "Loop Recording";
                    bool isTimelapseMode = recordingMode == "Timelapse";
                    
                    int successCount = 0;
                    List<string> aviFiles = new List<string>();
                    List<(string filename, int frames, double duration)> frameStats = new List<(string, int, double)>();
                    List<long> aviFileSizes = new List<long>();

                    if (isTimelapseMode)
                    {
                        // TIMELAPSE MODE: Compile images to video
                        LogCameraInfo("Timelapse mode - stopping and compiling videos");
                        
                        // Stop cameras
                        foreach (var camera in cameras)
                        {
                            try
                            {
                                camera.RecordingStopTime = DateTime.Now;
                                camera.ImagingControl.LiveStop();
                                LogCameraInfo($"{camera.CustomName}: Stopped - captured {camera.TimelapseFrameCount} frames");
                            }
                            catch (Exception ex)
                            {
                                LogCameraInfo($"{camera.CustomName}: Error stopping - {ex.Message}");
                            }
                        }
                        
                        // Stop timelapse timer - store reference at class level instead
                        if (timelapseTimer != null)
                        {
                            timelapseTimer.Stop();
                            timelapseTimer.Dispose();
                            timelapseTimer = null;
                        }
                        
                        // Restart cameras in live mode
                        foreach (var camera in cameras)
                        {
                            try
                            {
                                camera.ImagingControl.Sink = camera.OriginalSink;
                                camera.ImagingControl.LiveStart();
                            }
                            catch { }
                        }
                        
                        // Show timelapse compilation dialog
                        ShowTimelapseCompilationDialog();
                    }
                    else if (isLoopMode)
                    {
                        // LOOP MODE: CRITICAL - Stop cameras FIRST to freeze the buffer!
                        LogCameraInfo("Loop mode - stopping cameras immediately to freeze buffer");
                        
                        // STEP 1: Stop all cameras IMMEDIATELY (before touching threads or buffers)
                        foreach (var camera in cameras)
                        {
                            try
                            {
                                camera.RecordingStopTime = DateTime.Now;
                                camera.ImagingControl.LiveStop();
                                LogCameraInfo($"{camera.CustomName}: Camera stopped at {camera.RecordingStopTime.Value:HH:mm:ss.fff}");
                            }
                            catch (Exception ex)
                            {
                                LogCameraInfo($"{camera.CustomName}: Error stopping camera - {ex.Message}");
                            }
                        }
                        
                        // STEP 2: Give a tiny moment for any in-flight frames to finish processing
                        System.Threading.Thread.Sleep(100);
                        
                        // STEP 3: Now stop the polling threads (they won't add any more frames)
                        LogCameraInfo("Loop mode - stopping polling threads");
                        foreach (var camera in cameras)
                        {
                            if (camera.LoopCancelToken != null)
                            {
                                camera.LoopCancelToken.Cancel();
                                camera.LoopCaptureTask?.Wait(1000); // Wait up to 1 second
                                
                                // ✅ Dispose properly
                                camera.LoopCancelToken.Dispose();
                                camera.LoopCancelToken = null;
                                camera.LoopCaptureTask = null;
                                
                                LogCameraInfo($"{camera.CustomName}: Polling thread stopped and disposed");
                            }
                        }
                        
                        // STEP 4: Buffers are now frozen - save them to temporary AVI files
                        LogCameraInfo("Loop mode - saving buffered frames to temp files");
                        
                        string tempFolder = Path.Combine(Path.GetTempPath(), $"LoopRecording_{DateTime.Now:yyyyMMddHHmmss}");
                        Directory.CreateDirectory(tempFolder);
                        
                        for (int i = 0; i < cameras.Count; i++)
                        {
                            var camera = cameras[i];
                            try
                            {
                                // Save loop buffer to AVI file
                                if (camera.LoopBuffer != null && camera.LoopBuffer.Count > 0)
                                {
                                    // Sanitize camera name - remove invalid chars AND spaces
                                    string safeName = string.Join("_", camera.CustomName.Split(Path.GetInvalidFileNameChars()));
                                    safeName = safeName.Replace(" ", "_"); // Remove spaces
                                    if (string.IsNullOrWhiteSpace(safeName))
                                        safeName = $"Camera{i + 1}";
                                    
                                    string tempAviFile = Path.Combine(tempFolder, $"{currentRecordingBaseName}_{safeName}.avi");
                                    
                                    LogCameraInfo($"{camera.CustomName}: Saving {camera.LoopBuffer.Count} frames from frozen buffer");
                                    
                                    // CRITICAL: Create deep clones of bitmaps so we can dispose originals safely
                                    List<System.Drawing.Bitmap> clonedFrames = new List<System.Drawing.Bitmap>();
                                    
                                    lock (camera.LoopBufferLock)
                                    {
                                        foreach (var frame in camera.LoopBuffer)
                                        {
                                            // Deep clone each bitmap
                                            clonedFrames.Add((System.Drawing.Bitmap)frame.Clone());
                                        }
                                    }
                                    
                                    LogCameraInfo($"{camera.CustomName}: Cloned {clonedFrames.Count} frames for saving");
                                    
                                    // Save the cloned frames (this can take time without blocking the original buffer)
                                    SaveLoopBufferToAvi(clonedFrames, tempAviFile, camera);
                                    
                                    // Now dispose the clones after saving is complete
                                    foreach (var clonedFrame in clonedFrames)
                                    {
                                        clonedFrame.Dispose();
                                    }
                                    clonedFrames.Clear();
                                    
                                    aviFiles.Add(tempAviFile);
                                    LogCameraInfo($"{camera.CustomName}: Saved and cleaned up {camera.LoopBuffer.Count} buffered frames");
                                }
                                else
                                {
                                    LogCameraInfo($"{camera.CustomName}: Buffer was empty or null - no frames to save");
                                }
                                
                                // NOW it's safe to clear the original buffer and restore camera
                                lock (camera.LoopBufferLock)
                                {
                                    if (camera.LoopBuffer != null)
                                    {
                                        while (camera.LoopBuffer.Count > 0)
                                        {
                                            var frame = camera.LoopBuffer.Dequeue();
                                            frame.Dispose();
                                        }
                                        camera.LoopBuffer = null;
                                    }
                                }
                                
                                // Restore original sink and restart live
                                camera.ImagingControl.Sink = camera.OriginalSink;
                                camera.ImagingControl.LiveStart();
                                
                                successCount++;
                            }
                            catch (Exception ex)
                            {
                                LogCameraInfo($"Camera {i + 1} loop stop ERROR: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        // NORMAL MODE: Stop cameras and capture individual stop times
                        for (int i = 0; i < cameras.Count; i++)
                        {
                            var camera = cameras[i];
                            try
                            {
                                System.Threading.Thread.Sleep(10);
                                camera.RecordingStopTime = DateTime.Now;
                                LogCameraInfo($"Camera {i + 1} stopping at {camera.RecordingStopTime.Value:HH:mm:ss.fff}");
                                
                                camera.ImagingControl.LiveStop();
                                camera.ImagingControl.Sink = camera.OriginalSink;
                                camera.ImagingControl.LiveStart();
                                
                                if (camera.RecordingFilePath != null)
                                {
                                    aviFiles.Add(camera.RecordingFilePath);
                                }
                                
                                successCount++;
                            }
                            catch (Exception ex)
                            {
                                LogCameraInfo($"Camera {i + 1} stop ERROR: {ex.Message}");
                            }
                        }
                    }

                    isRecording = false;
                    btnStartRecording.Enabled = true;
                    btnStopRecording.Enabled = false;

                    // Give files a moment to finalize
                    System.Threading.Thread.Sleep(500);

                    // Get frame counts and calculate durations (same for both modes)

                    // Get frame counts and calculate durations (same for both modes)
                    for (int i = 0; i < aviFiles.Count && i < cameras.Count; i++)
                    {
                        string aviFile = aviFiles[i];
                        var camera = cameras[i];
                        
                        if (File.Exists(aviFile))
                        {
                            FileInfo fileInfo = new FileInfo(aviFile);
                            long fileSize = fileInfo.Length;
                            aviFileSizes.Add(fileSize);
                            
                            int frameCount = 0;
                            double actualDuration = 0;
                            
                            if (isLoopMode)
                            {
                                // For loop mode, we know the frame count from the buffer
                                // Try FFprobe first, but fall back to buffer count if it fails
                                frameCount = GetVideoFrameCount(aviFile);
                                
                                if (frameCount == 0)
                                {
                                    // FFprobe failed, use the buffer size we saved
                                    LogCameraInfo($"Camera {i + 1}: FFprobe failed, using buffer frame count");
                                    // Count frames directly from the file using OpenCV
                                    try
                                    {
                                        using (var capture = new VideoCapture(aviFile))
                                        {
                                            frameCount = (int)capture.FrameCount;
                                        }
                                    }
                                    catch
                                    {
                                        // Last resort: estimate from file size
                                        // Assume uncompressed grayscale 640x480 = ~300KB per frame
                                        frameCount = (int)(fileSize / 300000);
                                        LogCameraInfo($"Camera {i + 1}: Using estimated frame count: {frameCount}");
                                    }
                                }
                                
                                // Calculate duration from frame count
                                double expectedFps;
                                if (camera.Settings.UseExternalTrigger)
                                {
                                    expectedFps = (double)numExternalTriggerFps.Value;
                                }
                                else
                                {
                                    expectedFps = camera.Settings.SoftwareFrameRate;
                                }
                                actualDuration = frameCount / expectedFps;
                            }
                            else
                            {
                                // Normal recording mode
                                frameCount = GetVideoFrameCount(aviFile);
                                
                                if (camera.RecordingStartTime.HasValue && camera.RecordingStopTime.HasValue)
                                {
                                    actualDuration = (camera.RecordingStopTime.Value - camera.RecordingStartTime.Value).TotalSeconds;
                                }
                            }
                            
                            double aviDuration = GetVideoDuration(aviFile);
                            
                            LogCameraInfo($"Camera {i + 1} AVI contains: {frameCount} frames");
                            LogCameraInfo($"Camera {i + 1} Duration: {actualDuration:F2}s, AVI claims: {aviDuration:F2}s");
                            
                            if (camera.DroppedFrames > 0)
                            {
                                LogCameraInfo($"⚠️ Camera {i + 1} DROPPED {camera.DroppedFrames} frames during recording!");
                            }
                            else
                            {
                                LogCameraInfo($"✅ Camera {i + 1} captured all frames without drops");
                            }
                            string filename = Path.GetFileName(aviFile);
                            frameStats.Add((filename, frameCount, actualDuration));
                        }
                    }

                    // Check if we got any valid files
                    if (frameStats.Count == 0)
                    {
                        MessageBox.Show("No valid video files were created. Recording may have failed.",
                                        "Recording Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    // Show frame selection and save dialog (same as normal)
                    using (FrameSelectionDialog dialog = new FrameSelectionDialog(
                        frameStats, 
                        aviFileSizes,
                        aviFiles,
                        txtWorkingFolder.Text, 
                        currentRecordingBaseName))
                    {
                        if (dialog.ShowDialog() == DialogResult.OK)
                        {
                            ProcessRecordingOutputWithTrimming(aviFiles, dialog.SelectedPath, dialog.SelectedFilename, 
                                                            dialog.SelectedFps, dialog.SelectedFormat, dialog.FrameRanges);
                        }
                        else
                        {
                            // User cancelled - delete temp files
                            foreach (string aviFile in aviFiles)
                            {
                                try
                                {
                                    if (File.Exists(aviFile))
                                    {
                                        File.Delete(aviFile);
                                        LogCameraInfo($"Deleted: {aviFile}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogCameraInfo($"Failed to delete {aviFile}: {ex.Message}");
                                }
                            }
                            
                            // Delete temp folder if it exists
                            if (isLoopMode)
                            {
                                try
                                {
                                    string tempFolder = Path.GetDirectoryName(aviFiles[0]);
                                    if (tempFolder != null && Directory.Exists(tempFolder))
                                    {
                                        Directory.Delete(tempFolder, true);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error stopping recording: {ex.Message}",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                LogCameraInfo($"Stop recording ERROR: {ex.Message}");
            }
        }
        private void SaveLoopBufferToAvi(List<System.Drawing.Bitmap> frames, string outputPath, CameraControl camera)
        {
            if (frames.Count == 0) return;
            
            try
            {
                // Get frame dimensions and format from first frame
                int width = frames[0].Width;
                int height = frames[0].Height;
                bool isColor = frames[0].PixelFormat != System.Drawing.Imaging.PixelFormat.Format8bppIndexed;
                
                LogCameraInfo($"Saving {frames.Count} frames to {outputPath}");
                LogCameraInfo($"  Format: {width}x{height}, Color: {isColor}, PixelFormat: {frames[0].PixelFormat}");
                
                // Calculate FPS
                double fps;
                if (camera.Settings.UseExternalTrigger)
                {
                    fps = (double)numExternalTriggerFps.Value;
                }
                else
                {
                    fps = camera.Settings.SoftwareFrameRate;
                }
                
                // Use OpenCvSharp to write frames to AVI
                using (var writer = new OpenCvSharp.VideoWriter())
                {
                    // Use MJPG codec (more compatible than DIB, works for both grayscale and color)
                    // MJPG = Motion JPEG, widely supported
                    int fourcc = OpenCvSharp.FourCC.MJPG;
                    
                    // Open video writer with correct color flag
                    bool opened = writer.Open(outputPath, fourcc, fps, new OpenCvSharp.Size(width, height), isColor);
                    
                    if (!opened)
                    {
                        LogCameraInfo($"ERROR: Could not open video writer for {outputPath}");
                        LogCameraInfo($"  Tried: MJPG codec, {width}x{height}, {fps} fps, isColor={isColor}");
                        return;
                    }
                    
                    LogCameraInfo($"Video writer opened successfully");
                    
                    // Write all frames
                    int frameCount = 0;
                    foreach (var bitmap in frames)
                    {
                        try
                        {
                            // Convert bitmap to Mat
                            using (var mat = OpenCvSharp.Extensions.BitmapConverter.ToMat(bitmap))
                            {
                                // If grayscale but Mat has 3 channels, convert to single channel
                                if (!isColor && mat.Channels() == 3)
                                {
                                    using (var grayMat = new OpenCvSharp.Mat())
                                    {
                                        OpenCvSharp.Cv2.CvtColor(mat, grayMat, OpenCvSharp.ColorConversionCodes.BGR2GRAY);
                                        writer.Write(grayMat);
                                    }
                                }
                                // If color but Mat has 1 channel, convert to 3 channels
                                else if (isColor && mat.Channels() == 1)
                                {
                                    using (var colorMat = new OpenCvSharp.Mat())
                                    {
                                        OpenCvSharp.Cv2.CvtColor(mat, colorMat, OpenCvSharp.ColorConversionCodes.GRAY2BGR);
                                        writer.Write(colorMat);
                                    }
                                }
                                else
                                {
                                    // Channels match, write directly
                                    writer.Write(mat);
                                }
                                
                                frameCount++;
                                
                                // Log progress every 100 frames
                                if (frameCount % 100 == 0)
                                {
                                    LogCameraInfo($"  Wrote {frameCount}/{frames.Count} frames");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogCameraInfo($"ERROR writing frame {frameCount}: {ex.Message}");
                        }
                    }
                    
                    writer.Release();
                    LogCameraInfo($"Successfully saved {frameCount} frames to {outputPath}");
                }
                
                // Verify the file was created and has data
                if (File.Exists(outputPath))
                {
                    FileInfo fileInfo = new FileInfo(outputPath);
                    LogCameraInfo($"  Output file size: {fileInfo.Length / (1024 * 1024)} MB");
                }
                else
                {
                    LogCameraInfo($"ERROR: Output file was not created!");
                }
            }
            catch (Exception ex)
            {
                LogCameraInfo($"ERROR saving loop buffer to AVI: {ex.Message}");
                LogCameraInfo($"  Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private void ProcessRecordingOutput(List<string> aviFiles, string outputPath, string outputFilename, 
                                            double fps, FrameSelectionDialog.OutputFormat format)
        {
            try
            {
                Directory.CreateDirectory(outputPath);
                
                if (format == FrameSelectionDialog.OutputFormat.MP4)
                {
                    // Convert to MP4 and delete AVIs
                    ConvertAvisToMp4(aviFiles, outputPath, outputFilename, fps, deleteOriginals: true);
                }
                else if (format == FrameSelectionDialog.OutputFormat.AVI)
                {
                    // Re-encode AVIs with correct FPS metadata
                    ConvertAvisToAvi(aviFiles, outputPath, outputFilename, fps);
                }
                else // Both
                {
                    // First save AVIs
                    ConvertAvisToAvi(aviFiles, outputPath, outputFilename, fps);
                    // Then create MP4s (keep the newly created AVIs)
                    List<string> newAviFiles = new List<string>();
                    for (int i = 0; i < aviFiles.Count; i++)
                    {
                        string newAviPath = Path.Combine(outputPath, $"{outputFilename}_Camera{i + 1}.avi");
                        newAviFiles.Add(newAviPath);
                    }
                    ConvertAvisToMp4(newAviFiles, outputPath, outputFilename, fps, deleteOriginals: false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error processing recording output: {ex.Message}",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                LogCameraInfo($"ProcessRecordingOutput ERROR: {ex.Message}");
            }
        }

        private void ProcessRecordingOutputWithTrimming(List<string> aviFiles, string outputPath, string outputFilename, 
                                                        double fps, FrameSelectionDialog.OutputFormat format,
                                                        Dictionary<string, (int startFrame, int endFrame)> frameRanges)
        {
            try
            {
                Directory.CreateDirectory(outputPath);
                
                // Extract custom camera names from AVI filenames
                List<string> customNames = new List<string>();
                foreach (string aviFile in aviFiles)
                {
                    string filename = Path.GetFileNameWithoutExtension(aviFile);
                    // Remove the timestamp prefix to get just the camera name
                    // Format is: Recording_20251110_162636_CameraName
                    int lastUnderscore = filename.LastIndexOf('_');
                    if (lastUnderscore >= 0)
                    {
                        customNames.Add(filename.Substring(lastUnderscore + 1));
                    }
                    else
                    {
                        customNames.Add($"Camera{customNames.Count + 1}");
                    }
                }
                
                if (format == FrameSelectionDialog.OutputFormat.MP4)
                {
                    // Convert to MP4 with trimming and delete AVIs
                    ConvertAvisToMp4WithTrimming(aviFiles, outputPath, outputFilename, fps, frameRanges, deleteOriginals: true, customNames);
                }
                else if (format == FrameSelectionDialog.OutputFormat.AVI)
                {
                    // Re-encode AVIs with correct FPS metadata and trimming
                    ConvertAvisToAviWithTrimming(aviFiles, outputPath, outputFilename, fps, frameRanges, customNames);
                }
                else // Both
                {
                    // First save trimmed AVIs
                    ConvertAvisToAviWithTrimming(aviFiles, outputPath, outputFilename, fps, frameRanges, customNames);
                    // Then create MP4s (keep the newly created AVIs)
                    List<string> newAviFiles = new List<string>();
                    for (int i = 0; i < aviFiles.Count; i++)
                    {
                        string newAviPath = Path.Combine(outputPath, $"{outputFilename}_{customNames[i]}.avi");
                        newAviFiles.Add(newAviPath);
                    }
                    
                    // Create frame ranges for new files (full range since already trimmed)
                    var fullRanges = new Dictionary<string, (int, int)>();
                    foreach (var file in newAviFiles)
                    {
                        string filename = Path.GetFileName(file);
                        // Get frame count of trimmed file
                        int frameCount = GetVideoFrameCount(file);
                        fullRanges[filename] = (1, frameCount);
                    }
                    
                    ConvertAvisToMp4WithTrimming(newAviFiles, outputPath, outputFilename, fps, fullRanges, deleteOriginals: false, customNames);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error processing recording output: {ex.Message}",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                LogCameraInfo($"ProcessRecordingOutputWithTrimming ERROR: {ex.Message}");
            }
        }

        private void ConvertAvisToAviWithTrimming(List<string> aviFiles, string outputPath, string outputFilename,
                                    double fps, Dictionary<string, (int startFrame, int endFrame)> frameRanges,
                                    List<string> customNames)
        {
            // Ensure output directory exists
            Directory.CreateDirectory(outputPath);

            // Verify it's a valid path
            if (outputPath.Length > 260)
            {
                MessageBox.Show("Output path is too long! Windows has a 260 character limit.\n\nPlease choose a shorter path.",
                                "Path Too Long", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }    
                
            if (!File.Exists(FFMPEG_PATH))
            {
                MessageBox.Show($"FFmpeg not found at:\n{FFMPEG_PATH}\n\nPlease install FFmpeg.",
                                "FFmpeg Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Create progress form
            Form progressForm = new Form
            {
                Text = "Saving Trimmed AVI Files",
                Size = new System.Drawing.Size(500, 200),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ControlBox = false
            };

            Label statusLabel = new Label
            {
                Text = "Preparing...",
                Location = new System.Drawing.Point(20, 20),
                Size = new System.Drawing.Size(450, 40),
                Font = new System.Drawing.Font("Arial", 10)
            };
            progressForm.Controls.Add(statusLabel);

            Label detailLabel = new Label
            {
                Text = "",
                Location = new System.Drawing.Point(20, 65),
                Size = new System.Drawing.Size(450, 20),
                Font = new System.Drawing.Font("Arial", 8),
                ForeColor = System.Drawing.Color.Gray
            };
            progressForm.Controls.Add(detailLabel);

            ProgressBar progressBar = new ProgressBar
            {
                Location = new System.Drawing.Point(20, 95),
                Size = new System.Drawing.Size(450, 30),
                Minimum = 0,
                Maximum = aviFiles.Count
            };
            progressForm.Controls.Add(progressBar);

            progressForm.Show();
            Application.DoEvents();

            int successCount = 0;
            List<string> errors = new List<string>();
            
            for (int i = 0; i < aviFiles.Count; i++)
            {
                string aviFile = aviFiles[i];
                if (!File.Exists(aviFile))
                    continue;

                string filename = Path.GetFileName(aviFile);
                string cameraName = i < customNames.Count ? customNames[i] : $"Camera{i + 1}";
                // Sanitize camera name
                cameraName = string.Join("_", cameraName.Split(Path.GetInvalidFileNameChars()));

                // Use a temporary output file first, then rename
                string finalOutputAvi = Path.Combine(outputPath, $"{outputFilename}_{cameraName}.avi");
                string tempOutputAvi = Path.Combine(outputPath, $"temp_{outputFilename}_{cameraName}.avi");

                statusLabel.Text = $"Processing {i + 1} of {aviFiles.Count}: {cameraName}";
                Application.DoEvents();

                try
                {
                    // Detect source pixel format and codec tag
                    string pixelFormat = GetVideoPixelFormat(aviFile);
                    string codecTag = GetVideoCodecTag(aviFile);
                    
                    // Get frame range for this file
                    var range = frameRanges[filename];
                    int startFrame = range.startFrame;
                    int endFrame = range.endFrame;
                    int totalFrames = endFrame - startFrame + 1;
                    
                    // Calculate time offsets (frames are 1-based)
                    double startTime = (startFrame - 1) / fps;
                    double duration = totalFrames / fps;
                    
                    detailLabel.Text = $"Frames {startFrame}-{endFrame} ({totalFrames} frames) | Format: {pixelFormat} | Tag: {codecTag}";
                    Application.DoEvents();
                    
                    LogCameraInfo($"Trimming {filename}: frames {startFrame}-{endFrame}, pixel format: {pixelFormat}, codec tag: {codecTag}, fps: {fps}");
                    
                    // ✅ FIXED: Use temp file to avoid overwriting input
                    // For RGB/color formats, re-encode to ensure compatibility
                    // For grayscale (Y800), use stream copy for speed but fix frame rate with setpts
                    string videoCodec;
                    string filterComplex = "";

                    if (pixelFormat.Contains("bgr") || pixelFormat.Contains("rgb") || pixelFormat.Contains("bgra") || pixelFormat.Contains("rgba"))
                    {
                        // Color format - re-encode to ensure compatibility
                        videoCodec = "-c:v rawvideo -pix_fmt bgr24";
                        LogCameraInfo($"Using re-encoding for color format: {pixelFormat}");
                    }
                    else
                    {
                        // Grayscale - use stream copy for speed
                        videoCodec = "-c:v copy";
                        LogCameraInfo($"Using stream copy for format: {pixelFormat}");
                    }

                    string ffmpegArgs = $"-fflags +genpts -i \"{aviFile}\" -ss {startTime:F6} -t {duration:F6} {videoCodec} -r {fps:F3} -avoid_negative_ts make_zero -an -y \"{tempOutputAvi}\"";

                    LogCameraInfo($"FFmpeg command: ffmpeg {ffmpegArgs}");
                    
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = FFMPEG_PATH,
                        Arguments = ffmpegArgs,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true
                    };

                    string lastError = "";
                    
                    using (Process process = Process.Start(startInfo)!)
                    {
                        // Capture error output for debugging
                        process.ErrorDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                lastError = e.Data;
                                LogCameraInfo($"FFmpeg: {e.Data}");
                            }
                        };
                        process.BeginErrorReadLine();
                        
                        bool exited = process.WaitForExit(300000); // 5 minute timeout per file

                        if (!exited)
                        {
                            process.Kill();
                            errors.Add($"{cameraName}: Timeout (>5 minutes)");
                            LogCameraInfo($"ERROR: Timeout trimming {filename}");
                            
                            // ✅ Clean up temp file
                            if (File.Exists(tempOutputAvi))
                                File.Delete(tempOutputAvi);
                        }
                        else if (process.ExitCode == 0 && File.Exists(tempOutputAvi))  // ✅ CHANGED: Check temp file
                        {
                            FileInfo outputInfo = new FileInfo(tempOutputAvi);  // ✅ CHANGED: Use temp file
                            if (outputInfo.Length > 1000)
                            {
                                // ✅ NEW: Verify the temp output file is readable
                                int outputFrames = GetVideoFrameCount(tempOutputAvi);
                                LogCameraInfo($"Temp output file {Path.GetFileName(tempOutputAvi)}: {outputFrames} frames (expected {totalFrames}), {outputInfo.Length / (1024 * 1024)} MB");
                                
                                // Check if frame count matches expected
                                if (Math.Abs(outputFrames - totalFrames) > 2)
                                {
                                    LogCameraInfo($"WARNING: Frame count mismatch! Expected {totalFrames}, got {outputFrames}");
                                }
                                
                                // ✅ NEW: Delete original input file, then rename temp to final
                                File.Delete(aviFile);
                                File.Move(tempOutputAvi, finalOutputAvi);
                                
                                successCount++;
                                LogCameraInfo($"Successfully trimmed {filename} -> {Path.GetFileName(finalOutputAvi)}");
                            }
                            else
                            {
                                errors.Add($"{cameraName}: Output file too small (possibly corrupted)");
                                LogCameraInfo($"ERROR: Output file too small for {filename}");
                                
                                // ✅ Clean up temp file
                                if (File.Exists(tempOutputAvi))
                                    File.Delete(tempOutputAvi);
                            }
                        }
                        else
                        {
                            string errorMsg = $"Exit code: {process.ExitCode}";
                            if (!string.IsNullOrEmpty(lastError))
                                errorMsg += $" - {lastError}";
                            
                            errors.Add($"{cameraName}: {errorMsg}");
                            LogCameraInfo($"ERROR: Failed to trim {filename}: {errorMsg}");
                            
                            // ✅ CHANGED: Clean up temp file instead of outputAvi
                            if (File.Exists(tempOutputAvi))
                                File.Delete(tempOutputAvi);
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{cameraName}: {ex.Message}");
                    LogCameraInfo($"EXCEPTION trimming {filename}: {ex.Message}");
                    
                    // ✅ NEW: Clean up temp file on exception
                    try
                    {
                        if (File.Exists(tempOutputAvi))
                            File.Delete(tempOutputAvi);
                    }
                    catch { }
                }

                progressBar.Value = i + 1;
                Application.DoEvents();
            }

            progressForm.Close();

            if (errors.Count > 0)
            {
                string errorList = string.Join("\n", errors.Select(e => $"  • {e}"));
                MessageBox.Show($"Completed with {errors.Count} error(s):\n\n{errorList}\n\nCheck CameraRecording.log on Desktop for details.",
                                "Conversion Complete", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show($"Successfully saved {successCount} trimmed AVI file(s)!\n\nFiles saved to:\n{outputPath}\n\nNote: These files play in VLC Media Player.",
                                "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        private void ConvertAvisToMp4WithTrimming(List<string> aviFiles, string outputPath, string outputFilename, 
                                  double fps, Dictionary<string, (int startFrame, int endFrame)> frameRanges,
                                  bool deleteOriginals = true, List<string>? customNames = null)
        {
            if (!File.Exists(FFMPEG_PATH))
            {
                MessageBox.Show($"FFmpeg not found at:\n{FFMPEG_PATH}\n\nPlease install FFmpeg.",
                                "FFmpeg Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Create progress form
            Form progressForm = new Form
            {
                Text = "Converting Trimmed Videos to MP4",
                Size = new System.Drawing.Size(500, 220),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ControlBox = false
            };

            Label statusLabel = new Label
            {
                Text = "Preparing conversion...",
                Location = new System.Drawing.Point(20, 20),
                Size = new System.Drawing.Size(460, 20),
                Font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold)
            };
            progressForm.Controls.Add(statusLabel);

            ProgressBar overallProgress = new ProgressBar
            {
                Location = new System.Drawing.Point(20, 50),
                Size = new System.Drawing.Size(460, 30),
                Minimum = 0,
                Maximum = aviFiles.Count * 100,
                Value = 0
            };
            progressForm.Controls.Add(overallProgress);

            Label currentFileLabel = new Label
            {
                Text = "",
                Location = new System.Drawing.Point(20, 90),
                Size = new System.Drawing.Size(460, 20)
            };
            progressForm.Controls.Add(currentFileLabel);

            ProgressBar fileProgress = new ProgressBar
            {
                Location = new System.Drawing.Point(20, 115),
                Size = new System.Drawing.Size(460, 25),
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Style = ProgressBarStyle.Continuous
            };
            progressForm.Controls.Add(fileProgress);

            Label timeLabel = new Label
            {
                Text = "Elapsed: 0s",
                Location = new System.Drawing.Point(20, 150),
                Size = new System.Drawing.Size(460, 20),
                ForeColor = System.Drawing.Color.Gray
            };
            progressForm.Controls.Add(timeLabel);

            progressForm.Show();
            Application.DoEvents();

            int successCount = 0;
            int failCount = 0;
            List<string> convertedFiles = new List<string>();
            List<string> errorMessages = new List<string>();
            DateTime startTime = DateTime.Now;

            for (int i = 0; i < aviFiles.Count; i++)
            {
                string aviFile = aviFiles[i];
                
                if (!File.Exists(aviFile))
                {
                    errorMessages.Add($"{Path.GetFileName(aviFile)}: File not found");
                    overallProgress.Value = (i + 1) * 100;
                    Application.DoEvents();
                    continue;
                }

                string fileName = Path.GetFileName(aviFile);
                string cameraName = (customNames != null && i < customNames.Count) ? customNames[i] : $"Camera{i + 1}";
                // Sanitize the camera name to remove any invalid characters
                cameraName = string.Join("_", cameraName.Split(Path.GetInvalidFileNameChars()));
                string mp4File = Path.Combine(outputPath, $"{outputFilename}_{cameraName}.mp4");

                statusLabel.Text = $"Converting {i + 1} of {aviFiles.Count}";
                currentFileLabel.Text = $"File: {fileName}";
                fileProgress.Value = 0;
                Application.DoEvents();

                DateTime fileStartTime = DateTime.Now;

                try
                {
                    // Get frame range for this file
                    var range = frameRanges[fileName];
                    int startFrame = range.startFrame;
                    int endFrame = range.endFrame;
                    int totalFrames = endFrame - startFrame + 1;
                    
                    // Calculate time offsets
                    double startTime_sec = (startFrame - 1) / fps;
                    double endTime_sec = endFrame / fps;
                    double duration = endTime_sec - startTime_sec;
                    
                    LogCameraInfo($"Converting {fileName}: frames {startFrame}-{endFrame} ({totalFrames} frames, {duration:F2}s)");

                    statusLabel.Text = $"Converting {i + 1} of {aviFiles.Count} - Frames: {startFrame}-{endFrame}";
                    Application.DoEvents();

                    // FFmpeg command with trimming and encoding
                    string fpsFilter = $"setpts=N/(TB*{fps:F6})";
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = FFMPEG_PATH,
                        Arguments = $"-ss {startTime_sec:F6} -i \"{aviFile}\" -t {duration:F6} -vf \"{fpsFilter}\" -c:v libx264 -pix_fmt yuv420p -preset ultrafast -crf 23 -r {fps:F3} -progress pipe:1 -y \"{mp4File}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    bool conversionComplete = false;
                    string lastError = "";

                    using (Process process = Process.Start(startInfo)!)
                    {
                        process.OutputDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                try
                                {
                                    if (e.Data.StartsWith("out_time_ms="))
                                    {
                                        string timeValue = e.Data.Substring(12);
                                        if (long.TryParse(timeValue, out long microseconds) && duration > 0)
                                        {
                                            double currentSeconds = microseconds / 1000000.0;
                                            double progressPercent = Math.Min((currentSeconds / duration) * 100, 100);
                                            
                                            if (fileProgress.InvokeRequired)
                                            {
                                                fileProgress.Invoke(new Action(() =>
                                                {
                                                    fileProgress.Value = (int)progressPercent;
                                                    overallProgress.Value = (i * 100) + (int)progressPercent;
                                                    
                                                    TimeSpan elapsed = DateTime.Now - startTime;
                                                    timeLabel.Text = $"Elapsed: {elapsed.Minutes}m {elapsed.Seconds}s";
                                                }));
                                            }
                                        }
                                    }
                                    else if (e.Data.StartsWith("progress=end"))
                                    {
                                        conversionComplete = true;
                                    }
                                }
                                catch { }
                            }
                        };

                        process.ErrorDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                lastError = e.Data;
                            }
                        };

                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        bool exited = process.WaitForExit(300000); // 5 minute timeout

                        if (!exited)
                        {
                            process.Kill();
                            errorMessages.Add($"{fileName}: Conversion timeout (>5 minutes)");
                            failCount++;
                            
                            if (File.Exists(mp4File))
                                File.Delete(mp4File);
                        }
                        else if (process.ExitCode == 0 && File.Exists(mp4File))
                        {
                            FileInfo mp4Info = new FileInfo(mp4File);
                            if (mp4Info.Length > 1000)
                            {
                                fileProgress.Value = 100;
                                overallProgress.Value = (i + 1) * 100;
                                Application.DoEvents();
                                
                                TimeSpan conversionTime = DateTime.Now - fileStartTime;
                                
                                if (deleteOriginals)
                                {
                                    File.Delete(aviFile);
                                }
                                convertedFiles.Add(mp4File);
                                successCount++;
                                
                                LogCameraInfo($"Converted {fileName} in {conversionTime.TotalSeconds:F1}s");
                            }
                            else
                            {
                                errorMessages.Add($"{fileName}: Output file too small (possibly corrupted)");
                                File.Delete(mp4File);
                                failCount++;
                            }
                        }
                        else
                        {
                            string errorMsg = $"Exit code: {process.ExitCode}";
                            if (!string.IsNullOrEmpty(lastError))
                                errorMsg += $"\n{lastError}";
                            
                            errorMessages.Add($"{fileName}: {errorMsg}");
                            failCount++;
                            
                            if (File.Exists(mp4File))
                                File.Delete(mp4File);
                        }
                    }
                }
                catch (Exception ex)
                {
                    errorMessages.Add($"{fileName}: Exception - {ex.Message}");
                    failCount++;
                }

                Application.DoEvents();
            }

            progressForm.Close();

            // Show results
            TimeSpan totalTime = DateTime.Now - startTime;
            string resultMessage = $"Total time: {totalTime.Minutes}m {totalTime.Seconds}s\n\n";
            
            if (successCount > 0)
            {
                string fileList = string.Join("\n", convertedFiles.Select(f => 
                {
                    int frames = GetVideoFrameCount(f);
                    double duration = GetVideoDuration(f);
                    double calculatedFps = duration > 0 ? frames / duration : 0;
                    double storedFps = GetVideoFrameRate(f);
                    
                    return $"{Path.GetFileName(f)} - {frames} frames, {duration:F2}s\n   Calculated: {calculatedFps:F2} fps | Stored: {storedFps:F2} fps";
                }));
                
                string deleteMsg = deleteOriginals ? "Original AVI files deleted." : "";
                resultMessage += $"Successfully converted {successCount} trimmed video(s) to MP4!\n\nFinal Videos:\n{fileList}\n\n{deleteMsg}";
            }

            if (failCount > 0)
            {
                if (successCount > 0)
                    resultMessage += "\n\n";
                
                resultMessage += $"Failed to convert {failCount} video(s). AVI files kept.\n\nErrors:\n{string.Join("\n\n", errorMessages)}";
            }

            MessageBox.Show(resultMessage, 
                            failCount > 0 ? "Conversion Complete (with errors)" : "Conversion Complete", 
                            MessageBoxButtons.OK, 
                            failCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private void ShowTimelapseCompilationDialog()
        {
            try
            {
                // Check if any cameras have captured frames
                var camerasWithFrames = cameras.Where(c => c.TimelapseImagePaths != null && c.TimelapseImagePaths.Count > 0).ToList();
                
                if (camerasWithFrames.Count == 0)
                {
                    MessageBox.Show("No timelapse frames were captured!",
                                "No Frames", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                
                // Create compilation dialog
                Form compilationDialog = new Form
                {
                    Text = "Compile Timelapse Videos",
                    Size = new System.Drawing.Size(550, 450),  // ← Increased size
                    StartPosition = FormStartPosition.CenterParent,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false,
                    MinimizeBox = false
                };
                
                int y = 20;
                
                // Summary
                Label lblSummary = new Label
                {
                    Text = $"Captured timelapse frames from {camerasWithFrames.Count} camera(s):",
                    Location = new System.Drawing.Point(20, y),
                    Size = new System.Drawing.Size(450, 20),
                    Font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold)
                };
                compilationDialog.Controls.Add(lblSummary);
                y += 30;
                
                // Camera frame counts
                string frameInfo = "";
                foreach (var camera in camerasWithFrames)
                {
                    frameInfo += $"  • {camera.CustomName}: {camera.TimelapseFrameCount} frames\n";
                }
                
                Label lblFrameInfo = new Label
                {
                    Text = frameInfo.TrimEnd(),
                    Location = new System.Drawing.Point(40, y),
                    Size = new System.Drawing.Size(440, camerasWithFrames.Count * 20),
                    Font = new System.Drawing.Font("Arial", 9)
                };
                compilationDialog.Controls.Add(lblFrameInfo);
                y += camerasWithFrames.Count * 20 + 20;
                
                // Output frame rate
                Label lblOutputFps = new Label
                {
                    Text = "Output Video Frame Rate:",
                    Location = new System.Drawing.Point(20, y),
                    Size = new System.Drawing.Size(180, 20),
                    Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold)
                };
                compilationDialog.Controls.Add(lblOutputFps);
                
                NumericUpDown numOutputFps = new NumericUpDown
                {
                    Location = new System.Drawing.Point(210, y - 2),
                    Size = new System.Drawing.Size(80, 25),
                    Minimum = 1,
                    Maximum = 120,
                    Value = 24,
                    DecimalPlaces = 0
                };
                compilationDialog.Controls.Add(numOutputFps);
                
                Label lblFpsUnit = new Label
                {
                    Text = "fps",
                    Location = new System.Drawing.Point(295, y),
                    Size = new System.Drawing.Size(30, 20),
                    Font = new System.Drawing.Font("Arial", 9)
                };
                compilationDialog.Controls.Add(lblFpsUnit);
                y += 40;
                
                // Output format
                Label lblFormat = new Label
                {
                    Text = "Output Format:",
                    Location = new System.Drawing.Point(20, y),
                    Size = new System.Drawing.Size(180, 20),
                    Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold)
                };
                compilationDialog.Controls.Add(lblFormat);
                
                RadioButton rbMP4 = new RadioButton
                {
                    Text = "MP4 (compressed)",
                    Location = new System.Drawing.Point(210, y),
                    Size = new System.Drawing.Size(150, 20),
                    Checked = true
                };
                compilationDialog.Controls.Add(rbMP4);
                
                RadioButton rbAVI = new RadioButton
                {
                    Text = "AVI (uncompressed)",
                    Location = new System.Drawing.Point(210, y + 25),
                    Size = new System.Drawing.Size(150, 20)
                };
                compilationDialog.Controls.Add(rbAVI);
                y += 60;
                
                // Keep source images
                CheckBox chkKeepImages = new CheckBox
                {
                    Text = "Keep source images (don't delete after compilation)",
                    Location = new System.Drawing.Point(20, y),
                    Size = new System.Drawing.Size(400, 20),
                    Font = new System.Drawing.Font("Arial", 9),
                    Checked = false
                };
                compilationDialog.Controls.Add(chkKeepImages);
                y += 40;
                
                // Output location info
                Label lblOutputInfo = new Label
                {
                    Text = $"Videos will be saved to:\n{txtWorkingFolder.Text}",
                    Location = new System.Drawing.Point(20, y),
                    Size = new System.Drawing.Size(450, 40),
                    Font = new System.Drawing.Font("Arial", 8),
                    ForeColor = System.Drawing.Color.Gray
                };
                compilationDialog.Controls.Add(lblOutputInfo);
                y += 50;
                
                // Buttons
                Button btnCancel = new Button
                {
                    Text = "Cancel (Delete Images)",
                    Location = new System.Drawing.Point(220, y),
                    Size = new System.Drawing.Size(150, 35),
                    DialogResult = DialogResult.Cancel
                };
                compilationDialog.Controls.Add(btnCancel);
                
                Button btnCompile = new Button
                {
                    Text = "Compile Videos",
                    Location = new System.Drawing.Point(380, y),
                    Size = new System.Drawing.Size(100, 35),
                    DialogResult = DialogResult.OK,
                    Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold)
                };
                compilationDialog.Controls.Add(btnCompile);
                
                compilationDialog.AcceptButton = btnCompile;
                compilationDialog.CancelButton = btnCancel;
                
                if (compilationDialog.ShowDialog() == DialogResult.OK)
                {
                    double outputFps = (double)numOutputFps.Value;
                    bool outputMP4 = rbMP4.Checked;
                    bool keepImages = chkKeepImages.Checked;
                    
                    // Compile timelapse videos
                    CompileTimelapseVideos(camerasWithFrames, outputFps, outputMP4, keepImages);
                }
                else
                {
                    // User cancelled - delete all timelapse images
                    DeleteTimelapseImages(camerasWithFrames);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error in timelapse compilation dialog: {ex.Message}",
                            "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                LogCameraInfo($"Timelapse compilation dialog ERROR: {ex.Message}");
            }
        }

        private void CompileTimelapseVideos(List<CameraControl> camerasWithFrames, double outputFps, bool outputMP4, bool keepImages)
        {
            if (!File.Exists(FFMPEG_PATH))
            {
                MessageBox.Show($"FFmpeg not found at:\n{FFMPEG_PATH}\n\nCannot compile timelapse videos.\nSource images will be kept.",
                            "FFmpeg Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            // Create progress form
            Form progressForm = new Form
            {
                Text = "Compiling Timelapse Videos",
                Size = new System.Drawing.Size(500, 200),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ControlBox = false
            };
            
            Label statusLabel = new Label
            {
                Text = "Preparing...",
                Location = new System.Drawing.Point(20, 20),
                Size = new System.Drawing.Size(460, 20),
                Font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold)
            };
            progressForm.Controls.Add(statusLabel);
            
            ProgressBar progressBar = new ProgressBar
            {
                Location = new System.Drawing.Point(20, 50),
                Size = new System.Drawing.Size(460, 30),
                Minimum = 0,
                Maximum = camerasWithFrames.Count,
                Value = 0
            };
            progressForm.Controls.Add(progressBar);
            
            Label detailLabel = new Label
            {
                Text = "",
                Location = new System.Drawing.Point(20, 90),
                Size = new System.Drawing.Size(460, 40),
                Font = new System.Drawing.Font("Arial", 9)
            };
            progressForm.Controls.Add(detailLabel);
            
            progressForm.Show();
            Application.DoEvents();
            
            int successCount = 0;
            List<string> compiledVideos = new List<string>();
            List<string> errors = new List<string>();
            
            for (int i = 0; i < camerasWithFrames.Count; i++)
            {
                var camera = camerasWithFrames[i];
                
                statusLabel.Text = $"Compiling {i + 1} of {camerasWithFrames.Count}: {camera.CustomName}";
                detailLabel.Text = $"Processing {camera.TimelapseFrameCount} frames...";
                Application.DoEvents();
                
                try
                {
                    string cameraName = string.Join("_", camera.CustomName.Split(Path.GetInvalidFileNameChars()));
                    string timelapseFolder = Path.Combine(txtWorkingFolder.Text, $"Timelapse_{currentRecordingBaseName}_{cameraName}");
                    
                    // Create file list for FFmpeg
                    string fileListPath = Path.Combine(timelapseFolder, "filelist.txt");
                    using (StreamWriter writer = new StreamWriter(fileListPath))
                    {
                        foreach (string imagePath in camera.TimelapseImagePaths!)
                        {
                            // FFmpeg concat requires proper escaping
                            string escapedPath = imagePath.Replace("\\", "/").Replace("'", "'\\''");
                            writer.WriteLine($"file '{escapedPath}'");
                        }
                    }
                    
                    // Output video path
                    string extension = outputMP4 ? ".mp4" : ".avi";
                    string outputVideo = Path.Combine(txtWorkingFolder.Text, $"{currentRecordingBaseName}_{cameraName}_timelapse{extension}");
                    
                    // FFmpeg command
                    string codec = outputMP4 
                        ? "-c:v libx264 -pix_fmt yuv420p -preset medium -crf 23" 
                        : "-c:v rawvideo -pix_fmt bgr24";
                    
                    string ffmpegArgs = $"-f concat -safe 0 -r {outputFps:F3} -i \"{fileListPath}\" {codec} -y \"{outputVideo}\"";
                    
                    LogCameraInfo($"Compiling timelapse for {camera.CustomName}: {camera.TimelapseFrameCount} frames at {outputFps} fps");
                    LogCameraInfo($"FFmpeg command: ffmpeg {ffmpegArgs}");
                    
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = FFMPEG_PATH,
                        Arguments = ffmpegArgs,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true
                    };
                    
                    using (Process process = Process.Start(startInfo)!)
                    {
                        process.ErrorDataReceived += (s, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                LogCameraInfo($"FFmpeg: {e.Data}");
                            }
                        };
                        process.BeginErrorReadLine();
                        
                        bool exited = process.WaitForExit(300000); // 5 minute timeout
                        
                        if (!exited)
                        {
                            process.Kill();
                            errors.Add($"{camera.CustomName}: Compilation timeout");
                            LogCameraInfo($"ERROR: Timeout compiling {camera.CustomName}");
                        }
                        else if (process.ExitCode == 0 && File.Exists(outputVideo))
                        {
                            FileInfo videoInfo = new FileInfo(outputVideo);
                            if (videoInfo.Length > 1000)
                            {
                                compiledVideos.Add(outputVideo);
                                successCount++;
                                LogCameraInfo($"Successfully compiled {camera.CustomName} timelapse: {videoInfo.Length / (1024 * 1024)} MB");
                                
                                // Delete images if requested
                                if (!keepImages)
                                {
                                    try
                                    {
                                        Directory.Delete(timelapseFolder, true);
                                        LogCameraInfo($"Deleted source images for {camera.CustomName}");
                                    }
                                    catch (Exception ex)
                                    {
                                        LogCameraInfo($"Warning: Could not delete images for {camera.CustomName}: {ex.Message}");
                                    }
                                }
                            }
                            else
                            {
                                errors.Add($"{camera.CustomName}: Output file too small");
                                LogCameraInfo($"ERROR: Output file too small for {camera.CustomName}");
                            }
                        }
                        else
                        {
                            errors.Add($"{camera.CustomName}: FFmpeg error (exit code {process.ExitCode})");
                            LogCameraInfo($"ERROR: FFmpeg failed for {camera.CustomName}: exit code {process.ExitCode}");
                        }
                    }
                    
                    // Clean up file list
                    try { File.Delete(fileListPath); } catch { }
                }
                catch (Exception ex)
                {
                    errors.Add($"{camera.CustomName}: {ex.Message}");
                    LogCameraInfo($"EXCEPTION compiling {camera.CustomName}: {ex.Message}");
                }
                
                progressBar.Value = i + 1;
                Application.DoEvents();
            }
            
            progressForm.Close();
            
            // Show results
            string resultMessage = "";
            if (successCount > 0)
            {
                string videoList = string.Join("\n", compiledVideos.Select(v => $"  • {Path.GetFileName(v)}"));
                resultMessage = $"✅ Successfully compiled {successCount} timelapse video(s)!\n\nVideos:\n{videoList}";
                
                if (!keepImages)
                {
                    resultMessage += "\n\nSource images deleted.";
                }
                else
                {
                    resultMessage += "\n\nSource images kept in Timelapse_* folders.";
                }
            }
            
            if (errors.Count > 0)
            {
                if (successCount > 0) resultMessage += "\n\n";
                resultMessage += $"⚠️ Errors ({errors.Count}):\n{string.Join("\n", errors)}";
            }
            
            MessageBox.Show(resultMessage,
                        errors.Count > 0 ? "Compilation Complete (with errors)" : "Compilation Complete",
                        MessageBoxButtons.OK,
                        errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private void DeleteTimelapseImages(List<CameraControl> camerasWithFrames)
        {
            try
            {
                LogCameraInfo("User cancelled timelapse compilation - deleting source images");
                
                foreach (var camera in camerasWithFrames)
                {
                    try
                    {
                        string timelapseMainFolder = Path.Combine(txtWorkingFolder.Text, $"Timelapse_Frames_{currentRecordingBaseName}");

                        if (Directory.Exists(timelapseMainFolder))
                        {
                            Directory.Delete(timelapseMainFolder, true);
                            LogCameraInfo($"Deleted entire timelapse folder: {timelapseMainFolder}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogCameraInfo($"Error deleting images for {camera.CustomName}: {ex.Message}");
                    }
                }
                
                MessageBox.Show("Timelapse cancelled. Source images deleted.",
                            "Cancelled", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LogCameraInfo($"Error in DeleteTimelapseImages: {ex.Message}");
            }
        }

        private void ConvertAvisMenuItem_Click(object? sender, EventArgs e)
        {
            try
            {
                // File selection dialog
                OpenFileDialog openDialog = new OpenFileDialog
                {
                    Title = "Select AVI Files to Convert",
                    Filter = "AVI Files (*.avi)|*.avi|All Files (*.*)|*.*",
                    Multiselect = true
                };
                
                if (openDialog.ShowDialog() != DialogResult.OK || openDialog.FileNames.Length == 0)
                    return;
                
                List<string> selectedFiles = openDialog.FileNames.ToList();
                
                // Check for large files
                foreach (string file in selectedFiles)
                {
                    FileInfo fi = new FileInfo(file);
                    if (fi.Length > 1024L * 1024 * 1024) // 1GB
                    {
                        var result = MessageBox.Show(
                            $"Warning: {Path.GetFileName(file)} is very large ({fi.Length / (1024 * 1024)} MB).\n\n" +
                            "Conversion may take several minutes.\n\nContinue?",
                            "Large File Detected",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);
                        
                        if (result == DialogResult.No)
                            return;
                        
                        break; // Only show warning once
                    }
                }
                
                // Show simple conversion dialog
                Form conversionDialog = new Form
                {
                    Text = "Convert AVIs to MP4",
                    Size = new System.Drawing.Size(450, 220),
                    StartPosition = FormStartPosition.CenterParent,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false,
                    MinimizeBox = false
                };
                
                int y = 20;
                
                Label lblFiles = new Label
                {
                    Text = $"Selected {selectedFiles.Count} file(s)",
                    Location = new System.Drawing.Point(20, y),
                    Size = new System.Drawing.Size(400, 20),
                    Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold)
                };
                conversionDialog.Controls.Add(lblFiles);
                y += 30;
                
                Label lblFps = new Label
                {
                    Text = "Frame Rate (fps):",
                    Location = new System.Drawing.Point(20, y),
                    Size = new System.Drawing.Size(120, 20)
                };
                conversionDialog.Controls.Add(lblFps);
                
                TextBox txtFps = new TextBox
                {
                    Text = "30.0",
                    Location = new System.Drawing.Point(150, y - 2),
                    Size = new System.Drawing.Size(100, 25)
                };
                conversionDialog.Controls.Add(txtFps);
                y += 40;
                
                Label lblInfo = new Label
                {
                    Text = "Output: Same folder as input files",
                    Location = new System.Drawing.Point(20, y),
                    Size = new System.Drawing.Size(400, 20),
                    ForeColor = System.Drawing.Color.Gray
                };
                conversionDialog.Controls.Add(lblInfo);
                y += 40;
                
                Button btnCancel = new Button
                {
                    Text = "Cancel",
                    Location = new System.Drawing.Point(220, y),
                    Size = new System.Drawing.Size(90, 35),
                    DialogResult = DialogResult.Cancel
                };
                conversionDialog.Controls.Add(btnCancel);
                
                Button btnConvert = new Button
                {
                    Text = "Convert",
                    Location = new System.Drawing.Point(320, y),
                    Size = new System.Drawing.Size(90, 35),
                    DialogResult = DialogResult.OK
                };
                conversionDialog.Controls.Add(btnConvert);
                
                conversionDialog.AcceptButton = btnConvert;
                conversionDialog.CancelButton = btnCancel;
                
                if (conversionDialog.ShowDialog() == DialogResult.OK)
                {
                    if (!double.TryParse(txtFps.Text, out double fps) || fps < 0.1 || fps > 240)
                    {
                        MessageBox.Show("Please enter a valid frame rate between 0.1 and 240 fps.",
                                        "Invalid FPS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    
                    // Convert files
                    string outputPath = Path.GetDirectoryName(selectedFiles[0]) ?? "";
                    string dummyFilename = "converted"; // Not used for standalone conversion
                    
                    ConvertAvisToMp4(selectedFiles, outputPath, dummyFilename, fps, deleteOriginals: false, standaloneMode: true);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error in conversion tool: {ex.Message}",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void TrimVideosMenuItem_Click(object? sender, EventArgs e)
        {
            try
            {
                // File selection dialog
                OpenFileDialog openDialog = new OpenFileDialog
                {
                    Title = "Select Video Files to Trim",
                    Filter = "Video Files (*.avi;*.mp4)|*.avi;*.mp4|AVI Files (*.avi)|*.avi|MP4 Files (*.mp4)|*.mp4|All Files (*.*)|*.*",
                    Multiselect = true
                };
                
                if (openDialog.ShowDialog() != DialogResult.OK || openDialog.FileNames.Length == 0)
                    return;
                
                List<string> selectedFiles = openDialog.FileNames.ToList();
                
                // Validate all files exist and are readable
                foreach (string file in selectedFiles)
                {
                    if (!File.Exists(file))
                    {
                        MessageBox.Show($"File not found: {Path.GetFileName(file)}",
                                        "File Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
                
                // Build frame statistics for each video
                List<(string filename, int frames, double duration)> frameStats = new List<(string, int, double)>();
                List<long> fileSizes = new List<long>();
                List<string> filePaths = new List<string>();
                
                foreach (string file in selectedFiles)
                {
                    int frameCount = GetVideoFrameCount(file);
                    double duration = GetVideoDuration(file);
                    
                    if (frameCount == 0)
                    {
                        MessageBox.Show($"Could not read frame count from: {Path.GetFileName(file)}\n\nThis file may be corrupted or in an unsupported format.",
                                        "Video Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    
                    frameStats.Add((Path.GetFileName(file), frameCount, duration));
                    fileSizes.Add(new FileInfo(file).Length);
                    filePaths.Add(file);
                }
                
                // Initialize frame ranges (all frames by default)
                Dictionary<string, (int startFrame, int endFrame)> frameRanges = new Dictionary<string, (int, int)>();
                foreach (var stat in frameStats)
                {
                    frameRanges[stat.filename] = (1, stat.frames);
                }
                
                // Open trim dialog
                using (VideoTrimDialog trimDialog = new VideoTrimDialog(frameStats, filePaths, frameRanges))
                {
                    if (trimDialog.ShowDialog() != DialogResult.OK)
                        return;
                    
                    // Get the updated frame ranges
                    frameRanges = trimDialog.FrameRanges;
                    
                    // Ask user for output location
                    using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
                    {
                        folderDialog.Description = "Select output folder for trimmed videos";
                        folderDialog.SelectedPath = Path.GetDirectoryName(selectedFiles[0]) ?? "";
                        
                        if (folderDialog.ShowDialog() != DialogResult.OK)
                            return;
                        
                        string outputPath = folderDialog.SelectedPath;
                        
                        // Ask for output format and naming
                        Form formatDialog = new Form
                        {
                            Text = "Output Options",
                            Size = new System.Drawing.Size(400, 280),
                            StartPosition = FormStartPosition.CenterParent,
                            FormBorderStyle = FormBorderStyle.FixedDialog,
                            MaximizeBox = false,
                            MinimizeBox = false
                        };

                        int dialogY = 20;

                        Label lblFormat = new Label
                        {
                            Text = "Output format:",
                            Location = new System.Drawing.Point(20, dialogY),
                            Size = new System.Drawing.Size(350, 20),
                            Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold)
                        };
                        formatDialog.Controls.Add(lblFormat);
                        dialogY += 25;

                        RadioButton rbMP4 = new RadioButton
                        {
                            Text = "MP4 (compressed)",
                            Location = new System.Drawing.Point(40, dialogY),
                            Size = new System.Drawing.Size(200, 20),
                            Checked = true
                        };
                        formatDialog.Controls.Add(rbMP4);
                        dialogY += 25;

                        RadioButton rbAVI = new RadioButton
                        {
                            Text = "AVI (original format)",
                            Location = new System.Drawing.Point(40, dialogY),
                            Size = new System.Drawing.Size(200, 20)
                        };
                        formatDialog.Controls.Add(rbAVI);
                        dialogY += 35;

                        // Naming options
                        Label lblNaming = new Label
                        {
                            Text = "Output filename:",
                            Location = new System.Drawing.Point(20, dialogY),
                            Size = new System.Drawing.Size(350, 20),
                            Font = new System.Drawing.Font("Arial", 9, System.Drawing.FontStyle.Bold)
                        };
                        formatDialog.Controls.Add(lblNaming);
                        dialogY += 25;

                        Label lblPrefix = new Label
                        {
                            Text = "Prefix:",
                            Location = new System.Drawing.Point(40, dialogY + 2),
                            Size = new System.Drawing.Size(50, 20)
                        };
                        formatDialog.Controls.Add(lblPrefix);

                        TextBox txtPrefix = new TextBox
                        {
                            Text = "trimmed_",
                            Location = new System.Drawing.Point(95, dialogY),
                            Size = new System.Drawing.Size(100, 25)
                        };
                        formatDialog.Controls.Add(txtPrefix);

                        Label lblOriginal = new Label
                        {
                            Text = "[original name]",
                            Location = new System.Drawing.Point(200, dialogY + 2),
                            Size = new System.Drawing.Size(100, 20),
                            ForeColor = System.Drawing.Color.Gray
                        };
                        formatDialog.Controls.Add(lblOriginal);
                        dialogY += 30;

                        Label lblExample = new Label
                        {
                            Text = "Example: trimmed_video.mp4",
                            Location = new System.Drawing.Point(40, dialogY),
                            Size = new System.Drawing.Size(320, 20),
                            ForeColor = System.Drawing.Color.Blue,
                            Font = new System.Drawing.Font("Arial", 8)
                        };
                        formatDialog.Controls.Add(lblExample);
                        dialogY += 35;

                        Button btnCancel = new Button
                        {
                            Text = "Cancel",
                            Location = new System.Drawing.Point(180, dialogY),
                            Size = new System.Drawing.Size(90, 30),
                            DialogResult = DialogResult.Cancel
                        };
                        formatDialog.Controls.Add(btnCancel);

                        Button btnOK = new Button
                        {
                            Text = "Trim",
                            Location = new System.Drawing.Point(280, dialogY),
                            Size = new System.Drawing.Size(90, 30),
                            DialogResult = DialogResult.OK
                        };
                        formatDialog.Controls.Add(btnOK);

                        formatDialog.AcceptButton = btnOK;
                        formatDialog.CancelButton = btnCancel;

                        if (formatDialog.ShowDialog() != DialogResult.OK)
                            return;

                        bool outputMP4 = rbMP4.Checked;
                        string prefix = txtPrefix.Text;

                        // Perform trimming
                        TrimVideos(filePaths, frameStats, frameRanges, outputPath, outputMP4, prefix);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error in trim tool: {ex.Message}",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void TrimVideos(List<string> inputFiles, 
                            List<(string filename, int frames, double duration)> frameStats,
                            Dictionary<string, (int startFrame, int endFrame)> frameRanges,
                            string outputPath,
                            bool outputMP4,
                            string prefix = "trimmed_")
        {
            if (!File.Exists(FFMPEG_PATH))
            {
                MessageBox.Show($"FFmpeg not found at:\n{FFMPEG_PATH}\n\nPlease install FFmpeg to trim videos.",
                                "FFmpeg Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            // Create progress form
            Form progressForm = new Form
            {
                Text = "Trimming Videos",
                Size = new System.Drawing.Size(500, 200),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ControlBox = false
            };
            
            Label statusLabel = new Label
            {
                Text = "Preparing...",
                Location = new System.Drawing.Point(20, 20),
                Size = new System.Drawing.Size(460, 20),
                Font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold)
            };
            progressForm.Controls.Add(statusLabel);
            
            ProgressBar progressBar = new ProgressBar
            {
                Location = new System.Drawing.Point(20, 50),
                Size = new System.Drawing.Size(460, 30),
                Minimum = 0,
                Maximum = inputFiles.Count,
                Value = 0
            };
            progressForm.Controls.Add(progressBar);
            
            Label currentFileLabel = new Label
            {
                Text = "",
                Location = new System.Drawing.Point(20, 90),
                Size = new System.Drawing.Size(460, 20)
            };
            progressForm.Controls.Add(currentFileLabel);
            
            Label timeLabel = new Label
            {
                Text = "Elapsed: 0s",
                Location = new System.Drawing.Point(20, 120),
                Size = new System.Drawing.Size(460, 20),
                ForeColor = System.Drawing.Color.Gray
            };
            progressForm.Controls.Add(timeLabel);
            
            progressForm.Show();
            Application.DoEvents();
            
            int successCount = 0;
            List<string> trimmedFiles = new List<string>();
            List<string> errorMessages = new List<string>();
            DateTime startTime = DateTime.Now;
            
            for (int i = 0; i < inputFiles.Count; i++)
            {
                string inputFile = inputFiles[i];
                string filename = frameStats[i].filename;
                var range = frameRanges[filename];
                
                statusLabel.Text = $"Trimming {i + 1} of {inputFiles.Count}";
                currentFileLabel.Text = $"File: {filename} (frames {range.startFrame}-{range.endFrame})";
                Application.DoEvents();
                
                DateTime fileStartTime = DateTime.Now;
                
                try
                {
                    // Calculate time stamps from frame numbers
                    double fps = GetVideoFrameRate(inputFile);
                    if (fps <= 0)
                    {
                        fps = frameStats[i].duration > 0 ? frameStats[i].frames / frameStats[i].duration : 30.0;
                    }
                    
                    double startTime_sec = (range.startFrame - 1) / fps;
                    double endTime_sec = range.endFrame / fps;
                    double duration_sec = endTime_sec - startTime_sec;
                    
                    // Build output filename with prefix
                    string baseName = Path.GetFileNameWithoutExtension(filename);
                    string extension = outputMP4 ? ".mp4" : Path.GetExtension(inputFile);
                    string outputFile = Path.Combine(outputPath, $"{prefix}{baseName}{extension}");
                    
                    // Avoid overwriting
                    int counter = 1;
                    while (File.Exists(outputFile))
                    {
                        outputFile = Path.Combine(outputPath, $"{baseName}_trimmed_{counter}{extension}");
                        counter++;
                    }
                    
                    // Build FFmpeg command
                    string codec = outputMP4 ? "-c:v libx264 -pix_fmt yuv420p -preset fast -crf 23" : "-c:v copy";
                    
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = FFMPEG_PATH,
                        Arguments = $"-ss {startTime_sec:F6} -i \"{inputFile}\" -t {duration_sec:F6} {codec} -y \"{outputFile}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (Process process = Process.Start(startInfo)!)
                    {
                        // Read stderr asynchronously to prevent blocking
                        process.BeginErrorReadLine();
                        process.BeginOutputReadLine();
                        
                        process.WaitForExit(120000); // 2 minute timeout
                        
                        if (process.ExitCode == 0 && File.Exists(outputFile))
                        {
                            FileInfo outInfo = new FileInfo(outputFile);
                            if (outInfo.Length > 1000)
                            {
                                trimmedFiles.Add(outputFile);
                                successCount++;
                            }
                            else
                            {
                                errorMessages.Add($"{filename}: Output file too small");
                                if (File.Exists(outputFile))
                                    File.Delete(outputFile);
                            }
                        }
                        else
                        {
                            errorMessages.Add($"{filename}: FFmpeg error (exit code {process.ExitCode})");
                        }
                    }
                }
                catch (Exception ex)
                {
                    errorMessages.Add($"{filename}: {ex.Message}");
                }
                
                progressBar.Value = i + 1;
                TimeSpan elapsed = DateTime.Now - startTime;
                timeLabel.Text = $"Elapsed: {elapsed.Minutes}m {elapsed.Seconds}s";
                Application.DoEvents();
            }
            
            progressForm.Close();
            
            // Show results
            string resultMessage = "";
            if (successCount > 0)
            {
                string fileList = string.Join("\n", trimmedFiles.Select(f => $"  • {Path.GetFileName(f)}"));
                resultMessage = $"Successfully trimmed {successCount} video(s)!\n\nOutput files:\n{fileList}";
            }
            
            if (errorMessages.Count > 0)
            {
                if (successCount > 0)
                    resultMessage += "\n\n";
                resultMessage += $"Failed to trim {errorMessages.Count} video(s):\n{string.Join("\n", errorMessages)}";
            }
            
            MessageBox.Show(resultMessage,
                            errorMessages.Count > 0 ? "Trimming Complete (with errors)" : "Trimming Complete",
                            MessageBoxButtons.OK,
                            errorMessages.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private void ConvertAvisToAvi(List<string> aviFiles, string outputPath, string outputFilename, double fps)
        {
            if (!File.Exists(FFMPEG_PATH))
            {
                MessageBox.Show($"FFmpeg not found at:\n{FFMPEG_PATH}\n\nPlease install FFmpeg.",
                                "FFmpeg Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Create progress form (simplified version)
            Form progressForm = new Form
            {
                Text = "Re-encoding AVIs",
                Size = new System.Drawing.Size(400, 150),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ControlBox = false
            };

            Label statusLabel = new Label
            {
                Text = "Processing...",
                Location = new System.Drawing.Point(20, 20),
                Size = new System.Drawing.Size(360, 20),
                Font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold)
            };
            progressForm.Controls.Add(statusLabel);

            ProgressBar progressBar = new ProgressBar
            {
                Location = new System.Drawing.Point(20, 50),
                Size = new System.Drawing.Size(360, 30),
                Minimum = 0,
                Maximum = aviFiles.Count,
                Value = 0
            };
            progressForm.Controls.Add(progressBar);

            progressForm.Show();
            Application.DoEvents();

            int successCount = 0;
            List<string> errors = new List<string>();
            
            for (int i = 0; i < aviFiles.Count; i++)
            {
                string aviFile = aviFiles[i];
                if (!File.Exists(aviFile))
                    continue;

                string outputAvi = Path.Combine(outputPath, $"{outputFilename}_Camera{i + 1}.avi");
                
                statusLabel.Text = $"Processing {i + 1} of {aviFiles.Count}...";
                Application.DoEvents();

                try
                {
                    // Re-encode with correct FPS metadata using stream copy (fast)
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = FFMPEG_PATH,
                        Arguments = $"-r {fps:F3} -i \"{aviFile}\" -c:v copy -y \"{outputAvi}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true
                    };

                    using (Process process = Process.Start(startInfo)!)
                    {
                        process.WaitForExit(60000); // 1 minute timeout per file

                        if (process.ExitCode == 0 && File.Exists(outputAvi))
                        {
                            // Delete original timestamp-based AVI
                            File.Delete(aviFile);
                            successCount++;
                        }
                        else
                        {
                            errors.Add($"Camera {i + 1}: Conversion failed");
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Camera {i + 1}: {ex.Message}");
                }

                progressBar.Value = i + 1;
                Application.DoEvents();
            }

            progressForm.Close();

            if (errors.Count > 0)
            {
                MessageBox.Show($"Completed with {errors.Count} error(s):\n\n{string.Join("\n", errors)}",
                                "Conversion Complete", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show($"Successfully saved {successCount} AVI file(s)!",
                                "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        private void ConvertAvisToMp4(List<string> aviFiles, string? outputPath = null, string? outputFilename = null, 
                                      double? forcedFps = null, bool deleteOriginals = true, bool standaloneMode = false)
        {
            // If parameters not provided, use defaults for backward compatibility
            if (outputPath == null) outputPath = Path.GetDirectoryName(aviFiles[0]) ?? "";
            if (outputFilename == null) outputFilename = "converted";
            
            if (!File.Exists(FFMPEG_PATH))
            {
                MessageBox.Show($"FFmpeg not found at:\n{FFMPEG_PATH}\n\nPlease install FFmpeg to convert videos to MP4.\n\nAVI files have been saved.",
                                "FFmpeg Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Create a progress form
            Form progressForm = new Form
            {
                Text = "Converting to MP4",
                Size = new System.Drawing.Size(500, 220),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ControlBox = false
            };

            Label statusLabel = new Label
            {
                Text = "Preparing conversion...",
                Location = new System.Drawing.Point(20, 20),
                Size = new System.Drawing.Size(460, 20),
                Font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold)
            };
            progressForm.Controls.Add(statusLabel);

            ProgressBar overallProgress = new ProgressBar
            {
                Location = new System.Drawing.Point(20, 50),
                Size = new System.Drawing.Size(460, 30),
                Minimum = 0,
                Maximum = aviFiles.Count * 100,
                Value = 0
            };
            progressForm.Controls.Add(overallProgress);

            Label currentFileLabel = new Label
            {
                Text = "",
                Location = new System.Drawing.Point(20, 90),
                Size = new System.Drawing.Size(460, 20)
            };
            progressForm.Controls.Add(currentFileLabel);

            ProgressBar fileProgress = new ProgressBar
            {
                Location = new System.Drawing.Point(20, 115),
                Size = new System.Drawing.Size(460, 25),
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Style = ProgressBarStyle.Continuous
            };
            progressForm.Controls.Add(fileProgress);

            Label timeLabel = new Label
            {
                Text = "Elapsed: 0s",
                Location = new System.Drawing.Point(20, 150),
                Size = new System.Drawing.Size(460, 20),
                ForeColor = System.Drawing.Color.Gray
            };
            progressForm.Controls.Add(timeLabel);

            progressForm.Show();
            Application.DoEvents();

            int successCount = 0;
            int failCount = 0;
            List<string> convertedFiles = new List<string>();
            List<string> errorMessages = new List<string>();
            DateTime startTime = DateTime.Now;

            for (int i = 0; i < aviFiles.Count; i++)
            {
                string aviFile = aviFiles[i];
                
                if (!File.Exists(aviFile))
                {
                    errorMessages.Add($"{Path.GetFileName(aviFile)}: File not found");
                    overallProgress.Value = (i + 1) * 100;
                    Application.DoEvents();
                    continue;
                }

                string fileName = Path.GetFileName(aviFile);  // Declare once at the top
                string mp4File;
                if (standaloneMode)
                {
                    // Standalone conversion: replace .avi with .mp4 in same location
                    mp4File = Path.ChangeExtension(aviFile, ".mp4");
                }
                else
                {
                    // Recording conversion: use output path and filename
                    string outputFileName = $"{outputFilename}_Camera{i + 1}.mp4";  // ← Use different variable name
                    mp4File = Path.Combine(outputPath, outputFileName);
                }

                statusLabel.Text = $"Converting {i + 1} of {aviFiles.Count}";
                currentFileLabel.Text = $"File: {fileName}";
                fileProgress.Value = 0;
                Application.DoEvents();

                DateTime fileStartTime = DateTime.Now;

                try
                {
                    // Get video info from AVI
                    int aviFrameCount = GetVideoFrameCount(aviFile);
                    double aviDuration = GetVideoDuration(aviFile);
                    double aviStoredFrameRate = GetVideoFrameRate(aviFile); // What the AVI claims
                    FileInfo aviInfo = new FileInfo(aviFile);
                    long aviSize = aviInfo.Length;

                    // Use forced FPS if provided, otherwise calculate
                    double actualFps;
                    if (forcedFps.HasValue)
                    {
                        actualFps = forcedFps.Value;
                    }
                    else if (i < cameras.Count && cameras[i].RecordingStartTime.HasValue && cameras[i].RecordingStopTime.HasValue)
                    {
                        // Calculate from actual recording time
                        double recordingDuration = (cameras[i].RecordingStopTime!.Value - cameras[i].RecordingStartTime!.Value).TotalSeconds;
                        actualFps = recordingDuration > 0 ? aviFrameCount / recordingDuration : 11.5;
                    }
                    else if (aviDuration > 0)
                    {
                        // Fallback: use AVI duration
                        actualFps = aviFrameCount / aviDuration;
                    }
                    else
                    {
                        actualFps = 30.0; // Final fallback
                    }
                    
                    LogCameraInfo($"Converting {fileName}: {aviFrameCount} frames");
                    LogCameraInfo($"  AVI says: {aviDuration:F2}s @ {(aviDuration > 0 ? aviFrameCount/aviDuration : 0):F2}fps (stored rate: {aviStoredFrameRate:F2}fps)");
                    LogCameraInfo($"  Forcing actual: {actualFps:F2} fps");

                    statusLabel.Text = $"Converting {i + 1} of {aviFiles.Count} - Size: {aviSize / (1024 * 1024)}MB, FPS: {actualFps:F2}";
                    Application.DoEvents();

                    // FFmpeg command - use setpts filter to completely rebuild timestamps from scratch
                    // This ignores all input timing and creates perfect constant frame rate output
                    string fpsFilter = $"setpts=N/(TB*{actualFps:F6})";
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = FFMPEG_PATH,
                        Arguments = $"-i \"{aviFile}\" -vf \"{fpsFilter}\" -c:v libx264 -pix_fmt yuv420p -preset ultrafast -crf 23 -r {actualFps:F3} -progress pipe:1 -y \"{mp4File}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    bool conversionComplete = false;
                    string lastError = "";

                    using (Process process = Process.Start(startInfo)!)
                    {
                        // Read output for progress
                        process.OutputDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                try
                                {
                                    if (e.Data.StartsWith("out_time_ms="))
                                    {
                                        string timeValue = e.Data.Substring(12);
                                        if (long.TryParse(timeValue, out long microseconds) && aviDuration > 0)
                                        {
                                            double currentSeconds = microseconds / 1000000.0;
                                            double progressPercent = Math.Min((currentSeconds / aviDuration) * 100, 100);
                                            
                                            if (fileProgress.InvokeRequired)
                                            {
                                                fileProgress.Invoke(new Action(() =>
                                                {
                                                    fileProgress.Value = (int)progressPercent;
                                                    overallProgress.Value = (i * 100) + (int)progressPercent;
                                                    
                                                    TimeSpan elapsed = DateTime.Now - startTime;
                                                    timeLabel.Text = $"Elapsed: {elapsed.Minutes}m {elapsed.Seconds}s";
                                                }));
                                            }
                                        }
                                    }
                                    else if (e.Data.StartsWith("progress=end"))
                                    {
                                        conversionComplete = true;
                                    }
                                }
                                catch { }
                            }
                        };

                        process.ErrorDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                lastError = e.Data;
                            }
                        };

                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        bool exited = process.WaitForExit(300000); // 5 minute timeout

                        if (!exited)
                        {
                            process.Kill();
                            errorMessages.Add($"{fileName}: Conversion timeout (>5 minutes)");
                            failCount++;
                            
                            if (File.Exists(mp4File))
                                File.Delete(mp4File);
                        }
                        else if (process.ExitCode == 0 && File.Exists(mp4File))
                        {
                            FileInfo mp4Info = new FileInfo(mp4File);
                            if (mp4Info.Length > 1000)
                            {
                                fileProgress.Value = 100;
                                overallProgress.Value = (i + 1) * 100;
                                Application.DoEvents();
                                
                                TimeSpan conversionTime = DateTime.Now - fileStartTime;
                                
                                if (deleteOriginals)
                                {
                                    File.Delete(aviFile);
                                }
                                convertedFiles.Add(mp4File);
                                successCount++;
                                
                                LogCameraInfo($"Converted {fileName} in {conversionTime.TotalSeconds:F1}s");
                            }
                            else
                            {
                                errorMessages.Add($"{fileName}: Output file too small (possibly corrupted)");
                                File.Delete(mp4File);
                                failCount++;
                            }
                        }
                        else
                        {
                            string errorMsg = $"Exit code: {process.ExitCode}";
                            if (!string.IsNullOrEmpty(lastError))
                                errorMsg += $"\n{lastError}";
                            
                            errorMessages.Add($"{fileName}: {errorMsg}");
                            failCount++;
                            
                            if (File.Exists(mp4File))
                                File.Delete(mp4File);
                        }
                    }
                }
                catch (Exception ex)
                {
                    errorMessages.Add($"{fileName}: Exception - {ex.Message}");
                    failCount++;
                }

                Application.DoEvents();
            }

            progressForm.Close();

            // Show results with both calculated and stored frame rates
            TimeSpan totalTime = DateTime.Now - startTime;
            string resultMessage = $"Total time: {totalTime.Minutes}m {totalTime.Seconds}s\n\n";
            
            if (successCount > 0)
            {
                string fileList = string.Join("\n", convertedFiles.Select(f => 
                {
                    int frames = GetVideoFrameCount(f);
                    double duration = GetVideoDuration(f);
                    double calculatedFps = duration > 0 ? frames / duration : 0;
                    double storedFps = GetVideoFrameRate(f);
                    
                    return $"{Path.GetFileName(f)} - {frames} frames, {duration:F2}s\n   Calculated: {calculatedFps:F2} fps | Stored: {storedFps:F2} fps";
                }));
                resultMessage += $"Successfully converted {successCount} video(s) to MP4!\n\nFinal Videos:\n{fileList}\n\nOriginal AVI files deleted.";
            }

            if (failCount > 0)
            {
                if (successCount > 0)
                    resultMessage += "\n\n";
                
                resultMessage += $"Failed to convert {failCount} video(s). AVI files kept.\n\nErrors:\n{string.Join("\n\n", errorMessages)}";
            }

            MessageBox.Show(resultMessage, 
                            failCount > 0 ? "Conversion Complete (with errors)" : "Conversion Complete", 
                            MessageBoxButtons.OK, 
                            failCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private void LogCameraInfo(string message)
        {
            string logFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "CameraRecording.log");
            try
            {
                File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
            }
            catch { }
        }

        private void MonitorDiskWriteSpeed()
        {
            if (!isRecording)
                return;

            long totalBytes = 0;
            foreach (var camera in cameras)
            {
                if (camera.RecordingFilePath != null && File.Exists(camera.RecordingFilePath))
                {
                    try
                    {
                        FileInfo fileInfo = new FileInfo(camera.RecordingFilePath);
                        totalBytes += fileInfo.Length;
                    }
                    catch { }
                }
            }

            if (lastTotalBytesWritten > 0)
            {
                long bytesPerSecond = totalBytes - lastTotalBytesWritten;
                double mbPerSecond = bytesPerSecond / (1024.0 * 1024.0);
                LogCameraInfo($"Disk write speed: {mbPerSecond:F2} MB/s, Total written: {totalBytes / (1024 * 1024)} MB");
                
                if (mbPerSecond < 50 && mbPerSecond > 0)
                {
                    LogCameraInfo($"WARNING: Low disk write speed detected: {mbPerSecond:F2} MB/s");
                }
            }

            lastTotalBytesWritten = totalBytes;
        }
        private string GetVideoCodecTag(string videoFile)
        {
            try
            {
                string ffprobePath = Path.Combine(Path.GetDirectoryName(FFMPEG_PATH)!, "ffprobe.exe");
                
                if (!File.Exists(ffprobePath))
                    return "";

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v error -select_streams v:0 -show_entries stream=codec_tag_string -of default=noprint_wrappers=1:nokey=1 \"{videoFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using (Process process = Process.Start(startInfo)!)
                {
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();

                    if (!string.IsNullOrEmpty(output))
                    {
                        LogCameraInfo($"Detected codec tag for {Path.GetFileName(videoFile)}: {output}");
                        return output;
                    }
                }
            }
            catch (Exception ex)
            {
                LogCameraInfo($"Error detecting codec tag: {ex.Message}");
            }

            return "";
        }
        private string GetVideoPixelFormat(string videoFile)
        {
            try
            {
                string ffprobePath = Path.Combine(Path.GetDirectoryName(FFMPEG_PATH)!, "ffprobe.exe");
                
                if (!File.Exists(ffprobePath))
                    return ""; // Will let FFmpeg auto-detect

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v error -select_streams v:0 -show_entries stream=pix_fmt -of default=noprint_wrappers=1:nokey=1 \"{videoFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using (Process process = Process.Start(startInfo)!)
                {
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();

                    if (!string.IsNullOrEmpty(output))
                    {
                        LogCameraInfo($"Detected pixel format for {Path.GetFileName(videoFile)}: {output}");
                        return output;
                    }
                }
            }
            catch (Exception ex)
            {
                LogCameraInfo($"Error detecting pixel format: {ex.Message}");
            }

            return ""; // Empty means auto-detect
        }

        private double GetVideoFrameRateFromFile(string videoFile)
        {
            try
            {
                string ffprobePath = Path.Combine(Path.GetDirectoryName(FFMPEG_PATH)!, "ffprobe.exe");
                
                if (!File.Exists(ffprobePath))
                    return 0;

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v error -select_streams v:0 -show_entries stream=r_frame_rate -of default=noprint_wrappers=1:nokey=1 \"{videoFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using (Process process = Process.Start(startInfo)!)
                {
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();

                    // Frame rate comes as fraction like "30/1" or "30000/1001"
                    if (!string.IsNullOrEmpty(output) && output.Contains("/"))
                    {
                        string[] parts = output.Split('/');
                        if (parts.Length == 2 && 
                            double.TryParse(parts[0], out double num) && 
                            double.TryParse(parts[1], out double den) && 
                            den > 0)
                        {
                            return num / den;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogCameraInfo($"Error detecting frame rate: {ex.Message}");
            }

            return 0;
        }

        private void ToggleCameraExpansion(int cameraIndex)
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
            
            UpdateCameraLayout();
        }

        private void CollapseExpandedCamera()
        {
            if (expandedCameraIndex >= 0)
            {
                expandedCameraIndex = -1;
                UpdateCameraLayout();
            }
        }

        private double GetVideoDuration(string videoFile)
        {
            try
            {
                string ffprobePath = Path.Combine(Path.GetDirectoryName(FFMPEG_PATH)!, "ffprobe.exe");
                
                if (!File.Exists(ffprobePath))
                    return 0;

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using (Process process = Process.Start(startInfo)!)
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    if (double.TryParse(output.Trim(), out double duration))
                    {
                        return duration;
                    }
                }
            }
            catch { }

            return 0;
        }
    }
}