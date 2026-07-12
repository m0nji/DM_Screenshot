// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "DMShot",
    platforms: [.macOS(.v14)],
    dependencies: [
        .package(url: "https://github.com/sparkle-project/Sparkle", from: "2.6.0"),
    ],
    targets: [
        .executableTarget(
            name: "DMShot",
            dependencies: [.product(name: "Sparkle", package: "Sparkle")],
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
