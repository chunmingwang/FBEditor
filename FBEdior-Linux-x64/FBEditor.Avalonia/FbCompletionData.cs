using System;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace FBEditor.Avalonia;

/// <summary>A single autocomplete entry (FreeBASIC keyword / type / function / identifier).</summary>
public sealed class FbCompletionData : ICompletionData
{
    public FbCompletionData(string text, string description)
    {
        Text = text;
        Description = description;
    }

    public IImage? Image => null;
    public string Text { get; }
    public object Content => Text;
    public object Description { get; }
    public double Priority => 0;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, Text);
    }
}
