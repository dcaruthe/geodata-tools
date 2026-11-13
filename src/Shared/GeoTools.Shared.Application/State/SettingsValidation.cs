namespace GeoTools.Shared.Application.State;

public static class SettingsValidation
{
    public static SettingsResult ValidateAdminLevels(IReadOnlyList<int> levels, bool allowEmpty)
    {
        if (levels.Count == 0)
            return allowEmpty
                ? SettingsResult.Ok
                : SettingsResult.Rejected("Select at least one admin level.");

        if (levels.Any(level => level <= 0))
            return SettingsResult.Rejected("Admin levels must be positive.");

        return SettingsResult.Ok;
    }

    public static SettingsResult ValidateTolerance(double tolerance) =>
        double.IsFinite(tolerance) && tolerance >= 0 && tolerance <= 1
            ? SettingsResult.Ok
            : SettingsResult.Rejected("Simplify tolerance must be between 0 and 1.");

    public static SettingsResult ValidateWindowSize(WindowSize size) =>
        size switch
        {
            MaximizedWindow => SettingsResult.Ok,
            FixedWindow window
                when window.Width is >= 640 and <= 7680 && window.Height is >= 480 and <= 4320 =>
                SettingsResult.Ok,
            _ => SettingsResult.Rejected(
                "Window size must be maximized or between 640x480 and 7680x4320."
            ),
        };
}
