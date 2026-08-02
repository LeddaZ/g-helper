using GHelper.Helpers;
using GHelper.UI;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms.DataVisualization.Charting;

public static class ControlHelper
{

    [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

    static bool _invert = false;
    static bool _darkMode = false;
    static Color? _oldAccent = null;
    static float _scale = 1;

    public static float Scale => _scale;
    public static bool DarkMode => _darkMode;

    public static void Adjust(RForm container, bool invert = false, Color? oldAccent = null)
    {

        container.BackColor = RForm.formBack;
        container.ForeColor = RForm.foreMain;

        _invert = invert;
        _oldAccent = oldAccent;
        _darkMode = container.darkTheme;
        AdjustControls(container.Controls);
        _invert = false;
        _oldAccent = null;

    }

    public static void Resize(RForm container, float baseScale = 2)
    {
        _scale = GetDpiScale(container).Value / baseScale;
        if (Math.Abs(_scale - 1) > 0.2) ResizeControls(container.Controls);

    }

    private static void ResizeControls(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            var button = control as RButton;
            if (button != null && button.Image is not null)
                button.Image = ResizeImage(button.Image);

            /*
            var pictureBox = control as PictureBox;
            if (pictureBox != null && pictureBox.BackgroundImage is not null)
                pictureBox.BackgroundImage = ResizeImage(pictureBox.BackgroundImage);
            */

            ResizeControls(control.Controls);
        }
    }


    private static void AdjustControls(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {

            AdjustControls(control.Controls);

            var button = control as RButton;
            if (button != null)
            {
                button.BackColor = button.Secondary ? RForm.buttonSecond : RForm.buttonMain;
                button.ForeColor = RForm.foreMain;

                button.FlatStyle = FlatStyle.Flat;
                if (!button.Borderless)
                    button.FlatAppearance.BorderColor = button.Secondary ? RForm.borderSecond : RForm.borderMain;

                if (button.Image is not null && _invert)
                    button.Image = AdjustImage(button.Image);

                // Accent borders are assigned once when a form is built, so on a live
                // accent change they have to be swapped on the buttons still carrying the old one.
                if (_oldAccent is Color previous && button.BorderColor == previous)
                    button.BorderColor = RForm.colorStandard;
            }

            var pictureBox = control as PictureBox;
            if (pictureBox != null && pictureBox.BackgroundImage is not null && _invert)
                pictureBox.BackgroundImage = AdjustImage(pictureBox.BackgroundImage);


            var combo = control as RComboBox;
            if (combo != null)
            {
                combo.BackColor = RForm.buttonMain;
                combo.ForeColor = RForm.foreMain;
                combo.BorderColor = RForm.borderMain;
                combo.ButtonColor = RForm.buttonMain;
                combo.ArrowColor = RForm.foreMain;
            }
            var rNumeric = control as RNumericUpDown;
            if (rNumeric is not null)
            {
                rNumeric.ApplyTheme(_darkMode);
            }
            else if (control is NumericUpDown numbericUpDown)
            {
                numbericUpDown.ForeColor = RForm.foreMain;
                numbericUpDown.BackColor = RForm.buttonMain;
            }

            var rText = control as RTextBox;
            if (rText is not null)
            {
                rText.ApplyTheme(_darkMode);
            }

            var gb = control as GroupBox;
            if (gb != null)
            {
                gb.ForeColor = RForm.foreMain;
            }

            var pn = control as Panel;
            if (pn != null && pn.Name.Contains("Header"))
            {
                pn.BackColor = RForm.buttonSecond;
            }

            var sl = control as Slider;
            if (sl != null)
            {
                sl.borderColor = RForm.buttonMain;
                sl.accentColor = RForm.colorStandard;
            }

            var chk = control as CheckBox;
            if (chk != null)
            {
                if (chk.BackColor != RForm.formBack)
                {
                    chk.BackColor = RForm.buttonSecond;
                    if (chk is RCheckBox)
                        chk.FlatAppearance.BorderColor = RForm.borderSecond;
                }
                SetWindowTheme(chk.Handle, _darkMode ? "DarkMode_Explorer" : "Explorer", null);

                if (chk.Appearance == Appearance.Normal)
                {
                    chk.Paint -= PaintCheckIndicator;
                    chk.Paint += PaintCheckIndicator;
                    chk.Invalidate();
                }
            }

            var chart = control as Chart;
            if (chart != null)
            {
                chart.BackColor = RForm.chartMain;
                chart.ChartAreas[0].BackColor = RForm.chartMain;

                chart.ChartAreas[0].AxisX.TitleForeColor = RForm.foreMain;
                chart.ChartAreas[0].AxisY.TitleForeColor = RForm.foreMain;

                chart.ChartAreas[0].AxisX.LabelStyle.ForeColor = RForm.foreMain;
                chart.ChartAreas[0].AxisY.LabelStyle.ForeColor = RForm.foreMain;

                chart.ChartAreas[0].AxisX.MajorTickMark.LineColor = RForm.foreMain;
                chart.ChartAreas[0].AxisY.MajorTickMark.LineColor = RForm.foreMain;

                chart.ChartAreas[0].AxisX.MajorGrid.LineColor = RForm.chartGrid;
                chart.ChartAreas[0].AxisY.MajorGrid.LineColor = RForm.chartGrid;
                chart.ChartAreas[0].AxisX.LineColor = RForm.chartGrid;
                chart.ChartAreas[0].AxisY.LineColor = RForm.chartGrid;

                chart.Titles[0].ForeColor = RForm.foreMain;

            }

        }
    }

    // The themed WinForms glyph ignores the accent color, so the indicator is redrawn
    // on top of it. Erasing a slightly larger area first means our box doesn't have to
    // line up with the theme's pixel for pixel.
    private const int CheckGlyphSize = 13;

    private static void PaintCheckIndicator(object? sender, PaintEventArgs e)
    {
        if (sender is not CheckBox chk) return;

        var g = e.Graphics;
        float scale = g.DpiX / 96f;
        int size = (int)Math.Round(CheckGlyphSize * scale);
        Rectangle glyph = GlyphBounds(chk, size);

        using (var back = new SolidBrush(chk.BackColor))
            g.FillRectangle(back, Rectangle.Inflate(glyph, 2, 2));

        SmoothingMode prev = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int radius = Math.Max(2, (int)Math.Round(3 * scale));
        var box = new Rectangle(glyph.X, glyph.Y, glyph.Width - 1, glyph.Height - 1);
        Color fillColor = chk.Enabled ? RForm.colorStandard : RForm.colorGray;

        using (GraphicsPath path = RComboBox.RoundedRect(box, radius, radius))
        {
            if (chk.CheckState == CheckState.Unchecked)
            {
                // Half-transparent foreground reads as a mid grey against either theme.
                using var pen = new Pen(Color.FromArgb(chk.Enabled ? 140 : 70, RForm.foreMain), scale);
                g.DrawPath(pen, path);
            }
            else
            {
                using var fill = new SolidBrush(fillColor);
                g.FillPath(fill, path);
            }
        }

        if (chk.CheckState != CheckState.Unchecked)
        {
            using var tick = new Pen(AccentColor.Contrasting(fillColor), Math.Max(1.4f, 1.5f * scale))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            g.DrawLines(tick, new[]
            {
                new PointF(box.X + box.Width * 0.24f, box.Y + box.Height * 0.52f),
                new PointF(box.X + box.Width * 0.43f, box.Y + box.Height * 0.72f),
                new PointF(box.X + box.Width * 0.76f, box.Y + box.Height * 0.30f)
            });
        }

        g.SmoothingMode = prev;
    }

    private static Rectangle GlyphBounds(CheckBox chk, int size)
    {
        const ContentAlignment right = ContentAlignment.TopRight | ContentAlignment.MiddleRight | ContentAlignment.BottomRight;
        const ContentAlignment center = ContentAlignment.TopCenter | ContentAlignment.MiddleCenter | ContentAlignment.BottomCenter;
        const ContentAlignment top = ContentAlignment.TopLeft | ContentAlignment.TopCenter | ContentAlignment.TopRight;
        const ContentAlignment bottom = ContentAlignment.BottomLeft | ContentAlignment.BottomCenter | ContentAlignment.BottomRight;

        // WinForms lays the glyph out inside the padded area, not the raw client rect.
        Rectangle client = chk.ClientRectangle;
        Padding pad = chk.Padding;
        Rectangle content = Rectangle.FromLTRB(
            client.Left + pad.Left,
            client.Top + pad.Top,
            Math.Max(client.Left + pad.Left + size, client.Right - pad.Right),
            Math.Max(client.Top + pad.Top + size, client.Bottom - pad.Bottom));

        ContentAlignment align = chk.CheckAlign;

        int x = (align & right) != 0 ? content.Right - size
              : (align & center) != 0 ? content.X + (content.Width - size) / 2
              : content.X;

        int y = (align & bottom) != 0 ? content.Bottom - size
              : (align & top) != 0 ? content.Y
              : content.Y + (content.Height - size) / 2;

        return new Rectangle(x, y, size, size);
    }

    public static Lazy<float> GetDpiScale(Control control)
    {
        return new Lazy<float>(() =>
        {
            using (var graphics = control.CreateGraphics())
                return graphics.DpiX / 96.0f;
        });
    }

    private static Image ResizeImage(Image image)
    {
        return ResizeImage(image, _scale);
    }

    public static Image ResizeImage(Image image, float scale)
    {
        if (Math.Abs(scale - 1) < 0.1) return image;

        var newSize = new Size((int)(image.Width * scale), (int)(image.Height * scale));
        var pic = new Bitmap(newSize.Width, newSize.Height);

        using (var g = Graphics.FromImage(pic))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, new Rectangle(new Point(), newSize));
        }
        return pic;
    }

    // Design tokens
    private const float GradientHeightFraction = 0.3f;
    private const float LightGradientHeightFraction = 0.9f;
    private const int TopFadeAlpha = 64;

    public static void DrawGradientBorder(Graphics g, Rectangle bounds, Color sideColor, int radius, float strokeWidth = 1f, PenAlignment alignment = PenAlignment.Center, float topLighten = 0.1f)
    {
        Color topColor = !_darkMode && strokeWidth <= 1f
            ? Color.FromArgb(TopFadeAlpha, sideColor)
            : Color.FromArgb(sideColor.A,
                (int)(sideColor.R + (255 - sideColor.R) * topLighten),
                (int)(sideColor.G + (255 - sideColor.G) * topLighten),
                (int)(sideColor.B + (255 - sideColor.B) * topLighten));

        float flatHeight = Math.Max(1f, strokeWidth);
        float gradHeight = (float)Math.Round(bounds.Height * (_darkMode ? GradientHeightFraction : LightGradientHeightFraction));
        float pad = strokeWidth;
        float axisStart = bounds.Y - pad;
        float axisEnd = bounds.Y + bounds.Height + pad;
        float axisLen = axisEnd - axisStart;
        float p1 = Math.Max(0f, Math.Min(0.98f, (pad + flatHeight) / axisLen));
        float p2 = Math.Max(p1 + 0.01f, Math.Min(1f, (pad + flatHeight + gradHeight) / axisLen));

        using (GraphicsPath path = RComboBox.RoundedRect(bounds, radius, radius))
        using (LinearGradientBrush brush = new LinearGradientBrush(
            new PointF(0, axisStart), new PointF(0, axisEnd),
            topColor, sideColor))
        {
            brush.InterpolationColors = new ColorBlend(4)
            {
                Colors = new[] { topColor, topColor, sideColor, sideColor },
                Positions = new[] { 0f, p1, p2, 1f }
            };
            using (Pen pen = new Pen(brush, strokeWidth) { Alignment = alignment })
            {
                SmoothingMode prev = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawPath(pen, path);
                g.SmoothingMode = prev;
            }
        }
    }

    private static readonly ImageAttributes _invertAttributes = CreateInvertAttributes();

    private static ImageAttributes CreateInvertAttributes()
    {
        var matrix = new ColorMatrix(new[]
        {
            new float[] { -1,  0,  0, 0, 0 },
            new float[] {  0, -1,  0, 0, 0 },
            new float[] {  0,  0, -1, 0, 0 },
            new float[] {  0,  0,  0, 1, 0 },
            new float[] {  1,  1,  1, 0, 1 }
        });
        var attr = new ImageAttributes();
        attr.SetColorMatrix(matrix);
        return attr;
    }

    private static Image AdjustImage(Image image)
    {
        var pic = new Bitmap(image.Width, image.Height);
        using (var g = Graphics.FromImage(pic))
        {
            g.DrawImage(image,
                new Rectangle(0, 0, image.Width, image.Height),
                0, 0, image.Width, image.Height,
                GraphicsUnit.Pixel, _invertAttributes);
        }
        return pic;
    }

    public static Image TintImage(Image image, Color tintColor)
    {
        var pic = new Bitmap(image);

        for (int y = 0; (y <= (pic.Height - 1)); y++)
        {
            for (int x = 0; (x <= (pic.Width - 1)); x++)
            {
                Color col = pic.GetPixel(x, y);
                pic.SetPixel(x, y, Color.FromArgb(col.A, tintColor.R, tintColor.G, tintColor.B));
            }
        }

        return pic;
    }

    public static Image RecolorDarkPixels(Image image, Color targetColor, byte luminanceThreshold = 128)
    {
        var pic = new Bitmap(image);
        for (int y = 0; y < pic.Height; y++)
        {
            for (int x = 0; x < pic.Width; x++)
            {
                Color col = pic.GetPixel(x, y);
                if (col.A == 0) continue;
                int lum = (col.R + col.G + col.B) / 3;
                if (lum < luminanceThreshold)
                    pic.SetPixel(x, y, Color.FromArgb(col.A, targetColor));
            }
        }
        return pic;
    }

    public static Image OverlayBadge(Image baseImage, Image badge, Color circleColor,
        float badgeScale = 0.5f, float shiftFraction = 0.18f,
        int? iconWidth = null, int? iconHeight = null)
    {
        int iw = iconWidth ?? baseImage.Width;
        int ih = iconHeight ?? baseImage.Height;

        int badgeSize = (int)(iw * badgeScale);
        int shift = (int)(badgeSize * shiftFraction);

        int newW = Math.Max(baseImage.Width, iw + shift);
        int newH = Math.Max(baseImage.Height, ih + shift);

        var pic = new Bitmap(newW, newH);
        using (var g = Graphics.FromImage(pic))
        using (var coloredBadge = (Bitmap)RecolorDarkPixels(badge, circleColor))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawImage(baseImage, 0, 0, baseImage.Width, baseImage.Height);

            int badgeX = iw - badgeSize + shift;
            int badgeY = ih - badgeSize + shift;
            g.DrawImage(coloredBadge, badgeX, badgeY, badgeSize, badgeSize);
        }
        return pic;
    }

    public static Image OverlayChargeBars(Image baseImage, int level, int max, Color color,
        int? iconWidth = null, int? iconHeight = null)
    {
        if (max <= 0) return baseImage;

        int iw = iconWidth ?? baseImage.Width;
        int ih = iconHeight ?? baseImage.Height;

        float s = iw / 48f;
        int barHeight = Math.Max(2, (int)Math.Round(10 * s));
        int barWidth = Math.Max(1, (int)Math.Round(4 * s));
        int barGap = Math.Max(1, (int)Math.Round(2 * s));
        int totalGap = barGap * (max - 1);
        int usedW = barWidth * max + totalGap;
        int xStart = (iw - usedW) / 2;

        // If a previous overlay extended the canvas below the icon (e.g. a corner badge),
        // start the bars below that extension; otherwise sit them right under the icon.
        int yStart = baseImage.Height > ih
            ? baseImage.Height + Math.Max(2, (int)Math.Round(2 * s))
            : ih + Math.Max(2, (int)Math.Round(3 * s));

        int newH = Math.Max(baseImage.Height, yStart + barHeight);
        int newW = Math.Max(baseImage.Width, iw);

        var pic = new Bitmap(newW, newH);
        using (var g = Graphics.FromImage(pic))
        using (var filled = new SolidBrush(color))
        using (var empty = new SolidBrush(Color.FromArgb(72, color)))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(baseImage, 0, 0, baseImage.Width, baseImage.Height);

            for (int i = 0; i < max; i++)
            {
                var rect = new Rectangle(xStart + i * (barWidth + barGap), yStart, barWidth, barHeight);
                g.FillRectangle(i < level ? filled : empty, rect);
            }
        }
        return pic;
    }

}
