using System;

namespace QueenPix
{
    /// <summary>
    /// Stores settings for a single camera
    /// </summary>
    public class CameraSettings
    {
        public string DeviceName { get; set; } = "";
        public string Format { get; set; } = ""; // e.g., "Y800 (640x480)"
        
        // Store all VCD properties (exposure, gain, gamma, white balance, etc.) as XML
        public string VCDPropertiesXml { get; set; } = "";
        public float SoftwareFrameRate { get; set; } = 30.0f; 
        public bool UseExternalTrigger { get; set; } = false;
        
        // Date/Time overlay settings
        public bool ShowDate { get; set; } = false;
        public bool ShowTime { get; set; } = false;
        public bool ShowMilliseconds { get; set; } = false;
        
        // JSON timestamp file generation
        public bool GenerateJsonTimestamps { get; set; } = true; // Default to enabled

        // Camera type flag
        public bool IsImagingSource { get; set; } = true;
        public CameraSettings()
        {
        }
        
        public CameraSettings(string deviceName)
        {
            DeviceName = deviceName;
        }
        
        /// <summary>
        /// Creates a copy of the settings
        /// </summary>
        public CameraSettings Clone()
        {
            return new CameraSettings
            {
                DeviceName = this.DeviceName,
                Format = this.Format,
                VCDPropertiesXml = this.VCDPropertiesXml,
                SoftwareFrameRate = this.SoftwareFrameRate,
                UseExternalTrigger = this.UseExternalTrigger,
                ShowDate = this.ShowDate,
                ShowTime = this.ShowTime,
                ShowMilliseconds = this.ShowMilliseconds,
                GenerateJsonTimestamps = this.GenerateJsonTimestamps,
                IsImagingSource = this.IsImagingSource
            };
        }
    }
}