import AppKit
import SwiftUI

/// DM app family accent (shared with DM_Voice / DM_Workspace).
enum Theme {
    static let accentHex = "#c97b4a"
}

extension NSColor {
    static let dmAccent = NSColor(hex: Theme.accentHex)
}

extension Color {
    static let dmAccent = Color(nsColor: .dmAccent)
}
