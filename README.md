# MultiCamRecorder

A Windows Forms application for recording video from multiple cameras simultaneously using TIS Imaging Control and OpenCV.

## Features

- Multi-camera recording support
- Configurable camera settings per device
- Video recording with customizable frame rates
- Timelapse video creation
- Video trimming and conversion
- Configurable FFmpeg path (auto-detection supported)
- Camera name profiles for easy management

## Requirements

- **Operating System**: Windows 10/11 (64-bit)
- **.NET Runtime**: .NET 8.0 Desktop Runtime
- **TIS Imaging Control**: IC Imaging Control 3.5 .NET Library
- **FFmpeg**: FFmpeg 8.0 (or compatible version)

## Installation

See [DEPLOYMENT_GUIDE.md](DEPLOYMENT_GUIDE.md) for detailed installation and deployment instructions.

### Quick Start

1. Install .NET 8.0 Desktop Runtime from [Microsoft](https://dotnet.microsoft.com/download/dotnet/8.0)
2. Install TIS Imaging Control 3.5 from [The Imaging Source](https://www.theimagingsource.com/support/downloads/)
3. Install FFmpeg (any location, or add to PATH)
4. Clone this repository
5. Build the project:
   ```bash
   dotnet restore
   dotnet build
   ```
6. Run the application:
   ```bash
   dotnet run
   ```
   Or run `MultiCamRecorder.exe` from `bin\Debug\net8.0-windows\`

## Configuration

### FFmpeg Path

The application can auto-detect FFmpeg, or you can configure it manually:
1. Go to **Tools → FFmpeg Settings...**
2. Click "Auto-Detect" or "Browse..." to set the path
3. The path will be saved automatically

### Camera Settings

Configure individual camera settings through the camera settings dialog accessible from each camera preview.

## Building from Source

```bash
# Navigate to the project folder (if you cloned the repo, it's in MultiCamRecorder subfolder)
cd MultiCamRecorder

# Restore NuGet packages
dotnet restore

# Build the project
dotnet build

# Run the application
dotnet run
```

**Note**: If you're in a folder with multiple projects, specify the project file:
```bash
dotnet build MultiCamRecorder.csproj
dotnet run --project MultiCamRecorder.csproj
```

## Project Structure

- `Form1.cs` - Main application form and logic
- `CameraPreviewControl.cs` - Camera preview control component
- `CameraSettings.cs` - Camera settings data model
- `CameraSettingsDialog.cs` - Camera configuration dialog
- `VideoTrimDialog.cs` - Video trimming dialog
- `TimelapseStopDialog.cs` - Timelapse configuration dialog
- `FrameSelectionDialog.cs` - Frame selection dialog

## Dependencies

- **OpenCvSharp4** (v4.11.0.20250507) - Computer vision library
- **TIS.Imaging.ICImagingControl35** - Camera control library
- **.NET 8.0 Windows Forms** - UI framework

## License

[Add your license here]

## Author

[Add your name/contact information here]


