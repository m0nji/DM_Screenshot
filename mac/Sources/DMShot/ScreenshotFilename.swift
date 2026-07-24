import Foundation

/// Default file-name generation for saved screenshots.
enum ScreenshotFilename {
    /// Base name in the form `DM_Screenshot_DDMMYYYY_HH_MM`, e.g. `DM_Screenshot_18062026_14_30`.
    static func base(for date: Date, calendar: Calendar = .current) -> String {
        let c = calendar.dateComponents([.day, .month, .year, .hour, .minute], from: date)
        return String(format: "DM_Screenshot_%02d%02d%04d_%02d_%02d",
                      c.day ?? 0, c.month ?? 0, c.year ?? 0, c.hour ?? 0, c.minute ?? 0)
    }

    /// First non-colliding `<base>.<ext>`. When taken, appends `_1`, `_2`, … (so several
    /// shots within the same minute don't overwrite each other). `exists` decides collisions.
    static func unique(base: String, ext: String = "png", exists: (String) -> Bool) -> String {
        let first = "\(base).\(ext)"
        if !exists(first) { return first }
        var n = 1
        while exists("\(base)_\(n).\(ext)") { n += 1 }
        return "\(base)_\(n).\(ext)"
    }
}
