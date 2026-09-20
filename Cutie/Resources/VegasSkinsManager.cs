using ScriptPortal.MediaSoftware.Skins;
using System.Windows;
using System.Windows.Media;

namespace Cutie.Resources
{
    public static class VegasSkinsManager
    {
        public static void Apply(
            ResourceDictionary resources,
            SkinColors colors)
        {
            Set(resources, "Vegas.ButtonFace", colors.ButtonFace);
            Set(resources, "Vegas.ButtonText", colors.ButtonText);
            Set(resources, "Vegas.ButtonShadow", colors.ButtonShadow);
            Set(resources, "Vegas.ButtonHighlight", colors.ButtonHighlight);

            Set(resources, "Vegas.ControlLight", colors.ControlLight);
            Set(resources, "Vegas.ControlLightLight", colors.ControlLightLight);
            Set(resources, "Vegas.ControlDark", colors.ControlDark);
            Set(resources, "Vegas.ControlDarkDark", colors.ControlDarkDark);

            Set(resources, "Vegas.GrayText", colors.GrayText);

            Set(resources, "Vegas.Highlight", colors.Highlight);
            Set(resources, "Vegas.HighlightText", colors.HighlightText);

            Set(resources, "Vegas.MenuBackground", colors.MenuBackground);
            Set(resources, "Vegas.MenuText", colors.MenuText);

            Set(resources, "Vegas.WindowBackground", colors.WindowBackground);
            Set(resources, "Vegas.WindowText", colors.WindowText);

            Set(resources, "Vegas.Background", colors.Background);
        }

        private static void Set(
            ResourceDictionary resources,
            string name,
            System.Drawing.Color drawingColor)
        {
            var color = Color.FromArgb(
                drawingColor.A,
                drawingColor.R,
                drawingColor.G,
                drawingColor.B);

            var brush = new SolidColorBrush(color);

            if (brush.CanFreeze)
                brush.Freeze();

            resources[name + "Color"] = color;
            resources[name + "Brush"] = brush;
        }
    }
}
