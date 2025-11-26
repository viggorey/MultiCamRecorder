# MultiCamRecorder - Deployment Guide

This guide explains how to run the MultiCamRecorder application on another laptop.

## System Requirements

- **Operating System**: Windows 10/11 (64-bit)
- **.NET Runtime**: .NET 8.0 Desktop Runtime
- **TIS Imaging Control**: IC Imaging Control 3.5 .NET Library
- **FFmpeg**: FFmpeg 8.0 (or compatible version)

---

## Option 1: Copy Compiled Application (Recommended for End Users)

### Step 1: Install Prerequisites on the Target Laptop

1. **Install .NET 8.0 Desktop Runtime**
   - Download from: https://dotnet.microsoft.com/download/dotnet/8.0
   - Choose "Desktop Runtime 8.0.x" (not SDK)
   - Install the x64 version

2. **Install TIS Imaging Control 3.5**
   - Download from: https://www.theimagingsource.com/support/downloads/
   - Install "IC Imaging Control 3.5 .NET Library"
   - Default installation path: `C:\Program Files (x86)\The Imaging Source Europe GmbH\IC Imaging Control 3.5 .NET Library\`

3. **Install FFmpeg**
   - Download FFmpeg from: https://www.ffmpeg.org/download.html
   - Install FFmpeg to any location (or add it to your system PATH)
   - **Note**: The application can auto-detect FFmpeg in common locations or you can configure it manually:
     - After running the application, go to **Tools → FFmpeg Settings...**
     - Click "Auto-Detect" to find FFmpeg automatically, or
     - Click "Browse..." to manually select the `ffmpeg.exe` file
   - The path will be saved and remembered for future use

### Step 2: Copy Application Files

Copy the entire `bin\Debug\net8.0-windows` folder from your development machine to the target laptop. This folder contains:
- `MultiCamRecorder.exe` (main executable)
- All required DLLs (OpenCvSharp, TIS.Imaging, etc.)
- Native runtime files in the `runtimes` folder

**Recommended**: Create a folder like `C:\Program Files\MultiCamRecorder\` on the target laptop and copy all files there.

### Step 3: Run the Application

Double-click `MultiCamRecorder.exe` to run the application.

---

## Option 2: Build from Source (For Developers)

### Step 1: Install Prerequisites

1. **Install .NET 8.0 SDK** (not just runtime)
   - Download from: https://dotnet.microsoft.com/download/dotnet/8.0
   - Choose "SDK 8.0.x"

2. **Install TIS Imaging Control 3.5** (same as Option 1, Step 1.2)

3. **Install FFmpeg** (same as Option 1, Step 1.3)
   - The FFmpeg path is configurable through the application UI (Tools → FFmpeg Settings...)

### Step 2: Copy Source Code

Copy the entire project folder to the target laptop, including:
- All `.cs` files
- `MultiCamRecorder.csproj`
- `obj` and `bin` folders (optional, can be regenerated)

### Step 3: Build and Run

Open a terminal in the project directory and run:

```bash
dotnet restore
dotnet build
dotnet run
```

Or simply double-click `MultiCamRecorder.exe` from the `bin\Debug\net8.0-windows` folder after building.

---

## Troubleshooting

### "Could not load file or assembly" Error

- Ensure .NET 8.0 Desktop Runtime is installed
- Verify TIS.Imaging.ICImagingControl35.dll is in the same folder as the executable
- Check that all files from `bin\Debug\net8.0-windows` were copied

### FFmpeg Not Found Error

- The application will try to auto-detect FFmpeg in common locations and PATH
- If FFmpeg is not found automatically:
  1. Go to **Tools → FFmpeg Settings...** in the application
  2. Click "Auto-Detect" to search for FFmpeg
  3. If auto-detect fails, click "Browse..." and manually select `ffmpeg.exe`
  4. The path will be saved automatically
- Ensure FFmpeg is properly installed and `ffmpeg.exe` exists at the specified location

### Camera Not Detected

- Ensure TIS Imaging Control 3.5 is properly installed
- Check that camera drivers are installed on the target laptop
- Verify camera permissions in Windows Settings

### Missing Native DLLs

- Ensure the `runtimes` folder and its contents are copied with the application
- The `runtimes\win-x64\native\` folder contains OpenCvSharp native libraries

---

## Quick Checklist

- [ ] .NET 8.0 Desktop Runtime installed
- [ ] TIS Imaging Control 3.5 installed
- [ ] FFmpeg installed (any location, or in PATH)
- [ ] FFmpeg path configured in application (Tools → FFmpeg Settings...) if not auto-detected
- [ ] All application files copied (entire `bin\Debug\net8.0-windows` folder)
- [ ] Application runs without errors

---

## Notes

- The application is configured for Windows Forms and requires Windows OS
- Camera functionality depends on TIS Imaging Control library
- Video processing requires FFmpeg (path is configurable via Tools → FFmpeg Settings...)
- The application will auto-detect FFmpeg in common installation locations and PATH
- All dependencies are included in the `bin\Debug\net8.0-windows` folder except for the system-level prerequisites listed above

