using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace DesktopSnap
{
    public static class PathService
    {
        private static readonly Dictionary<string, string> _tokens = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _inverseTokens = new(StringComparer.OrdinalIgnoreCase);
        
        // Cache sorted lists to avoid repeated sorting during runtime
        private static readonly List<KeyValuePair<string, string>> _sortedInverseTokens;
        private static readonly List<KeyValuePair<string, string>> _sortedTokens;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

        private static readonly Guid FolderDownloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");

        static PathService()
        {
            RegisterToken("{DESKTOP}", Environment.SpecialFolder.Desktop);
            RegisterToken("{PUBLIC_DESKTOP}", Environment.SpecialFolder.CommonDesktopDirectory);
            RegisterToken("{USERPROFILE}", Environment.SpecialFolder.UserProfile);
            RegisterToken("{PROGRAM_FILES}", Environment.SpecialFolder.ProgramFiles);
            RegisterToken("{PROGRAM_FILES_X86}", Environment.SpecialFolder.ProgramFilesX86);
            RegisterToken("{APPDATA}", Environment.SpecialFolder.ApplicationData);
            RegisterToken("{LOCALAPPDATA}", Environment.SpecialFolder.LocalApplicationData);
            RegisterToken("{WINDOWS}", Environment.SpecialFolder.Windows);
            RegisterToken("{SYSTEM}", Environment.SpecialFolder.System);
            RegisterToken("{DOCUMENTS}", Environment.SpecialFolder.MyDocuments);
            RegisterToken("{PICTURES}", Environment.SpecialFolder.MyPictures);
            RegisterToken("{VIDEOS}", Environment.SpecialFolder.MyVideos);
            RegisterToken("{MUSIC}", Environment.SpecialFolder.MyMusic);

            // Special handling for Downloads using Win32 KnownFolders
            try
            {
                IntPtr pszPath = IntPtr.Zero;
                try
                {
                    int hr = SHGetKnownFolderPath(FolderDownloads, 0, IntPtr.Zero, out pszPath);
                    if (hr >= 0 && pszPath != IntPtr.Zero)
                    {
                        string downloadsPath = Marshal.PtrToStringUni(pszPath);
                        if (!string.IsNullOrEmpty(downloadsPath) && Directory.Exists(downloadsPath))
                        {
                            string normalizedDownloads = downloadsPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                            _tokens["{DOWNLOADS}"] = normalizedDownloads;
                            _inverseTokens[normalizedDownloads] = "{DOWNLOADS}";
                        }
                    }
                }
                finally
                {
                    if (pszPath != IntPtr.Zero) Marshal.FreeCoTaskMem(pszPath);
                }
            }
            catch { }

            // Cache descending by length to ensure longest match
            _sortedInverseTokens = _inverseTokens.OrderByDescending(x => x.Key.Length).ToList();
            _sortedTokens = _tokens.OrderByDescending(x => x.Key.Length).ToList();
        }

        private static void RegisterToken(string token, Environment.SpecialFolder folder)
        {
            try
            {
                string path = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(path))
                {
                    string normalizedSource = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    _tokens[token] = normalizedSource;
                    _inverseTokens[normalizedSource] = token;
                }
            }
            catch { }
        }

        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            foreach (var kvp in _sortedInverseTokens)
            {
                if (path.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    // Ensure we only match full directory segments
                    if (path.Length == kvp.Key.Length || 
                        path[kvp.Key.Length] == Path.DirectorySeparatorChar || 
                        path[kvp.Key.Length] == Path.AltDirectorySeparatorChar)
                    {
                        return kvp.Value + path.Substring(kvp.Key.Length);
                    }
                }
            }

            return path;
        }

        public static string Denormalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            foreach (var kvp in _sortedTokens)
            {
                if (path.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    // Ensure we only match full directory segments
                    if (path.Length == kvp.Key.Length ||
                        path[kvp.Key.Length] == Path.DirectorySeparatorChar ||
                        path[kvp.Key.Length] == Path.AltDirectorySeparatorChar)
                    {
                        return kvp.Value + path.Substring(kvp.Key.Length);
                    }
                }
            }

            return path;
        }
    }
}
