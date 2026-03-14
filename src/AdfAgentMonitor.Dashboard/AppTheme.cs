using MudBlazor;

namespace AdfAgentMonitor.Dashboard;

public static class AppTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteDark = new PaletteDark
        {
            // Brand colors
            Primary            = "#7C3AED",   // Purple — agent orchestration
            PrimaryDarken      = "#6D28D9",
            PrimaryLighten     = "#8B5CF6",
            Secondary          = "#0F6E56",   // Teal — monitor agent
            SecondaryDarken    = "#065F46",
            SecondaryLighten   = "#10B981",

            // Surfaces
            Background         = "#12121F",
            BackgroundGray     = "#16162A",
            Surface            = "#1E1E2E",
            DrawerBackground   = "#16162A",
            AppbarBackground   = "#1E1E2E",

            // Text
            TextPrimary        = "#E2E8F0",
            TextSecondary      = "#94A3B8",
            TextDisabled       = "#475569",
            DrawerText         = "#E2E8F0",
            DrawerIcon         = "#94A3B8",
            AppbarText         = "#E2E8F0",

            // Lines / dividers
            Divider            = "#2D2D44",
            DividerLight       = "#252538",
            TableLines         = "#2D2D44",
            LinesDefault       = "#2D2D44",
            LinesInputs        = "#3D3D5C",

            // State / semantic
            Success            = "#10B981",
            Warning            = "#F59E0B",
            Error              = "#EF4444",
            Info               = "#3B82F6",

            // Overlays
            OverlayDark        = "rgba(18,18,31,0.8)",
            OverlayLight       = "rgba(30,30,46,0.6)",

            // Action states
            ActionDefault      = "#94A3B8",
            ActionDisabled     = "#475569",
            ActionDisabledBackground = "#1E1E2E",
        },

        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Roboto", "sans-serif"],
            },
            H4 = new H4Typography
            {
                FontWeight = "600",
            },
            H5 = new H5Typography
            {
                FontWeight = "600",
            },
            H6 = new H6Typography
            {
                FontWeight = "600",
            },
        },

        LayoutProperties = new LayoutProperties
        {
            DrawerWidthLeft    = "240px",
            AppbarHeight       = "64px",
        },
    };
}
