using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using AutoGameSoftwareSync.PlayniteExtension.Logic;

namespace AutoGameSoftwareSync.PlayniteExtension.UI
{
    /// <summary>Builds and fills the badge: [shield][State · date · ● device]. Uses the theme's own styles and brushes when present.</summary>
    internal static class BadgeFactory
    {
        public const string BadgeName = "AgssBadge";

        // Shield with check mark (Material Design Icons, Apache 2.0).
        internal static readonly Geometry ShieldGeometry = Freeze(Geometry.Parse(
            "M12,1L3,5V11C3,16.55 6.84,21.74 12,23C17.16,21.74 21,16.55 21,11V5L12,1M10,17L6,13L7.41,11.59L10,14.17L16.59,7.58L18,9L10,17Z"));

        private static Geometry Freeze(Geometry g) { g.Freeze(); return g; }

        private class Parts { public Path Icon; public TextBlock Text; public bool InlineIcon; }
        // Parts kept aside (never in Tag: some themes use Tag in their styles).
        private static readonly ConditionalWeakTable<FrameworkElement, Parts> parts = new ConditionalWeakTable<FrameworkElement, Parts>();

        internal static void Attach(FrameworkElement badge, Path icon, TextBlock text, bool inlineIcon)
        {
            parts.Remove(badge);
            parts.Add(badge, new Parts { Icon = icon, Text = text, InlineIcon = inlineIcon });
        }

        /// <summary>Badge welded to the theme's boxes (recipe). Null when the theme's box style is missing.</summary>
        public static StackPanel BuildWelded(FrameworkElement scope, BadgeRecipe r)
        {
            var box = scope.TryFindResource(r.BoxStyleKey) as Style;
            if (box == null) return null;
            var iconStyle = string.IsNullOrEmpty(r.IconStyleKey) ? null : scope.TryFindResource(r.IconStyleKey) as Style;
            var textStyle = string.IsNullOrEmpty(r.TextStyleKey) ? null : scope.TryFindResource(r.TextStyleKey) as Style;

            var icon = new Path { Data = ShieldGeometry, Tag = r.IconTag };
            if (iconStyle != null) icon.Style = iconStyle; else { icon.Width = icon.Height = 17; icon.Stretch = Stretch.Uniform; icon.VerticalAlignment = VerticalAlignment.Center; }
            var text = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
            if (textStyle != null) text.Style = textStyle;

            var group = new StackPanel { Name = BadgeName, Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, r.RightMargin, 0) };
            group.Children.Add(new Border { Style = box, Tag = r.LeftTag, Child = icon });
            group.Children.Add(new Border { Style = box, Tag = r.RightTag, Child = text });
            Attach(group, icon, text, false);
            return group;
        }

        /// <summary>Self-contained badge for themes hosting the custom element (no recipe needed).</summary>
        public static FrameworkElement BuildStandalone(bool withIcon)
        {
            var icon = new Path { Data = ShieldGeometry, Width = 16, Height = 16, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            if (withIcon) panel.Children.Add(icon);
            panel.Children.Add(text);
            var border = new Border
            {
                Name = BadgeName,
                Child = panel,
                Background = Brushes.Transparent, // the theme decides the background around the element
            };
            Attach(border, withIcon ? icon : null, text, false);
            return border;
        }

        private static Brush StateBrush(FrameworkElement scope, StateTone tone)
        {
            foreach (var key in ColorMath.ResourceKeys(tone))
                if (scope.TryFindResource(key) is Brush b) return b;
            var c = ColorMath.Fallback(tone);
            return new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        }

        private static Rgb TextColor(FrameworkElement scope, string key)
        {
            if (scope.TryFindResource(string.IsNullOrEmpty(key) ? "TextBrush" : key) is SolidColorBrush b)
                return new Rgb(b.Color.R, b.Color.G, b.Color.B);
            return new Rgb(0xFF, 0xFF, 0xFF); // fullscreen themes are dark: assume light text
        }

        public static void Fill(FrameworkElement badge, FrameworkElement scope, BadgeModel m, string textBrushKey)
        {
            Parts p;
            if (m == null || !parts.TryGetValue(badge, out p)) return;
            var stateBrush = StateBrush(scope, m.Tone);
            if (p.Icon != null) p.Icon.Fill = stateBrush;
            var t = p.Text;
            t.Inlines.Clear();
            if (p.InlineIcon)
            {
                double size = Math.Max(12, t.FontSize * 0.9);
                var icon = new Path { Data = ShieldGeometry, Stretch = Stretch.Uniform, Width = size, Height = size, Fill = stateBrush, Margin = new Thickness(0, 0, 6, 0) };
                t.Inlines.Add(new InlineUIContainer(icon) { BaselineAlignment = BaselineAlignment.Center });
            }
            t.Inlines.Add(new Run(m.StateText) { Foreground = stateBrush });
            if (m.WhenText != null) t.Inlines.Add(new Run(" · " + m.WhenText)); // theme text colour (inherited)
            if (m.DeviceText != null)
            {
                t.Inlines.Add(new Run(" · "));
                var run = new Run("● " + m.DeviceText);
                if (m.DeviceColor.HasValue)
                {
                    var c = ColorMath.EnsureReadable(m.DeviceColor.Value, ColorMath.BackgroundForText(TextColor(scope, textBrushKey)));
                    run.Foreground = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
                }
                t.Inlines.Add(run);
            }
        }
    }
}
