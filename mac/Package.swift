// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "DMShot",
    platforms: [.macOS(.v14)],
    targets: [
        .executableTarget(
            name: "DMShot",
            path: "Sources/DMShot",
            swiftSettings: [.swiftLanguageMode(.v5)]
        ),
        .testTarget(
            name: "DMShotTests",
            dependencies: ["DMShot"],
            path: "Tests/DMShotTests",
            swiftSettings: [.swiftLanguageMode(.v5)]
        )
    ]
)
