using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AutoGameSoftwareSync.PlayniteExtension.Logic;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace AutoGameSoftwareSync.PlayniteExtension.UI
{
    /// <summary>
    /// Inserts the badge at run time into the theme's game page, right after the anchor box named by the recipe.
    /// No theme file is changed. Detection: scan at start + game selection + a light timer (the Loaded event is not
    /// raised for the targeted row). Never duplicates, re-inserts when the view is recreated, does nothing when the
    /// targeted elements are missing. Reports a durable failure so that the caller can switch to the genre fallback.
    /// </summary>
    internal sealed class BadgeInjector
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const int TickMs = 750;
        /// <summary>Game page visible for this long without a usable row = durable failure.</summary>
        private const int FailAfterMs = 8000;
        /// <summary>While the game page is not shown, the tree is scanned only every N ticks.</summary>
        private const int IdleScanEveryTicks = 4;

        private readonly BadgeRecipe recipe;
        private readonly Func<Game, BadgeModel> modelFor;
        private readonly Func<Game> selectedGame;
        private readonly Func<bool?> detailsVisible;
        private readonly ConditionalWeakTable<Panel, object> hooked = new ConditionalWeakTable<Panel, object>();
        private WeakReference<Panel> lastRow;
        private DispatcherTimer timer;
        private int ticks, failMs;
        private bool stopped;

        public bool FailedDurably { get; private set; }
        public event Action Failed;
        public int Insertions { get; private set; }

        /// <param name="detailsVisible">True when the game page is shown, null when unknown.</param>
        public BadgeInjector(BadgeRecipe recipe, Func<Game, BadgeModel> modelFor, Func<Game> selectedGame, Func<bool?> detailsVisible)
        {
            this.recipe = recipe;
            this.modelFor = modelFor;
            this.selectedGame = selectedGame;
            this.detailsVisible = detailsVisible;
        }

        public void Start()
        {
            var app = Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(new Action(() => Rescan()), DispatcherPriority.Loaded);
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(TickMs) };
            timer.Tick += (s, a) => Tick();
            timer.Start();
            logger.Info("AGSS: badge insertion active for theme recipe " + recipe.Name);
        }

        /// <summary>Stops and hides every inserted badge (switch to another display).</summary>
        public void Stop()
        {
            stopped = true;
            try { timer?.Stop(); } catch { }
            timer = null;
            try
            {
                var app = Application.Current;
                if (app == null) return;
                foreach (Window w in app.Windows)
                    foreach (var row in FindRows(w))
                        foreach (var b in row.Children.OfType<FrameworkElement>().Where(c => c.Name == BadgeFactory.BadgeName).ToList())
                            row.Children.Remove(b);
            }
            catch (Exception ex) { logger.Warn(ex, "AGSS: badge removal failed"); }
        }

        private bool KnownRowAlive(out Panel row)
        {
            row = null;
            return lastRow != null && lastRow.TryGetTarget(out row) && row.IsLoaded;
        }

        private void Tick()
        {
            if (stopped) return;
            try
            {
                ticks++;
                bool? details = null;
                try { details = detailsVisible(); } catch { }
                if (!KnownRowAlive(out _))
                {
                    if (details != false || ticks % IdleScanEveryTicks == 0) Rescan();
                }
                // Durable failure: game page shown but no usable row for a while.
                if (details == true && !KnownRowAlive(out _))
                {
                    failMs += TickMs;
                    if (failMs >= FailAfterMs && !FailedDurably)
                    {
                        FailedDurably = true;
                        logger.Warn("AGSS: theme elements of recipe " + recipe.Name + " not found, switching to the genre fallback");
                        Stop();
                        Failed?.Invoke();
                    }
                }
                else if (details != true) failMs = 0;
            }
            catch (Exception ex) { logger.Warn(ex, "AGSS: badge tick failed"); }
        }

        /// <summary>Called on game selection: refreshes the known row, or scans again when it is gone.</summary>
        public void OnSelectionChanged()
        {
            var app = Application.Current;
            if (app == null || stopped) return;
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (KnownRowAlive(out var row)) Refresh(row);
                else Rescan();
            }), DispatcherPriority.Background); // after the theme bindings
        }

        /// <summary>Status changed: refresh the visible badge.</summary>
        public void RefreshAll()
        {
            if (stopped) return;
            if (KnownRowAlive(out var row)) Refresh(row);
        }

        private void Rescan()
        {
            if (stopped) return;
            try
            {
                var app = Application.Current;
                if (app == null) return;
                foreach (Window w in app.Windows)
                    foreach (var row in FindRows(w)) Hook(row);
            }
            catch (Exception ex) { logger.Warn(ex, "AGSS: badge scan failed"); }
        }

        /// <summary>Type of the fullscreen game page: copied entries are only searched inside it (never in list item templates).</summary>
        private const string DetailsViewType = "Playnite.FullscreenApp.Controls.Views.GameDetails";

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                yield return d;
                int c;
                try { c = VisualTreeHelper.GetChildrenCount(d); } catch { continue; }
                for (int i = 0; i < c; i++) stack.Push(VisualTreeHelper.GetChild(d, i));
            }
        }

        private IEnumerable<Panel> FindRows(DependencyObject root)
        {
            if (recipe.Layout == BadgeLayout.Welded)
            {
                foreach (var d in Descendants(root))
                    if (d is Panel p && p.Name == recipe.RowName) yield return p;
                yield break;
            }
            var seen = new HashSet<Panel>();
            foreach (var view in Descendants(root).Where(d => d.GetType().FullName == DetailsViewType).ToList())
                foreach (var tb in Descendants(view).OfType<TextBlock>().Where(IsAnchor).ToList())
                {
                    var row = RowOf(tb);
                    if (row != null && seen.Add(row)) yield return row;
                }
        }

        private bool IsAnchor(TextBlock tb)
        {
            return !string.IsNullOrEmpty(recipe.AnchorBinding) && MimicBuilder.BindingPath(tb) == recipe.AnchorBinding;
        }

        /// <summary>Row panel of a copied entry: named panel above the anchor, or the N-th panel above it.</summary>
        private Panel RowOf(DependencyObject anchor)
        {
            int level = 0;
            for (var d = VisualTreeHelper.GetParent(anchor); d != null; d = VisualTreeHelper.GetParent(d))
            {
                if (d.GetType().FullName == DetailsViewType) return null;
                if (!(d is Panel p)) continue;
                level++;
                bool match = !string.IsNullOrEmpty(recipe.RowName) ? p.Name == recipe.RowName : level == Math.Max(1, recipe.RowLevel);
                if (match) return p.IsItemsHost ? null : p;
            }
            return null;
        }

        private void Hook(Panel row)
        {
            try
            {
                if (AnchorSlot(row) < 0) return;                          // not the expected row: ignore
                if (recipe.Layout == BadgeLayout.Welded && row.TryFindResource(recipe.BoxStyleKey) as Style == null) return; // theme changed: nothing inserted
                lastRow = new WeakReference<Panel>(row);
                failMs = 0;
                if (!hooked.TryGetValue(row, out _))
                {
                    hooked.Add(row, new object());
                    row.DataContextChanged += (s, a) => { if (!stopped) Refresh((Panel)s); };
                }
                Refresh(row);
            }
            catch (Exception ex) { logger.Warn(ex, "AGSS: badge hook failed"); }
        }

        /// <summary>Index of the row child containing the anchor (named element, or TextBlock bound to the anchor path), or -1.</summary>
        private int AnchorSlot(Panel row)
        {
            TextBlock _;
            return AnchorSlot(row, out _);
        }

        private int AnchorSlot(Panel row, out TextBlock anchor)
        {
            anchor = null;
            for (int i = 0; i < row.Children.Count; i++)
            {
                if (!(row.Children[i] is FrameworkElement fe) || fe.Name == BadgeFactory.BadgeName) continue;
                if (recipe.Layout == BadgeLayout.Welded)
                {
                    if (FindByName(fe, recipe.AnchorName) != null) return i;
                }
                else
                {
                    anchor = Descendants(fe).OfType<TextBlock>().FirstOrDefault(IsAnchor);
                    if (anchor != null) return i;
                }
            }
            return -1;
        }

        private static FrameworkElement FindByName(DependencyObject root, string name)
        {
            if (root is FrameworkElement f && f.Name == name) return f;
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                var r = FindByName(child, name);
                if (r != null) return r;
            }
            return null;
        }

        internal static Game GameOf(object dataContext)
        {
            if (dataContext == null) return null;
            if (dataContext is Game g) return g;
            try { return dataContext.GetType().GetProperty("Game")?.GetValue(dataContext) as Game; } catch { return null; }
        }

        private void Refresh(Panel row)
        {
            try
            {
                var existing = row.Children.OfType<FrameworkElement>().Where(c => c.Name == BadgeFactory.BadgeName).ToList();
                foreach (var extra in existing.Skip(1)) row.Children.Remove(extra); // never more than one
                var badge = existing.FirstOrDefault();

                var game = GameOf(row.DataContext) ?? selectedGame();
                var model = game == null ? null : modelFor(game);
                if (model == null)
                {
                    if (badge != null) badge.Visibility = Visibility.Collapsed; // game not followed by AGSS
                    return;
                }
                if (badge == null)
                {
                    TextBlock anchor;
                    int slot = AnchorSlot(row, out anchor);
                    if (slot < 0) return;
                    if (recipe.Layout == BadgeLayout.Welded) badge = BadgeFactory.BuildWelded(row, recipe);
                    else
                    {
                        var slotEl = (FrameworkElement)row.Children[slot];
                        var previous = slot > 0 ? row.Children[slot - 1] as FrameworkElement : null;
                        badge = MimicBuilder.Build(recipe, slotEl, previous, anchor);
                    }
                    if (badge == null) return;
                    row.Children.Insert(slot + 1, badge);
                    Insertions++;
                }
                BadgeFactory.Fill(badge, row, model, recipe.TextBrushKey);
                badge.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { logger.Warn(ex, "AGSS: badge refresh failed"); }
        }
    }
}
