#pragma warning disable CS0649
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    }

    public static class DesktopIconManager
    {
        public static string LastLog { get; set; } = "";

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

        const uint PROCESS_VM_OPERATION = 0x0008;
        const uint PROCESS_VM_READ = 0x0010;
        const uint PROCESS_VM_WRITE = 0x0020;

        const uint MEM_COMMIT = 0x1000;
        const uint MEM_RELEASE = 0x8000;
        const uint PAGE_READWRITE = 0x04;

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
                LastLog += $"Found Progman SysListView32: {lv} (Items: {progmanCount})\n";
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
                        LastLog += $"Found WorkerW ({hwnd}) SysListView32: {tempLv} (Items: {count})\n";
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
                LastLog += $"Selected WorkerW SysListView32: {listView}\n";
                return listView;
            }

            LastLog += $"Selected Progman SysListView32: {lv}\n";
            return lv; // Return whatever Progman had, even if 0
        }

        public static List<IconInfo> GetIcons()
        {
            LastLog = "";
            var icons = new List<IconInfo>();
            IntPtr listView = GetDesktopListView();
            
            if (listView == IntPtr.Zero)
            {
                LastLog += "Error: Could not find any SysListView32 handle for desktop.\n";
                return icons;
            }

            GetWindowThreadProcessId(listView, out uint pid);
            LastLog += $"Target Process ID: {pid}\n";
            
            IntPtr process = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
            if (process == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                LastLog += $"Error: OpenProcess failed with code {err}\n";
                return icons;
            }

            int count = (int)SendMessage(listView, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            LastLog += $"Action Get: Count verified: {count}\n";
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
                LastLog += $"Error: VirtualAllocEx failed with code {err}\n";
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

            LastLog += $"GetIcons completion: Valid names retrieved: {icons.Count}\n";
            return icons;
        }

        public static void SetIcons(List<IconInfo> icons)
        {
            LastLog = "";
            IntPtr listView = GetDesktopListView();
            if (listView == IntPtr.Zero)
            {
                LastLog += "Error (Set): SysListView32 handle not found.\n";
                return;
            }

            var currentIcons = new Dictionary<string, int>();

            GetWindowThreadProcessId(listView, out uint pid);
            IntPtr process = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
            if (process == IntPtr.Zero)
            {
                LastLog += $"Error (Set): OpenProcess failed with code {Marshal.GetLastWin32Error()}\n";
                return;
            }

            int count = (int)SendMessage(listView, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            LastLog += $"Action Set: Target count verified: {count}\n";

            uint itemSize = (uint)Marshal.SizeOf(typeof(LVITEMW));
            uint stringBufSize = 512;

            IntPtr pPointStr = VirtualAllocEx(process, IntPtr.Zero, itemSize + stringBufSize, MEM_COMMIT, PAGE_READWRITE);

            IntPtr itemAddress = pPointStr;
            IntPtr strAddress = pPointStr + (int)itemSize;

            for (int i = 0; i < count; i++)
            {
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
                    currentIcons[name] = i;
                }
            }

            VirtualFreeEx(process, pPointStr, 0, MEM_RELEASE);
            CloseHandle(process);

            int matched = 0;
            foreach (var icon in icons)
            {
                if (currentIcons.TryGetValue(icon.Name, out int index))
                {
                    int x = icon.X & 0xFFFF;
                    int y = (icon.Y & 0xFFFF) << 16;
                    IntPtr lParam = unchecked((IntPtr)(x | y));
                    SendMessage(listView, LVM_SETITEMPOSITION, (IntPtr)index, lParam);
                    matched++;
                }
            }
            LastLog += $"SetIcons completion: Relocated {matched} icons.\n";
        }
    }
}
