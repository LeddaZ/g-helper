using Microsoft.Win32;

namespace GHelper.Helpers
{
    /// <summary>
    /// Reads the user's Windows accent color, picking the shade Windows itself would
    /// use against the current light / dark background.
    /// Overridable with the "accent_color" config key: "default" keeps the classic
    /// G-Helper blue, a hex value ("#3AAEEF") forces a specific color.
    /// </summary>
    public static class AccentColor
    {
        public static readonly Color Default = Color.FromArgb(255, 58, 174, 239);

        private const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
        private const string DwmKey = @"Software\Microsoft\Windows\DWM";

        // AccentPalette is 8 RGBA entries, lightest first:
        // Light3, Light2, Light1, Accent, Dark1, Dark2, Dark3, (unused)
        private const int PaletteLight2 = 1;
        private const int PaletteAccent = 3;
        private const int PaletteDark1 = 4;
        private const int PaletteDark2 = 5;

        // Above this relative luminance an accent stops being readable on the light
        // theme's near-white background, so we step down the palette's dark shades.
        private const double LightThemeMaxLuminance = 0.4;

        public static Color Get(bool darkTheme)
        {
            string? custom = AppConfig.GetString("accent_color");

            if (custom is not null)
            {
                if (custom.ToLower() == "default") return Default;
                try
                {
                    return ColorTranslator.FromHtml(custom);
                }
                catch
                {
                    Logger.WriteLine("Invalid accent_color: " + custom);
                    return Default;
                }
            }

            return FromPalette(darkTheme) ?? FromDwm() ?? Default;
        }

        private static Color? FromPalette(bool darkTheme)
        {
            using var key = Registry.CurrentUser.OpenSubKey(AccentKey);
            if (key?.GetValue("AccentPalette") is not byte[] palette || palette.Length < 32) return null;

            // Windows puts a light shade on dark backgrounds and the base accent on light ones.
            if (darkTheme) return Entry(palette, PaletteLight2);

            foreach (int index in new[] { PaletteAccent, PaletteDark1, PaletteDark2 })
            {
                Color color = Entry(palette, index);
                if (Luminance(color) <= LightThemeMaxLuminance) return color;
            }

            return Entry(palette, PaletteDark2);
        }

        private static Color Entry(byte[] palette, int index)
        {
            int offset = index * 4;
            return Color.FromArgb(255, palette[offset], palette[offset + 1], palette[offset + 2]);
        }

        private static Color? FromDwm()
        {
            using var key = Registry.CurrentUser.OpenSubKey(DwmKey);
            if (key?.GetValue("AccentColor") is not int abgr) return null;

            // Stored as 0xAABBGGRR
            return Color.FromArgb(255, abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
        }

        /// <summary>Black or white, whichever stays readable on top of <paramref name="background"/>.</summary>
        public static Color Contrasting(Color background)
        {
            return Luminance(background) > 0.3 ? Color.Black : Color.White;
        }

        private static double Luminance(Color color)
        {
            return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        }

        private static double Channel(byte value)
        {
            double c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
    }
}
