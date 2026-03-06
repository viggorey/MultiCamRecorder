# QueenPix Distribution Package - Structure

## Cleaned Up Structure

The package has been reorganized for clarity and to remove redundancy:

### Before (Redundant):
```
QueenPix/
├── QueenPix/          ← Redundant nested folder
│   ├── QueenPix.exe
│   ├── ffmpeg.exe
│   └── ffprobe.exe
├── Installers/
└── README.md
```

### After (Clean):
```
QueenPix/
├── QueenPix.exe       ← Application files at root level
├── ffmpeg.exe
├── ffprobe.exe
├── Installers/         ← All installers in one place
│   └── usbcam_3.0.4.2535_tis/
│       ├── drvInstaller.exe
│       └── data/
└── README.md           ← Installation instructions
```

## Files Included

### Application Files (Root Level)
- `QueenPix.exe` - Main application (self-contained, includes all dependencies)
- `ffmpeg.exe` - Video processing tool
- `ffprobe.exe` - Video analysis tool

### Installers Folder
- `usbcam_3.0.4.2535_tis/` - Camera driver installer with data folder
  - `drvInstaller.exe` - Driver installer executable
  - `data/` - Driver data files

### Documentation
- `README.md` - Installation and usage instructions

## Removed/Not Included

- ❌ `create-zip.ps1` - Temporary script (removed)
- ❌ Nested `QueenPix\QueenPix\` folder structure (simplified)
- ❌ `.pdb` debug files (excluded from package)
- ⚠️ `TIS_Imaging_SDK_Installer.exe` - Not included (user must download)

## Notes

- All application files are at the root of the `QueenPix` folder for easy access
- Installers are organized in the `Installers` subfolder
- No duplicate files
- Clean, flat structure for easy navigation
