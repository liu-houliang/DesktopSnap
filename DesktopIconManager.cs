#pragma warning disable CS0649
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
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
        const int GWL_STYLE = -16;
        const int LVS_AUTOARRANGE = 0x0100;

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

            IntPtr pPointStr = VirtualAllocEx(process, IntPtr.Zero, pointSize + itemSize + stringBufSize, MEM_COMMIT, PAGE_READWRITE);
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
                ReadProcessMemory(process, ptAddress, localPt, pointStructSize, out _);
                POINT pt = Marshal.PtrToStructure<POINT>(localPt);
                Marshal.FreeHGlobal(localPt);

                // Text (Architecture safe struct injection)
                byte[] itemBytes;
                int itemStructSize;

                if (Environment.Is64BitOperatingSystem)
                {
                    itemStructSize = 88;
                    itemBytes = new byte[88];
                    BitConverter.GetBytes(strAddress.ToInt64()).CopyTo(itemBytes, 24); // pszText
                    BitConverter.GetBytes(255).CopyTo(itemBytes, 32); // cchTextMax
                }
                else
                {
                    itemStructSize = 60;
                    itemBytes = new byte[60];
                    BitConverter.GetBytes(strAddress.ToInt32()).CopyTo(itemBytes, 20); // pszText
                    BitConverter.GetBytes(255).CopyTo(itemBytes, 24); // cchTextMax
                }

                IntPtr localItem = Marshal.AllocHGlobal(itemStructSize);
                Marshal.Copy(itemBytes, 0, localItem, itemStructSize);

                WriteProcessMemory(process, itemAddress, localItem, itemStructSize, out _);
                SendMessage(listView, LVM_GETITEMTEXTW, (IntPtr)i, itemAddress);

                IntPtr localStr = Marshal.AllocHGlobal((int)stringBufSize);
                ReadProcessMemory(process, strAddress, localStr, (int)stringBufSize, out _);
                string name = Marshal.PtrToStringUni(localStr);
                
                Marshal.FreeHGlobal(localStr);
                Marshal.FreeHGlobal(localItem);

                if (!string.IsNullOrEmpty(name))
                {
                    icons.Add(new IconInfo { Name = name, X = pt.x, Y = pt.y });
                }
            }

            VirtualFreeEx(process, pPointStr, 0, MEM_RELEASE);
            CloseHandle(process);

            AppendLog($"GetIcons completion: Valid names retrieved: {icons.Count}\n");

            // Resolve file paths and shortcut metadata
            ResolveFilePaths(icons);

            return icons;
        }

        private static void ResolveFilePaths(List<IconInfo> icons)
        {
            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

            // Build lookup table once instead of scanning per-icon
            var fileLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

            foreach (var icon in icons)
            {
                if (fileLookup.TryGetValue(icon.Name, out string found))
                {
                    icon.FilePath = found;

                    if (shell != null && found.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            dynamic shortcut = shell.CreateShortcut(found);
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

        private static void ScanDesktopDir(string desktopPath, Dictionary<string, string> lookup)
        {
            if (!Directory.Exists(desktopPath)) return;
            try
            {
                foreach (var entry in Directory.GetFileSystemEntries(desktopPath))
                {
                    string fileName = Path.GetFileName(entry);
                    string fileNameNoExt = Path.GetFileNameWithoutExtension(entry);

                    // Map by both full name and name-without-extension (for .lnk display)
                    if (!lookup.ContainsKey(fileName))
                        lookup[fileName] = entry;
                    if (!lookup.ContainsKey(fileNameNoExt))
                        lookup[fileNameNoExt] = entry;
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
            var savedNames = new HashSet<string>(icons.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
            
            // Get current desktop icon names first
            var currentNames = GetCurrentIconNames(listView, pid);

            foreach (var icon in icons)
            {
                if (!currentNames.Contains(icon.Name))
                {
                    // This icon is in the snapshot but NOT on the current desktop
                    if (!string.IsNullOrEmpty(icon.ShortcutTarget) && !string.IsNullOrEmpty(icon.FilePath))
                    {
                        // It was a shortcut - try to recreate it
                        try
                        {
                            string targetPath = icon.FilePath;
                            // If original was on user desktop, recreate there
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
                        // It was a regular file/folder - we cannot recreate it
                        result.MissingFiles.Add(icon.Name);
                        AppendLog($"Cannot restore deleted file: {icon.Name}\n");
                    }
                    else
                    {
                        // No file path info (old snapshot format or system icon)
                        result.MissingFiles.Add(icon.Name);
                        AppendLog($"Cannot restore (no path data): {icon.Name}\n");
                    }
                }
            }

            // ===== PHASE 1.5: Handle extra icons (on desktop but not in snapshot) =====
            // We count them but leave them in place - don't delete user files
            currentNames = GetCurrentIconNames(listView, pid); // re-read after recreations
            foreach (var name in currentNames)
            {
                if (!savedNames.Contains(name))
                {
                    result.ExtraIcons++;
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

            // ===== PHASE 3: Build fresh name-to-index map and reposition all matched icons =====
            var currentIcons = new Dictionary<string, int>();
            
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

            int count = (int)SendMessage(listView, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            AppendLog($"Action Set Phase 3: Current count: {count}\n");

            uint itemSize = (uint)Marshal.SizeOf(typeof(LVITEMW));
            uint stringBufSize = 512;

            IntPtr pBuf = VirtualAllocEx(process, IntPtr.Zero, itemSize + stringBufSize, MEM_COMMIT, PAGE_READWRITE);
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
                    BitConverter.GetBytes(strAddress.ToInt64()).CopyTo(itemBytes, 24);
                    BitConverter.GetBytes(255).CopyTo(itemBytes, 32);
                }
                else
                {
                    itemStructSize = 60;
                    itemBytes = new byte[60];
                    BitConverter.GetBytes(strAddress.ToInt32()).CopyTo(itemBytes, 20);
                    BitConverter.GetBytes(255).CopyTo(itemBytes, 24);
                }

                IntPtr localItem = Marshal.AllocHGlobal(itemStructSize);
                Marshal.Copy(itemBytes, 0, localItem, itemStructSize);

                WriteProcessMemory(process, itemAddress, localItem, itemStructSize, out _);
                SendMessage(listView, LVM_GETITEMTEXTW, (IntPtr)i, itemAddress);

                IntPtr localStr = Marshal.AllocHGlobal((int)stringBufSize);
                ReadProcessMemory(process, strAddress, localStr, (int)stringBufSize, out _);
                string name = Marshal.PtrToStringUni(localStr);

                Marshal.FreeHGlobal(localStr);
                Marshal.FreeHGlobal(localItem);

                if (!string.IsNullOrEmpty(name))
                {
                    currentIcons[name] = i;
                }
            }

            VirtualFreeEx(process, pBuf, 0, MEM_RELEASE);
            CloseHandle(process);

            // Position icons
            IntPtr writeProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, false, pid);
            if (writeProcess != IntPtr.Zero)
            {
                IntPtr pPoint = VirtualAllocEx(writeProcess, IntPtr.Zero, (uint)Marshal.SizeOf(typeof(POINT)), MEM_COMMIT, PAGE_READWRITE);

                if (pPoint != IntPtr.Zero)
                {
                    foreach (var icon in icons)
                    {
                        if (currentIcons.TryGetValue(icon.Name, out int index))
                        {
                            POINT pt = new POINT { x = icon.X, y = icon.Y };
                            int pointSize = Marshal.SizeOf(typeof(POINT));
                            IntPtr localPt = Marshal.AllocHGlobal(pointSize);
                            Marshal.StructureToPtr(pt, localPt, false);

                            WriteProcessMemory(writeProcess, pPoint, localPt, pointSize, out _);
                            SendMessage(listView, LVM_SETITEMPOSITION32, (IntPtr)index, pPoint);
                            SendMessage(listView, LVM_UPDATE, (IntPtr)index, IntPtr.Zero);

                            Marshal.FreeHGlobal(localPt);
                            result.Repositioned++;
                        }
                    }
                    VirtualFreeEx(writeProcess, pPoint, 0, MEM_RELEASE);
                }
                CloseHandle(writeProcess);
            }

            // Always refresh desktop to force redraw and apply positions
            IntPtr desktopHwnd = FindWindow("Progman", null);
            if (desktopHwnd != IntPtr.Zero)
            {
                SendMessage(desktopHwnd, 0x0111, (IntPtr)0x7103, IntPtr.Zero); // Refresh command
            }
            // Also notify the listview directly
            SendMessage(listView, LVM_ARRANGE, (IntPtr)0, IntPtr.Zero); // LVA_DEFAULT (0) or LVA_SNAPTOGRID
            InvalidateRect(listView, IntPtr.Zero, true);

            AppendLog($"SetIcons completion: Repositioned {result.Repositioned}, Recreated {result.Recreated}, Missing {result.MissingFiles.Count}\n");
            return result;
        }

        /// <summary>
        /// Quick helper to get current icon names from the desktop ListView without full enumeration
        /// </summary>
        private static HashSet<string> GetCurrentIconNames(IntPtr listView, uint pid)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IntPtr process = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
            if (process == IntPtr.Zero) return names;

            int count = (int)SendMessage(listView, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            uint itemSize = (uint)Marshal.SizeOf(typeof(LVITEMW));
            uint stringBufSize = 512;

            IntPtr pBuf = VirtualAllocEx(process, IntPtr.Zero, itemSize + stringBufSize, MEM_COMMIT, PAGE_READWRITE);
            if (pBuf == IntPtr.Zero) { CloseHandle(process); return names; }

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
                    BitConverter.GetBytes(strAddr.ToInt64()).CopyTo(itemBytes, 24);
                    BitConverter.GetBytes(255).CopyTo(itemBytes, 32);
                }
                else
                {
                    itemStructSize = 60;
                    itemBytes = new byte[60];
                    BitConverter.GetBytes(strAddr.ToInt32()).CopyTo(itemBytes, 20);
                    BitConverter.GetBytes(255).CopyTo(itemBytes, 24);
                }

                IntPtr localItem = Marshal.AllocHGlobal(itemStructSize);
                Marshal.Copy(itemBytes, 0, localItem, itemStructSize);

                WriteProcessMemory(process, itemAddr, localItem, itemStructSize, out _);
                SendMessage(listView, LVM_GETITEMTEXTW, (IntPtr)i, itemAddr);

                IntPtr localStr = Marshal.AllocHGlobal((int)stringBufSize);
                ReadProcessMemory(process, strAddr, localStr, (int)stringBufSize, out _);
                string name = Marshal.PtrToStringUni(localStr);

                Marshal.FreeHGlobal(localStr);
                Marshal.FreeHGlobal(localItem);

                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }

            VirtualFreeEx(process, pBuf, 0, MEM_RELEASE);
            CloseHandle(process);
            return names;
        }
    }
}
