using DMShot.Capture;
using DMShot.Editor;
namespace DMShot.History;

public record AnnotationDto(string Kind, double X0, double Y0, double X1, double Y1,
    uint ColorArgb, double StrokeWidth, string Text, int StepNumber, int BlurStrength)
{
    public static AnnotationDto From(Annotation a) =>
        new(a.Kind.ToString(), a.X0, a.Y0, a.X1, a.Y1, a.ColorArgb, a.StrokeWidth, a.Text, a.StepNumber, a.BlurStrength);
    public Annotation To() => new()
    {
        Kind = Enum.Parse<ToolKind>(Kind), X0 = X0, Y0 = Y0, X1 = X1, Y1 = Y1,
        ColorArgb = ColorArgb, StrokeWidth = StrokeWidth, Text = Text, StepNumber = StepNumber, BlurStrength = BlurStrength
    };
}

public sealed class HistoryEntry
{
    public string Id { get; set; } = "";
    public string OriginalPngPath { get; set; } = "";
    public string ThumbnailPngPath { get; set; } = "";
    public List<AnnotationDto> Annotations { get; set; } = new();
    public PixelRect? Crop { get; set; }
    public DateTime CreatedUtc { get; set; }
}
