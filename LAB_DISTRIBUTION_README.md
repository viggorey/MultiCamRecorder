# QueenPix - Lab Distribution Package

## Quick Start for Lab Users

### Step 1: Install Camera Drivers
1. Connect your camera(s) to the computer
2. **If driver installer is included in this package:**
   - Run the appropriate driver installer from the `Installers` folder
   - Follow the installation wizard (may require Administrator privileges)
3. **If driver installer is not included:**
   - Download the driver from the camera manufacturer's website
   - Install the driver
4. Verify in Device Manager (Win+X → Device Manager) that cameras appear under "Cameras" or "Imaging devices" without yellow warning icons

### Step 2: Install TIS.Imaging SDK (Required for Y800 Format)
1. Run `Installers\TIS_Imaging_SDK_Installer.exe` (included in this package)
2. Follow the installation wizard
   - **When asked for installation location:** Use the default location (recommended)
   - Default: `C:\Program Files (x86)\The Imaging Source Europe GmbH\IC Imaging Control 3.5 .NET Library\`
   - **Note:** The installation location doesn't affect the application - it's self-contained
3. Restart your computer (recommended)

### Step 3: Run the Application
1. After extracting the ZIP, navigate to the `QueenPix\` folder
2. Run `QueenPix.exe`
3. Click "Refresh Cameras" to detect connected cameras

## Package Contents

```
QueenPix/
├── QueenPix.exe                 ← Main application
├── ffmpeg.exe                   ← Video processing tool
├── ffprobe.exe                  ← Video analysis tool
├── Installers/
│   ├── TIS_Imaging_SDK_Installer.exe  ← Install this for Y800 format support (optional - download if needed)
│   └── usbcam_3.0.4.2535_tis/         ← Camera driver installer
│       ├── drvInstaller.exe
│       └── data/
└── README.md (this file)
```

**Note:** The TIS.Imaging SDK installer may not be included in the package. If you need it:
1. Download from: https://www.theimagingsource.com/support/downloads/
2. Look for "IC Imaging Control 3.5" SDK installer
3. Place it in the `Installers\` folder before creating the package

## Installation Order

**Important:** Install in this order for best results:

1. **Camera Drivers** (from manufacturer)
   - If you see a driver folder (e.g., `usbcam_3.0.4.2535_tis/`), run the installer inside that folder (e.g., `drvInstaller.exe`)
   - If you see a standalone installer (e.g., `DriverDMKCameras.exe`), run it directly
2. **TIS.Imaging SDK** (optional - only if `TIS_Imaging_SDK_Installer.exe` is present in the `Installers\` folder)
   - Install this if you need Y800 format support
   - If the installer is not in the package, download it from: https://www.theimagingsource.com/support/downloads/
3. **Run QueenPix** (no installation needed)

## Troubleshooting

### "No cameras detected"
- Check Device Manager - cameras should appear without yellow warning icons
- Ensure camera drivers are installed
- Try running as Administrator

### "Y800 format not available"
- Install TIS.Imaging SDK from this package
- Restart the application after installation
- Y800 format should appear in camera settings

### Application won't start
- Ensure Windows 10/11 (64-bit)
- Try running as Administrator
- Check Windows Event Viewer for error details

## Notes

- **No .NET installation needed** - everything is bundled
- **No FFmpeg installation needed** - included with application
- **Camera drivers** must be installed separately (from manufacturer)
- **TIS.Imaging SDK** is included for convenience (lab use only)
- **GenTL Producer / IC4 Drivers** - Not needed for USB cameras with IC3.5 SDK (only needed for GigE Vision cameras with IC4 SDK)

## Support

For issues or questions, contact the lab administrator.

