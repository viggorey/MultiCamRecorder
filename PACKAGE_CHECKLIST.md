# QueenPix Distribution Package Checklist

## Before Creating the Package

### Required Files:

1. **TIS.Imaging SDK Installer** (REQUIRED)
   - Download from: https://www.theimagingsource.com/support/downloads/
   - Look for "IC Imaging Control 3.5" installer
   - Save as: `Installers\TIS_Imaging_SDK_Installer.exe`
   - **Note:** This is required for the application to work properly

2. **Camera Driver Installer** (Already included ✓)
   - Location: `Installers\usbcam_3.0.4.2535_tis\`
   - This folder contains the driver installer and data folder
   - The script will automatically include it

### Optional Files:

- `LAB_DISTRIBUTION_README.md` - Will be copied as `README.md` in the package

## Creating the Package

### Step 1: Ensure SDK Installer is Present

Check if the SDK installer exists:
```powershell
Test-Path "Installers\TIS_Imaging_SDK_Installer.exe"
```

If it doesn't exist:
1. Download "IC Imaging Control 3.5" from The Imaging Source website
2. Save it as `Installers\TIS_Imaging_SDK_Installer.exe`

### Step 2: Build the Application

```powershell
.\publish.ps1
```

This creates the self-contained executable in the `publish` folder.

### Step 3: Create the Distribution Package

```powershell
.\create-lab-package.ps1
```

This will:
- Copy the application files from `publish\` to `QueenPix\QueenPix\`
- Copy the SDK installer to `QueenPix\Installers\`
- Copy the camera driver folder to `QueenPix\Installers\`
- Copy the README to `QueenPix\README.md`
- Create `QueenPix_Lab_v1.0.zip`

### Step 4: Verify Package Contents

The package should contain:
```
QueenPix_Lab_v1.0.zip
├── QueenPix/
│   ├── QueenPix/
│   │   ├── QueenPix.exe
│   │   ├── ffmpeg.exe
│   │   └── ffprobe.exe
│   ├── Installers/
│   │   ├── TIS_Imaging_SDK_Installer.exe  ← REQUIRED
│   │   └── usbcam_3.0.4.2535_tis/         ← Driver folder
│   │       ├── drvInstaller.exe
│   │       └── data/
│   └── README.md
```

## Distribution Instructions for Lab Users

1. **Extract the ZIP file** to any location
2. **Install camera driver:**
   - Run `Installers\usbcam_3.0.4.2535_tis\drvInstaller.exe`
   - Follow the installation wizard
   - May require Administrator privileges
3. **Install TIS.Imaging SDK:**
   - Run `Installers\TIS_Imaging_SDK_Installer.exe`
   - Follow the installation wizard
   - Restart computer (recommended)
4. **Run the application:**
   - Navigate to `QueenPix\QueenPix\`
   - Run `QueenPix.exe`
   - Click "Refresh Cameras"

## Troubleshooting

If the package creation script reports missing files:
- **SDK installer missing:** Download from The Imaging Source website
- **Driver missing:** The driver folder is already included, so this shouldn't happen
- **Application not found:** Run `.\publish.ps1` first

## Notes

- The application is self-contained (no .NET installation needed)
- FFmpeg tools are included
- Camera drivers must be installed separately
- TIS.Imaging SDK must be installed for proper functionality
- All installers are included in the package for lab distribution
