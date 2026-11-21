using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace GHelper.Helpers
{
    /// <summary>
    /// Per-mode override for the Windows "Transparency effects" setting.
    /// A mode with "disable_transparency" turns transparency off; leaving it restores
    /// transparency only if G-Helper was the one that turned it off.
    /// </summary>
    public static class TransparencyControl
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string ValueName = "EnableTransparency";

        // Set while transparency is off because of a mode, so it can be restored later.
        private const string RestoreFlag = "transparency_restore";

        private const int HWND_BROADCAST = 0xFFFF;
        private const int WM_SETTINGCHANGE = 0x001A;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

        private static readonly object _lock = new();

        public static bool IsDisabledForMode() => AppConfig.IsMode("disable_transparency");

        public static void SetDisabledForMode(bool disabled)
        {
            AppConfig.SetMode("disable_transparency", disabled ? 1 : 0);
        }

        public static void Apply()
        {
            bool disable = IsDisabledForMode();

            lock (_lock)
            {
                try
                {
                    if (disable)
                    {
                        if (IsEnabled() == false) return;
                        if (SetEnabled(false)) AppConfig.Set(RestoreFlag, 1);
                    }
                    else if (AppConfig.Is(RestoreFlag))
                    {
                        if (SetEnabled(true)) AppConfig.Remove(RestoreFlag);
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("Transparency: " + ex.Message);
                }
            }
        }

        private static bool? IsEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            if (key?.GetValue(ValueName) is int value) return value != 0;
            return null;
        }

        private static bool SetEnabled(bool enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey, true);
            if (key is null) return false;

            key.SetValue(ValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
            Logger.WriteLine("Transparency effects: " + (enabled ? "on" : "off"));

            // Lets Explorer, the taskbar and other apps pick up the change without a sign-out.
            SendMessageTimeout((IntPtr)HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet", SMTO_ABORTIFHUNG, 1000, out _);
            return true;
        }
    }
}
