using System;
using System.Windows.Forms;
using TIS.Imaging;
using System.Linq;

namespace MultiCamRecorder
{
    public class CameraSettingsDialog : Form
    {
        private ICImagingControl imagingControl;
        private CameraSettings settings;
        
        private ComboBox cmbFormat;
        private Button btnShowProperties;
        private NumericUpDown numSoftwareFps;  // <-- NEW LINE
        private Label lblFpsRange; 
        private RadioButton rbExternalTrigger;
        private RadioButton rbSoftwareControlled;
        private int cameraNumber = 0;
        public CameraSettings Settings { get; private set; }
        public bool SaveAsDefault { get; private set; }
        
        public CameraSettingsDialog(ICImagingControl control, CameraSettings currentSettings, int cameraNum)
        {
            imagingControl = control;
            settings = currentSettings.Clone();
            Settings = settings;
            cameraNumber = cameraNum;
            
            InitializeDialog();
        }
        
        private void InitializeDialog()
        {
            this.Text = $"Camera {cameraNumber}: {imagingControl.Device} Settings";
            this.Size = new System.Drawing.Size(500, 340);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            
            int y = 20;
            int leftMargin = 20;
            int labelWidth = 120;
            int controlWidth = 340;
            
            // Format + Resolution dropdown
            Label lblFormat = new Label
            {
                Text = "Format + Resolution:",
                Location = new System.Drawing.Point(leftMargin, y),
                Size = new System.Drawing.Size(labelWidth, 20)
            };
            this.Controls.Add(lblFormat);
            
            cmbFormat = new ComboBox
            {
                Location = new System.Drawing.Point(leftMargin + labelWidth, y - 2),
                Size = new System.Drawing.Size(controlWidth, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            this.Controls.Add(cmbFormat);
            y += 40;

        // Frame Rate Control radio buttons
        Label lblFrameRateControl = new Label
        {
            Text = "Frame Rate Control:",
            Location = new System.Drawing.Point(leftMargin, y),
            Size = new System.Drawing.Size(labelWidth, 20)
        };
        this.Controls.Add(lblFrameRateControl);

        rbExternalTrigger = new RadioButton
        {
            Text = "External Trigger",
            Location = new System.Drawing.Point(leftMargin + labelWidth, y),
            Size = new System.Drawing.Size(150, 20),
            Checked = settings.UseExternalTrigger
        };
        rbExternalTrigger.CheckedChanged += RbFrameRateControl_CheckedChanged;
        this.Controls.Add(rbExternalTrigger);

        rbSoftwareControlled = new RadioButton
        {
            Text = "Software Controlled",
            Location = new System.Drawing.Point(leftMargin + labelWidth + 160, y),
            Size = new System.Drawing.Size(180, 20),
            Checked = !settings.UseExternalTrigger
        };
        rbSoftwareControlled.CheckedChanged += RbFrameRateControl_CheckedChanged;
        this.Controls.Add(rbSoftwareControlled);

        y += 35;

        // Software Frame Rate input
        Label lblSoftwareFps = new Label
        {
            Text = "Software Frame Rate:",
            Location = new System.Drawing.Point(leftMargin, y),
            Size = new System.Drawing.Size(labelWidth, 20)
        };
        this.Controls.Add(lblSoftwareFps);

        numSoftwareFps = new NumericUpDown
        {
            Location = new System.Drawing.Point(leftMargin + labelWidth, y - 2),
            Size = new System.Drawing.Size(80, 25),
            DecimalPlaces = 4,          // ✅ CHANGED: 2 decimal places for precision
            Minimum = 0.0001M,            // ✅ CHANGED: 0.01 fps = 1 frame per 100 seconds
            Maximum = 240M,
            Value = (decimal)settings.SoftwareFrameRate,
            Enabled = !settings.UseExternalTrigger,
            Increment = 0.1M            // ✅ ADD THIS: Smaller increment steps
        };
        this.Controls.Add(numSoftwareFps);

        lblFpsRange = new Label
        {
            Location = new System.Drawing.Point(leftMargin + labelWidth + 90, y + 2),
            Size = new System.Drawing.Size(250, 20),
            ForeColor = System.Drawing.Color.Gray,
            Font = new System.Drawing.Font("Arial", 8)
        };
        this.Controls.Add(lblFpsRange);

        // Populate FPS range info
        PopulateFpsRangeInfo();

        y += 40;

            // Info note about external trigger
            Label lblTriggerNote = new Label
            {
                Text = "ℹ️ Note: External function generator takes priority if connected",
                Location = new System.Drawing.Point(leftMargin, y),
                Size = new System.Drawing.Size(460, 20),
                ForeColor = System.Drawing.Color.Blue,
                Font = new System.Drawing.Font("Arial", 8)
            };
            this.Controls.Add(lblTriggerNote);
            y += 30;
            
            // Populate format dropdown
            if (imagingControl.VideoFormats != null)
            {
                foreach (var format in imagingControl.VideoFormats)
                {
                    cmbFormat.Items.Add(format.ToString());
                }
                
                // Select current format
                string currentFormat = imagingControl.VideoFormatCurrent?.ToString() ?? "";
                int idx = cmbFormat.Items.IndexOf(currentFormat);
                if (idx >= 0)
                    cmbFormat.SelectedIndex = idx;
            }
            
            y += 10;
            
            // Camera Properties button (uses SDK's built-in dialog)
            Label lblProperties = new Label
            {
                Text = "Camera Properties:",
                Location = new System.Drawing.Point(leftMargin, y + 5),
                Size = new System.Drawing.Size(labelWidth, 20)
            };
            this.Controls.Add(lblProperties);
            
            btnShowProperties = new Button
            {
                Text = "Adjust Exposure, Gain, etc...",
                Location = new System.Drawing.Point(leftMargin + labelWidth, y),
                Size = new System.Drawing.Size(controlWidth, 30)
            };
            btnShowProperties.Click += BtnShowProperties_Click;
            this.Controls.Add(btnShowProperties);
            y += 50;
            
            Label lblNote = new Label
            {
                Text = "Note: Property changes (exposure, gain, etc.) are saved automatically by the camera.",
                Location = new System.Drawing.Point(leftMargin, y),
                Size = new System.Drawing.Size(460, 30),
                ForeColor = System.Drawing.Color.Gray,
                Font = new System.Drawing.Font("Arial", 8)
            };
            this.Controls.Add(lblNote);
            y += 40;
            
            // Buttons
            Button btnCancel = new Button
            {
                Text = "Cancel",
                Location = new System.Drawing.Point(290, y),
                Size = new System.Drawing.Size(85, 32),
                Font = new System.Drawing.Font("Arial", 9),
                DialogResult = DialogResult.Cancel
            };
            this.Controls.Add(btnCancel);

            Button btnApply = new Button
            {
                Text = "Save",
                Location = new System.Drawing.Point(385, y),
                Size = new System.Drawing.Size(85, 32),
                Font = new System.Drawing.Font("Arial", 9),
                DialogResult = DialogResult.OK
            };
            btnApply.Click += BtnApply_Click;
            this.Controls.Add(btnApply);

            this.AcceptButton = btnApply;
            this.CancelButton = btnCancel;
        }
        
        private void PopulateFpsRangeInfo()
        {
            try
            {
                if (imagingControl.DeviceFrameRateAvailable && 
                    imagingControl.DeviceFrameRates != null && 
                    imagingControl.DeviceFrameRates.Length > 0)
                {
                    float[] rates = imagingControl.DeviceFrameRates;
                    float minRate = rates.Min();
                    float maxRate = rates.Max();
                    lblFpsRange.Text = $"Available: {minRate:F1} - {maxRate:F1} fps";
                    lblFpsRange.ForeColor = System.Drawing.Color.Gray;
                }
                else
                {
                    lblFpsRange.Text = "Available: Frame rate not supported by camera";
                    lblFpsRange.ForeColor = System.Drawing.Color.Orange;
                }
            }
            catch
            {
                lblFpsRange.Text = "Available: Frame rate not supported by camera";
                lblFpsRange.ForeColor = System.Drawing.Color.Orange;
            }
        }

        private void RbFrameRateControl_CheckedChanged(object? sender, EventArgs e)
        {
            // Enable/disable software FPS control based on selection
            numSoftwareFps.Enabled = rbSoftwareControlled.Checked;
            lblFpsRange.Enabled = rbSoftwareControlled.Checked;
        }
        private float ValidateAndAdjustFrameRate(float requestedFps)
        {
            try
            {
                if (imagingControl.DeviceFrameRateAvailable && 
                    imagingControl.DeviceFrameRates != null && 
                    imagingControl.DeviceFrameRates.Length > 0)
                {
                    float[] rates = imagingControl.DeviceFrameRates;
                    
                    // Check if exact match exists
                    if (rates.Contains(requestedFps))
                    {
                        return requestedFps;
                    }
                    
                    // Find closest valid rate (nearest neighbor)
                    float closestRate;
                    var lowerRates = rates.Where(r => r <= requestedFps).ToArray();
                    var higherRates = rates.Where(r => r > requestedFps).ToArray();

                    if (lowerRates.Length == 0)
                    {
                        // All rates are higher, use minimum
                        closestRate = rates.Min();
                    }
                    else if (higherRates.Length == 0)
                    {
                        // All rates are lower, use maximum
                        closestRate = rates.Max();
                    }
                    else
                    {
                        // Find closest from both sides
                        float lower = lowerRates.Max();
                        float higher = higherRates.Min();
                        
                        // Pick whichever is closer
                        closestRate = (requestedFps - lower) < (higher - requestedFps) ? lower : higher;
                    }
                    
                    // Show warning if adjusted
                    if (Math.Abs(closestRate - requestedFps) > 0.01f)
                    {
                        MessageBox.Show(
                            $"Frame rate adjusted to nearest supported value: {closestRate:F1} fps\n\n" +
                            $"Requested: {requestedFps:F1} fps",
                            "Frame Rate Adjusted",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    
                    return closestRate;
                }
                else
                {
                    // Camera doesn't support frame rate control - allow any value with warning
                    if (requestedFps != settings.SoftwareFrameRate)
                    {
                        MessageBox.Show(
                            $"This camera does not report supported frame rates.\n\n" +
                            $"Frame rate set to {requestedFps:F1} fps, but may not be applied correctly.",
                            "Frame Rate Warning",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                    return requestedFps;
                }
            }
            catch
            {
                return requestedFps;
            }
        }

        private void BtnShowProperties_Click(object? sender, EventArgs e)
        {
            try
            {
                // Show the built-in property dialog from IC Imaging Control SDK
                // This handles all camera properties (exposure, gain, white balance, gamma, etc.)
                imagingControl.ShowPropertyDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error showing property dialog: {ex.Message}",
                               "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        
        private void BtnApply_Click(object? sender, EventArgs e)
        {
            SaveAsDefault = true;  // Always save as default now
            
            // Save format setting
            if (cmbFormat.SelectedItem != null)
            {
                settings.Format = cmbFormat.SelectedItem.ToString() ?? "";
            }
            
            // Save frame rate control mode
            settings.UseExternalTrigger = rbExternalTrigger.Checked;
            
            // Validate and save frame rate (only if software controlled)
            if (rbSoftwareControlled.Checked)
            {
                float requestedFps = (float)numSoftwareFps.Value;
                float finalFps = ValidateAndAdjustFrameRate(requestedFps);
                settings.SoftwareFrameRate = finalFps;
            }
            
            // Save VCD properties as XML string
            try
            {
                if (imagingControl.VCDPropertyItems != null)
                {
                    settings.VCDPropertiesXml = imagingControl.VCDPropertyItems.Save();
                }
            }
            catch { }
            
            Settings = settings;
        }
    }
}