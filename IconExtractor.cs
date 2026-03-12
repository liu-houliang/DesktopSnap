using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopSnap
{
    public static class IconExtractor
    {
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll")]
        private static extern IntPtr SHGetFileInfo(IntPtr ppidl, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("shell32.dll")]
        private static extern int SHGetDesktopFolder(out IShellFolder ppshf);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr pv);

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_SMALLICON = 0x000000001;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint SHGFI_PIDL = 0x000000008;

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214E6-0000-0000-C000-000000000046")]
        private interface IShellFolder
        {
            [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, string pszDisplayName, out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
            [PreserveSig] int EnumObjects(IntPtr hwnd, int grfFlags, out IEnumIDList ppenumIDList);
            void BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
            void BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
            void CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
            void CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
            void GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);
            void GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
            [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint uFlags, out STRRET pName);
            void SetNameOf(IntPtr hwnd, IntPtr pidl, string pszName, uint uFlags, out IntPtr ppidlOut);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F2-0000-0000-C000-000000000046")]
        private interface IEnumIDList
        {
            [PreserveSig]
            int Next(uint celt, out IntPtr rgelt, out uint pceltFetched);
            void Skip(uint celt);
            void Reset();
            void Clone(out IEnumIDList ppenum);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STRRET
        {
            public uint uType;
            public IntPtr pOleStr;
            public uint uOffset;
            public IntPtr cStr;
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Auto)]
        private static extern int StrRetToBuf(ref STRRET pstr, IntPtr pidl, StringBuilder pszBuf, uint cchBuf);

        private const int SHGDN_NORMAL = 0x0000;
        private const int SHCONTF_FOLDERS = 0x0020;
        private const int SHCONTF_NONFOLDERS = 0x0040;
        private const int SHCONTF_INCLUDEHIDDEN = 0x0080;

        // Pre-cached system icon bytes: localized name (lowercase) -> PNG bytes
        private static readonly Dictionary<string, byte[]> _systemIconBytesCache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private static bool _systemIconsInitialized = false;
        private static readonly object _initLock = new object();

        /// <summary>
        /// Must be called from the main (STA) thread at app startup to pre-cache system icon images.
        /// Shell COM interfaces require an STA thread with a message pump to work correctly.
        /// </summary>
        public static void InitSystemIcons()
        {
            lock (_initLock)
            {
                if (_systemIconsInitialized) return;

                string[] knownGuids = {
                    "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", // This PC
                    "::{645FF040-5081-101B-9F08-00AA002F954E}", // Recycle Bin
                    "::{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", // Control Panel
                    "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", // Network
                    "::{59031a47-3f72-44a7-89c5-5595fe6b30ee}"  // User Files
                };

                IShellFolder desktopFolder;
                if (SHGetDesktopFolder(out desktopFolder) == 0 && desktopFolder != null)
                {
                    foreach (var guid in knownGuids)
                    {
                        try
                        {
                            uint pch = 0;
                            uint attrs = 0;
                            IntPtr pidl;
                            if (desktopFolder.ParseDisplayName(IntPtr.Zero, IntPtr.Zero, guid, out pch, out pidl, ref attrs) == 0 && pidl != IntPtr.Zero)
                            {
                                // Get localized display name
                                string localizedName = null;
                                STRRET str;
                                if (desktopFolder.GetDisplayNameOf(pidl, SHGDN_NORMAL, out str) == 0)
                                {
                                    StringBuilder sb = new StringBuilder(260);
                                    StrRetToBuf(ref str, pidl, sb, (uint)sb.Capacity);
                                    localizedName = sb.ToString();
                                }

                                // Extract icon immediately and convert to PNG bytes
                                var shinfo = new SHFILEINFO();
                                SHGetFileInfo(pidl, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_PIDL | SHGFI_ICON | SHGFI_LARGEICON);

                                if (shinfo.hIcon != IntPtr.Zero)
                                {
                                    byte[] pngBytes = HIconToPngBytes(shinfo.hIcon);
                                    DestroyIcon(shinfo.hIcon);

                                    if (pngBytes != null && !string.IsNullOrEmpty(localizedName))
                                    {
                                        _systemIconBytesCache[localizedName] = pngBytes;
                                    }
                                }

                                CoTaskMemFree(pidl);
                            }
                        }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"IconExtractor Error: {ex}"); }
                    }
                    Marshal.ReleaseComObject(desktopFolder);
                }

                _systemIconsInitialized = true;
            }
        }

        /// <summary>
        /// Get icon PNG bytes for a given file path or icon name.
        /// For system icons (no file path), returns pre-cached bytes from InitSystemIcons.
        /// For regular files, uses SHGetFileInfo with the file path.
        /// Thread-safe: can be called from any thread (including Task.Run thread pool threads).
        /// </summary>
        public static byte[] GetIconBytes(string filePath, string iconName)
        {
            // 1. Check pre-cached system icons first (thread-safe, no COM needed)
            if (!string.IsNullOrEmpty(iconName) && _systemIconBytesCache.TryGetValue(iconName, out byte[] cachedBytes))
            {
                return cachedBytes;
            }

            IntPtr hIcon = IntPtr.Zero;
            var shinfo = new SHFILEINFO();

            // 2. Real path on disk — extract icon directly (works on any thread)
            bool isDir = !string.IsNullOrEmpty(filePath) && Directory.Exists(filePath);
            if (!string.IsNullOrEmpty(filePath) && (isDir || File.Exists(filePath)))
            {
                SHGetFileInfo(filePath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);
                hIcon = shinfo.hIcon;
            }

            // 3. Fallback: try extension/attribute-based icon if file/folder is missing
            if (hIcon == IntPtr.Zero && !string.IsNullOrEmpty(filePath))
            {
                uint attr = isDir ? 0x10u /* FILE_ATTRIBUTE_DIRECTORY */ : 0x80u /* FILE_ATTRIBUTE_NORMAL */;
                SHGetFileInfo(filePath, attr, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);
                hIcon = shinfo.hIcon;
            }

            // 4. System icon fallback: try COM-based extraction with CoInitialize for thread safety
            if (hIcon == IntPtr.Zero && !string.IsNullOrEmpty(iconName))
            {
                hIcon = GetDesktopItemIconByNameSafe(iconName);
            }

            if (hIcon != IntPtr.Zero)
            {
                byte[] result = HIconToPngBytes(hIcon);
                DestroyIcon(hIcon);
                return result;
            }

            return null;
        }

        /// <summary>
        /// Convert an HICON to PNG byte array using System.Drawing.
        /// </summary>
        private static byte[] HIconToPngBytes(IntPtr hIcon)
        {
            try
            {
                using (Icon icon = Icon.FromHandle(hIcon))
                {
                    using (Bitmap bmp = icon.ToBitmap())
                    {
                        using (MemoryStream ms = new MemoryStream())
                        {
                            bmp.Save(ms, ImageFormat.Png);
                            return ms.ToArray();
                        }
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Thread-safe version: ensures COM is initialized before using IShellFolder on the current thread.
        /// </summary>
        private static IntPtr GetDesktopItemIconByNameSafe(string name)
        {
            IntPtr hIcon = IntPtr.Zero;

            // Ensure COM is initialized on this thread (harmless if already initialized)
            CoInitializeEx(IntPtr.Zero, 0x0 /* COINIT_APARTMENTTHREADED */);

            try
            {
                IShellFolder desktopFolder;
                if (SHGetDesktopFolder(out desktopFolder) == 0 && desktopFolder != null)
                {
                    IEnumIDList enumIDList = null;
                    desktopFolder.EnumObjects(IntPtr.Zero, SHCONTF_FOLDERS | SHCONTF_NONFOLDERS | SHCONTF_INCLUDEHIDDEN, out enumIDList);
                    if (enumIDList != null)
                    {
                        IntPtr pidl = IntPtr.Zero;
                        uint fetched = 0;
                        while (enumIDList.Next(1, out pidl, out fetched) == 0 && fetched == 1)
                        {
                            STRRET strret;
                            desktopFolder.GetDisplayNameOf(pidl, SHGDN_NORMAL, out strret);

                            StringBuilder sb = new StringBuilder(260);
                            StrRetToBuf(ref strret, pidl, sb, (uint)sb.Capacity);
                            string itemName = sb.ToString();

                            if (string.Equals(itemName, name, StringComparison.OrdinalIgnoreCase))
                            {
                                var shinfo = new SHFILEINFO();
                                SHGetFileInfo(pidl, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_PIDL | SHGFI_ICON | SHGFI_LARGEICON);
                                hIcon = shinfo.hIcon;
                                CoTaskMemFree(pidl);
                                break;
                            }

                            CoTaskMemFree(pidl);
                        }
                        Marshal.ReleaseComObject(enumIDList);
                    }
                    Marshal.ReleaseComObject(desktopFolder);
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"IconExtractor Error: {ex}"); }

            return hIcon;
        }
    }
}
