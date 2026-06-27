# Windows BrandDesign Parity Hotfix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Windows DM_Screenshot UI match the approved BrandDesign/macOS visual treatment for Standard Design and Black Utility.

**Architecture:** Keep the existing WPF resource-token approach. Fix the shared theme dictionary so all common controls receive the layered BrandDesign chrome, and remove hard-coded editor/QuickEdit surfaces that bypass the selected design.

**Tech Stack:** WPF XAML, C# resource dictionaries, xUnit static regression tests, existing release scripts.

---

### Task 1: Regression Tests

**Files:**
- Modify: `windows/DMShot.Tests/BlackUtilityThemeTests.cs`

- [ ] **Step 1: Add assertions that Windows controls use layered chrome**

Add assertions that `DmTheme.xaml` contains `LinearGradientBrush`, `DmBorderControlHighlight`, and `DmControlShadow` inside the common button/control templates.

- [ ] **Step 2: Add assertions that hard-coded non-theme surfaces are gone**

Add assertions that `EditorWindow.xaml` does not contain `Background="#141418"` and QuickEdit parsed styles no longer hard-code the Black Utility control colors.

- [ ] **Step 3: Run test command**

Run: `dotnet test windows/DMShot.Tests/DMShot.Tests.csproj --filter BlackUtilityThemeTests`

Expected on a machine with .NET installed: tests fail before implementation because the Windows chrome is still flat and contains hard-codes. On this macOS environment, `dotnet` is not installed, so run static checks with `rg` and rely on GitHub Actions for Windows compilation.

### Task 2: Shared WPF Chrome

**Files:**
- Modify: `windows/DMShot/Theme/DmTheme.xaml`

- [ ] **Step 1: Add shared layered chrome to Button, IconButton, ToolRadio, SidebarButton, NavItem, ComboBox, TextBox, SwitchToggle**

Use the existing dynamic brushes: `DmSurfaceAlt`, `DmSurfaceLight`, `DmBorderControl`, `DmBorderHover`, `DmBorderControlHighlight`, `DmControlShadow`, `DmAccentTint`, and `DmAccent`.

- [ ] **Step 2: Preserve Standard vs Black Utility through resources**

Do not introduce new fixed colors for Standard or Black Utility. `AppDesignTheme.Apply` must remain the single source of dynamic theme values.

### Task 3: Hard-Code Removal

**Files:**
- Modify: `windows/DMShot/Editor/EditorWindow.xaml`
- Modify: `windows/DMShot/Editor/QuickEditOverlayWindow.xaml.cs`

- [ ] **Step 1: Replace editor canvas hard-code**

Change the editor work area from `#141418` to `{StaticResource DmBackground}`.

- [ ] **Step 2: Replace QuickEdit parsed style hard-codes**

Change QuickEdit parsed toolbar styles to use dynamic resources where possible instead of fixed Black Utility colors.

### Task 4: Release Bump

**Files:**
- Modify: `VERSION`
- Modify: `CHANGELOG.md`
- Modify: `mac/Info.plist`
- Modify: `mac/Sources/DMShot/App.swift`

- [ ] **Step 1: Bump version to 0.4.23**

Keep all repository version markers consistent.

- [ ] **Step 2: Add changelog entry**

Document the Windows BrandDesign parity fix and the removal of flat/hard-coded Windows control chrome.

### Task 5: Verification and Release

**Files:**
- No source modifications.

- [ ] **Step 1: Run local verification**

Run XML parse checks for modified XAML, `plutil -lint mac/Info.plist`, `swift test --filter VersionConsistencyTests`, full `swift test`, `git diff --check`, and static `rg` checks for removed hard-codes.

- [ ] **Step 2: Commit and push**

Commit implementation and `release: 0.4.23`, push `main` to GitLab.

- [ ] **Step 3: Trigger GitHub release**

Run `scripts/sync-to-github.sh v0.4.23`, watch the GitHub Release workflow, and verify `DM_Screenshot-win-Setup.exe`, `DM_Screenshot-win-Portable.zip`, `.nupkg`, `RELEASES`, and `releases.win.json`.
