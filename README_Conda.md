# Conda Environment Setup for QueenPix

## Important Note

This is a **C# .NET 8.0** project. Conda environments are typically used for Python projects, but this environment file is provided for convenience if you want to manage Python tooling alongside the project.

## Setup Instructions

### 1. Install .NET SDK 8.0 (Required)

The .NET SDK is **not available via conda** and must be installed separately:

- Download from: https://dotnet.microsoft.com/download/dotnet/8.0
- Install the **.NET 8.0 SDK** (not just the runtime)
- Verify installation: `dotnet --version` (should show 8.0.x)

### 2. Create Conda Environment (Optional)

If you want to use conda for Python tooling:

```bash
conda env create -f environment.yml
conda activate queenpix
```

### 3. Build the Project

```bash
# Restore NuGet packages
dotnet restore

# Build the project
dotnet build

# Run the application
dotnet run
```

## Project Dependencies

### Managed by .NET/NuGet (in `.csproj`):
- Microsoft.VisualBasic (10.0.0)
- OpenCvSharp4 (4.11.0.20250507)
- OpenCvSharp4.Extensions (4.11.0.20250507)
- OpenCvSharp4.runtime.win (4.11.0.20250507)

### External Dependencies (must be installed separately):
- **TIS.Imaging SDK** - Required for camera control
  - Install from the `Installers/` folder or download from TIS website
- **FFmpeg** - Included in `tools/` folder
- **Camera Drivers** - Install from manufacturer or `Installers/` folder

## Alternative: Standard .NET Development

For standard .NET development, you don't need conda. Just:

1. Install .NET SDK 8.0
2. Run `dotnet restore` to get NuGet packages
3. Run `dotnet build` to build
4. Run `dotnet run` to execute

The conda environment is only useful if you plan to add Python scripts or tooling to the project.