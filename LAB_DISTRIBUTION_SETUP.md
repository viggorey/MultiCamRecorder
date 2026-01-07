# Lab Distribution Setup Guide

This guide is for preparing the complete distribution package for lab use (non-commercial).

## What to Include in Lab Distribution

Since this is for internal lab use, you can bundle everything needed:

### Required Components

1. **Application Package** (from `publish` folder)
   - QueenPix.exe
   - ffmpeg.exe
   - ffprobe.exe

2. **TIS.Imaging SDK Installer**
   - Download from: https://www.theimagingsource.com/support/downloads/
   - File: "IC Imaging Control 3.5" installer
   - Include this in your distribution package

3. **Camera Driver Installers** (optional but recommended)
   - Download drivers for your specific camera models
   - **Two types of driver packages:**
     - **Type A:** Standalone installer (e.g., `DriverDMKCameras.exe`) - copy just the .exe file to `Installers/`
     - **Type B:** Driver folder with installer + `data` folder (e.g., `usbcam_3.0.4.2535_tis/`) - copy the entire folder to `Installers/`
   - **Important:** If the installer requires a `data` folder, you must include the entire driver folder, not just the installer executable

4. **Documentation**
   - LAB_DISTRIBUTION_README.md (user guide)
   - Quick setup instructions

## Step-by-Step Distribution Package Creation

### Step 1: Prepare Application Files

```powershell
# Run the publish script
.\publish.ps1

# Your publish folder now contains:
# - QueenPix.exe
# - ffmpeg.exe
# - ffprobe.exe
```

### Step 2: Download TIS.Imaging SDK Installer

1. Go to: https://www.theimagingsource.com/support/downloads/
2. Download "IC Imaging Control 3.5" (or latest version)
3. Save the installer file as `TIS_Imaging_SDK_Installer.exe` in the `Installers/` folder

**Note for Lab Users:** When installing the SDK, use the default installation location. The application is self-contained and doesn't depend on the SDK installation path.

### Step 3: Create Distribution Folder Structure

```
QueenPix/
├── QueenPix/
│   ├── QueenPix.exe
│   ├── ffmpeg.exe
│   └── ffprobe.exe
│
├── Installers/
│   ├── TIS_Imaging_SDK_Installer.exe  ← TIS.Imaging SDK installer
│   ├── DriverDMKCameras.exe           ← Standalone driver installer (if applicable)
│   └── usbcam_3.0.4.2535_tis/         ← Driver folder (if installer requires data folder)
│       ├── drvInstaller.exe
│       └── data/
│
└── README.md  ← Copy LAB_DISTRIBUTION_README.md here
```

**Note:** 
- Standalone installer executables go directly in `Installers/` folder
- Driver folders (containing installer + `data` folder) should be placed as entire folders in `Installers/`

### Step 4: Create Distribution Package

**Option A: ZIP File (Recommended)**
```powershell
# Create ZIP file
Compress-Archive -Path "Lab_Distribution\*" -DestinationPath "MultiCamRecorder_Lab_v1.0.zip"
```

**Option B: Network Share**
- Copy the `Lab_Distribution` folder to a network share
- Lab users can access and run from there

**Option C: USB Drive**
- Copy the `Lab_Distribution` folder to USB drive
- Distribute to lab computers

## Distribution Script

Create a simple script to automate package creation:

```powershell
# create-lab-package.ps1
$packageName = "MultiCamRecorder_Lab_v1.0"
$distFolder = "Lab_Distribution"

# Clean previous package
if (Test-Path $distFolder) {
    Remove-Item -Recurse -Force $distFolder
}

# Create folder structure
New-Item -ItemType Directory -Path "$distFolder\MultiCamRecorder" -Force
New-Item -ItemType Directory -Path "$distFolder\Installers" -Force

# Copy application files
Copy-Item "publish\*.exe" -Destination "$distFolder\MultiCamRecorder\" -Exclude "*.pdb"

# Copy documentation
Copy-Item "LAB_DISTRIBUTION_README.md" -Destination "$distFolder\README.md"

# Check for SDK installer
if (Test-Path "Installers\TIS_Imaging_SDK_Installer.exe") {
    Copy-Item "Installers\TIS_Imaging_SDK_Installer.exe" -Destination "$distFolder\Installers\"
    Write-Host "✓ TIS.Imaging SDK installer included" -ForegroundColor Green
} else {
    Write-Host "⚠ TIS.Imaging SDK installer not found in Installers folder" -ForegroundColor Yellow
    Write-Host "  Download from: https://www.theimagingsource.com/support/downloads/" -ForegroundColor Yellow
    Write-Host "  Save as: Installers\TIS_Imaging_SDK_Installer.exe" -ForegroundColor Yellow
}

# Create ZIP
if (Test-Path "$packageName.zip") {
    Remove-Item "$packageName.zip"
}
Compress-Archive -Path "$distFolder\*" -DestinationPath "$packageName.zip"

Write-Host "`nPackage created: $packageName.zip" -ForegroundColor Green
```

## Installation Instructions for Lab Users

### Quick Setup (3 Steps)

1. **Extract the ZIP file** to any location
2. **Install TIS.Imaging SDK:**
   - Run `Installers\TIS_Imaging_SDK_Installer.exe`
   - Follow the installation wizard
   - Restart computer (recommended)
3. **Run the application:**
   - Navigate to `QueenPix\QueenPix` folder
   - Run `QueenPix.exe`
   - Click "Refresh Cameras"

### Detailed Setup

See `LAB_DISTRIBUTION_README.md` for complete instructions.

## Updating the Distribution

When you update the application:

1. Run `.\publish.ps1` to rebuild
2. Run `.\create-lab-package.ps1` (if you create the script)
3. Or manually copy files to `Lab_Distribution` folder
4. Create new ZIP file with version number

## Benefits of This Approach

✅ **Complete package** - Everything needed in one place  
✅ **Easy distribution** - Single ZIP file  
✅ **No external downloads** - All components included  
✅ **Consistent setup** - Same package for all lab computers  
✅ **Version control** - Easy to track which version is deployed  

## Notes

- **License compliance:** Since this is for internal lab use only, you can redistribute the TIS.Imaging SDK installer
- **Camera drivers:** Still need to be installed separately (system-level installation)
- **Updates:** When updating, create a new package with incremented version number
- **Network deployment:** Consider using a network share for easier updates across multiple lab computers

