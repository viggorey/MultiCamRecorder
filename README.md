# QueenPix

Multi-camera recording application for lab use. Built with .NET 8 Windows Forms, supporting TIS (The Imaging Source) cameras via ICImagingControl SDK and webcams via OpenCvSharp.

## Requirements

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- TIS.Imaging ICImagingControl 3.5 SDK (place `TIS.Imaging.ICImagingControl35.dll` in `libs/`)
- Windows 10/11 64-bit

## Build & Run

```powershell
dotnet restore
dotnet build
dotnet run
```

## Publishing a Distribution Package

### Step 1 — Build the self-contained executable

```powershell
.\publish.ps1
```

Output goes to `./publish/`. The exe is self-contained (no .NET install needed on target machines).

### Step 2 — Create the zip package

```powershell
.\create-lab-package.ps1
```

This produces `QueenPix_Lab.zip` containing the exe, ffmpeg tools, camera drivers, and TIS SDK installer.

**Before running**, make sure `Installers\TIS_Imaging_SDK_Installer.exe` is present. If not, download "IC Imaging Control 3.5" from the TIS website and place it there.

### Package structure

```
QueenPix_Lab.zip
└── QueenPix/
    ├── QueenPix.exe
    ├── ffmpeg.exe
    ├── ffprobe.exe
    ├── README.md
    └── Installers/
        ├── TIS_Imaging_SDK_Installer.exe
        └── usbcam_3.0.4.2535_tis/
            ├── drvInstaller.exe
            └── data/
```

## Dependencies

| Dependency | How managed |
|---|---|
| OpenCvSharp4 4.11.x | NuGet (in `.csproj`) |
| TIS.Imaging ICImagingControl 3.5 | Manual — `libs/TIS.Imaging.ICImagingControl35.dll` |
| FFmpeg / FFprobe | Manual — `tools/ffmpeg.exe`, `tools/ffprobe.exe` |
| Camera drivers | Installed on target machine from `Installers/` |

## End-User Instructions

See [LAB_DISTRIBUTION_README.md](LAB_DISTRIBUTION_README.md) — this file is packaged as `README.md` inside the zip for lab users.
