namespace NexInstaller.App.Behaviors;

internal static class ScrollMotion
{
    public static double Target(double current, double previousTarget, bool moving, int delta,
        int lines, double viewport, double maximum)
    {
        var distance = lines < 0 ? viewport : lines * 16d;
        // Reversing direction must respond immediately, without finishing old momentum.
        var origin = moving && (previousTarget - current) * delta < 0 ? previousTarget : current;
        return Math.Clamp(origin - delta / 120d * distance, 0, Math.Max(0, maximum));
    }
}
