#pragma warning disable CS0649
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace DesktopSnap
{
    public class IconInfo
    {
        public string Name { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public string FilePath { get; set; }           // Full path to file on desktop
        public string ShortcutTarget { get; set; }     // For .lnk: the target exe/folder path
        public string ShortcutArgs { get; set; }       // For .lnk: command line arguments
        public string ShortcutIconLocation { get; set; } // For .lnk: icon path
        public string ShortcutWorkingDir { get; set; }   // For .lnk: working directory

        [JsonIgnore]
        public int ImageIndex { get; set; } = -1;
        [JsonIgnore]
        public bool HasShortcutOverlay { get; set; }
    }

    public class RestoreResult
    {
        public int Repositioned { get; set; }
        public int Recreated { get; set; }
        public List<string> MissingFiles { get; set; } = new List<string>();
        public int ExtraIcons { get; set; }  // Icons on desktop not in snapshot
        public bool AutoArrangeEnabled { get; set; } // Detected auto-arrange state
    }

    public static class DesktopIconManager
    {
        private static readonly object _logLock = new object();
        private static System.Text.StringBuilder _logBuilder = new System.Text.StringBuilder();

        public static string LastLog
        {
            get { lock (_logLock) return _logBuilder.ToString(); }
        }

        private static void AppendLog(string msg)
        {
            lock (_logLock) _logBuilder.Append(msg);
        }

        private static void ClearLog()
        {
            lock (_logLock) _logBuilder.Clear();
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

        [DllImport("kernel32.dll")]
        static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, int nSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        const uint LVM_GETITEMCOUNT = 0x1004;
        const uint LVM_GETITEMTEXTW = 0x1073;
        const uint LVM_GETITEMPOSITION = 0x1010;
        const uint LVM_SETITEMPOSITION = 0x100F;
        const uint LVM_SETITEMPOSITION32 = 0x1031;
        const uint LVM_ARRANGE = 0x1016;
        const uint LVM_REDRAWITEMS = 0x1015;
        const uint LVM_UPDATE = 0x102A;
        const uint LVM_GETITEMW = 0x104B;
        const int GWL_STYLE = -16;
        const int LVS_AUTOARRANGE = 0x0100;
        
        const uint LVIF_TEXT = 0x0001;
        const uint LVIF_IMAGE = 0x0002;
        const uint LVIF_STATE = 0x0008;
        const uint LVIS_OVERLAYMASK = 0x0F00;

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        const uint SHGFI_SYSICONINDEX = 0x000004000;
        const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

        const uint PROCESS_VM_OPERATION = 0x0008;
        const uint PROCESS_VM_READ = 0x0010;
        const uint PROCESS_VM_WRITE = 0x0020;

        const uint MEM_COMMIT = 0x1000;
        const uint MEM_RELEASE = 0x8000;
        const uint PAGE_READWRITE = 0x04;

        [DllImport("user32.dll", SetLastError = true)]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        
        [DllImport("user32.dll")]
        static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        struct POINT
        {
            public int x;
            public int y;
        }

        struct LVITEMW
        {
            public uint mask;
            public int iItem;
            public int iSubItem;
            public uint state;
            public uint stateMask;
            public IntPtr pszText;
            public int cchTextMax;
            public int iImage;
            public IntPtr lParam;
            public int iIndent;
            public int iGroupId;
            public uint cColumns;
            public IntPtr puColumns;
            public IntPtr piColFmt;
            public int iGroup;
        }

        private static IntPtr GetDesktopListView()
        {
            IntPtr listView = IntPtr.Zero;
            
            IntPtr progman = FindWindow("Progman", null);
            IntPtr shelldll = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            IntPtr lv = FindWindowEx(shelldll, IntPtr.Zero, "SysListView32", null);

            int progmanCount = 0;
            if (lv != IntPtr.Zero)
            {
                progmanCount = (int)SendMessage(lv, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
                AppendLog($"Found Progman SysListView32: {lv} (Items: {progmanCount})\n");
                if (progmanCount > 0)
                {
                    return lv;
                }
            }

            // Fallback to WorkerW
            EnumWindows((hwnd, lParam) =>
            {
                IntPtr p = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (p != IntPtr.Zero)
                {
                    IntPtr tempLv = FindWindowEx(p, IntPtr.Zero, "SysListView32", null);
                    if (tempLv != IntPtr.Zero)
                    {
                        int count = (int)SendMessage(tempLv, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
                        AppendLog($"Found WorkerW ({hwnd}) SysListView32: {tempLv} (Items: {count})\n");
                        if (count > 0)
                        {
                            listView = tempLv;
                            return false; // Found the active one, stop iterating
                        }
                        if (listView == IntPtr.Zero)
                        {
                            listView = tempLv; // Keep it as a backup
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            if (listView != IntPtr.Zero)
            {
                AppendLog($"Selected WorkerW SysListView32: {listView}\n");
                return listView;
            }

            AppendLog($"Selected Progman SysListView32: {lv}\n");
            return lv; // Return whatever Progman had, even if 0
        }

        public static List<IconInfo> GetIcons()
        {
            ClearLog();
            var icons = new List<IconInfo>();
            IntPtr listView = GetDesktopListView();
            
            if (listView == IntPtr.Zero)
            {
                AppendLog("Error: Could not find any SysListView32 handle for desktop.\n");
                return icons;
            }

            GetWindowThreadProcessId(listView, out uint pid);
            AppendLog($"Target Process ID: {pid}\n");
            
            IntPtr process = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
            if (process == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                AppendLog($"Error: OpenProcess failed with code {err}\n");
                return icons;
            }

            int count = (int)SendMessage(listView, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            AppendLog($"Action Get: Count verified: {count}\n");
            if (count == 0)
            {
                CloseHandle(process);
                return icons;
            }

            uint pointSize = (uint)Marshal.SizeOf(typeof(POINT));
            uint itemSize = (uint)Marshal.SizeOf(typeof(LVITEMW));
            uint stringBufSize = 512;

            IntPtr pPointStr = IntPtr.Zero;
            try
            {
                pPointStr = VirtualAllocEx(process, IntPtr.Zero, pointSize + itemSize + stringBufSize, MEM_COMMIT, PAGE_READWRITE);
                
                // Safety: if allocation fails, abort cleanly rather than computing addresses from IntPtr.Zero
                if (pPointStr == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    AppendLog($"Error: VirtualAllocEx failed with code {err}\n");
                    CloseHandle(process);
                    return icons;
                }

                IntPtr ptAddress = pPointStr;
                IntPtr itemAddress = pPointStr + (int)pointSize;
                IntPtr strAddress = pPointStr + (int)pointSize + (int)itemSize;

                for (int i = 0; i < count; i++)
                {
                    // Position
                    SendMessage(listView, LVM_GETITEMPOSITION, (IntPtr)i, ptAddress);
                    int pointStructSize = Marshal.SizeOf(typeof(POINT));
                    IntPtr localPt = Marshal.AllocHGlobal(pointStructSize);
                    POINT pt;
                    try
                    {
                        if (!ReadProcessMemory(process, ptAddress, localPt, pointStructSize, out _)) continue;
                        pt = Marshal.PtrToStructure<POINT>(localPt);
                    }
                    finally { Marshal.FreeHGlobal(localPt); }

                    // Text and State (Architecture safe struct injection)
                    byte[] itemBytes;
                    int itemStructSize;

                    if (Environment.Is64BitOperatingSystem)
                    {
                        itemStructSize = 88;
                        itemBytes = new byte[88];
                        BitConverter.GetBytes(LVIF_TEXT | LVIF_IMAGE | LVIF_STATE).CopyTo(itemBytes, 0); // mask
                        BitConverter.GetBytes(i).CopyTo(itemBytes, 4); // iItem
                        BitConverter.GetBytes(LVIS_OVERLAYMASK).CopyTo(itemBytes, 16); // stateMask
                        BitConverter.GetBytes(strAddress.ToInt64()).CopyTo(itemBytes, 24); // pszText
                        BitConverter.GetBytes(255).CopyTo(itemBytes, 32); // cchTextMax
                    }
                    else
                    {
                        itemStructSize = 60;
                        itemBytes = new byte[60];
                        BitConverter.GetBytes(LVIF_TEXT | LVIF_IMAGE | LVIF_STATE).CopyTo(itemBytes, 0); // mask
                        BitConverter.GetBytes(i).CopyTo(itemBytes, 4); // iItem
                        BitConverter.GetBytes(LVIS_OVERLAYMASK).CopyTo(itemBytes, 16); // stateMask
                        BitConverter.GetBytes(strAddress.ToInt32()).CopyTo(itemBytes, 20); // pszText
                        BitConverter.GetBytes(255).CopyTo(itemBytes, 24); // cchTextMax
                    }

                    IntPtr localItem = Marshal.AllocHGlobal(itemStructSize);
                    try
                    {
                        Marshal.Copy(itemBytes, 0, localItem, itemStructSize);
                        if (!WriteProcessMemory(process, itemAddress, localItem, itemStructSize, out _)) continue;
                        SendMessage(listView, LVM_GETITEMW, (IntPtr)0, itemAddress);
                        
                        // Read back to get iImage and state
                        if (!ReadProcessMemory(process, itemAddress, localItem, itemStructSize, out _)) continue;
                        Marshal.Copy(localItem, itemBytes, 0, itemStructSize);
                    }
                    finally { Marshal.FreeHGlobal(localItem); }

                    IntPtr localStr = Marshal.AllocHGlobal((int)stringBufSize);
                    string name;
                    try
                    {
                        if (!ReadProcessMemory(process, strAddress, localStr, (int)stringBufSize, out _)) continue;
                        name = Marshal.PtrToStringUni(localStr);
                    }
                    finally { Marshal.FreeHGlobal(localStr); }

                    int imageIndex = -1;
                    uint state = 0;
                    if (Environment.Is64BitOperatingSystem)
                    {
                        state = BitConverter.ToUInt32(itemBytes, 12);
                        imageIndex = BitConverter.ToInt32(itemBytes, 36);
                    }
                    else
                    {
                        state = BitConverter.ToUInt32(itemBytes, 12);
                        imageIndex = BitConverter.ToInt32(itemBytes, 28);
                    }
                    
                    bool hasShortcutOverlay = (state & LVIS_OVERLAYMASK) != 0;

                    if (!string.IsNullOrEmpty(name))
                    {
                        icons.Add(new IconInfo { 
                            Name = name, 
                            X = pt.x, 
                            Y = pt.y, 
                            ImageIndex = imageIndex,
                            HasShortcutOverlay = hasShortcutOverlay
                        });
                    }
                }
            }
            finally
            {
                // Always release remote memory and process handle, even on exception
                if (pPointStr != IntPtr.Zero) VirtualFreeEx(process, pPointStr, 0, MEM_RELEASE);
                CloseHandle(process);
            }

            AppendLog($"GetIcons completion: Valid names retrieved: {icons.Count}\n");

            // Resolve file paths and shortcut metadata
            ResolveFilePaths(icons);

            return icons;
        }

        private static void ResolveFilePaths(List<IconInfo> icons)
        {
            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

            // Build lookup table: DisplayName -> List of full paths
            var fileLookup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            ScanDesktopDir(userDesktop, fileLookup);
            ScanDesktopDir(publicDesktop, fileLookup);

            // Single COM instance for all shortcut reads
            dynamic shell = null;
            Type shellType = null;
            try
            {
                shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null) shell = Activator.CreateInstance(shellType);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"DesktopIconManager Error: {ex}"); }

            var claimedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // STAGE 1: Perfect matches (Name + Shortcut State + Icon Index)
            foreach (var icon in icons)
            {
                if (fileLookup.TryGetValue(icon.Name, out List<string> candidates))
                {
                    string bestMatch = null;
                    if (candidates.Count == 1)
                    {
                        if (!claimedPaths.Contains(candidates[0]))
                            bestMatch = candidates[0];
                    }
                    else if (candidates.Count > 1)
                    {
                        foreach (var path in candidates)
                        {
                            if (claimedPaths.Contains(path)) continue;

                            bool isLnk = path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
                            if (isLnk != icon.HasShortcutOverlay) continue; // Priority 1: Shortcut state must match

                            var shinfo = new SHFILEINFO();
                            uint flags = SHGFI_SYSICONINDEX;
                            bool isDir = Directory.Exists(path);
                            if (isDir) flags |= SHGFI_USEFILEATTRIBUTES;

                            IntPtr res = SHGetFileInfo(path, isDir ? 0x10u : 0x80u, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
                            if (res != IntPtr.Zero && shinfo.iIcon == icon.ImageIndex)
                            {
                                bestMatch = path;
                                break;
                            }
                        }
                    }

                    if (bestMatch != null)
                    {
                        icon.FilePath = bestMatch;
                        claimedPaths.Add(bestMatch);
                        
                        // Fix the name if it was hidden by Explorer settings
                        string realName = Path.GetFileName(bestMatch);
                        if (!realName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                        {
                            icon.Name = realName;
                        }
                    }
                }
            }

            // STAGE 2: Fallback matches for unresolved icons
            foreach (var icon in icons)
            {
                if (string.IsNullOrEmpty(icon.FilePath) && fileLookup.TryGetValue(icon.Name, out List<string> candidates))
                {
                    string bestMatch = null;
                    
                    // Priority fallback: matching shortcut state
                    foreach (var path in candidates)
                    {
                        if (claimedPaths.Contains(path)) continue;
                        bool isLnk = path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
                        if (isLnk == icon.HasShortcutOverlay)
                        {
                            bestMatch = path;
                            break;
                        }
                    }

                    // Final fallback: just take the first available
                    if (bestMatch == null)
                    {
                        foreach (var path in candidates)
                        {
                            if (!claimedPaths.Contains(path))
                            {
                                bestMatch = path;
                                break;
                            }
                        }
                    }

                    if (bestMatch != null)
                    {
                        icon.FilePath = bestMatch;
                        claimedPaths.Add(bestMatch);
                        
                        // Fix the name if it was hidden by Explorer settings
                        string realName = Path.GetFileName(bestMatch);
                        if (!realName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                        {
                            icon.Name = realName;
                        }
                    }
                }
                
                // Read shortcut properties if it's a shortcut
                if (!string.IsNullOrEmpty(icon.FilePath) && icon.FilePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    if (shell != null)
                    {
                        try
                        {
                            dynamic shortcut = shell.CreateShortcut(icon.FilePath);
                            icon.ShortcutTarget = shortcut.TargetPath;
                            icon.ShortcutArgs = shortcut.Arguments;
                            icon.ShortcutIconLocation = shortcut.IconLocation;
                            icon.ShortcutWorkingDir = shortcut.WorkingDirectory;
                            Marshal.ReleaseComObject(shortcut);
                        }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"DesktopIconManager Error: {ex}"); }
                    }
                }
            }


            if (shell != null)
            {
                try { Marshal.ReleaseComObject(shell); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"DesktopIconManager Error: {ex}"); }
            }
        }

        private static void ScanDesktopDir(string desktopPath, Dictionary<string, List<string>> lookup)
        {
            if (!Directory.Exists(desktopPath)) return;
            try
            {
                foreach (var entry in Directory.GetFileSystemEntries(desktopPath))
                {
                    string fileName = Path.GetFileName(entry);
                    string fileNameNoExt = Path.GetFileNameWithoutExtension(entry);

                    if (!lookup.TryGetValue(fileName, out var list1))
                        lookup[fileName] = list1 = new List<string>();
                    if (!list1.Contains(entry)) list1.Add(entry);

                    if (!string.Equals(fileName, fileNameNoExt, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!lookup.TryGetValue(fileNameNoExt, out var list2))
                            lookup[fileNameNoExt] = list2 = new List<string>();
                        if (!list2.Contains(entry)) list2.Add(entry);
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"DesktopIconManager Error: {ex}"); }
        }

        public static RestoreResult SetIcons(List<IconInfo> icons)
        {
            ClearLog();
            var result = new RestoreResult();
            IntPtr listView = GetDesktopListView();
            if (listView == IntPtr.Zero)
            {
                AppendLog("Error (Set): SysListView32 handle not found.\n");
                return result;
            }

            int style = GetWindowLong(listView, GWL_STYLE);
            if (style == 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 0) AppendLog($"Warning: GetWindowLong failed with error {error}\n");
            }
            result.AutoArrangeEnabled = (style & LVS_AUTOARRANGE) != 0;
            if (result.AutoArrangeEnabled)
            {
                AppendLog("Warning: Auto-arrange is enabled on desktop. Positions might not be applied.\n");
            }

            GetWindowThreadProcessId(listView, out uint pid);

            // ===== PHASE 1: Recreate missing shortcuts =====
            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            
            // Get current desktop icon names as a list to handle duplicates (only needed for system icons now)
            var currentNames = GetCurrentIconNames(listView, pid);

            foreach (var icon in icons)
            {
                bool isMissing = false;
                
                // 1. Determine if it's missing
                if (!string.IsNullOrEmpty(icon.FilePath))
                {
                    // For files/folders with a physical path, the disk is the ultimate truth
                    isMissing = !File.Exists(icon.FilePath) && !Directory.Exists(icon.FilePath);
                }
                else
                {
                    // For system icons without paths, check if the displayed name exists
                    isMissing = !currentNames.Contains(icon.Name);
                }

                // 2. Process missing status
                if (isMissing)
                {
                    // This icon is in the snapshot but NOT on the current desktop
                    if (!string.IsNullOrEmpty(icon.ShortcutTarget) && !string.IsNullOrEmpty(icon.FilePath))
                    {
                        // ... recreation logic ...
                        try
                        {
                            string targetPath = icon.FilePath;
                            if (!File.Exists(targetPath))
                            {
                                targetPath = Path.Combine(userDesktop, Path.GetFileName(icon.FilePath));
                            }

                            var shellType = Type.GetTypeFromProgID("WScript.Shell");
                            if (shellType != null)
                            {
                                dynamic shell = Activator.CreateInstance(shellType);
                                dynamic shortcut = shell.CreateShortcut(targetPath);
                                shortcut.TargetPath = icon.ShortcutTarget;
                                if (!string.IsNullOrEmpty(icon.ShortcutArgs))
                                    shortcut.Arguments = icon.ShortcutArgs;
                                if (!string.IsNullOrEmpty(icon.ShortcutIconLocation))
                                    shortcut.IconLocation = icon.ShortcutIconLocation;
                                if (!string.IsNullOrEmpty(icon.ShortcutWorkingDir))
                                    shortcut.WorkingDirectory = icon.ShortcutWorkingDir;
                                shortcut.Save();
                                Marshal.ReleaseComObject(shortcut);
                                Marshal.ReleaseComObject(shell);
                                result.Recreated++;
                                AppendLog($"Recreated shortcut: {icon.Name}\n");
                                // Add to current names so we don't recreate it again if there are more duplicates
                                currentNames.Add(icon.Name); 
                            }
                        }
                        catch (Exception ex)
                        {
                            result.MissingFiles.Add(icon.Name);
                            AppendLog($"Failed to recreate shortcut {icon.Name}: {ex.Message}\n");
                        }
                    }
                    else if (!string.IsNullOrEmpty(icon.FilePath) && 
                             !icon.FilePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        result.MissingFiles.Add(icon.Name);
                        AppendLog($"Cannot restore deleted file: {icon.Name}\n");
                    }
                    else
                    {
                        result.MissingFiles.Add(icon.Name);
                        AppendLog($"Cannot restore (no path data): {icon.Name}\n");
                    }
                }
                else
                {
                    // Consume one instance of the name for system icons
                    if (string.IsNullOrEmpty(icon.FilePath))
                    {
                        currentNames.Remove(icon.Name);
                    }
                }
            }

            // ===== PHASE 2: Wait for Explorer to register new shortcuts, then re-enumerate =====
            if (result.Recreated > 0)
            {
                System.Threading.Thread.Sleep(800); // Give Explorer time to register
                // Refresh desktop
                IntPtr refreshHwnd = FindWindow("Progman", null);
                if (refreshHwnd != IntPtr.Zero)
                {
                    SendMessage(refreshHwnd, 0x0111, (IntPtr)0x7103, IntPtr.Zero); // Refresh
                }
                System.Threading.Thread.Sleep(400);
            }

            // ===== PHASE 3: Build fresh map using robust path resolution =====
            var currentDesktopIcons = new List<IconInfo>();
            
            // Re-read listView handle in case it changed after refresh
            listView = GetDesktopListView();
            if (listView == IntPtr.Zero)
            {
                AppendLog("Error (Set Phase 3): SysListView32 handle lost.\n");
                return result;
            }
            GetWindowThreadProcessId(listView, out pid);

            IntPtr process = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
            if (process == IntPtr.Zero)
            {
                AppendLog($"Error (Set): OpenProcess failed with code {Marshal.GetLastWin32Error()}\n");
                return result;
            }

            bool processClosed = false;

            int count = (int)SendMessage(listView, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            AppendLog($"Action Set Phase 3: Current count: {count}\n");

            uint itemSize = (uint)Marshal.SizeOf(typeof(LVITEMW));
            uint stringBufSize = 512;

            IntPtr pBuf = IntPtr.Zero;
            try
            {
                pBuf = VirtualAllocEx(process, IntPtr.Zero, itemSize + stringBufSize, MEM_COMMIT, PAGE_READWRITE);
                
                if (pBuf == IntPtr.Zero)
                {
                    AppendLog($"Error (Set Phase 3): VirtualAllocEx failed with code {Marshal.GetLastWin32Error()}\n");
                    CloseHandle(process);
                    processClosed = true;
                    return result;
                }

                IntPtr itemAddress = pBuf;
                IntPtr strAddress = pBuf + (int)itemSize;

                for (int i = 0; i < count; i++)
                {
                    byte[] itemBytes;
                    int itemStructSize;

                    if (Environment.Is64BitOperatingSystem)
                    {
                        itemStructSize = 88;
                        itemBytes = new byte[88];
                        BitConverter.GetBytes(LVIF_TEXT | LVIF_IMAGE | LVIF_STATE).CopyTo(itemBytes, 0); // mask
                        BitConverter.GetBytes(i).CopyTo(itemBytes, 4); // iItem
                        BitConverter.GetBytes(LVIS_OVERLAYMASK).CopyTo(itemBytes, 16); // stateMask
                        BitConverter.GetBytes(strAddress.ToInt64()).CopyTo(itemBytes, 24); // pszText
                        BitConverter.GetBytes(255).CopyTo(itemBytes, 32); // cchTextMax
                    }
                    else
                    {
                        itemStructSize = 60;
                        itemBytes = new byte[60];
                        BitConverter.GetBytes(LVIF_TEXT | LVIF_IMAGE | LVIF_STATE).CopyTo(itemBytes, 0); // mask
                        BitConverter.GetBytes(i).CopyTo(itemBytes, 4); // iItem
                        BitConverter.GetBytes(LVIS_OVERLAYMASK).CopyTo(itemBytes, 16); // stateMask
                        BitConverter.GetBytes(strAddress.ToInt32()).CopyTo(itemBytes, 20); // pszText
                        BitConverter.GetBytes(255).CopyTo(itemBytes, 24); // cchTextMax
                    }

                    IntPtr localItem = Marshal.AllocHGlobal(itemStructSize);
                    try
                    {
                        Marshal.Copy(itemBytes, 0, localItem, itemStructSize);
                        if (!WriteProcessMemory(process, itemAddress, localItem, itemStructSize, out _)) { currentDesktopIcons.Add(null); continue; }
                        SendMessage(listView, LVM_GETITEMW, (IntPtr)0, itemAddress);
                        
                        if (!ReadProcessMemory(process, itemAddress, localItem, itemStructSize, out _)) { currentDesktopIcons.Add(null); continue; }
                        Marshal.Copy(localItem, itemBytes, 0, itemStructSize);
                    }
                    finally { Marshal.FreeHGlobal(localItem); }

                    IntPtr localStr = Marshal.AllocHGlobal((int)stringBufSize);
                    string name;
                    try
                    {
                        if (!ReadProcessMemory(process, strAddress, localStr, (int)stringBufSize, out _)) { currentDesktopIcons.Add(null); continue; }
                        name = Marshal.PtrToStringUni(localStr);
                    }
                    finally { Marshal.FreeHGlobal(localStr); }

                    if (!string.IsNullOrEmpty(name))
                    {
                        int currentImageIndex = -1;
                        uint state = 0;
                        if (Environment.Is64BitOperatingSystem)
                        {
                            state = BitConverter.ToUInt32(itemBytes, 12);
                            currentImageIndex = BitConverter.ToInt32(itemBytes, 36);
                        }
                        else
                        {
                            state = BitConverter.ToUInt32(itemBytes, 12);
                            currentImageIndex = BitConverter.ToInt32(itemBytes, 28);
                        }
                        
                        bool hasShortcutOverlay = (state & LVIS_OVERLAYMASK) != 0;
                        
                        currentDesktopIcons.Add(new IconInfo {
                            Name = name,
                            ImageIndex = currentImageIndex,
                            HasShortcutOverlay = hasShortcutOverlay,
                            X = i // Temporarily store the ListView index in the X coordinate!
                        });
                    }
                    else
                    {
                        // Keep indices perfectly aligned
                        currentDesktopIcons.Add(null);
                    }
                }
            }
            finally
            {
                if (pBuf != IntPtr.Zero) VirtualFreeEx(process, pBuf, 0, MEM_RELEASE);
                if (!processClosed) CloseHandle(process);
            }

            // Resolve actual file paths for current desktop icons
            var iconsToResolve = currentDesktopIcons.Where(x => x != null).ToList();
            ResolveFilePaths(iconsToResolve);

            // Build lookup dictionaries
            var currentIconsByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var currentIconsByName = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

            foreach (var curIcon in currentDesktopIcons)
            {
                if (curIcon != null)
                {
                    int indexInListView = curIcon.X; // Recover the index

                    if (!string.IsNullOrEmpty(curIcon.FilePath))
                    {
                        currentIconsByPath[curIcon.FilePath] = indexInListView;
                    }
                    
                    if (!currentIconsByName.TryGetValue(curIcon.Name, out var indices))
                    {
                        indices = new List<int>();
                        currentIconsByName[curIcon.Name] = indices;
                    }
                    indices.Add(indexInListView);
                }
            }

            // ===== PHASE 4: Write icon positions =====
            IntPtr writeProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, false, pid);
            if (writeProcess != IntPtr.Zero)
            {
                IntPtr pPoint = IntPtr.Zero;
                try
                {
                    pPoint = VirtualAllocEx(writeProcess, IntPtr.Zero, (uint)Marshal.SizeOf(typeof(POINT)), MEM_COMMIT, PAGE_READWRITE);
                    if (pPoint != IntPtr.Zero)
                    {
                        var usedIndices = new HashSet<int>();
                        foreach (var icon in icons)
                        {
                            int index = -1;
                            
                            // 1. Try exact path match (Most robust, handles collisions perfectly)
                            if (!string.IsNullOrEmpty(icon.FilePath) && 
                                currentIconsByPath.TryGetValue(icon.FilePath, out int exactIdx) && 
                                !usedIndices.Contains(exactIdx))
                            {
                                index = exactIdx;
                            }
                            // 2. Fallback to name match (For system icons like Recycle Bin or missing paths)
                            else if (currentIconsByName.TryGetValue(icon.Name, out var fallbackIndices))
                            {
                                for (int j = 0; j < fallbackIndices.Count; j++)
                                {
                                    if (!usedIndices.Contains(fallbackIndices[j]))
                                    {
                                        index = fallbackIndices[j];
                                        fallbackIndices.RemoveAt(j); // Consume it
                                        break;
                                    }
                                }
                            }

                            if (index != -1)
                            {
                                usedIndices.Add(index);
                                POINT pt = new POINT { x = icon.X, y = icon.Y };
                                int pointSize = Marshal.SizeOf(typeof(POINT));
                                IntPtr localPt = Marshal.AllocHGlobal(pointSize);
                                try
                                {
                                    Marshal.StructureToPtr(pt, localPt, false);
                                    if (WriteProcessMemory(writeProcess, pPoint, localPt, pointSize, out _))
                                    {
                                        SendMessage(listView, LVM_SETITEMPOSITION32, (IntPtr)index, pPoint);
                                        SendMessage(listView, LVM_UPDATE, (IntPtr)index, IntPtr.Zero);
                                        result.Repositioned++;
                                    }
                                }
                                finally { Marshal.FreeHGlobal(localPt); }
                            }
                        }

                        // Calculate extra icons based on unused items on the desktop
                        int extraCount = 0;
                        for (int i = 0; i < currentDesktopIcons.Count; i++)
                        {
                            if (currentDesktopIcons[i] != null && !usedIndices.Contains(i))
                            {
                                extraCount++;
                            }
                        }
                        result.ExtraIcons = extraCount;
                    }
                }
                finally
                {
                    if (pPoint != IntPtr.Zero) VirtualFreeEx(writeProcess, pPoint, 0, MEM_RELEASE);
                    CloseHandle(writeProcess);
                }
            }

            // Repaint the desktop without triggering a full Explorer re-sort.
            // NOTE: We intentionally avoid LVM_ARRANGE(LVA_DEFAULT=0) here because on some
            // Windows versions it acts as LVA_SNAPTOGRID and moves ALL icons, overriding
            // the positions we just set and potentially causing icons to visually "stack" or disappear.
            InvalidateRect(listView, IntPtr.Zero, true);

            AppendLog($"SetIcons completion: Repositioned {result.Repositioned}, Recreated {result.Recreated}, Missing {result.MissingFiles.Count}\n");
            return result;
        }

        /// <summary>
        /// Quick helper to get current icon names from the desktop ListView as a list to handle duplicates
        /// </summary>
        private static List<string> GetCurrentIconNames(IntPtr listView, uint pid)
        {
            var names = new List<string>();
            
            // Re-fetch pid from the listView handle to avoid stale pid issues
            GetWindowThreadProcessId(listView, out uint actualPid);
            IntPtr process = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, actualPid);
            if (process == IntPtr.Zero) return names;

            int count = (int)SendMessage(listView, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            uint itemSize = (uint)Marshal.SizeOf(typeof(LVITEMW));
            uint stringBufSize = 512;

            IntPtr pBuf = IntPtr.Zero;
            try
            {
                pBuf = VirtualAllocEx(process, IntPtr.Zero, itemSize + stringBufSize, MEM_COMMIT, PAGE_READWRITE);
                if (pBuf == IntPtr.Zero) return names;

                IntPtr itemAddr = pBuf;
                IntPtr strAddr = pBuf + (int)itemSize;

                for (int i = 0; i < count; i++)
                {
                    byte[] itemBytes;
                    int itemStructSize;

                    if (Environment.Is64BitOperatingSystem)
                    {
                        itemStructSize = 88;
                        itemBytes = new byte[88];
                        BitConverter.GetBytes(LVIF_TEXT).CopyTo(itemBytes, 0);
                        BitConverter.GetBytes(i).CopyTo(itemBytes, 4);
                        BitConverter.GetBytes(strAddr.ToInt64()).CopyTo(itemBytes, 24);
                        BitConverter.GetBytes(255).CopyTo(itemBytes, 32);
                    }
                    else
                    {
                        itemStructSize = 60;
                        itemBytes = new byte[60];
                        BitConverter.GetBytes(LVIF_TEXT).CopyTo(itemBytes, 0);
                        BitConverter.GetBytes(i).CopyTo(itemBytes, 4);
                        BitConverter.GetBytes(strAddr.ToInt32()).CopyTo(itemBytes, 20);
                        BitConverter.GetBytes(255).CopyTo(itemBytes, 24);
                    }

                    IntPtr localItem = Marshal.AllocHGlobal(itemStructSize);
                    try
                    {
                        Marshal.Copy(itemBytes, 0, localItem, itemStructSize);
                        WriteProcessMemory(process, itemAddr, localItem, itemStructSize, out _);
                        SendMessage(listView, LVM_GETITEMTEXTW, (IntPtr)i, itemAddr);
                    }
                    finally { Marshal.FreeHGlobal(localItem); }

                    IntPtr localStr = Marshal.AllocHGlobal((int)stringBufSize);
                    string name;
                    try
                    {
                        ReadProcessMemory(process, strAddr, localStr, (int)stringBufSize, out _);
                        name = Marshal.PtrToStringUni(localStr);
                    }
                    finally { Marshal.FreeHGlobal(localStr); }

                    if (!string.IsNullOrEmpty(name)) names.Add(name);
                }
            }
            finally
            {
                if (pBuf != IntPtr.Zero) VirtualFreeEx(process, pBuf, 0, MEM_RELEASE);
                CloseHandle(process);
            }
            return names;
        }
    }
}
