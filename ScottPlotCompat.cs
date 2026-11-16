using System;
using ScottPlot;

namespace HP_34401A
{
    /// <summary>
    /// Replacement for the deprecated ScottPlot 4 overload Plot.AddAnnotation(label, x, y).
    /// Same meaning as the old one: positive x/y = pixels from the top/left edge,
    /// negative x/y = pixels from the bottom/right edge.
    /// </summary>
    internal static class ScottPlotCompat
    {
        public static ScottPlot.Plottable.Annotation AddAnnotationAt(this Plot plot, string label, double x, double y)
        {
            Alignment alignment;
            if (x >= 0 && y >= 0) alignment = Alignment.UpperLeft;
            else if (x < 0 && y >= 0) alignment = Alignment.UpperRight;
            else if (x >= 0) alignment = Alignment.LowerLeft;
            else alignment = Alignment.LowerRight;

            var annotation = plot.AddAnnotation(label, alignment);
            annotation.MarginX = (float)Math.Abs(x);
            annotation.MarginY = (float)Math.Abs(y);
            return annotation;
        }
    }
}
