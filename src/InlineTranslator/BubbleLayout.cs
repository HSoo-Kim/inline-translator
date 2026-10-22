using System.Drawing;

namespace InlineTranslator;

public static class BubbleLayout
{
    public static Rectangle? Union(IReadOnlyList<RectangleF> boxes)
    {
        if (boxes.Count == 0) return null;
        float left = boxes.Min(box => box.Left), top = boxes.Min(box => box.Top);
        float right = boxes.Max(box => box.Right), bottom = boxes.Max(box => box.Bottom);
        return Rectangle.Round(RectangleF.FromLTRB(left, top, right, bottom));
    }

    // Directly below the selection, left-aligned with it; above it when there is no room below.
    public static Point Placement(Rectangle target, Size size, Rectangle area, int gap)
    {
        int x = Math.Clamp(target.Left, area.Left + gap, Math.Max(area.Left + gap, area.Right - size.Width - gap));
        int below = target.Bottom + gap;
        int y = below + size.Height <= area.Bottom ? below : Math.Max(area.Top + gap, target.Top - gap - size.Height);
        return new Point(x, y);
    }
}
