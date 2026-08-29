# ==============================================================================
# DesktopSnap - 一键打包发布脚本 (Open Source Release Script)
# ==============================================================================
# 功能:
# 1. 自动同步版本号 (DesktopSnap.csproj, Package.appxmanifest Identity, desktopsnap-web)
# 2. 编译并打包便携版 (dist/DesktopSnap-vX.Y.Z.zip)
# 3. 编译并打包微软商店版 (dist/DesktopSnap_X.Y.Z.0_x64.msix)
# 4. 本地 Git 提交与打 Tag (绝对不自动向远端 push，保持安全可控)
# 5. 输出规范的 GitHub Release Notes 与微软商店发行说明
# ==============================================================================

param (
    [string]$Version = "1.0.5"
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
$projectFile = Join-Path $scriptDir "DesktopSnap.csproj"
$manifestFile = Join-Path $scriptDir "Package.appxmanifest"
$distDir = Join-Path $scriptDir "dist"
$webConfigPath = Join-Path (Join-Path $scriptDir "..") "desktopsnap-web\config.js"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "   DesktopSnap 自动化打包发布系统 - v$Version" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 1. 停止正在运行的 DesktopSnap
$process = Get-Process "DesktopSnap" -ErrorAction SilentlyContinue
if ($process) {
    Write-Host "[1/6] 正在停止运行中的 DesktopSnap 进程..." -ForegroundColor Yellow
    Stop-Process -Name "DesktopSnap" -Force
    Start-Sleep -Seconds 1
} else {
    Write-Host "[1/6] 未发现运行中的 DesktopSnap 进程。" -ForegroundColor Gray
}

# 2. 同步与校验版本号
Write-Host "[2/6] 正在同步版本号到项目文件与清单..." -ForegroundColor Cyan
$fourPartVersion = "$Version.0"

# 2.1 更新 DesktopSnap.csproj
$csprojContent = [System.IO.File]::ReadAllText($projectFile, [System.Text.Encoding]::UTF8)
$csprojContent = [regex]::Replace($csprojContent, '<Version>[^<]+</Version>', "<Version>$Version</Version>")
[System.IO.File]::WriteAllText($projectFile, $csprojContent, [System.Text.Encoding]::UTF8)
Write-Host "  -> DesktopSnap.csproj: $Version" -ForegroundColor Green

# 2.2 更新 Package.appxmanifest (注意：仅替换 <Identity> 中的 Version，严禁修改 TargetDeviceFamily 的 MinVersion)
$manifestContent = [System.IO.File]::ReadAllText($manifestFile, [System.Text.Encoding]::UTF8)
$manifestContent = [regex]::Replace($manifestContent, '(?<=<Identity[^>]*\bVersion=")[0-9\.]+(?=")', $fourPartVersion)
[System.IO.File]::WriteAllText($manifestFile, $manifestContent, [System.Text.Encoding]::UTF8)
Write-Host "  -> Package.appxmanifest Identity Version: $fourPartVersion" -ForegroundColor Green

# 3. 准备输出目录
if (Test-Path $distDir) {
    Remove-Item -Recurse -Force $distDir
}
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

$publishTempDir = Join-Path $scriptDir "bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
if (Test-Path $publishTempDir) {
    Remove-Item -Recurse -Force $publishTempDir
}

# 4. 构建便携版 ZIP (Portable ZIP)
Write-Host "[3/6] 正在编译并打包便携版 (Portable ZIP)..." -ForegroundColor Cyan
$publishProfile = Join-Path $scriptDir "Properties\PublishProfiles\win-x64.pubxml"
dotnet publish $projectFile -c Release -p:Platform=x64 -p:PublishProfile=$publishProfile

if ($LASTEXITCODE -ne 0) {
    Write-Host "便携版构建失败！" -ForegroundColor Red
    exit $LASTEXITCODE
}

$zipFileName = "DesktopSnap-v$Version.zip"
$zipTargetPath = Join-Path $distDir $zipFileName
Write-Host "  -> 正在压缩输出文件到: $zipTargetPath" -ForegroundColor Gray
Compress-Archive -Path "$publishTempDir\*" -DestinationPath $zipTargetPath -CompressionLevel Optimal -Force

$zipSizeMb = [math]::Round(((Get-Item $zipTargetPath).Length / 1MB), 1)
$zipSizeStr = "$zipSizeMb MB"
Write-Host "  -> 便携版打包完成! 大小: $zipSizeStr" -ForegroundColor Green

# 5. 构建微软商店安装包 (MSIX)
Write-Host "[4/6] 正在编译并打包微软商店包 (MSIX)..." -ForegroundColor Cyan
$appPackagesTempDir = Join-Path $scriptDir "AppPackages"
if (Test-Path $appPackagesTempDir) {
    Remove-Item -Recurse -Force $appPackagesTempDir
}

dotnet publish $projectFile -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:AppxPackageDir="$appPackagesTempDir\"

if ($LASTEXITCODE -ne 0) {
    Write-Host "微软商店包构建失败！" -ForegroundColor Red
    exit $LASTEXITCODE
}

$msixFile = Get-ChildItem -Path $appPackagesTempDir -Filter "*.msix" -Recurse | Select-Object -First 1
if ($msixFile) {
    $msixTargetName = "DesktopSnap_${fourPartVersion}_x64.msix"
    $msixTargetPath = Join-Path $distDir $msixTargetName
    Copy-Item $msixFile.FullName -Destination $msixTargetPath -Force
    $msixSizeMb = [math]::Round(((Get-Item $msixTargetPath).Length / 1MB), 1)
    Write-Host "  -> 微软商店包打包完成! $msixTargetName ($msixSizeMb MB)" -ForegroundColor Green
} else {
    Write-Host "  [Warning] 未在 AppPackages 中找到生成的 .msix 文件。" -ForegroundColor Yellow
}

# 清理临时 AppPackages 文件夹
if (Test-Path $appPackagesTempDir) {
    Remove-Item -Recurse -Force $appPackagesTempDir
}

# 6. 同步更新官网配置 (desktopsnap-web)
Write-Host "[5/6] 正在检查并更新官网配置 (desktopsnap-web)..." -ForegroundColor Cyan
if (Test-Path $webConfigPath) {
    $webConfig = [System.IO.File]::ReadAllText($webConfigPath, [System.Text.Encoding]::UTF8)
    $webConfig = [regex]::Replace($webConfig, 'version:\s*"[^"]+"', "version: ""v$Version""")
    $webConfig = [regex]::Replace($webConfig, 'file_size:\s*"[^"]+"', "file_size: ""$zipSizeStr""")
    [System.IO.File]::WriteAllText($webConfigPath, $webConfig, [System.Text.Encoding]::UTF8)
    Write-Host "  -> 已更新 desktopsnap-web/config.js (版本: v$Version, 大小: $zipSizeStr)" -ForegroundColor Green
    
    # 本地提交 Web 仓库
    $webRepoDir = Split-Path $webConfigPath -Parent
    git -C $webRepoDir add config.js
    $webStatus = git -C $webRepoDir status --porcelain
    if ($webStatus) {
        git -C $webRepoDir commit -m "chore: release v$Version" -q
        Write-Host "  -> desktopsnap-web 本地 Git commit 已创建。" -ForegroundColor Gray
    }
} else {
    Write-Host "  -> 未检测到相邻的 desktopsnap-web 目录，跳过官网更新。" -ForegroundColor Gray
}

# 7. 本地 Tag (桌面端应用仓库)
Write-Host "[6/6] 正在创建本地 Tag (v$Version)..." -ForegroundColor Cyan
$existingTag = git -C $scriptDir tag -l "v$Version"
if ($existingTag) {
    git -C $scriptDir tag -d "v$Version" | Out-Null
}
git -C $scriptDir tag -a "v$Version" -m "Release v$Version"
Write-Host "  -> desktopsnap 本地 Tag (v$Version) 创建成功！" -ForegroundColor Green

Write-Host "`n==================================================" -ForegroundColor Green
Write-Host "   🎉 发布构建完成！所有产物已输出到 dist/ 目录" -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Green
Write-Host "📦 便携版安装包: $distDir\$zipFileName" -ForegroundColor White
if ($msixFile) {
    Write-Host "🛍️ 微软商店安装包: $distDir\$msixTargetName" -ForegroundColor White
}
Write-Host "`n📋 后续手动步骤建议:" -ForegroundColor Yellow
Write-Host "1. 推送代码与标签 (手动执行):" -ForegroundColor Cyan
Write-Host "   git push origin dev --tags" -ForegroundColor Gray
Write-Host "   git -C ..\desktopsnap-web push origin main" -ForegroundColor Gray
Write-Host "2. 上传便携版 ZIP ($zipFileName):" -ForegroundColor Cyan
Write-Host "   -> 上传到 OSS: https://oss.liuhouliang.com/packages/desktopsnap/" -ForegroundColor Gray
Write-Host "   -> 在 GitHub 发布 Release (v$Version) 并附带此 ZIP" -ForegroundColor Gray
Write-Host "3. 上传微软商店包:" -ForegroundColor Cyan
Write-Host "   -> 登录 Microsoft Partner Center，上传 $msixTargetName 提交审核" -ForegroundColor Gray
