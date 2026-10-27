using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using AutoGameSoftwareSync.PlayniteExtension.Logic;

namespace AutoGameSoftwareSync.PlayniteExtension.UI
{
    /// <summary>
    /// Builds a badge that copies the look of an existing entry of the theme (label + value, separators, boxes...):
    /// the label becomes "AGSS", the value becomes the coloured state text, other data-bound texts are left out.
    /// Only the values written in the theme are copied (styles keep working); nothing of the theme is modified.
    /// </summary>
    internal static class MimicBuilder
    {
        private class Ctx
        {
            public TextBlock Anchor;
            public BadgeRecipe Recipe;
            public bool LabelDone;
            public TextBlock Value;
            public Path Icon;
        }

        /// <summary>Text binding path of a TextBlock, or null.</summary>
        internal static string BindingPath(TextBlock tb)
        {
            try
            {
                var be = BindingOperations.GetBindingExpression(tb, TextBlock.TextProperty);
                return be?.ParentBinding?.Path?.Path;
            }
            catch { return null; }
        }

        private static bool IsDataBound(TextBlock tb)
        {
            try { return BindingOperations.GetBindingExpressionBase(tb, TextBlock.TextProperty) != null; } catch { return false; }
        }

        /// <summary>Copies the values the theme wrote on the element (local or from its template), not inherited/default ones.</summary>
        private static void Copy(DependencyObject src, DependencyObject dst, params DependencyProperty[] props)
        {
            foreach (var p in props)
            {
                try
                {
                    var source = DependencyPropertyHelper.GetValueSource(src, p).BaseValueSource;
                    if (source == BaseValueSource.Local || source == BaseValueSource.ParentTemplate || source == BaseValueSource.ParentTemplateTrigger)
                    {
                        var v = src.GetValue(p);
                        if (v is Freezable f && !f.IsFrozen && f.CanFreeze) v = f.GetAsFrozen();
                        dst.SetValue(p, v);
                    }
                }
                catch { }
            }
        }

        private static readonly DependencyProperty[] Common =
        {
            FrameworkElement.StyleProperty, FrameworkElement.MarginProperty, FrameworkElement.WidthProperty, FrameworkElement.MinWidthProperty,
            FrameworkElement.MaxWidthProperty, FrameworkElement.HeightProperty, FrameworkElement.MinHeightProperty,
            FrameworkElement.HorizontalAlignmentProperty, FrameworkElement.VerticalAlignmentProperty, UIElement.OpacityProperty,
            FrameworkElement.TagProperty, Grid.ColumnProperty, Grid.RowProperty, Grid.ColumnSpanProperty, Grid.RowSpanProperty, DockPanel.DockProperty,
            UIElement.SnapsToDevicePixelsProperty
        };

        private static readonly DependencyProperty[] TextProps =
        {
            TextBlock.FontSizeProperty, TextBlock.FontFamilyProperty, TextBlock.FontWeightProperty, TextBlock.FontStyleProperty,
            TextBlock.ForegroundProperty, TextBlock.TextWrappingProperty, TextBlock.TextTrimmingProperty, TextBlock.TextAlignmentProperty,
            TextBlock.PaddingProperty, TextBlock.LineHeightProperty
        };

        private static TextBlock CopyText(TextBlock src)
        {
            var t = new TextBlock();
            Copy(src, t, Common);
            Copy(src, t, TextProps);
            return t;
        }

        private static Path ShieldFor(TextBlock like, Brush placeholder)
        {
            double size = Math.Max(12, like.FontSize);
            var p = new Path { Data = BadgeFactory.ShieldGeometry, Stretch = Stretch.Uniform, Width = size, Height = size, Fill = placeholder, VerticalAlignment = VerticalAlignment.Center };
            Copy(like, p, FrameworkElement.MarginProperty, UIElement.OpacityProperty, FrameworkElement.VerticalAlignmentProperty, Grid.ColumnProperty, Grid.RowProperty);
            return p;
        }

        private static FrameworkElement Clone(FrameworkElement src, Ctx ctx)
        {
            if (src == null) return null;
            if (src == ctx.Anchor)
            {
                // Width and wrapping of the theme's value are kept: a long state wraps like the theme's own values.
                ctx.Value = CopyText(ctx.Anchor);
                if (ctx.Value.TextTrimming != TextTrimming.None && ctx.Value.TextWrapping == TextWrapping.NoWrap) ctx.Value.TextWrapping = TextWrapping.Wrap;
                return ctx.Value;
            }
            switch (src)
            {
                case TextBlock tb:
                {
                    if (IsDataBound(tb)) return null; // other game data: not repeated
                    var text = tb.Text ?? "";
                    if (!ctx.LabelDone && text.Trim().Length >= 3)
                    {
                        ctx.LabelDone = true;
                        var l = CopyText(tb); l.Text = ctx.Recipe.LabelText; return l;
                    }
                    if (ctx.Recipe.GlyphToIcon && ctx.Icon == null && text.Trim().Length > 0 && text.Trim().Length <= 2 && !text.Trim().All(char.IsPunctuation))
                    {
                        ctx.Icon = ShieldFor(tb, tb.Foreground);
                        return ctx.Icon;
                    }
                    var c = CopyText(tb); c.Text = text; return c;
                }
                case ContentControl cc when !(cc is UserControl):
                {
                    if (!ctx.LabelDone && cc.Content is string s && s.Trim().Length >= 3)
                    {
                        ctx.LabelDone = true;
                        var l = new ContentControl { Focusable = false, Content = ctx.Recipe.LabelText };
                        Copy(cc, l, Common);
                        Copy(cc, l, Control.FontSizeProperty, Control.FontFamilyProperty, Control.FontWeightProperty, Control.ForegroundProperty, ContentControl.ContentStringFormatProperty);
                        return l;
                    }
                    return null;
                }
                case Border b:
                {
                    var child = Clone(b.Child as FrameworkElement, ctx);
                    if (child == null) return null;
                    var nb = new Border { Child = child };
                    Copy(b, nb, Common);
                    Copy(b, nb, Border.BackgroundProperty, Border.BorderBrushProperty, Border.BorderThicknessProperty, Border.CornerRadiusProperty, Border.PaddingProperty);
                    nb.ClearValue(FrameworkElement.WidthProperty);
                    return nb;
                }
                case Rectangle r:
                {
                    var nr = new Rectangle();
                    Copy(r, nr, Common);
                    Copy(r, nr, Shape.FillProperty, Shape.StrokeProperty, Shape.StrokeThicknessProperty);
                    return nr;
                }
                case Panel p when !p.IsItemsHost && (p is StackPanel || p is Grid || p is WrapPanel || p is DockPanel):
                {
                    Panel np;
                    if (p is StackPanel sp) np = new StackPanel { Orientation = sp.Orientation };
                    else if (p is WrapPanel wp) np = new WrapPanel { Orientation = wp.Orientation };
                    else if (p is DockPanel dp) np = new DockPanel { LastChildFill = dp.LastChildFill };
                    else
                    {
                        var g = (Grid)p; var ng = new Grid();
                        foreach (var cd in g.ColumnDefinitions) ng.ColumnDefinitions.Add(new ColumnDefinition { Width = cd.Width, MinWidth = cd.MinWidth, MaxWidth = cd.MaxWidth });
                        foreach (var rd in g.RowDefinitions) ng.RowDefinitions.Add(new RowDefinition { Height = rd.Height, MinHeight = rd.MinHeight, MaxHeight = rd.MaxHeight });
                        np = ng;
                    }
                    Copy(p, np, Common);
                    Copy(p, np, Panel.BackgroundProperty);
                    bool any = false;
                    foreach (var child in p.Children.OfType<FrameworkElement>().ToList())
                    {
                        if (child.Name == BadgeFactory.BadgeName) continue;
                        var c = Clone(child, ctx);
                        if (c != null) { np.Children.Add(c); any = true; }
                    }
                    return any && ctx.Value != null ? np : (any ? np : null);
                }
                default:
                    return null;
            }
        }

        /// <summary>Badge for a Clone/CloneWithPrevious/Line recipe. Null when nothing usable could be built.</summary>
        public static FrameworkElement Build(BadgeRecipe r, FrameworkElement slot, FrameworkElement previous, TextBlock anchor)
        {
            var ctx = new Ctx { Anchor = anchor, Recipe = r };
            FrameworkElement result;
            switch (r.Layout)
            {
                case BadgeLayout.Line:
                {
                    ctx.Value = CopyText(anchor);
                    ctx.Value.ClearValue(FrameworkElement.MarginProperty);
                    ctx.Value.ClearValue(FrameworkElement.WidthProperty);
                    ctx.Value.ClearValue(FrameworkElement.HorizontalAlignmentProperty);
                    ctx.Value.TextWrapping = TextWrapping.Wrap;
                    // DockPanel (not a horizontal StackPanel): the text gets the width of the block and wraps instead of being cut.
                    var line = new DockPanel { LastChildFill = true, HorizontalAlignment = HorizontalAlignment.Stretch };
                    var m = r.LineMargin ?? new double[] { 0, 10, 0, 0 };
                    line.Margin = new Thickness(m[0], m[1], m[2], m[3]);
                    ctx.Icon = ShieldFor(ctx.Value, Brushes.Transparent);
                    ctx.Icon.Margin = new Thickness(0, 0, 8, 0);
                    line.Children.Add(ctx.Icon);
                    line.Children.Add(ctx.Value);
                    result = line;
                    break;
                }
                case BadgeLayout.CloneWithPrevious:
                {
                    var label = Clone(previous, ctx);
                    var value = Clone(slot, ctx);
                    if (value == null) return null;
                    var wrap = new StackPanel { Orientation = Orientation.Vertical };
                    if (label != null) wrap.Children.Add(label);
                    wrap.Children.Add(value);
                    result = wrap;
                    break;
                }
                default:
                    result = Clone(slot, ctx);
                    break;
            }
            if (result == null || ctx.Value == null) return null;
            result.Name = BadgeFactory.BadgeName;
            BadgeFactory.Attach(result, ctx.Icon, ctx.Value, inlineIcon: ctx.Icon == null);
            return result;
        }
    }
}
