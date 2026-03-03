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
            IntPtr progman = FindWindow("Progman", null);
            IntPtr shelldll = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            IntPtr listview = FindWindowEx(shelldll, IntPtr.Zero, "SysListView32", null);

            if (listview != IntPtr.Zero)
                return listview;

            IntPtr workerw = IntPtr.Zero;
            EnumWindows((hwnd, lParam) =>
            {
                IntPtr p = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (p != IntPtr.Zero)
                {
                    listview = FindWindowEx(p, IntPtr.Zero, "SysListView32", null);
                    workerw = hwnd;
                    return false; // stop enums
                }
                return true;
            }, IntPtr.Zero);

            return listview;
        }

        public static List<IconInfo> GetIcons()
        {
            var icons = new List<IconInfo>();
            IntPtr listView = GetDesktopListView();
            if (listView == IntPtr.Zero) return icons;

            GetWindowThreadProcessId(listView, out uint pid);
            IntPtr process = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
            if (process == IntPtr.Zero) return icons;

            int count = (int)SendMessage(listView, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);

            uint pointSize = (uint)Marshal.SizeOf(typeof(POINT));
            uint itemSize = (uint)Marshal.SizeOf(typeof(LVITEMW));
            uint stringBufSize = 512;

            IntPtr pPointStr = VirtualAllocEx(process, IntPtr.Zero, pointSize + itemSize + stringBufSize, MEM_COMMIT, PAGE_READWRITE);

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

                // Text
                LVITEMW item = new LVITEMW();
                item.cchTextMax = 255;
                item.pszText = strAddress;

                int itemStructSize = Marshal.SizeOf(typeof(LVITEMW));
                IntPtr localItem = Marshal.AllocHGlobal(itemStructSize);
                Marshal.StructureToPtr(item, localItem, false);

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

            return icons;
        }

        public static void SetIcons(List<IconInfo> icons)
        {
            IntPtr listView = GetDesktopListView();
            if (listView == IntPtr.Zero) return;

            // First, get current icons to know their indices
            var currentIcons = new Dictionary<string, int>();

            GetWindowThreadProcessId(listView, out uint pid);
            IntPtr process = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
            if (process == IntPtr.Zero) return;

            int count = (int)SendMessage(listView, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);

            uint itemSize = (uint)Marshal.SizeOf(typeof(LVITEMW));
            uint stringBufSize = 512;

            IntPtr pPointStr = VirtualAllocEx(process, IntPtr.Zero, itemSize + stringBufSize, MEM_COMMIT, PAGE_READWRITE);
            IntPtr itemAddress = pPointStr;
            IntPtr strAddress = pPointStr + (int)itemSize;

            for (int i = 0; i < count; i++)
            {
                LVITEMW item = new LVITEMW();
                item.cchTextMax = 255;
                item.pszText = strAddress;

                int itemStructSize = Marshal.SizeOf(typeof(LVITEMW));
                IntPtr localItem = Marshal.AllocHGlobal(itemStructSize);
                Marshal.StructureToPtr(item, localItem, false);

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

            // Now apply positions
            foreach (var icon in icons)
            {
                if (currentIcons.TryGetValue(icon.Name, out int index))
                {
                    IntPtr lParam = (IntPtr)((icon.Y << 16) | (icon.X & 0xffff));
                    SendMessage(listView, LVM_SETITEMPOSITION, (IntPtr)index, lParam);
                }
            }
        }
    }
}
