using System;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace FBEditor.Avalonia;

/// <summary>
/// Paints debugger decorations on the editor's background layer: a red dot at the
/// left edge for lines with breakpoints, and a translucent highlight across the
/// current execution line. Driven by MainWindow via the two public members.
/// </summary>
public sealed class DebugLineRenderer : IBackgroundRenderer
{
    /// <summary>Returns true if the given 1-based document line has a breakpoint.</summary>
    public Func<int, bool>? IsBreakpoint;

    /// <summary>1-based current execution line; 0 = not stopped anywhere.</summary>
    public int CurrentLine;

    private static readonly IBrush BreakpointBrush = new SolidColorBrush(Color.Parse("#E51400"));
    private static readonly IBrush CurrentLineBrush = new SolidColorBrush(Color.FromArgb(55, 255, 215, 0));

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (textView is null || !textView.VisualLinesValid) return;

        double width = textView.Bounds.Width;
        foreach (var vl in textView.VisualLines)
        {
            int lineNo = vl.FirstDocumentLine.LineNumber;
            double y = vl.VisualTop - textView.VerticalOffset;
            double h = vl.Height;

            if (CurrentLine > 0 && lineNo == CurrentLine)
                dc.DrawRectangle(CurrentLineBrush, null, new Rect(0, y, width, h));

            if (IsBreakpoint != null && IsBreakpoint(lineNo))
            {
                double r = Math.Min(h, 14) / 2 - 1;
                if (r < 2) r = 2;
                dc.DrawEllipse(BreakpointBrush, null, new Point(r + 3, y + h / 2), r, r);
            }
        }
    }
}
