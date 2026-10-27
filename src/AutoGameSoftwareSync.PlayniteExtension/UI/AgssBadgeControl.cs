using System;
using System.Windows;
using AutoGameSoftwareSync.PlayniteExtension.Logic;
using Playnite.SDK.Controls;
using Playnite.SDK.Models;

namespace AutoGameSoftwareSync.PlayniteExtension.UI
{
    /// <summary>
    /// Custom element for theme authors: AGSS_Badge (icon + coloured text) and AGSS_Text (text only).
    /// Collapsed when the game is not followed by AGSS.
    /// </summary>
    public class AgssBadgeControl : PluginUserControl
    {
        private readonly Func<Game, BadgeModel> modelFor;
        private readonly FrameworkElement badge;
        private Game game;

        internal AgssBadgeControl(Func<Game, BadgeModel> modelFor, bool withIcon)
        {
            this.modelFor = modelFor;
            badge = BadgeFactory.BuildStandalone(withIcon);
            Content = badge;
            Visibility = Visibility.Collapsed;
        }

        public override void GameContextChanged(Game oldContext, Game newContext)
        {
            game = newContext;
            Update();
        }

        internal void Update()
        {
            try
            {
                var m = game == null ? null : modelFor(game);
                if (m == null) { Visibility = Visibility.Collapsed; return; }
                BadgeFactory.Fill(badge, this, m, "TextBrush");
                Visibility = Visibility.Visible;
            }
            catch (Exception) { Visibility = Visibility.Collapsed; }
        }
    }
}
