using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopSnap
{
    public static class AppEnv
    {
        private static bool? _isPackaged;

        public static bool IsPackaged
        {
            get
            {
                if (!_isPackaged.HasValue)
                {
                    _isPackaged = CheckIsPackaged();
                }
                return _isPackaged.Value;
            }
        }

        private static bool CheckIsPackaged()
        {
            try
            {
                // In a desktop bridge app (MSIX), Package.Current.Id will succeed.
                // In a normal Win32/portable app, it will throw an exception.
                return Windows.ApplicationModel.Package.Current != null && 
                       Windows.ApplicationModel.Package.Current.Id != null;
            }
            catch
            {
                return false;
            }
        }

        public static string GetDataDirectory()
        {
            if (IsPackaged)
            {
                // Standard MSIX path for data
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopSnap");
            }
            else
            {
                // Portable mode: Use executable directory
                return Path.Combine(AppContext.BaseDirectory, "Config");
            }
        }
    }
}
