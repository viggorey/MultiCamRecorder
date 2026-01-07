# How to Export Your Application to Another Laptop

## Quick Steps

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

# Create package (the script does this automatically)
```

### Step 2: Find Your Package

After running the script, you'll have:
- **File:** `QueenPix_Lab_v1.0.zip`
- **Location:** Your project folder (same location as `.csproj` file)
- **Size:** ~600-700 MB (contains everything needed)

### Step 3: Transfer to Another Laptop

**Option A: USB Drive**
1. Copy `QueenPix_Lab_v1.0.zip` to a USB drive
2. Plug USB drive into the other laptop
3. Copy the ZIP file to the laptop
4. Extract the ZIP file

**Option B: Network Share**
1. Copy `QueenPix_Lab_v1.0.zip` to a network share
2. Access the network share from the other laptop
3. Copy the ZIP file to the laptop
4. Extract the ZIP file

**Option C: Cloud Storage**
1. Upload `QueenPix_Lab_v1.0.zip` to Google Drive, OneDrive, Dropbox, etc.
2. Download it on the other laptop
3. Extract the ZIP file

**Option D: Email (if size allows)**
1. Attach `QueenPix_Lab_v1.0.zip` to an email
2. Send to yourself or the laptop user
3. Download and extract on the other laptop

### Step 4: On the Other Laptop

1. **Extract the ZIP file** to any location (Desktop, Documents, etc.)
   - After extraction, you should see a `QueenPix` folder
   - Inside `QueenPix`, you'll find:
     - `QueenPix` folder (contains the application)
     - `Installers` folder (contains driver and SDK installers)
     - `README.md` (instructions)

2. **Install Camera Drivers:**
   - Navigate to `QueenPix\Installers\`
   - Run `DriverDMKCameras.exe`
   - Follow the installation wizard
   - May require Administrator privileges

3. **Install TIS.Imaging SDK:**
   - Still in `QueenPix\Installers\`
   - Run `TIS_Imaging_SDK_Installer.exe`
   - Use default installation location
   - Restart computer (recommended)

4. **Run the Application:**
   - Navigate to `QueenPix\QueenPix\` folder
   - Run `QueenPix.exe`
   - Click "Refresh Cameras" to detect cameras

## Package Contents

```
QueenPix_Lab_v1.0.zip
├── QueenPix/
│   ├── QueenPix.exe            ← Main application (self-contained)
│   ├── ffmpeg.exe              ← Video processing
│   └── ffprobe.exe             ← Video analysis
├── Installers/
│   ├── TIS_Imaging_SDK_Installer.exe  ← Install for Y800 format
│   └── DriverDMKCameras.exe           ← Camera driver
└── README.md                    ← User instructions
```

## Important Notes

- **No .NET installation needed** - everything is bundled in the .exe
- **No FFmpeg installation needed** - included with application
- **Camera drivers must be installed** - run the driver installer first
- **TIS.Imaging SDK must be installed** - for Y800 format support

## Troubleshooting

If the application doesn't work on the other laptop:
1. Check that camera drivers are installed (Device Manager)
2. Check that TIS.Imaging SDK is installed
3. Try running as Administrator
4. Check Windows Event Viewer for errors

## Summary

1. Run `.\create-lab-package.ps1` → Creates ZIP file
2. Transfer ZIP to other laptop (USB, network, cloud, etc.)
3. Extract ZIP on other laptop
4. Install drivers and SDK from `Installers` folder
5. Run `QueenPix.exe`

That's it! The application is completely self-contained - no additional installations needed (except the drivers and SDK from the package).

