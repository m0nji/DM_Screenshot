import Foundation
import ServiceManagement

protocol LaunchAtLoginManaging {
    func apply(enabled: Bool) throws
}

struct LaunchAtLoginManager: LaunchAtLoginManaging {
    func apply(enabled: Bool) throws {
        guard Self.canManageLoginItem else { return }

        if enabled {
            try SMAppService.mainApp.register()
        } else {
            try SMAppService.mainApp.unregister()
        }
    }

    private static var canManageLoginItem: Bool {
        let bundle = Bundle.main
        // SwiftPM tests and unbundled launches must not touch the login item service.
        guard bundle.bundleURL.pathExtension == "app" else { return false }
        return bundle.object(forInfoDictionaryKey: "CFBundlePackageType") as? String == "APPL"
    }
}
