namespace DMShot.Platform;

/// <summary>One recognized line of text with its bounding box in a top-left origin
/// coordinate space (y grows downward). Only relative positions matter.</summary>
public readonly record struct RecognizedLine(string Text, double X, double Y, double Width, double Height)
{
    public double Bottom => Y + Height;
    public double MidY => Y + Height / 2;
}

/// <summary>
/// Pure ordering of OCR output into plain text (mirrors <c>OCRTextOrder</c> in
/// mac/Sources/DMShot/TextRecognition.swift). Recognizers do not guarantee reading
/// order, so lines are grouped into visual rows (vertical overlap of at least half the
/// smaller line height), rows are read top-to-bottom, fragments within a row
/// left-to-right and joined with a space, and rows are joined with newlines.
/// </summary>
public static class OcrTextOrder
{
    public static string Join(IEnumerable<RecognizedLine> lines)
    {
        // OrderBy is stable (List.Sort is not) — equal keys keep their input order.
        var items = lines
            .Select(l => l with { Text = (l.Text ?? "").Trim() })
            .Where(l => l.Text.Length > 0)
            .OrderBy(l => l.Y).ThenBy(l => l.X)
            .ToList();

        var rows = new List<(double Top, double Bottom, List<RecognizedLine> Items)>();
        foreach (var item in items)
        {
            if (rows.Count > 0 && SameRow(rows[^1].Top, rows[^1].Bottom, item))
            {
                var last = rows[^1];
                last.Items.Add(item);
                rows[^1] = (Math.Min(last.Top, item.Y), Math.Max(last.Bottom, item.Bottom), last.Items);
            }
            else rows.Add((item.Y, item.Bottom, new List<RecognizedLine> { item }));
        }
        return string.Join("\n", rows.Select(r =>
            string.Join(" ", r.Items.OrderBy(i => i.X).Select(i => i.Text))));
    }

    /// <summary>Same visual row when the vertical overlap covers at least half of the
    /// smaller height (zero-height boxes only match when their middle lies in the band).</summary>
    public static bool SameRow(double bandTop, double bandBottom, RecognizedLine box)
    {
        double overlap = Math.Min(bandBottom, box.Bottom) - Math.Max(bandTop, box.Y);
        double smaller = Math.Min(bandBottom - bandTop, box.Height);
        if (smaller <= 0) return overlap >= 0 && box.MidY >= bandTop && box.MidY <= bandBottom;
        return overlap >= smaller * 0.5;
    }
}
