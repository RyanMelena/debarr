using MudBlazor;

namespace Debarr.Components.Layout;

/// <summary>
/// The app's palettes and type scale.
/// Amber marks actions, the current page and focus; each state has its own colour.
/// </summary>
public static class DebarrTheme
{
    private static readonly string[] FontFamily = ["Inter", "system-ui", "Segoe UI", "Roboto", "Helvetica", "Arial", "sans-serif"];

    public static MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#9A5A0B",
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#625D55",
            SecondaryContrastText = "#FFFFFF",
            Tertiary = "#6D4AA8",
            TertiaryContrastText = "#FFFFFF",
            Info = "#1F6694",
            InfoContrastText = "#FFFFFF",
            Success = "#2E7D32",
            SuccessContrastText = "#FFFFFF",
            Warning = "#B4470B",
            WarningContrastText = "#FFFFFF",
            Error = "#C62828",
            ErrorContrastText = "#FFFFFF",
            Background = "#F7F5F1",
            BackgroundGray = "#EFEBE5",
            Surface = "#FFFFFF",
            AppbarBackground = "#23211E",
            AppbarText = "#F7F5F1",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#3A3631",
            DrawerIcon = "#716B62",
            TextPrimary = "#23211E",
            TextSecondary = "#625D55",
            TextDisabled = "#A39D93",
            ActionDefault = "#625D55",
            ActionDisabled = "#A39D93",
            ActionDisabledBackground = "#ECE8E1",
            LinesDefault = "#E3DED6",
            LinesInputs = "#8A847A",
            TableLines = "#EAE5DE",
            TableHover = "#FAF6EF",
            Divider = "#E3DED6",
            DividerLight = "#F0ECE6",
            OverlayDark = "rgba(35, 33, 30, 0.5)",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#EBA93F",
            PrimaryContrastText = "#1C1305",
            Secondary = "#A8A196",
            SecondaryContrastText = "#1B1A18",
            Tertiary = "#B79BEB",
            TertiaryContrastText = "#1B1A18",
            Info = "#7FB8E0",
            InfoContrastText = "#1B1A18",
            Success = "#7BC67E",
            SuccessContrastText = "#1B1A18",
            Warning = "#F2995A",
            WarningContrastText = "#1B1A18",
            Error = "#F2766B",
            ErrorContrastText = "#1B1A18",
            // An error alert's text, which sits on the error tint over the dark surface.
            ErrorDarken = "#F47E73",
            Background = "#1B1A18",
            BackgroundGray = "#161513",
            Surface = "#252321",
            AppbarBackground = "#0F0E0D",
            AppbarText = "#ECE7DF",
            DrawerBackground = "#201F1C",
            DrawerText = "#D6D0C6",
            DrawerIcon = "#A39C90",
            TextPrimary = "#ECE7DF",
            TextSecondary = "#ADA69B",
            TextDisabled = "#7D776C",
            ActionDefault = "#BDB5A8",
            ActionDisabled = "#7D776C",
            ActionDisabledBackground = "#34312C",
            LinesDefault = "#3A3733",
            LinesInputs = "#7D776C",
            TableLines = "#34312D",
            TableHover = "#2C2926",
            Divider = "#3A3733",
            DividerLight = "#2F2C29",
            OverlayDark = "rgba(0, 0, 0, 0.65)",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = FontFamily, FontSize = ".875rem", LineHeight = "1.5" },
            H4 = new H4Typography { FontFamily = FontFamily, FontSize = "1.625rem", FontWeight = "600", LineHeight = "1.25", LetterSpacing = "-.01em" },
            H5 = new H5Typography { FontFamily = FontFamily, FontSize = "1.25rem", FontWeight = "600", LineHeight = "1.3", LetterSpacing = "-.005em" },
            H6 = new H6Typography { FontFamily = FontFamily, FontSize = "1.0625rem", FontWeight = "600", LineHeight = "1.4", LetterSpacing = "0" },
            Subtitle1 = new Subtitle1Typography { FontFamily = FontFamily, FontSize = ".9375rem", FontWeight = "500", LineHeight = "1.5" },
            Subtitle2 = new Subtitle2Typography { FontFamily = FontFamily, FontSize = ".875rem", FontWeight = "600", LineHeight = "1.45" },
            Body1 = new Body1Typography { FontFamily = FontFamily, FontSize = ".9375rem", LineHeight = "1.5", LetterSpacing = "0" },
            Body2 = new Body2Typography { FontFamily = FontFamily, FontSize = ".875rem", LineHeight = "1.45", LetterSpacing = "0" },
            Button = new ButtonTypography { FontFamily = FontFamily, FontSize = ".875rem", FontWeight = "500", LineHeight = "1.75", LetterSpacing = "0", TextTransform = "none" },
            Caption = new CaptionTypography { FontFamily = FontFamily, FontSize = ".75rem", LineHeight = "1.45", LetterSpacing = "0" },
            Overline = new OverlineTypography { FontFamily = FontFamily, FontSize = ".6875rem", FontWeight = "600", LineHeight = "1.6", LetterSpacing = ".06em" },
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "6px" },
    };
}
