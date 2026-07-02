namespace DMShot.Editor;

public enum ToolKind { Select, Arrow, Rectangle, Ellipse, Underline, Highlighter, Step, Text, Blur, Crop }

public sealed class Annotation
{
    public ToolKind Kind { get; set; }
    public double X0 { get; set; }
    public double Y0 { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public uint ColorArgb { get; set; } = 0xFFEF4444; // mac default annotation red
    public double StrokeWidth { get; set; } = 4;
    public string Text { get; set; } = "";
    public int StepNumber { get; set; }
    public int BlurStrength { get; set; } = 12;

    public Annotation Clone() => (Annotation)MemberwiseClone();
}
