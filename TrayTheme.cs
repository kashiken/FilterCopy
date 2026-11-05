using Microsoft.Win32;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace FilterCopy;

internal static class TrayTheme
{
    public static void Apply(Forms.ContextMenuStrip menu)
    {
        if (Forms.SystemInformation.HighContrast)
        {
            menu.RenderMode = Forms.ToolStripRenderMode.System;
            menu.BackColor = SystemColors.Menu;
            menu.ForeColor = SystemColors.MenuText;
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            var colors = new MenuColors(dark);
            menu.Renderer = new Forms.ToolStripProfessionalRenderer(colors);
            menu.BackColor = colors.ToolStripDropDownBackground;
            menu.ForeColor = dark ? Color.FromArgb(242, 242, 242) : Color.FromArgb(26, 26, 26);
        }
        foreach (Forms.ToolStripItem item in menu.Items) item.ForeColor = menu.ForeColor;
    }

    private sealed class MenuColors(bool dark) : Forms.ProfessionalColorTable
    {
        private Color Surface => dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(249, 249, 249);
        private Color Hover => dark ? Color.FromArgb(58, 58, 58) : Color.FromArgb(229, 229, 229);
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemBorder => Hover;
        public override Color MenuBorder => dark ? Color.FromArgb(72, 72, 72) : Color.FromArgb(205, 205, 205);
    }
}
