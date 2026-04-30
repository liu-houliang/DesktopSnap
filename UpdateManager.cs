using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DesktopSnap
{
    public class UpdateInfo
    {
        public string Version { get; set; }
        public string ReleaseNotes { get; set; }
        public string DownloadUrl { get; set; }
        public bool IsNewer { get; set; }
    }

    public static class UpdateManager
    {
        private const string RepoOwner = "liu-houliang";
        private const string RepoName = "DesktopSnap";
        private const string GitHubApiUrl = "https://api.github.com/repos/liu-houliang/DesktopSnap/releases/latest";

        public static async Task<UpdateInfo> CheckForUpdateAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"DesktopSnap/{I18n.Instance.AppVersion}");
                
                var response = await client.GetStringAsync(GitHubApiUrl);
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                var latestVersionTag = root.GetProperty("tag_name").GetString()?.TrimStart('v');
                var currentVersion = I18n.Instance.AppVersion;

                if (string.IsNullOrEmpty(latestVersionTag)) return null;

                bool isNewer = IsVersionNewer(currentVersion, latestVersionTag);

                var assets = root.GetProperty("assets").EnumerateArray();
                var zipAsset = assets.FirstOrDefault(a => a.GetProperty("name").GetString().EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

                return new UpdateInfo
                {
                    Version = latestVersionTag,
                    ReleaseNotes = root.GetProperty("body").GetString(),
                    DownloadUrl = zipAsset.ValueKind != JsonValueKind.Undefined ? $"https://oss.liuhouliang.com/packages/desktopsnap/DesktopSnap-v{latestVersionTag}.zip" : null,
                    IsNewer = isNewer
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update check failed: {ex.Message}");
                return null;
            }
        }

        public static async Task<UpdateInfo> CheckStoreUpdateAsync(IntPtr hwnd)
        {
            try
            {
                var context = Windows.Services.Store.StoreContext.GetDefault();
                WinRT.Interop.InitializeWithWindow.Initialize(context, hwnd);

                var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
                if (updates == null || updates.Count == 0)
                    return null;

                // Get the current app package's identity to filter for the main package (avoiding optional packages)
                var currentPackage = Windows.ApplicationModel.Package.Current;
                var currentFamilyName = currentPackage.Id.FamilyName;

                // Find the package in the update list that matches the current app
                var mainUpdate = updates.FirstOrDefault(u =>
                    string.Equals(u.Package.Id.FamilyName, currentFamilyName, StringComparison.OrdinalIgnoreCase));

                if (mainUpdate == null)
                    return null;

                // Extract the version number
                var v = mainUpdate.Package.Id.Version;
                string latestVersion = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";

                // Explicitly compare versions: only treat as an update if the server version is newer
                string currentVersion = I18n.Instance.AppVersion;
                bool isNewer = IsVersionNewer(currentVersion, latestVersion);

                if (!isNewer)
                    return null;

                return new UpdateInfo
                {
                    Version = latestVersion,
                    IsNewer = true,
                    ReleaseNotes = I18n.Instance.StoreUpdateNotes,
                    DownloadUrl = null   // Store updates don't require a direct download URL
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Store update check failed: {ex.Message}");
                return null;
            }
        }

        private static bool IsVersionNewer(string current, string latest)
        {
            if (Version.TryParse(current, out var currentV) && Version.TryParse(latest, out var latestV))
            {
                return latestV > currentV;
            }
            return false;
        }

        public static async Task<string> DownloadUpdateAsync(string url, Action<double> progressCallback)
        {
            if (string.IsNullOrEmpty(url)) return null;
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(300); // Longer timeout for downloads
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"DesktopSnap/{I18n.Instance.AppVersion}");
                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var tempFile = Path.Combine(Path.GetTempPath(), $"DesktopSnap_Update_{Guid.NewGuid():N}.zip");

                using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                using var downloadStream = await response.Content.ReadAsStreamAsync();

                var buffer = new byte[8192];
                var totalRead = 0L;
                int bytesRead;

                while ((bytesRead = await downloadStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    if (totalBytes != -1)
                    {
                        progressCallback?.Invoke((double)totalRead / totalBytes);
                    }
                }

                return tempFile;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Download failed: {ex.Message}");
                return null;
            }
        }

        public static void ApplyUpdatePortable(string zipPath)
        {
            if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
            {
                Debug.WriteLine("Invalid zip path for update.");
                return;
            }

            try
            {
                var appDir = AppContext.BaseDirectory;
                var tempExtractDir = Path.Combine(Path.GetTempPath(), $"DesktopSnap_Extracted_{Guid.NewGuid():N}");
                
                if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true);
                Directory.CreateDirectory(tempExtractDir);
                
                ZipFile.ExtractToDirectory(zipPath, tempExtractDir);

                // Basic integrity check: Ensure the exe exists in the extracted files
                var exeName = Process.GetCurrentProcess().MainModule.ModuleName;
                var extractedExe = Path.Combine(tempExtractDir, exeName);
                if (!File.Exists(extractedExe))
                {
                    Debug.WriteLine("Extracted package is missing the executable.");
                    return;
                }

                var batchPath = Path.Combine(Path.GetTempPath(), "DesktopSnap_Update.bat");
                var exePath = Path.Combine(appDir, exeName);

                // Use parameters (%~1, %~2...) instead of string interpolation to prevent injection
                var script = @"
@echo off
chcp 65001 >nul
setlocal
set ""APP_DIR=%~1""
set ""TEMP_DIR=%~2""
set ""EXE_PATH=%~3""
set ""EXE_NAME=%~4""
set ""BACKUP_DIR=%TEMP%\DesktopSnap_Backup_%RANDOM%""

echo Waiting for %EXE_NAME% to exit...
:wait
tasklist /FI ""IMAGENAME eq %EXE_NAME%"" 2>NUL | find /I /N ""%EXE_NAME%"">NUL
if ""%ERRORLEVEL%""==""0"" (
    timeout /t 1 /nobreak >nul
    goto wait
)

echo Backing up current version...
mkdir ""%BACKUP_DIR%""
xcopy ""%APP_DIR%\*"" ""%BACKUP_DIR%"" /S /Y /Q /E

echo Replacing files...
xcopy ""%TEMP_DIR%\*"" ""%APP_DIR%"" /S /Y /Q /E
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
    echo !! UPDATE FAILED! Rolling back to previous version !!
    echo !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
    echo.
    xcopy ""%BACKUP_DIR%\*"" ""%APP_DIR%"" /S /Y /Q /E
    pause
) else (
    echo Update success, cleaning up...
    rd /S /Q ""%BACKUP_DIR%""
)

echo Cleaning up temp files...
rd /S /Q ""%TEMP_DIR%""

echo Restarting app...
start """" ""%EXE_PATH%""

echo Done.
del ""%~f0""
";
                File.WriteAllText(batchPath, script, Encoding.UTF8);

                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                // Safely pass arguments using ArgumentList
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add(batchPath);
                startInfo.ArgumentList.Add(appDir);
                startInfo.ArgumentList.Add(tempExtractDir);
                startInfo.ArgumentList.Add(exePath);
                startInfo.ArgumentList.Add(exeName);

                Process.Start(startInfo);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Apply update failed: {ex.Message}");
            }
        }
    }
}
