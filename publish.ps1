# PowerShell script to publish QueenPix as self-contained single-file executable

Write-Host "Publishing QueenPix..." -ForegroundColor Green

# Clean previous publish
if (Test-Path "./publish") {
    Write-Host "Cleaning previous publish folder..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force "./publish"
}

# Publish command
$publishCommand = @(
    "dotnet", "publish",
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:IncludeAllContentForSelfExtract=true",
    "-o", "./publish"
)

Write-Host "Running: $($publishCommand -join ' ')" -ForegroundColor Cyan
& $publishCommand[0] $publishCommand[1..($publishCommand.Length-1)]

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "Publish completed successfully!" -ForegroundColor Green
    Write-Host "Output location: $PWD\publish" -ForegroundColor Cyan
    
    # Check if TIS.Imaging DLLs exist in libs folder
    Write-Host ""
    Write-Host "TIS.Imaging dependencies:" -ForegroundColor Cyan
    if (Test-Path "./libs/TIS.Imaging.ICImagingControl35.dll") {
        Write-Host "  TIS.Imaging.ICImagingControl35.dll" -ForegroundColor Green
    } else {
        Write-Host "  TIS.Imaging.ICImagingControl35.dll NOT FOUND" -ForegroundColor Yellow
        Write-Host "    This is REQUIRED - copy it to ./libs/ folder" -ForegroundColor Yellow
    }
    
    if (Test-Path "./libs/tis_udshl12.dll") {
        Write-Host "  tis_udshl12.dll (native dependency)" -ForegroundColor Green
    } else {
        Write-Host "  tis_udshl12.dll NOT FOUND (recommended)" -ForegroundColor Yellow
        Write-Host "    May be needed for some cameras - copy from SDK bin folder" -ForegroundColor Yellow
    }
    
    $vcRuntimeDlls = @("msvcp140.dll", "msvcp140_1.dll", "vcruntime140.dll", "vcruntime140_1.dll")
    $vcRuntimeFound = $false
    foreach ($dll in $vcRuntimeDlls) {
        if (Test-Path "./libs/$dll") {
            $vcRuntimeFound = $true
            break
        }
    }
    if ($vcRuntimeFound) {
        Write-Host "  Visual C++ runtime DLLs found" -ForegroundColor Green
    } else {
        Write-Host "  Visual C++ runtime DLLs not included (usually OK - already on Windows)" -ForegroundColor Gray
    }
    
    # Check publish output
    $exePath = "./publish/QueenPix.exe"
    if (Test-Path $exePath) {
        $fileInfo = Get-Item $exePath
        $sizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
        Write-Host ""
        Write-Host "Executable size: $sizeMB MB" -ForegroundColor Cyan
    }
    
    # Check if FFmpeg tools exist
    if (Test-Path "./tools/ffmpeg.exe") {
        Write-Host ""
        Write-Host "FFmpeg tools found in tools folder:" -ForegroundColor Green
        if (Test-Path "./tools/ffprobe.exe") {
            Write-Host "  ffmpeg.exe" -ForegroundColor Green
            Write-Host "  ffprobe.exe" -ForegroundColor Green
        } else {
            Write-Host "  ffmpeg.exe" -ForegroundColor Green
            Write-Host "  ffprobe.exe NOT FOUND" -ForegroundColor Yellow
            Write-Host "    Warning: Application uses ffprobe.exe for video analysis!" -ForegroundColor Yellow
        }
    } else {
        Write-Host ""
        Write-Host "FFmpeg tools not found in tools folder (optional)" -ForegroundColor Yellow
    }
    
    # Copy FFmpeg executables if they exist in tools folder
    if (Test-Path "./tools/ffmpeg.exe") {
        Copy-Item "./tools/ffmpeg.exe" -Destination "./publish/" -Force
        Write-Host "  Copied ffmpeg.exe to publish folder" -ForegroundColor Green
    }
    if (Test-Path "./tools/ffprobe.exe") {
        Copy-Item "./tools/ffprobe.exe" -Destination "./publish/" -Force
        Write-Host "  Copied ffprobe.exe to publish folder" -ForegroundColor Green
    }
    
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "1. Test the executable on a clean Windows machine (without .NET 8.0 installed)" -ForegroundColor White
    Write-Host "2. Verify cameras are detected (may need camera drivers installed)" -ForegroundColor White
    Write-Host "3. Zip the publish folder contents for distribution" -ForegroundColor White
    Write-Host ""
    Write-Host "Distribution package ready in: $PWD\publish" -ForegroundColor Cyan
} else {
    Write-Host ""
    Write-Host "Publish failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}
