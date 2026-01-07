# Script to create lab distribution package
# Usage: .\create-lab-package.ps1

$packageName = "QueenPix_Lab_v1.0"
$distFolder = "QueenPix"

Write-Host "Creating lab distribution package..." -ForegroundColor Green

# Clean previous package
if (Test-Path $distFolder) {
    Write-Host "Cleaning previous distribution folder..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $distFolder
}

# Create folder structure
Write-Host "Creating folder structure..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path "$distFolder\QueenPix" -Force | Out-Null
New-Item -ItemType Directory -Path "$distFolder\Installers" -Force | Out-Null

# Check if publish folder exists
if (-not (Test-Path "publish")) {
    Write-Host ""
    Write-Host "ERROR: publish folder not found!" -ForegroundColor Red
    Write-Host "Please run .\publish.ps1 first to build the application." -ForegroundColor Yellow
    exit 1
}

# Copy application files
Write-Host "Copying application files..." -ForegroundColor Cyan
$exeFiles = Get-ChildItem "publish\*.exe" -Exclude "*.pdb"
if ($exeFiles.Count -eq 0) {
    Write-Host "ERROR: No .exe files found in publish folder!" -ForegroundColor Red
    exit 1
}

foreach ($file in $exeFiles) {
    Copy-Item $file.FullName -Destination "$distFolder\QueenPix\"
    Write-Host "  Copied $($file.Name)" -ForegroundColor Green
}

# Copy FFmpeg tools if they exist
if (Test-Path "publish\ffmpeg.exe") {
    Copy-Item "publish\ffmpeg.exe" -Destination "$distFolder\QueenPix\" -Force
    Write-Host "  Copied ffmpeg.exe" -ForegroundColor Green
}
if (Test-Path "publish\ffprobe.exe") {
    Copy-Item "publish\ffprobe.exe" -Destination "$distFolder\QueenPix\" -Force
    Write-Host "  Copied ffprobe.exe" -ForegroundColor Green
}

# Copy documentation
if (Test-Path "LAB_DISTRIBUTION_README.md") {
    Copy-Item "LAB_DISTRIBUTION_README.md" -Destination "$distFolder\README.md"
    Write-Host "  Copied README.md" -ForegroundColor Green
} else {
    Write-Host "  LAB_DISTRIBUTION_README.md not found" -ForegroundColor Yellow
}

# Check for SDK installer
if (Test-Path "Installers\TIS_Imaging_SDK_Installer.exe") {
    Copy-Item "Installers\TIS_Imaging_SDK_Installer.exe" -Destination "$distFolder\Installers\"
    Write-Host "  TIS.Imaging SDK installer included" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "TIS.Imaging SDK installer not found!" -ForegroundColor Yellow
    Write-Host "  Expected location: Installers\TIS_Imaging_SDK_Installer.exe" -ForegroundColor Yellow
    Write-Host "  Download from: https://www.theimagingsource.com/support/downloads/" -ForegroundColor Yellow
    Write-Host "  Save the installer to: Installers\TIS_Imaging_SDK_Installer.exe" -ForegroundColor Yellow
    Write-Host "  Then run this script again." -ForegroundColor Yellow
}

# Check for camera driver installers (optional)
# First, check for driver folders (containing installer + data folder)
$driverFolders = Get-ChildItem "Installers\" -Directory -ErrorAction SilentlyContinue | Where-Object {
    $folder = $_
    # Check if folder contains an installer executable and a data folder
    $hasInstaller = (Get-ChildItem $folder.FullName -Filter "*.exe" -ErrorAction SilentlyContinue).Count -gt 0
    $hasDataFolder = Test-Path (Join-Path $folder.FullName "data")
    return $hasInstaller -and $hasDataFolder
}

if ($driverFolders.Count -gt 0) {
    Write-Host ""
    Write-Host "Camera driver folders found (with installer + data):" -ForegroundColor Cyan
    foreach ($folder in $driverFolders) {
        Copy-Item $folder.FullName -Destination "$distFolder\Installers\" -Recurse -Force
        Write-Host "  Copied driver folder: $($folder.Name)" -ForegroundColor Green
    }
}

# Also check for standalone driver installer .exe files
$driverFiles = Get-ChildItem "Installers\*.exe" -Exclude "TIS_Imaging_SDK_Installer.exe" -ErrorAction SilentlyContinue | Where-Object {
    # Exclude .exe files that are inside driver folders (already copied above)
    $file = $_
    $parentFolder = $file.Directory.Name
    $isInDriverFolder = $driverFolders | Where-Object { $_.Name -eq $parentFolder }
    return $null -eq $isInDriverFolder
}

if ($driverFiles.Count -gt 0) {
    Write-Host ""
    Write-Host "Standalone camera driver installers found:" -ForegroundColor Cyan
    foreach ($file in $driverFiles) {
        Copy-Item $file.FullName -Destination "$distFolder\Installers\"
        Write-Host "  Copied driver installer: $($file.Name)" -ForegroundColor Green
    }
}

if ($driverFolders.Count -eq 0 -and $driverFiles.Count -eq 0) {
    Write-Host ""
    Write-Host "No camera driver installers found (optional)" -ForegroundColor Gray
    Write-Host "  To include driver installers:" -ForegroundColor Gray
    Write-Host "  - Place entire driver folders (with installer + data) in: Installers\" -ForegroundColor Gray
    Write-Host "  - Or place standalone installer .exe files in: Installers\" -ForegroundColor Gray
}

# Create ZIP
Write-Host ""
Write-Host "Creating ZIP package..." -ForegroundColor Cyan
if (Test-Path "$packageName.zip") {
    Remove-Item "$packageName.zip" -Force
    Write-Host "  Removed existing ZIP file" -ForegroundColor Yellow
}

# Create ZIP with proper folder structure
$tempZip = "$packageName.tmp.zip"
if (Test-Path $tempZip) { Remove-Item $tempZip -Force }
Compress-Archive -Path "$distFolder" -DestinationPath $tempZip -CompressionLevel Optimal
Move-Item -Path $tempZip -Destination "$packageName.zip" -Force

# Calculate package size
$zipSize = (Get-Item "$packageName.zip").Length / 1MB
Write-Host ""
Write-Host "Package created successfully!" -ForegroundColor Green
Write-Host "  File: $packageName.zip" -ForegroundColor Cyan
Write-Host "  Size: $([math]::Round($zipSize, 2)) MB" -ForegroundColor Cyan
Write-Host ""
Write-Host "Package contents:" -ForegroundColor Yellow
Get-ChildItem -Recurse $distFolder | Select-Object FullName | ForEach-Object {
    $relativePath = $_.FullName.Replace((Resolve-Path $distFolder).Path + "\", "")
    Write-Host "  - $relativePath" -ForegroundColor White
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Test the package on a clean lab computer" -ForegroundColor White
Write-Host "2. Distribute the ZIP file to lab users" -ForegroundColor White
Write-Host "3. Provide installation instructions (see README.md in package)" -ForegroundColor White
Write-Host ""
Write-Host "Note: GenTL Producer/IC4 Drivers are NOT needed for USB cameras with IC3.5 SDK" -ForegroundColor Gray
