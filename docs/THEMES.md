# AGSS for theme authors

The AutoGameSoftwareSync (AGSS) extension shows, for each game, the AGSS state, the last sync date and the device the version in place comes from. Nothing is required from theme authors. This page is for those who want to place the information themselves.

## Custom elements

Add a `ContentControl` named after the element, anywhere a game is the data context (game page, details panel):

```xml
<!-- Shield icon + coloured text: "Protected · 27/10 21:14 · ● Handheld" -->
<ContentControl x:Name="AGSS_Badge" />

<!-- Same text, without the icon -->
<ContentControl x:Name="AGSS_Text" />
```

- Collapsed when the game is not followed by AGSS (no empty box).
- Colours: the state uses your `GreenBrush` / `OrangeBrush` / `RedBrush` when they exist (fixed green / orange / red otherwise); the date uses the inherited text colour; the device name uses its own colour, lightened if it would be unreadable on your background (computed from `TextBrush`).
- Background: transparent. Font, size and alignment are inherited: wrap the control in your own box style.
- Texts follow Playnite's language (EN, FR, RU, ES, ZH).

When the active theme hosts one of these elements, the extension stops writing its fallback genre.

## Badge inserted at run time (recognised themes)

For the themes below the extension inserts the badge into the game page without any theme change. The recipes are in `Logic/Display.cs` (`ThemeRecipes.All`).

| Fullscreen theme | Checked version | Where the badge goes | Layout |
|---|---|---|---|
| Solaris | 1.3.6 | `Row2`, right after the install size box (`InfoBox` Left/Right, `PathIconStyle`, `TextBlockSmallStyle`) | Welded |
| ReskinPS5 | 1.11 | `GameInfo` row, new "AGSS" column after "Released" | Clone |
| (u)biquity | 2.0.4 | Row "Time played / Last activity / Install size", new column after the install size | Clone |
| PS5ish | 5.0.3 | "Details" card, new "AGSS:" line after "Genre" | Clone |
| PlayniteModernUI | 1.6.2 | Details list, new "AGSS" entry after "Genres" | Clone |
| Playnite Deck | 3.0.2 | Side list, new "AGSS: ..." line after "Platform" | Clone |
| Nintendo Switch-ish | 2.0.0 | Info column, new header bar + value after "Platform" | CloneWithPrevious |
| Toggle | 1.10.1 | Row of entries, new entry after the release year (glyph replaced by the shield) | Clone |

Not supported (genre fallback): **XBOXSERIESish 2.0.0**. Its "Game details" card has a fixed height, with labels and values in two separate columns aligned by hand and nothing below the list, so any insertion overflows the card.

Layouts:
- **Welded**: two boxes in the theme's own box style (icon | text), next to a named box.
- **Clone**: the entry that contains the anchor (a `TextBlock` bound to e.g. `InstallSize`, `Genres`, `Platforms[0]`, found only inside the game page) is copied with the values written by the theme (style, margins, fonts, boxes, separators). Its label becomes "AGSS" and its value the coloured state. Other game data in the entry is left out.
- **CloneWithPrevious**: same, the label being the element just before the value.

These elements are not a public API: if you rename them, the extension detects it (game page shown for 8 s without the expected elements), remembers it for that theme version and falls back to the genre. A later version of the theme is tried again. To keep the badge, either keep the names or use the custom elements above.

## Fallback genre

The decision depends only on the fullscreen theme configured in Playnite (the same in Desktop and Fullscreen modes, so genres never change when switching mode). Fullscreen themes without a recipe and without the custom elements get a genre `● AGSS : <state> · <device>` (no date). The `●` prefix sorts it before letters and digits in all supported languages. It is replaced when the state changes and removed when no longer needed.
