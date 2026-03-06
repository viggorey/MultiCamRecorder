# Distribution Package Guide

This guide covers how to create and distribute the QueenPix application package.

## Quick Export Steps

### Step 1: Create the Distribution Package

Run this command in PowerShell (from your project folder):

```powershell
.\create-lab-package.ps1
```

Or manually:
```powershell
# Publish the application
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -o ./publish

# Copy FFmpeg executables
Copy-Item "tools\ffmpeg.exe" -Destination "publish\"
Copy-Item "tools\ffprobe.exe" -Destination "publish\"
```

### Step 2: Transfer to Another Computer

**Option A: USB Drive**
1. Copy `QueenPix_Lab.zip` to a USB drive
2. Transfer to the target computer
3. Extract the ZIP file

**Option B: Network Share**
1. Copy `QueenPix_Lab.zip` to a network share
2. Access from the target computer
3. Extract the ZIP file

**Option C: Cloud Storage**
1. Upload `QueenPix_Lab.zip` to Google Drive, OneDrive, Dropbox, etc.
2. Download on the target computer
3. Extract the ZIP file

## Distribution Package Setup

### What to Include

1. **Application Package** (from `publish` folder)
   - QueenPix.exe
   - ffmpeg.exe
   - ffprobe.exe

2. **TIS.Imaging SDK Installer**
   - Download from: https://www.theimagingsource.com/support/downloads/
   - File: "IC Imaging Control 3.5" installer
   - Save as `Installers\TIS_Imaging_SDK_Installer.exe`

3. **Camera Driver Installers**
   - **Type A:** Standalone installer (e.g., `DriverDMKCameras.exe`) - copy just the .exe to `Installers/`
   - **Type B:** Driver folder with installer + `data` folder (e.g., `usbcam_3.0.4.2535_tis/`) - copy the entire folder to `Installers/`

4. **Documentation**
   - Copy `LAB_DISTRIBUTION_README.md` as `README.md` in the distribution package

### Package Structure

```
QueenPix_Lab.zip
├── QueenPix.exe                 ← Main application (self-contained)
├── ffmpeg.exe                     ← Video processing
├── ffprobe.exe                    ← Video analysis
├── Installers/
│   ├── TIS_Imaging_SDK_Installer.exe  ← Install for Y800 format
│   ├── DriverDMKCameras.exe           ← Camera driver (if applicable)
│   └── usbcam_3.0.4.2535_tis/         ← Driver folder (if applicable)
│       ├── drvInstaller.exe
│       └── data/
└── README.md                         ← User instructions
```

## Installation Instructions for End Users

### Quick Setup (3 Steps)

1. **Extract the ZIP file** to any location
2. **Install TIS.Imaging SDK:**
   - Run `Installers\TIS_Imaging_SDK_Installer.exe`
   - Follow the installation wizard
   - Restart computer (recommended)
3. **Run the application:**
   - Navigate to the extracted `QueenPix\` folder
   - Run `QueenPix.exe`
   - Click "Refresh Cameras"

### Detailed Setup

See `LAB_DISTRIBUTION_README.md` for complete end-user instructions (this file should be copied as `README.md` in the distribution package).

## Important Notes

- **No .NET installation needed** - everything is bundled in the .exe
- **No FFmpeg installation needed** - included with application
- **Camera drivers must be installed** - run the driver installer first
- **TIS.Imaging SDK must be installed** - for Y800 format support
- **License compliance:** For internal lab use only, you can redistribute the TIS.Imaging SDK installer

## Updating the Distribution

When you update the application:

1. Run `.\publish.ps1` to rebuild
2. Run `.\create-lab-package.ps1` to create the package
3. Or manually copy files to distribution folder
4. Create new ZIP file with incremented version number

## Troubleshooting

If the application doesn't work on the target computer:
1. Check that camera drivers are installed (Device Manager)
2. Check that TIS.Imaging SDK is installed
3. Try running as Administrator
4. Check Windows Event Viewer for errors
