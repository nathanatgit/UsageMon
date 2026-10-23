using Windows.Graphics;

namespace UsageMon;

public enum PanelKind { Joined, Codex, Claude }

internal static class PanelSnapping
{
    // Inputs and outputs are physical screen pixels, including negative monitor coordinates.
    internal static PointInt32 Snap(RectInt32 panel, RectInt32 work, IEnumerable<RectInt32> others, int threshold)
    {
        var xs = new List<int> { work.X, work.X + work.Width - panel.Width };
        var ys = new List<int> { work.Y, work.Y + work.Height - panel.Height };
        foreach (var other in others)
        {
            bool overlapsY = panel.Y < other.Y + other.Height && panel.Y + panel.Height > other.Y;
            bool overlapsX = panel.X < other.X + other.Width && panel.X + panel.Width > other.X;
            bool nearY = Math.Min(Math.Abs(panel.Y - other.Y - other.Height), Math.Abs(panel.Y + panel.Height - other.Y)) <= threshold;
            bool nearX = Math.Min(Math.Abs(panel.X - other.X - other.Width), Math.Abs(panel.X + panel.Width - other.X)) <= threshold;
            if (overlapsY) { xs.Add(other.X - panel.Width); xs.Add(other.X + other.Width); }
            if (overlapsX) { ys.Add(other.Y - panel.Height); ys.Add(other.Y + other.Height); }
            if (nearY) { xs.Add(other.X); xs.Add(other.X + other.Width - panel.Width); }
            if (nearX) { ys.Add(other.Y); ys.Add(other.Y + other.Height - panel.Height); }
        }
        int Nearest(int value, List<int> candidates)
        {
            int candidate = candidates.OrderBy(x => Math.Abs(x - value)).First();
            return Math.Abs(candidate - value) <= threshold ? candidate : value;
        }
        return new PointInt32(Nearest(panel.X, xs), Nearest(panel.Y, ys));
    }
}
