# Publish script for DesktopSnap

$projectFile = "DesktopSnap.csproj"
$projectName = "DesktopSnap"

# 1. Get version from csproj
Write-Host "Reading version from $projectFile..." -ForegroundColor Cyan
[xml]$projectXml = Get-Content $projectFile
$version = ($projectXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if ($version) { $version = $version.Trim() }
if (-not $version) {
    $version = "1.0.0"
    Write-Host "Version not found in csproj, using default: $version" -ForegroundColor Yellow
} else {
    Write-Host "Found version: $version" -ForegroundColor Green
}

# 2. Stop DesktopSnap if running
$process = Get-Process $projectName -ErrorAction SilentlyContinue
if ($process) {
    Write-Host "Stopping running instance of $projectName..." -ForegroundColor Yellow
    Stop-Process -Name $projectName -Force
    Start-Sleep -Seconds 1
}

# 3. Define paths
$publishDir = "bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
$zipName = "$projectName-v$version.zip"

# 3. Clean previous results
if (Test-Path $publishDir) {
    Write-Host "Cleaning old publish directory..." -ForegroundColor Gray
    Remove-Item -Recurse -Force $publishDir
}
if (Test-Path $zipName) {
    Write-Host "Removing old zip..." -ForegroundColor Gray
    Remove-Item -Force $zipName
}

# 4. Run dotnet publish
Write-Host "Starting build and publish..." -ForegroundColor Cyan
dotnet publish -f net8.0-windows10.0.19041.0 -c Release -r win-x64 --self-contained true -p:Platform=x64

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

# 5. Zip the output
Write-Host "Compressing to $zipName..." -ForegroundColor Cyan
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipName -Force

Write-Host "Done! Release package created: $zipName" -ForegroundColor Green
