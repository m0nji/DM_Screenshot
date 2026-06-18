# DM_Screenshot Windows Port — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a standalone native Windows screenshot + annotation app under `windows/` that mirrors the shipped macOS app's behavior, runnable and live-testable via `dotnet run`.

**Architecture:** A single WPF (.NET 8) desktop app. A thin **platform layer** behind interfaces (`IScreenCapturer`, `IHotkeyManager`, `ITrayIcon`, `IClipboardService`) isolates Win32 plumbing from a platform-independent **editor core** (annotation model, coordinate/crop math, rendering, history, settings). Capture is an instant GDI `BitBlt` of every display (the "freeze"); area selection uses borderless per-monitor overlay windows; the editor is a non-destructive annotation canvas built on WPF `DrawingVisual`.

**Tech Stack:** C# / .NET 8, WPF (`net8.0-windows`), `System.Drawing.Common` (GDI+ capture/pixels), Win32 P/Invoke (`RegisterHotKey`, `BitBlt`), `Hardcodet.NotifyIcon.Wpf` (tray), xUnit (tests).

## Global Constraints

- Target framework: **`net8.0-windows`**, `<UseWPF>true</UseWPF>`, `<UseWindowsForms>false</UseWindowsForms>` (use WPF + System.Drawing only; no WinForms UI).
- Language/UI copy: **English** (matches macOS app).
- **DPI:** app is **Per-Monitor-V2** DPI-aware via `app.manifest`. All capture math is in **physical source pixels**; map overlay/editor DIP coordinates to source pixels explicitly. Never assume 96 DPI.
- **Hotkeys:** `Ctrl+Shift+1` = full screen, `Ctrl+Shift+2` = area selection (user-rebindable in M2).
- **No special permission / onboarding** — Windows capture needs none.
- **Accent color:** DM orange `#c97b4a`; active tint `#1FFF8A4C` (ARGB for `rgba(255,138,76,0.12)`). Dark surfaces.
- **Persistence root:** `%APPDATA%\DMShot\` (history under `history\`, settings `settings.json`).
- **Tray behavior:** closing the editor window **hides it to the tray** (app keeps running); Quit (tray menu) exits.
- **Blur tool:** mosaic / pixelate region with adjustable strength (block size).
- **Bundle/app id concept:** product name `DM_Screenshot`, internal `DMShot`.
- Commit after every task. Commit messages end with: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- Reference (do not modify, do not import): the macOS app under `mac/Sources/DMShot/` is the behavior spec. Design spec: `docs/superpowers/specs/2026-06-18-windows-port-design.md`.

---

## File Structure

```
windows/
├─ DMShot.sln
├─ DMShot/DMShot.csproj
├─ DMShot/app.manifest
├─ DMShot/App.xaml(.cs)
├─ DMShot/Platform/{IScreenCapturer,GdiScreenCapturer,DisplayInfo,
│                    IHotkeyManager,Win32HotkeyManager,HotkeySpec,
│                    ITrayIcon,NotifyIconTray,IClipboardService,WpfClipboard}.cs
├─ DMShot/Capture/{CaptureCoordinator,Selection,OverlayWindow.xaml(.cs)}.cs
├─ DMShot/Editor/{Annotation,EditorModel,Renderer,CanvasControl,EditorWindow.xaml(.cs)}.cs
├─ DMShot/History/{HistoryEntry,HistoryStore}.cs
├─ DMShot/Settings/{Settings,SettingsStore,SettingsWindow.xaml(.cs),ShortcutRecorderControl}.cs
├─ DMShot/Theme/DmTheme.xaml
└─ DMShot/Resources/AppIcon.ico
└─ DMShot.Tests/DMShot.Tests.csproj
   DMShot.Tests/{CropMathTests,HotkeySpecTests,EditorModelTests,HistoryStoreTests,SettingsTests,RendererTests}.cs
```

**Milestones:** M1 = Tasks 1–11 (runnable capture+editor, live-testable). M2 = Tasks 12–17 (history, tray, settings, theme, icon). M3 (installer + signing) is **out of scope** for this plan.

---

## Task 1: Solution + WPF project scaffold (runnable empty app)

**Files:**
- Create: `windows/DMShot/DMShot.csproj`, `windows/DMShot/app.manifest`, `windows/DMShot/App.xaml`, `windows/DMShot/App.xaml.cs`, `windows/DMShot/MainPlaceholderWindow.xaml`(+`.cs`), `windows/DMShot.sln`, `windows/.gitignore`
- Create (tests): `windows/DMShot.Tests/DMShot.Tests.csproj`

**Interfaces:**
- Produces: a buildable solution; `App` class as the application entry/orchestration host.

- [ ] **Step 1: Create the project files**

`windows/DMShot/DMShot.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>false</UseWindowsForms>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AssemblyName>DMShot</AssemblyName>
    <RootNamespace>DMShot</RootNamespace>
    <ApplicationIcon>Resources\AppIcon.ico</ApplicationIcon>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.Drawing.Common" Version="8.0.*" />
    <PackageReference Include="Hardcodet.NotifyIcon.Wpf" Version="2.0.*" />
  </ItemGroup>
</Project>
```
Note: until Task 17 adds `Resources\AppIcon.ico`, temporarily remove the `<ApplicationIcon>` line (re-add it in Task 17).

`windows/DMShot/app.manifest`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/PM</dpiAware>
    </windowsSettings>
  </application>
</assembly>
```

`windows/DMShot/App.xaml`:
```xml
<Application x:Class="DMShot.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainPlaceholderWindow.xaml" />
```

`windows/DMShot/App.xaml.cs`:
```csharp
using System.Windows;
namespace DMShot;
public partial class App : Application { }
```

`windows/DMShot/MainPlaceholderWindow.xaml`:
```xml
<Window x:Class="DMShot.MainPlaceholderWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="DM_Screenshot" Height="200" Width="400">
  <TextBlock Text="DM_Screenshot — scaffold OK" HorizontalAlignment="Center" VerticalAlignment="Center"/>
</Window>
```
`windows/DMShot/MainPlaceholderWindow.xaml.cs`:
```csharp
using System.Windows;
namespace DMShot;
public partial class MainPlaceholderWindow : Window { public MainPlaceholderWindow() => InitializeComponent(); }
```

`windows/.gitignore`:
```
bin/
obj/
*.user
```

- [ ] **Step 2: Create the test project**

`windows/DMShot.Tests/DMShot.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\DMShot\DMShot.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create the solution and add projects**

Run from `windows/`:
```bash
cd windows
dotnet new sln -n DMShot
dotnet sln add DMShot/DMShot.csproj
dotnet sln add DMShot.Tests/DMShot.Tests.csproj
```

- [ ] **Step 4: Build to verify the scaffold compiles**

Run: `cd windows && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Live-run the placeholder window**

Run: `cd windows && dotnet run --project DMShot`
Expected: a window titled "DM_Screenshot" showing "scaffold OK". Close it.

- [ ] **Step 6: Commit**

```bash
git add windows/
git commit -m "feat(win): WPF .NET 8 scaffold + test project

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Display enumeration + GDI screen capture (the freeze)

**Files:**
- Create: `windows/DMShot/Platform/DisplayInfo.cs`, `windows/DMShot/Platform/IScreenCapturer.cs`, `windows/DMShot/Platform/GdiScreenCapturer.cs`

**Interfaces:**
- Produces:
  - `record DisplayInfo(int Index, System.Drawing.Rectangle Bounds, bool IsPrimary)` — `Bounds` in **physical pixels** (virtual-desktop coordinates, may be negative).
  - `interface IScreenCapturer { IReadOnlyList<DisplayInfo> GetDisplays(); System.Drawing.Bitmap CaptureDisplay(DisplayInfo display); System.Drawing.Bitmap CaptureVirtualDesktop(out System.Drawing.Rectangle virtualBounds); }`
  - `class GdiScreenCapturer : IScreenCapturer`
- Consumes: nothing (leaf platform module).

- [ ] **Step 1: Write DisplayInfo and the interface**

`windows/DMShot/Platform/DisplayInfo.cs`:
```csharp
using System.Drawing;
namespace DMShot.Platform;

public record DisplayInfo(int Index, Rectangle Bounds, bool IsPrimary);

public interface IScreenCapturer
{
    IReadOnlyList<DisplayInfo> GetDisplays();
    Bitmap CaptureDisplay(DisplayInfo display);
    Bitmap CaptureVirtualDesktop(out Rectangle virtualBounds);
}
```

- [ ] **Step 2: Implement GdiScreenCapturer**

`windows/DMShot/Platform/GdiScreenCapturer.cs`:
```csharp
using System.Drawing;
using System.Runtime.InteropServices;
namespace DMShot.Platform;

public sealed class GdiScreenCapturer : IScreenCapturer
{
    // Physical-pixel virtual screen metrics (we are PerMonitorV2 DPI-aware).
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77,
                      SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int i);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    private delegate bool MonitorEnumProc(IntPtr h, IntPtr hdc, ref RECT r, IntPtr d);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr h, ref MONITORINFO mi);
    private const uint MONITORINFOF_PRIMARY = 1;

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var list = new List<DisplayInfo>();
        int idx = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr hdc, ref RECT r, IntPtr d) =>
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(h, ref mi);
            var b = Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
            list.Add(new DisplayInfo(idx++, b, (mi.dwFlags & MONITORINFOF_PRIMARY) != 0));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public Bitmap CaptureDisplay(DisplayInfo display) => CaptureRect(display.Bounds);

    public Bitmap CaptureVirtualDesktop(out Rectangle virtualBounds)
    {
        virtualBounds = new Rectangle(
            GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
            GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));
        return CaptureRect(virtualBounds);
    }

    private static Bitmap CaptureRect(Rectangle r)
    {
        var bmp = new Bitmap(r.Width, r.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(r.Left, r.Top, 0, 0, r.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }
}
```

- [ ] **Step 3: Write a smoke test for display enumeration**

`windows/DMShot.Tests/CaptureSmokeTests.cs`:
```csharp
using DMShot.Platform;
using Xunit;

public class CaptureSmokeTests
{
    [Fact]
    public void GetDisplays_ReturnsAtLeastOnePrimary()
    {
        var cap = new GdiScreenCapturer();
        var displays = cap.GetDisplays();
        Assert.NotEmpty(displays);
        Assert.Contains(displays, d => d.IsPrimary);
    }

    [Fact]
    public void CaptureDisplay_ProducesBitmapOfDisplaySize()
    {
        var cap = new GdiScreenCapturer();
        var primary = cap.GetDisplays().First(d => d.IsPrimary);
        using var bmp = cap.CaptureDisplay(primary);
        Assert.Equal(primary.Bounds.Width, bmp.Width);
        Assert.Equal(primary.Bounds.Height, bmp.Height);
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `cd windows && dotnet test`
Expected: PASS (2 tests). On a headless CI without a desktop this may be skipped; on the live Windows machine it passes.

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Platform windows/DMShot.Tests/CaptureSmokeTests.cs
git commit -m "feat(win): GDI screen capture + multi-monitor enumeration

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Coordinate / crop math (pure, fully TDD)

**Files:**
- Create: `windows/DMShot/Capture/Selection.cs`
- Test: `windows/DMShot.Tests/CropMathTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct PixelRect(int X, int Y, int Width, int Height)`
  - `static class SelectionMath` with:
    - `static PixelRect DipSelectionToSourcePixels(double dipX, double dipY, double dipW, double dipH, double dpiScale)` — converts a selection expressed in an overlay window's DIPs (origin at that monitor's top-left) into source pixels on that monitor's captured bitmap. `dpiScale` = window DPI / 96.
    - `static PixelRect Normalize(double x0, double y0, double x1, double y1)` — turn two drag points into a positive-size integer rect.
    - `static PixelRect Clamp(PixelRect r, int maxW, int maxH)` — clamp to bitmap bounds.
- Consumes: nothing.

- [ ] **Step 1: Write the failing tests**

`windows/DMShot.Tests/CropMathTests.cs`:
```csharp
using DMShot.Capture;
using Xunit;

public class CropMathTests
{
    [Fact]
    public void Normalize_OrdersPointsAndRounds()
    {
        var r = SelectionMath.Normalize(30.4, 80.6, 10.2, 20.1);
        Assert.Equal(new PixelRect(10, 20, 20, 61), r); // x=10,y=20,w=30.4-10.2≈20,h=80.6-20.1≈61
    }

    [Fact]
    public void DipSelectionToSourcePixels_ScalesByDpi()
    {
        // 100x50 DIP selection at (10,20) on a 150%-scaled monitor -> *1.5
        var r = SelectionMath.DipSelectionToSourcePixels(10, 20, 100, 50, 1.5);
        Assert.Equal(new PixelRect(15, 30, 150, 75), r);
    }

    [Fact]
    public void DipSelectionToSourcePixels_NoScaleIsIdentity()
    {
        var r = SelectionMath.DipSelectionToSourcePixels(5, 6, 200, 100, 1.0);
        Assert.Equal(new PixelRect(5, 6, 200, 100), r);
    }

    [Fact]
    public void Clamp_LimitsToBitmapBounds()
    {
        var r = SelectionMath.Clamp(new PixelRect(-5, -5, 50, 50), 30, 30);
        Assert.Equal(new PixelRect(0, 0, 30, 30), r);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd windows && dotnet test --filter CropMathTests`
Expected: FAIL (type `SelectionMath` not found).

- [ ] **Step 3: Implement Selection.cs**

`windows/DMShot/Capture/Selection.cs`:
```csharp
namespace DMShot.Capture;

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

public static class SelectionMath
{
    public static PixelRect Normalize(double x0, double y0, double x1, double y1)
    {
        double x = Math.Min(x0, x1), y = Math.Min(y0, y1);
        double w = Math.Abs(x1 - x0), h = Math.Abs(y1 - y0);
        return new PixelRect((int)Math.Round(x), (int)Math.Round(y),
                             (int)Math.Round(w), (int)Math.Round(h));
    }

    public static PixelRect DipSelectionToSourcePixels(double dipX, double dipY, double dipW, double dipH, double dpiScale)
        => new((int)Math.Round(dipX * dpiScale), (int)Math.Round(dipY * dpiScale),
               (int)Math.Round(dipW * dpiScale), (int)Math.Round(dipH * dpiScale));

    public static PixelRect Clamp(PixelRect r, int maxW, int maxH)
    {
        int x = Math.Clamp(r.X, 0, maxW);
        int y = Math.Clamp(r.Y, 0, maxH);
        int w = Math.Clamp(r.Width + Math.Min(r.X, 0), 0, maxW - x);
        int h = Math.Clamp(r.Height + Math.Min(r.Y, 0), 0, maxH - y);
        return new PixelRect(x, y, w, h);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd windows && dotnet test --filter CropMathTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Capture/Selection.cs windows/DMShot.Tests/CropMathTests.cs
git commit -m "feat(win): selection/crop coordinate math (DPI-aware) with tests

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Global hotkeys (parse/format TDD + Win32 registration)

**Files:**
- Create: `windows/DMShot/Platform/HotkeySpec.cs`, `windows/DMShot/Platform/IHotkeyManager.cs`, `windows/DMShot/Platform/Win32HotkeyManager.cs`
- Test: `windows/DMShot.Tests/HotkeySpecTests.cs`

**Interfaces:**
- Produces:
  - `[Flags] enum HotkeyModifiers { None=0, Alt=1, Ctrl=2, Shift=4, Win=8 }` (values match Win32 `MOD_*`).
  - `record HotkeySpec(HotkeyModifiers Modifiers, uint VirtualKey)` with `static HotkeySpec Parse(string s)`, `string Format()`. String form e.g. `"Ctrl+Shift+1"`.
  - `interface IHotkeyManager : IDisposable { void Register(int id, HotkeySpec spec); void UnregisterAll(); event Action<int> HotkeyPressed; }`
  - `class Win32HotkeyManager : IHotkeyManager` — owns a message-only window; raises `HotkeyPressed(id)` on the UI thread.
- Consumes: nothing.

- [ ] **Step 1: Write failing parse/format tests**

`windows/DMShot.Tests/HotkeySpecTests.cs`:
```csharp
using DMShot.Platform;
using Xunit;

public class HotkeySpecTests
{
    [Fact]
    public void Parse_CtrlShiftDigit()
    {
        var s = HotkeySpec.Parse("Ctrl+Shift+1");
        Assert.Equal(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, s.Modifiers);
        Assert.Equal((uint)0x31, s.VirtualKey); // VK '1'
    }

    [Fact]
    public void Format_RoundTrips()
    {
        var s = new HotkeySpec(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 0x32);
        Assert.Equal("Ctrl+Shift+2", s.Format());
        Assert.Equal(s, HotkeySpec.Parse(s.Format()));
    }

    [Fact]
    public void Parse_LetterKey()
    {
        var s = HotkeySpec.Parse("Alt+A");
        Assert.Equal(HotkeyModifiers.Alt, s.Modifiers);
        Assert.Equal((uint)0x41, s.VirtualKey);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `cd windows && dotnet test --filter HotkeySpecTests`
Expected: FAIL (`HotkeySpec` not found).

- [ ] **Step 3: Implement HotkeySpec**

`windows/DMShot/Platform/HotkeySpec.cs`:
```csharp
namespace DMShot.Platform;

[Flags] public enum HotkeyModifiers { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

public record HotkeySpec(HotkeyModifiers Modifiers, uint VirtualKey)
{
    public static HotkeySpec Parse(string s)
    {
        var parts = s.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var mods = HotkeyModifiers.None;
        uint vk = 0;
        foreach (var p in parts)
        {
            switch (p.ToLowerInvariant())
            {
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "ctrl": case "control": mods |= HotkeyModifiers.Ctrl; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                case "win": case "cmd": mods |= HotkeyModifiers.Win; break;
                default: vk = KeyToVk(p); break;
            }
        }
        return new HotkeySpec(mods, vk);
    }

    public string Format()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(VkToKey(VirtualKey));
        return string.Join("+", parts);
    }

    private static uint KeyToVk(string key)
    {
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= '0' and <= '9' or >= 'A' and <= 'Z') return c;
        }
        if (key.StartsWith('F') && int.TryParse(key[1..], out int n) && n is >= 1 and <= 24)
            return (uint)(0x70 + n - 1); // VK_F1..
        throw new FormatException($"Unsupported key: {key}");
    }

    private static string VkToKey(uint vk)
    {
        if (vk is >= 0x30 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x70 + 1);
        return $"0x{vk:X2}";
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `cd windows && dotnet test --filter HotkeySpecTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Implement the interface and Win32 manager**

`windows/DMShot/Platform/IHotkeyManager.cs`:
```csharp
namespace DMShot.Platform;
public interface IHotkeyManager : IDisposable
{
    void Register(int id, HotkeySpec spec);
    void UnregisterAll();
    event Action<int>? HotkeyPressed;
}
```

`windows/DMShot/Platform/Win32HotkeyManager.cs`:
```csharp
using System.Runtime.InteropServices;
using System.Windows.Interop;
namespace DMShot.Platform;

public sealed class Win32HotkeyManager : IHotkeyManager
{
    private const int WM_HOTKEY = 0x0312;
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private readonly List<int> _ids = new();
    public event Action<int>? HotkeyPressed;

    public Win32HotkeyManager()
    {
        // Message-only window to receive WM_HOTKEY.
        var p = new HwndSourceParameters("DMShotHotkeys")
        { WindowStyle = 0, ParentWindow = new IntPtr(-3) /*HWND_MESSAGE*/ };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    public void Register(int id, HotkeySpec spec)
    {
        // MOD_NOREPEAT (0x4000) avoids auto-repeat storms.
        RegisterHotKey(_source.Handle, id, (uint)spec.Modifiers | 0x4000, spec.VirtualKey);
        _ids.Add(id);
    }

    public void UnregisterAll()
    {
        foreach (var id in _ids) UnregisterHotKey(_source.Handle, id);
        _ids.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WM_HOTKEY) { HotkeyPressed?.Invoke((int)w); handled = true; }
        return IntPtr.Zero;
    }

    public void Dispose() { UnregisterAll(); _source.Dispose(); }
}
```

- [ ] **Step 6: Build (manager is verified live in Task 5)**

Run: `cd windows && dotnet build`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add windows/DMShot/Platform/HotkeySpec.cs windows/DMShot/Platform/IHotkeyManager.cs windows/DMShot/Platform/Win32HotkeyManager.cs windows/DMShot.Tests/HotkeySpecTests.cs
git commit -m "feat(win): global hotkey manager + spec parse/format with tests

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Clipboard service + capture coordinator + app wiring (full-screen path live)

**Files:**
- Create: `windows/DMShot/Platform/IClipboardService.cs`, `windows/DMShot/Platform/WpfClipboard.cs`, `windows/DMShot/Capture/CaptureCoordinator.cs`, `windows/DMShot/Platform/ImageInterop.cs`
- Modify: `windows/DMShot/App.xaml` (remove `StartupUri`), `windows/DMShot/App.xaml.cs`

**Interfaces:**
- Produces:
  - `interface IClipboardService { void SetImage(System.Drawing.Bitmap bmp); }`
  - `static class ImageInterop { static System.Windows.Media.Imaging.BitmapSource ToBitmapSource(System.Drawing.Bitmap bmp); static System.Drawing.Bitmap Crop(System.Drawing.Bitmap src, Capture.PixelRect r); }`
  - `class CaptureCoordinator` with `event Action<System.Drawing.Bitmap> ImageCaptured;`, `void CaptureFullScreen();`, `void CaptureArea();` (CaptureArea implemented in Task 6 — stub raises nothing yet).
- Consumes: `IScreenCapturer` (Task 2), `SelectionMath`/`PixelRect` (Task 3), `IHotkeyManager` (Task 4).

- [ ] **Step 1: Clipboard + image interop**

`windows/DMShot/Platform/IClipboardService.cs`:
```csharp
using System.Drawing;
namespace DMShot.Platform;
public interface IClipboardService { void SetImage(Bitmap bmp); }
```

`windows/DMShot/Platform/ImageInterop.cs`:
```csharp
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Media.Imaging;
using DMShot.Capture;
namespace DMShot.Platform;

public static class ImageInterop
{
    public static BitmapSource ToBitmapSource(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var src = BitmapSource.Create(bmp.Width, bmp.Height, bmp.HorizontalResolution,
                bmp.VerticalResolution, System.Windows.Media.PixelFormats.Pbgra32, null,
                data.Scan0, data.Stride * bmp.Height, data.Stride);
            src.Freeze();
            return src;
        }
        finally { bmp.UnlockBits(data); }
    }

    public static Bitmap Crop(Bitmap src, PixelRect r)
    {
        var rect = new Rectangle(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height));
        var dst = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dst);
        g.DrawImage(src, new Rectangle(0, 0, rect.Width, rect.Height), rect, GraphicsUnit.Pixel);
        return dst;
    }
}
```

`windows/DMShot/Platform/WpfClipboard.cs`:
```csharp
using System.Drawing;
using DMShot.Capture;
namespace DMShot.Platform;

public sealed class WpfClipboard : IClipboardService
{
    public void SetImage(Bitmap bmp)
    {
        var src = ImageInterop.ToBitmapSource(bmp);
        System.Windows.Clipboard.SetImage(src);
    }
}
```

- [ ] **Step 2: Capture coordinator (full-screen path; area added Task 6)**

`windows/DMShot/Capture/CaptureCoordinator.cs`:
```csharp
using System.Drawing;
using System.Runtime.InteropServices;
using DMShot.Platform;
namespace DMShot.Capture;

public sealed class CaptureCoordinator
{
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    private struct POINT { public int X, Y; }

    private readonly IScreenCapturer _capturer;
    public event Action<Bitmap>? ImageCaptured;
    public CaptureCoordinator(IScreenCapturer capturer) => _capturer = capturer;

    public void CaptureFullScreen()
    {
        var displays = _capturer.GetDisplays();
        var target = DisplayUnderCursor(displays);
        var bmp = _capturer.CaptureDisplay(target);
        ImageCaptured?.Invoke(bmp);
    }

    // Implemented in Task 6 (this stub does nothing yet).
    public void CaptureArea() { /* replaced in Task 6 */ }

    private static DisplayInfo DisplayUnderCursor(IReadOnlyList<DisplayInfo> displays)
    {
        GetCursorPos(out var p);
        return displays.FirstOrDefault(d => d.Bounds.Contains(p.X, p.Y))
               ?? displays.First(d => d.IsPrimary);
    }
}
```
Note: deliberately uses Win32 `GetCursorPos` (physical pixels) — do **not** add a WinForms reference.

- [ ] **Step 3: Wire App to hotkeys → coordinator; full-screen shows a temporary preview window**

`windows/DMShot/App.xaml` (remove `StartupUri`):
```xml
<Application x:Class="DMShot.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
```

`windows/DMShot/App.xaml.cs`:
```csharp
using System.Windows;
using DMShot.Capture;
using DMShot.Platform;
namespace DMShot;

public partial class App : Application
{
    private Win32HotkeyManager _hotkeys = null!;
    private CaptureCoordinator _coordinator = null!;
    private readonly IClipboardService _clipboard = new WpfClipboard();

    private const int HK_FULL = 1, HK_AREA = 2;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown; // tray app; no main window yet

        _coordinator = new CaptureCoordinator(new GdiScreenCapturer());
        _coordinator.ImageCaptured += OnImageCaptured;

        _hotkeys = new Win32HotkeyManager();
        _hotkeys.HotkeyPressed += id =>
        {
            if (id == HK_FULL) _coordinator.CaptureFullScreen();
            else if (id == HK_AREA) _coordinator.CaptureArea();
        };
        _hotkeys.Register(HK_FULL, HotkeySpec.Parse("Ctrl+Shift+1"));
        _hotkeys.Register(HK_AREA, HotkeySpec.Parse("Ctrl+Shift+2"));
    }

    private void OnImageCaptured(System.Drawing.Bitmap bmp)
    {
        _clipboard.SetImage(bmp);
        // Temporary preview until the editor exists (Task 10 replaces this).
        var w = new Window { Title = $"Captured {bmp.Width}x{bmp.Height}", Width = 800, Height = 600 };
        w.Content = new System.Windows.Controls.Image
        { Source = ImageInterop.ToBitmapSource(bmp), Stretch = System.Windows.Media.Stretch.Uniform };
        w.Show();
    }

    protected override void OnExit(ExitEventArgs e) { _hotkeys.Dispose(); base.OnExit(e); }
}
```

- [ ] **Step 4: Live-test the full-screen hotkey + auto-clipboard**

Run: `cd windows && dotnet run --project DMShot`
Then press `Ctrl+Shift+1`.
Expected: a preview window opens showing the captured screen at the right resolution; pasting (`Ctrl+V`) into Paint shows the same image. Confirm the freeze captured exactly what was on screen. Press `Ctrl+Shift+2` → nothing yet (area is Task 6). Quit with the console `Ctrl+C` or Task Manager (no tray yet).

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot
git commit -m "feat(win): clipboard, image interop, capture coordinator + full-screen hotkey

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: Area selection overlay (per-monitor freeze + crosshair)

**Files:**
- Create: `windows/DMShot/Capture/OverlayWindow.xaml`(+`.cs`)
- Modify: `windows/DMShot/Capture/CaptureCoordinator.cs` (implement `CaptureArea`)

**Interfaces:**
- Consumes: `IScreenCapturer`, `SelectionMath`, `ImageInterop`.
- Produces: `CaptureCoordinator.CaptureArea()` raises `ImageCaptured` with the cropped frozen region; `Esc`/click-without-drag cancels.

- [ ] **Step 1: Overlay window XAML**

`windows/DMShot/Capture/OverlayWindow.xaml`:
```xml
<Window x:Class="DMShot.Capture.OverlayWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" ResizeMode="NoResize" AllowsTransparency="False"
        ShowInTaskbar="False" Topmost="True" Background="Black" Cursor="Cross">
  <Grid>
    <Image x:Name="FrozenImage" Stretch="Fill"/>
    <Canvas x:Name="Overlay">
      <Rectangle x:Name="DimTop" Fill="#80000000"/>
      <Rectangle x:Name="DimBottom" Fill="#80000000"/>
      <Rectangle x:Name="DimLeft" Fill="#80000000"/>
      <Rectangle x:Name="DimRight" Fill="#80000000"/>
      <Rectangle x:Name="SelRect" Stroke="#C97B4A" StrokeThickness="1.5"/>
      <Border x:Name="ReadoutBox" Background="#CC1F1F24" CornerRadius="3" Padding="6,2">
        <TextBlock x:Name="Readout" Foreground="White" FontSize="12"/>
      </Border>
    </Canvas>
  </Grid>
</Window>
```

- [ ] **Step 2: Overlay window code-behind**

`windows/DMShot/Capture/OverlayWindow.xaml.cs`:
```csharp
using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DMShot.Platform;
namespace DMShot.Capture;

public partial class OverlayWindow : Window
{
    private readonly DisplayInfo _display;
    private readonly Bitmap _frozen;
    private System.Windows.Point _start;
    private bool _dragging;

    /// <summary>The frozen capture of this display (source pixels). Used by the coordinator to crop.</summary>
    public Bitmap Frozen => _frozen;
    /// <summary>Set when this overlay produced a selection. Source-pixel rect in this display's bitmap.</summary>
    public PixelRect? Result { get; private set; }
    /// <summary>Raised on any terminal action (commit or cancel) so the coordinator can close all overlays.</summary>
    public event Action<OverlayWindow, bool>? Finished; // bool committed

    public OverlayWindow(DisplayInfo display, Bitmap frozen)
    {
        InitializeComponent();
        _display = display; _frozen = frozen;
        FrozenImage.Source = ImageInterop.ToBitmapSource(frozen);
        Loaded += OnLoaded;
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Finish(false); };
    }

    private void OnLoaded(object? s, RoutedEventArgs e)
    {
        // Position the borderless window exactly over this monitor (in DIPs).
        var scale = VisualTreeHelperDpi();
        Left = _display.Bounds.Left / scale;
        Top = _display.Bounds.Top / scale;
        Width = _display.Bounds.Width / scale;
        Height = _display.Bounds.Height / scale;
        Activate(); Focus();
        UpdateDim(new Rect());
    }

    private double VisualTreeHelperDpi()
    {
        var src = PresentationSource.FromVisual(this);
        return src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
    }

    private void OnDown(object? s, MouseButtonEventArgs e) { _start = e.GetPosition(Overlay); _dragging = true; }

    private void OnMove(object? s, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(Overlay);
        var rect = new Rect(_start, p);
        System.Windows.Controls.Canvas.SetLeft(SelRect, rect.Left);
        System.Windows.Controls.Canvas.SetTop(SelRect, rect.Top);
        SelRect.Width = rect.Width; SelRect.Height = rect.Height;
        double scale = VisualTreeHelperDpi();
        Readout.Text = $"{(int)(rect.Width * scale)} × {(int)(rect.Height * scale)}";
        System.Windows.Controls.Canvas.SetLeft(ReadoutBox, rect.Left);
        System.Windows.Controls.Canvas.SetTop(ReadoutBox, Math.Max(0, rect.Top - 24));
        UpdateDim(rect);
    }

    private void OnUp(object? s, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        var p = e.GetPosition(Overlay);
        double scale = VisualTreeHelperDpi();
        var norm = SelectionMath.Normalize(_start.X, _start.Y, p.X, p.Y);
        if (norm.Width < 3 || norm.Height < 3) { Finish(false); return; }
        var px = SelectionMath.DipSelectionToSourcePixels(norm.X, norm.Y, norm.Width, norm.Height, scale);
        Result = SelectionMath.Clamp(px, _frozen.Width, _frozen.Height);
        Finish(true);
    }

    private void UpdateDim(Rect sel)
    {
        DimTop.Width = ActualWidth; DimTop.Height = Math.Max(0, sel.Top);
        System.Windows.Controls.Canvas.SetTop(DimBottom, sel.Bottom);
        DimBottom.Width = ActualWidth; DimBottom.Height = Math.Max(0, ActualHeight - sel.Bottom);
        System.Windows.Controls.Canvas.SetTop(DimLeft, sel.Top);
        DimLeft.Width = Math.Max(0, sel.Left); DimLeft.Height = sel.Height;
        System.Windows.Controls.Canvas.SetLeft(DimRight, sel.Right);
        System.Windows.Controls.Canvas.SetTop(DimRight, sel.Top);
        DimRight.Width = Math.Max(0, ActualWidth - sel.Right); DimRight.Height = sel.Height;
    }

    private void Finish(bool committed) { Finished?.Invoke(this, committed); }
}
```

- [ ] **Step 3: Implement CaptureArea in the coordinator**

Replace the `CaptureArea`/`CaptureAreaImpl` stub from Task 5 with:
```csharp
public void CaptureArea()
{
    var displays = _capturer.GetDisplays();
    var overlays = new List<OverlayWindow>();
    bool done = false;

    foreach (var d in displays)
    {
        var frozen = _capturer.CaptureDisplay(d);
        var o = new OverlayWindow(d, frozen);
        o.Finished += (win, committed) =>
        {
            if (done) return;
            done = true;
            foreach (var ov in overlays) ov.Close();
            if (committed && win.Result is { } r && r.Width > 0 && r.Height > 0)
            {
                var cropped = ImageInterop.Crop(win.Frozen, r);
                ImageCaptured?.Invoke(cropped);
            }
        };
        overlays.Add(o);
    }
    foreach (var o in overlays) o.Show();
}
```
(`win.Frozen` is the per-display frozen bitmap exposed on `OverlayWindow`; `win.Result` is the clamped source-pixel rect set on mouse-up.)

- [ ] **Step 4: Live-test area selection**

Run: `cd windows && dotnet run --project DMShot`, press `Ctrl+Shift+2`.
Expected: every monitor dims with a crosshair; dragging shows an orange selection rectangle + live `W × H` pixel readout; releasing opens the preview window with exactly the selected region; `Esc` or a tiny click cancels with no capture. On a 150%-scaled monitor the captured pixel size matches the readout. Multi-monitor: selection on the secondary monitor crops from the correct display.

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Capture
git commit -m "feat(win): per-monitor area selection overlay (freeze, crosshair, DPI-correct crop)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: Annotation model + EditorModel (TDD)

**Files:**
- Create: `windows/DMShot/Editor/Annotation.cs`, `windows/DMShot/Editor/EditorModel.cs`
- Test: `windows/DMShot.Tests/EditorModelTests.cs`

**Interfaces:**
- Produces:
  - `enum ToolKind { Select, Arrow, Rectangle, Ellipse, Underline, Highlighter, Step, Text, Blur, Crop }`
  - `class Annotation { ToolKind Kind; double X0,Y0,X1,Y1; uint ColorArgb; double StrokeWidth; string Text=""; int StepNumber; int BlurStrength=12; }` (mutable; geometry in **image pixels**).
  - `class EditorModel` with: `IReadOnlyList<Annotation> Annotations`, `Annotation? Selected`, methods `Add(Annotation)`, `Remove(Annotation)`, `CreateStep()`, `Undo()`, `Redo()`, `bool CanUndo`, `bool CanRedo`, `SetCrop(PixelRect?)`, `PixelRect? Crop`, event `Changed`.
- Consumes: `PixelRect` (Task 3).

- [ ] **Step 1: Write failing tests**

`windows/DMShot.Tests/EditorModelTests.cs`:
```csharp
using DMShot.Capture;
using DMShot.Editor;
using Xunit;

public class EditorModelTests
{
    [Fact]
    public void Add_ThenUndo_Redo_RestoresState()
    {
        var m = new EditorModel();
        m.Add(new Annotation { Kind = ToolKind.Arrow });
        Assert.Single(m.Annotations);
        Assert.True(m.CanUndo);
        m.Undo();
        Assert.Empty(m.Annotations);
        Assert.True(m.CanRedo);
        m.Redo();
        Assert.Single(m.Annotations);
    }

    [Fact]
    public void Step_AutoIncrements()
    {
        var m = new EditorModel();
        var a = m.CreateStep(); var b = m.CreateStep();
        Assert.Equal(1, a.StepNumber);
        Assert.Equal(2, b.StepNumber);
    }

    [Fact]
    public void NewAction_ClearsRedo()
    {
        var m = new EditorModel();
        m.Add(new Annotation { Kind = ToolKind.Rectangle });
        m.Undo();
        m.Add(new Annotation { Kind = ToolKind.Ellipse });
        Assert.False(m.CanRedo);
        Assert.Equal(ToolKind.Ellipse, m.Annotations[0].Kind);
    }

    [Fact]
    public void SetCrop_StoresRect()
    {
        var m = new EditorModel();
        m.SetCrop(new PixelRect(1, 2, 3, 4));
        Assert.Equal(new PixelRect(1, 2, 3, 4), m.Crop);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `cd windows && dotnet test --filter EditorModelTests`
Expected: FAIL (types missing).

- [ ] **Step 3: Implement Annotation + EditorModel**

`windows/DMShot/Editor/Annotation.cs`:
```csharp
namespace DMShot.Editor;

public enum ToolKind { Select, Arrow, Rectangle, Ellipse, Underline, Highlighter, Step, Text, Blur, Crop }

public sealed class Annotation
{
    public ToolKind Kind { get; set; }
    public double X0 { get; set; }
    public double Y0 { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public uint ColorArgb { get; set; } = 0xFFC97B4A; // DM orange default
    public double StrokeWidth { get; set; } = 3;
    public string Text { get; set; } = "";
    public int StepNumber { get; set; }
    public int BlurStrength { get; set; } = 12;

    public Annotation Clone() => (Annotation)MemberwiseClone();
}
```

`windows/DMShot/Editor/EditorModel.cs`:
```csharp
using DMShot.Capture;
namespace DMShot.Editor;

public sealed class EditorModel
{
    private readonly List<Annotation> _items = new();
    private readonly Stack<Action> _undo = new();
    private readonly Stack<Action> _redo = new();
    private int _stepCounter;

    public IReadOnlyList<Annotation> Annotations => _items;
    public Annotation? Selected { get; set; }
    public PixelRect? Crop { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event Action? Changed;

    public Annotation CreateStep() => new() { Kind = ToolKind.Step, StepNumber = ++_stepCounter };

    public void Add(Annotation a)
    {
        Do(() => _items.Add(a), () => _items.Remove(a));
    }

    public void Remove(Annotation a)
    {
        int idx = _items.IndexOf(a);
        if (idx < 0) return;
        Do(() => _items.Remove(a), () => _items.Insert(idx, a));
    }

    public void SetCrop(PixelRect? rect)
    {
        var prev = Crop;
        Do(() => Crop = rect, () => Crop = prev);
    }

    private void Do(Action apply, Action revert)
    {
        apply();
        _undo.Push(() => { revert(); _redoPush(apply, revert); });
        _redo.Clear();
        Changed?.Invoke();
    }

    private void _redoPush(Action apply, Action revert)
        => _redo.Push(() => { apply(); _undo.Push(() => { revert(); _redoPush(apply, revert); }); Changed?.Invoke(); });

    public void Undo() { if (_undo.Count > 0) { _undo.Pop()(); Changed?.Invoke(); } }
    public void Redo() { if (_redo.Count > 0) { _redo.Pop()(); } }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `cd windows && dotnet test --filter EditorModelTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Editor/Annotation.cs windows/DMShot/Editor/EditorModel.cs windows/DMShot.Tests/EditorModelTests.cs
git commit -m "feat(win): annotation model + editor state with undo/redo (tested)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: Renderer (draw + flatten + blur)

**Files:**
- Create: `windows/DMShot/Editor/Renderer.cs`
- Test: `windows/DMShot.Tests/RendererTests.cs`

**Interfaces:**
- Produces: `static class Renderer` with
  - `void Draw(System.Windows.Media.DrawingContext dc, System.Windows.Media.Imaging.BitmapSource baseImage, EditorModel model)` — draws base + all annotations (used by the live canvas).
  - `System.Drawing.Bitmap Flatten(System.Drawing.Bitmap baseImage, EditorModel model)` — renders base + annotations to a new bitmap honoring `model.Crop`, for clipboard/save/thumbnail.
- Consumes: `Annotation`, `EditorModel`, `PixelRect`.

Implementation note: the screen path uses WPF `DrawingContext`; the export path uses `System.Drawing.Graphics` so it is deterministic and testable off the UI thread. Keep one private helper per shape that both paths call with primitive args to stay DRY where practical; where the two graphics APIs diverge, duplicate the minimal drawing code.

- [ ] **Step 1: Write a flatten dimensions test (export path is unit-testable)**

`windows/DMShot.Tests/RendererTests.cs`:
```csharp
using System.Drawing;
using DMShot.Capture;
using DMShot.Editor;
using Xunit;

public class RendererTests
{
    [Fact]
    public void Flatten_NoCrop_KeepsBaseSize()
    {
        using var baseBmp = new Bitmap(200, 100);
        var m = new EditorModel();
        using var outp = Renderer.Flatten(baseBmp, m);
        Assert.Equal(200, outp.Width);
        Assert.Equal(100, outp.Height);
    }

    [Fact]
    public void Flatten_WithCrop_UsesCropSize()
    {
        using var baseBmp = new Bitmap(200, 100);
        var m = new EditorModel();
        m.SetCrop(new PixelRect(10, 10, 50, 40));
        using var outp = Renderer.Flatten(baseBmp, m);
        Assert.Equal(50, outp.Width);
        Assert.Equal(40, outp.Height);
    }

    [Fact]
    public void Flatten_DrawsArrowWithoutThrowing()
    {
        using var baseBmp = new Bitmap(100, 100);
        var m = new EditorModel();
        m.Add(new Annotation { Kind = ToolKind.Arrow, X0 = 10, Y0 = 10, X1 = 80, Y1 = 80 });
        using var outp = Renderer.Flatten(baseBmp, m);
        Assert.Equal(100, outp.Width);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `cd windows && dotnet test --filter RendererTests`
Expected: FAIL (`Renderer` not found).

- [ ] **Step 3: Implement Renderer**

`windows/DMShot/Editor/Renderer.cs`:
```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using DMShot.Capture;
using WpfDc = System.Windows.Media.DrawingContext;
namespace DMShot.Editor;

public static class Renderer
{
    private static Color ToGdi(uint argb) =>
        Color.FromArgb((int)(argb >> 24), (int)((argb >> 16) & 0xFF), (int)((argb >> 8) & 0xFF), (int)(argb & 0xFF));

    public static Bitmap Flatten(Bitmap baseImage, EditorModel model)
    {
        var crop = model.Crop;
        int w = crop?.Width ?? baseImage.Width;
        int h = crop?.Height ?? baseImage.Height;
        double ox = crop?.X ?? 0, oy = crop?.Y ?? 0;

        var outp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(outp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawImage(baseImage, new Rectangle(0, 0, w, h),
            new Rectangle((int)ox, (int)oy, w, h), GraphicsUnit.Pixel);

        foreach (var a in model.Annotations)
            DrawGdi(g, a, ox, oy, baseImage);
        return outp;
    }

    private static void DrawGdi(Graphics g, Annotation a, double ox, double oy, Bitmap baseImage)
    {
        float x0 = (float)(a.X0 - ox), y0 = (float)(a.Y0 - oy);
        float x1 = (float)(a.X1 - ox), y1 = (float)(a.Y1 - oy);
        var color = ToGdi(a.ColorArgb);
        using var pen = new Pen(color, (float)a.StrokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        switch (a.Kind)
        {
            case ToolKind.Arrow:
                pen.CustomEndCap = new AdjustableArrowCap((float)Math.Max(2, a.StrokeWidth), (float)Math.Max(2, a.StrokeWidth));
                g.DrawLine(pen, x0, y0, x1, y1);
                break;
            case ToolKind.Rectangle:
                g.DrawRectangle(pen, Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0), Math.Abs(y1 - y0));
                break;
            case ToolKind.Ellipse:
                g.DrawEllipse(pen, Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0), Math.Abs(y1 - y0));
                break;
            case ToolKind.Underline:
                g.DrawLine(pen, x0, y1, x1, y1);
                break;
            case ToolKind.Highlighter:
                using (var hp = new Pen(Color.FromArgb(90, color), (float)Math.Max(10, a.StrokeWidth * 3)))
                    g.DrawLine(hp, x0, y1, x1, y1);
                break;
            case ToolKind.Text:
                using (var b = new SolidBrush(color))
                using (var f = new Font("Segoe UI", (float)Math.Max(10, a.StrokeWidth * 5)))
                    g.DrawString(a.Text, f, b, x0, y0);
                break;
            case ToolKind.Step:
                float d = (float)Math.Max(22, a.StrokeWidth * 7);
                using (var b = new SolidBrush(color))
                using (var tb = new SolidBrush(Color.White))
                using (var f = new Font("Segoe UI", d * 0.45f, System.Drawing.FontStyle.Bold))
                {
                    g.FillEllipse(b, x0, y0, d, d);
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(a.StepNumber.ToString(), f, tb, new RectangleF(x0, y0, d, d), sf);
                }
                break;
            case ToolKind.Blur:
                DrawMosaic(g, baseImage, a, ox, oy);
                break;
        }
    }

    private static void DrawMosaic(Graphics g, Bitmap baseImage, Annotation a, double ox, double oy)
    {
        int rx = (int)Math.Min(a.X0, a.X1), ry = (int)Math.Min(a.Y0, a.Y1);
        int rw = (int)Math.Abs(a.X1 - a.X0), rh = (int)Math.Abs(a.Y1 - a.Y0);
        rx = Math.Clamp(rx, 0, baseImage.Width - 1); ry = Math.Clamp(ry, 0, baseImage.Height - 1);
        rw = Math.Clamp(rw, 1, baseImage.Width - rx); rh = Math.Clamp(rh, 1, baseImage.Height - ry);
        int block = Math.Max(2, a.BlurStrength);
        int sw = Math.Max(1, rw / block), sh = Math.Max(1, rh / block);
        using var small = new Bitmap(sw, sh);
        using (var sg = Graphics.FromImage(small))
        {
            sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
            sg.DrawImage(baseImage, new Rectangle(0, 0, sw, sh), new Rectangle(rx, ry, rw, rh), GraphicsUnit.Pixel);
        }
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(small, new Rectangle((int)(rx - ox), (int)(ry - oy), rw, rh));
        g.InterpolationMode = InterpolationMode.Default;
    }

    // Live-canvas path: draw onto a WPF DrawingContext. Mirrors DrawGdi shape-by-shape.
    public static void Draw(WpfDc dc, System.Windows.Media.Imaging.BitmapSource baseImage, EditorModel model)
    {
        dc.DrawImage(baseImage, new System.Windows.Rect(0, 0, baseImage.PixelWidth, baseImage.PixelHeight));
        foreach (var a in model.Annotations)
            DrawWpf(dc, a);
    }

    private static System.Windows.Media.Color ToWpf(uint argb) =>
        System.Windows.Media.Color.FromArgb((byte)(argb >> 24), (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));

    private static void DrawWpf(WpfDc dc, Annotation a)
    {
        var brush = new System.Windows.Media.SolidColorBrush(ToWpf(a.ColorArgb));
        var pen = new System.Windows.Media.Pen(brush, a.StrokeWidth)
        { StartLineCap = System.Windows.Media.PenLineCap.Round, EndLineCap = System.Windows.Media.PenLineCap.Round };
        var p0 = new System.Windows.Point(a.X0, a.Y0);
        var p1 = new System.Windows.Point(a.X1, a.Y1);
        switch (a.Kind)
        {
            case ToolKind.Arrow: DrawArrowWpf(dc, pen, brush, p0, p1); break;
            case ToolKind.Rectangle: dc.DrawRectangle(null, pen, RectOf(p0, p1)); break;
            case ToolKind.Ellipse:
                var r = RectOf(p0, p1);
                dc.DrawEllipse(null, pen, new System.Windows.Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
                break;
            case ToolKind.Underline: dc.DrawLine(pen, new System.Windows.Point(a.X0, a.Y1), new System.Windows.Point(a.X1, a.Y1)); break;
            case ToolKind.Highlighter:
                var hpen = new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(90, (byte)((a.ColorArgb >> 16) & 0xFF), (byte)((a.ColorArgb >> 8) & 0xFF), (byte)(a.ColorArgb & 0xFF))),
                    Math.Max(10, a.StrokeWidth * 3));
                dc.DrawLine(hpen, new System.Windows.Point(a.X0, a.Y1), new System.Windows.Point(a.X1, a.Y1));
                break;
            case ToolKind.Step:
                double d = Math.Max(22, a.StrokeWidth * 7);
                dc.DrawEllipse(brush, null, new System.Windows.Point(a.X0 + d / 2, a.Y0 + d / 2), d / 2, d / 2);
                var ft = new System.Windows.Media.FormattedText(a.StepNumber.ToString(),
                    System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                    new System.Windows.Media.Typeface("Segoe UI"), d * 0.5, System.Windows.Media.Brushes.White, 1.0);
                dc.DrawText(ft, new System.Windows.Point(a.X0 + d / 2 - ft.Width / 2, a.Y0 + d / 2 - ft.Height / 2));
                break;
            case ToolKind.Text:
                var t = new System.Windows.Media.FormattedText(a.Text,
                    System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                    new System.Windows.Media.Typeface("Segoe UI"), Math.Max(10, a.StrokeWidth * 5), brush, 1.0);
                dc.DrawText(t, p0);
                break;
            // Blur on the live canvas: draw a translucent marker; the real mosaic is applied on flatten.
            case ToolKind.Blur:
                dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 0, 0, 0)),
                    new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 1), RectOf(p0, p1));
                break;
        }
    }

    private static System.Windows.Rect RectOf(System.Windows.Point a, System.Windows.Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static void DrawArrowWpf(WpfDc dc, System.Windows.Media.Pen pen, System.Windows.Media.Brush brush, System.Windows.Point p0, System.Windows.Point p1)
    {
        dc.DrawLine(pen, p0, p1);
        double ang = Math.Atan2(p1.Y - p0.Y, p1.X - p0.X);
        double len = Math.Max(8, pen.Thickness * 3);
        var a1 = new System.Windows.Point(p1.X - len * Math.Cos(ang - Math.PI / 6), p1.Y - len * Math.Sin(ang - Math.PI / 6));
        var a2 = new System.Windows.Point(p1.X - len * Math.Cos(ang + Math.PI / 6), p1.Y - len * Math.Sin(ang + Math.PI / 6));
        var fig = new System.Windows.Media.PathFigure { StartPoint = p1, IsClosed = true };
        fig.Segments.Add(new System.Windows.Media.LineSegment(a1, false));
        fig.Segments.Add(new System.Windows.Media.LineSegment(a2, false));
        var geo = new System.Windows.Media.PathGeometry(); geo.Figures.Add(fig);
        dc.DrawGeometry(brush, null, geo);
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `cd windows && dotnet test --filter RendererTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Editor/Renderer.cs windows/DMShot.Tests/RendererTests.cs
git commit -m "feat(win): renderer with flatten/export + mosaic blur (tested)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: Canvas control (interactive drawing)

**Files:**
- Create: `windows/DMShot/Editor/CanvasControl.cs`

**Interfaces:**
- Produces: `class CanvasControl : FrameworkElement` with `void Load(System.Drawing.Bitmap image)`, `ToolKind ActiveTool { get; set; }`, `uint ActiveColor`, `double ActiveStroke`, `int ActiveBlurStrength`, `EditorModel Model { get; }`, event `Action ContentChanged`. Renders via `OnRender` using `Renderer.Draw`; mouse drag creates/edits annotations in image-pixel space.
- Consumes: `EditorModel`, `Renderer`, `ImageInterop`.

- [ ] **Step 1: Implement CanvasControl**

`windows/DMShot/Editor/CanvasControl.cs`:
```csharp
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DMShot.Platform;
namespace DMShot.Editor;

public sealed class CanvasControl : FrameworkElement
{
    private BitmapSource? _image;
    private Annotation? _draft;
    private Point _start;

    public EditorModel Model { get; } = new();
    public ToolKind ActiveTool { get; set; } = ToolKind.Arrow;
    public uint ActiveColor { get; set; } = 0xFFC97B4A;
    public double ActiveStroke { get; set; } = 3;
    public int ActiveBlurStrength { get; set; } = 12;
    public event Action? ContentChanged;

    public CanvasControl()
    {
        Model.Changed += () => { InvalidateVisual(); ContentChanged?.Invoke(); };
        Focusable = true;
    }

    public void Load(System.Drawing.Bitmap image)
    {
        _image = ImageInterop.ToBitmapSource(image);
        Width = _image.PixelWidth; Height = _image.PixelHeight;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size _) =>
        _image is null ? new Size(0, 0) : new Size(_image.PixelWidth, _image.PixelHeight);

    protected override void OnRender(DrawingContext dc)
    {
        if (_image is null) return;
        Renderer.Draw(dc, _image, Model);
        if (_draft is not null)
        {
            var tmp = new EditorModel();
            tmp.Add(_draft);
            Renderer.Draw(dc, _image, tmp); // draft preview over base; cheap for one item
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_image is null) return;
        Focus();
        _start = e.GetPosition(this);
        _draft = new Annotation
        {
            Kind = ActiveTool, ColorArgb = ActiveColor, StrokeWidth = ActiveStroke, BlurStrength = ActiveBlurStrength,
            X0 = _start.X, Y0 = _start.Y, X1 = _start.X, Y1 = _start.Y
        };
        if (ActiveTool == ToolKind.Step) { _draft = Model.CreateStep(); _draft.ColorArgb = ActiveColor; _draft.X0 = _start.X; _draft.Y0 = _start.Y; }
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_draft is null) return;
        var p = e.GetPosition(this);
        _draft.X1 = p.X; _draft.Y1 = p.Y;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_draft is null) return;
        ReleaseMouseCapture();
        var d = _draft; _draft = null;
        if (d.Kind == ToolKind.Text)
        {
            d.Text = Microsoft.VisualBasic.Interaction.InputBox("Text:", "Add text", ""); // see note
            if (string.IsNullOrEmpty(d.Text)) { InvalidateVisual(); return; }
        }
        if (d.Kind == ToolKind.Crop)
        {
            Model.SetCrop(new Capture.PixelRect((int)Math.Min(d.X0, d.X1), (int)Math.Min(d.Y0, d.Y1),
                (int)Math.Abs(d.X1 - d.X0), (int)Math.Abs(d.Y1 - d.Y0)));
            return;
        }
        Model.Add(d);
    }
}
```
Note on the text prompt: do **not** add a `Microsoft.VisualBasic` reference. Replace the `InputBox` line with a tiny custom modal — a `Window` containing a `TextBox` + OK/Cancel returning a string. Implement it as `Editor/TextPromptWindow.xaml(.cs)` (a 30-line dialog) and call `TextPromptWindow.Ask(owner)`. Add that file in this task.

- [ ] **Step 2: Add the text prompt dialog**

`windows/DMShot/Editor/TextPromptWindow.xaml`:
```xml
<Window x:Class="DMShot.Editor.TextPromptWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Add text" Width="320" Height="130" WindowStartupLocation="CenterOwner"
        WindowStyle="ToolWindow" ResizeMode="NoResize">
  <StackPanel Margin="12">
    <TextBox x:Name="Input" Height="26"/>
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,12,0,0">
      <Button Content="Cancel" Width="70" IsCancel="True" Margin="0,0,8,0"/>
      <Button Content="OK" Width="70" IsDefault="True" Click="Ok"/>
    </StackPanel>
  </StackPanel>
</Window>
```
`windows/DMShot/Editor/TextPromptWindow.xaml.cs`:
```csharp
using System.Windows;
namespace DMShot.Editor;
public partial class TextPromptWindow : Window
{
    public TextPromptWindow() { InitializeComponent(); Loaded += (_, _) => Input.Focus(); }
    private void Ok(object sender, RoutedEventArgs e) { DialogResult = true; }
    public static string Ask(Window owner)
    {
        var w = new TextPromptWindow { Owner = owner };
        return w.ShowDialog() == true ? w.Input.Text : "";
    }
}
```
Then in `CanvasControl.OnMouseLeftButtonUp`, replace the InputBox line with:
```csharp
d.Text = TextPromptWindow.Ask(Window.GetWindow(this)!);
```

- [ ] **Step 3: Build**

Run: `cd windows && dotnet build`
Expected: Build succeeded. (Interactive behavior is verified live in Task 10.)

- [ ] **Step 4: Commit**

```bash
git add windows/DMShot/Editor/CanvasControl.cs windows/DMShot/Editor/TextPromptWindow.xaml windows/DMShot/Editor/TextPromptWindow.xaml.cs
git commit -m "feat(win): interactive annotation canvas + text prompt dialog

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 10: Editor window (toolbar, tools, copy/save) — replaces preview

**Files:**
- Create: `windows/DMShot/Editor/EditorWindow.xaml`(+`.cs`)
- Modify: `windows/DMShot/App.xaml.cs` (`OnImageCaptured` opens/raises the editor instead of the preview)

**Interfaces:**
- Produces: `class EditorWindow : Window` with `void LoadImage(System.Drawing.Bitmap bmp)`; a single shared instance is reused/raised. Has tool buttons, color picker (hex), stroke slider, blur-strength slider, Copy, Save, Undo/Redo, dimensions + zoom readout.
- Consumes: `CanvasControl`, `Renderer`, `IClipboardService`.

- [ ] **Step 1: Editor window XAML**

`windows/DMShot/Editor/EditorWindow.xaml`:
```xml
<Window x:Class="DMShot.Editor.EditorWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ed="clr-namespace:DMShot.Editor"
        Title="DM_Screenshot" Width="1100" Height="750" Background="#1F1F24">
  <DockPanel>
    <!-- Top toolbar -->
    <ToolBar DockPanel.Dock="Top" Background="#26262C">
      <Button x:Name="BtnSelect" Content="Select" Tag="Select" Click="ToolClick"/>
      <Button x:Name="BtnArrow" Content="Arrow" Tag="Arrow" Click="ToolClick"/>
      <Button Content="Rect" Tag="Rectangle" Click="ToolClick"/>
      <Button Content="Ellipse" Tag="Ellipse" Click="ToolClick"/>
      <Button Content="Underline" Tag="Underline" Click="ToolClick"/>
      <Button Content="Highlight" Tag="Highlighter" Click="ToolClick"/>
      <Button Content="Step" Tag="Step" Click="ToolClick"/>
      <Button Content="Text" Tag="Text" Click="ToolClick"/>
      <Button Content="Blur" Tag="Blur" Click="ToolClick"/>
      <Button Content="Crop" Tag="Crop" Click="ToolClick"/>
      <Separator/>
      <TextBlock Text="Color" Foreground="White" VerticalAlignment="Center" Margin="4,0"/>
      <TextBox x:Name="HexBox" Width="80" Text="#C97B4A" KeyDown="HexChanged"/>
      <TextBlock Text="Stroke" Foreground="White" VerticalAlignment="Center" Margin="8,0,4,0"/>
      <Slider x:Name="StrokeSlider" Width="90" Minimum="1" Maximum="20" Value="3" VerticalAlignment="Center"/>
      <TextBlock Text="Blur" Foreground="White" VerticalAlignment="Center" Margin="8,0,4,0"/>
      <Slider x:Name="BlurSlider" Width="90" Minimum="4" Maximum="40" Value="12" VerticalAlignment="Center"/>
      <Separator/>
      <Button Content="Undo" Click="UndoClick"/>
      <Button Content="Redo" Click="RedoClick"/>
      <Button Content="Copy" Click="CopyClick"/>
      <Button Content="Save" Click="SaveClick"/>
    </ToolBar>
    <!-- Status bar -->
    <StatusBar DockPanel.Dock="Bottom" Background="#26262C">
      <TextBlock x:Name="DimText" Foreground="#AAAAAA"/>
      <Separator/>
      <TextBlock x:Name="ZoomText" Foreground="#AAAAAA" Text="100%"/>
    </StatusBar>
    <!-- Canvas area -->
    <ScrollViewer HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Auto" Background="#141418">
      <ed:CanvasControl x:Name="Canvas" HorizontalAlignment="Center" VerticalAlignment="Center" Margin="20"/>
    </ScrollViewer>
  </DockPanel>
</Window>
```

- [ ] **Step 2: Editor window code-behind**

`windows/DMShot/Editor/EditorWindow.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Input;
using DMShot.Platform;
namespace DMShot.Editor;

public partial class EditorWindow : Window
{
    private readonly IClipboardService _clipboard = new WpfClipboard();
    private System.Drawing.Bitmap? _baseImage;

    public EditorWindow()
    {
        InitializeComponent();
        StrokeSlider.ValueChanged += (_, _) => Canvas.ActiveStroke = StrokeSlider.Value;
        BlurSlider.ValueChanged += (_, _) => Canvas.ActiveBlurStrength = (int)BlurSlider.Value;
        Canvas.ContentChanged += UpdateStatus;
        KeyDown += OnKey;
    }

    public void LoadImage(System.Drawing.Bitmap bmp)
    {
        _baseImage?.Dispose();
        _baseImage = (System.Drawing.Bitmap)bmp.Clone();
        Canvas.Load(_baseImage);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_baseImage is null) return;
        var crop = Canvas.Model.Crop;
        int w = crop?.Width ?? _baseImage.Width, h = crop?.Height ?? _baseImage.Height;
        DimText.Text = $"{w} × {h} px";
    }

    private void ToolClick(object sender, RoutedEventArgs e)
        => Canvas.ActiveTool = Enum.Parse<ToolKind>((string)((FrameworkElement)sender).Tag);

    private void HexChanged(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        try { Canvas.ActiveColor = ParseHex(HexBox.Text); } catch { /* ignore bad input */ }
    }

    private static uint ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6) hex = "FF" + hex;
        return Convert.ToUInt32(hex, 16);
    }

    private void UndoClick(object s, RoutedEventArgs e) => Canvas.Model.Undo();
    private void RedoClick(object s, RoutedEventArgs e) => Canvas.Model.Redo();

    private void CopyClick(object s, RoutedEventArgs e)
    {
        if (_baseImage is null) return;
        using var flat = Renderer.Flatten(_baseImage, Canvas.Model);
        _clipboard.SetImage(flat);
    }

    private void SaveClick(object s, RoutedEventArgs e)
    {
        if (_baseImage is null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "PNG image|*.png", FileName = "screenshot.png" };
        if (dlg.ShowDialog() != true) return;
        using var flat = Renderer.Flatten(_baseImage, Canvas.Model);
        flat.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Png);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.C) CopyClick(sender, e);
        else if (e.Key == Key.Z) Canvas.Model.Undo();
        else if (e.Key == Key.Y) Canvas.Model.Redo();
        else if (e.Key == Key.S) SaveClick(sender, e);
    }
}
```

- [ ] **Step 3: Wire App to the editor (replace the temporary preview)**

In `windows/DMShot/App.xaml.cs`, add a field and replace `OnImageCaptured`:
```csharp
using DMShot.Editor;
// ...
private EditorWindow? _editor;

private void OnImageCaptured(System.Drawing.Bitmap bmp)
{
    _clipboard.SetImage(bmp);                 // auto-copy the raw capture immediately
    if (_editor is null || !_editor.IsLoaded)
        _editor = new EditorWindow();
    _editor.LoadImage(bmp);
    if (!_editor.IsVisible) _editor.Show();
    _editor.Activate();
    _editor.WindowState = WindowState.Normal;
}
```

- [ ] **Step 4: Live-test the full editor**

Run: `cd windows && dotnet run --project DMShot`.
- `Ctrl+Shift+2`, select a region → editor opens with the image, dimensions shown, image already on clipboard.
- Draw Arrow, Rect, Ellipse, Underline, Highlight, Step (auto-incrementing numbers), Text (dialog), Blur (mosaic visible after Copy/flatten), Crop (status dims update).
- Change hex color (e.g. `#3AA0FF` + Enter) and stroke slider → next shapes use them.
- `Ctrl+Z`/`Ctrl+Y` undo/redo. Copy → paste into Paint shows annotations flattened. Save → PNG on disk matches.

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Editor/EditorWindow.xaml windows/DMShot/Editor/EditorWindow.xaml.cs windows/DMShot/App.xaml.cs
git commit -m "feat(win): annotation editor window (tools, color/stroke, copy/save, crop)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 11: M1 polish — equal-width capture buttons + zoom-to-fit

**Files:**
- Modify: `windows/DMShot/Editor/EditorWindow.xaml`(+`.cs`)

**Interfaces:**
- Consumes: existing editor. Produces: a left header with equal-width "Full Screen" / "Selection" buttons (parity with the macOS sidebar request) that trigger captures via a callback, and a fit-to-window zoom showing the real percentage.

- [ ] **Step 1: Add capture trigger callback to the editor**

In `EditorWindow.xaml.cs` add:
```csharp
public Action? OnRequestFullScreen { get; set; }
public Action? OnRequestArea { get; set; }
```
And in `App.OnStartup`, after creating the editor lazily, wire these to `_coordinator.CaptureFullScreen/CaptureArea`. (Set them whenever `_editor` is (re)created in `OnImageCaptured`.)

- [ ] **Step 2: Add equal-width buttons + zoom readout**

In `EditorWindow.xaml`, add a top row above the canvas (inside the DockPanel, `DockPanel.Dock="Top"`) :
```xml
<UniformGrid DockPanel.Dock="Top" Rows="1" Columns="2" Margin="8" Background="#1F1F24">
  <Button Content="Full Screen" Margin="4" Padding="0,8" Click="FullScreenClick"/>
  <Button Content="Selection" Margin="4" Padding="0,8" Click="AreaClick"/>
</UniformGrid>
```
`UniformGrid` guarantees equal widths. Code-behind:
```csharp
private void FullScreenClick(object s, RoutedEventArgs e) => OnRequestFullScreen?.Invoke();
private void AreaClick(object s, RoutedEventArgs e) => OnRequestArea?.Invoke();
```
Set `ZoomText` from the `ScrollViewer`/canvas scale (use `Canvas.LayoutTransform` ScaleTransform if you add zoom; for M1 a fixed `100%` is acceptable — keep the readout but mark zoom controls as deferred).

- [ ] **Step 3: Live-test**

Run: `cd windows && dotnet run --project DMShot`, capture, then click "Full Screen" and "Selection" from inside the editor — both trigger captures; buttons are visibly equal width.

- [ ] **Step 4: Commit**

```bash
git add windows/DMShot/Editor
git commit -m "feat(win): in-editor capture buttons (equal width) + zoom readout

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

**✅ M1 complete:** instant capture (full + area), per-monitor freeze, full editor, auto-clipboard, save. Live-testable end to end.

---

## Task 12: History store (persist last 10) — TDD

**Files:**
- Create: `windows/DMShot/History/HistoryEntry.cs`, `windows/DMShot/History/HistoryStore.cs`
- Test: `windows/DMShot.Tests/HistoryStoreTests.cs`

**Interfaces:**
- Produces:
  - `record AnnotationDto(string Kind, double X0,Y0,X1,Y1, uint ColorArgb, double StrokeWidth, string Text, int StepNumber, int BlurStrength)` + mappers `ToDto(Annotation)`, `FromDto(AnnotationDto)`.
  - `class HistoryEntry { string Id; string OriginalPngPath; string ThumbnailPngPath; List<AnnotationDto> Annotations; PixelRect? Crop; DateTime CreatedUtc; }`
  - `class HistoryStore` with `string Root`, `IReadOnlyList<HistoryEntry> Entries`, `HistoryEntry Add(Bitmap original, IEnumerable<Annotation> annotations, PixelRect? crop, DateTime nowUtc)`, `void Load()`. Keeps **max 10** (evicts oldest, deletes its files). Persists an `index.json`.
- Consumes: `Annotation`, `PixelRect`.

- [ ] **Step 1: Write failing tests (use a temp root)**

`windows/DMShot.Tests/HistoryStoreTests.cs`:
```csharp
using System.Drawing;
using DMShot.Capture;
using DMShot.Editor;
using DMShot.History;
using Xunit;

public class HistoryStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dmshot_test_" + Guid.NewGuid().ToString("N"));
    private DateTime _t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private DateTime Next() { _t = _t.AddMinutes(1); return _t; }

    [Fact]
    public void Add_EvictsOldestPastTen()
    {
        var store = new HistoryStore(_root);
        for (int i = 0; i < 12; i++)
            using (var bmp = new Bitmap(10, 10))
                store.Add(bmp, Array.Empty<Annotation>(), null, Next());
        Assert.Equal(10, store.Entries.Count);
    }

    [Fact]
    public void Load_RoundTripsAnnotations()
    {
        var store = new HistoryStore(_root);
        using (var bmp = new Bitmap(10, 10))
            store.Add(bmp, new[] { new Annotation { Kind = ToolKind.Arrow, X0 = 1, Y0 = 2, X1 = 3, Y1 = 4 } },
                      new PixelRect(0, 0, 5, 5), Next());

        var store2 = new HistoryStore(_root);
        store2.Load();
        Assert.Single(store2.Entries);
        Assert.Equal("Arrow", store2.Entries[0].Annotations[0].Kind);
        Assert.Equal(new PixelRect(0, 0, 5, 5), store2.Entries[0].Crop);
        Assert.True(File.Exists(store2.Entries[0].OriginalPngPath));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `cd windows && dotnet test --filter HistoryStoreTests`
Expected: FAIL (types missing).

- [ ] **Step 3: Implement HistoryEntry + HistoryStore**

`windows/DMShot/History/HistoryEntry.cs`:
```csharp
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
```

`windows/DMShot/History/HistoryStore.cs`:
```csharp
using System.Drawing;
using System.Text.Json;
using DMShot.Capture;
using DMShot.Editor;
namespace DMShot.History;

public sealed class HistoryStore
{
    private const int Max = 10;
    private readonly List<HistoryEntry> _entries = new();
    public string Root { get; }
    public IReadOnlyList<HistoryEntry> Entries => _entries;
    private string IndexPath => Path.Combine(Root, "index.json");

    public HistoryStore(string root)
    {
        Root = root;
        Directory.CreateDirectory(Root);
    }

    public void Load()
    {
        _entries.Clear();
        if (!File.Exists(IndexPath)) return;
        var list = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(IndexPath)) ?? new();
        _entries.AddRange(list.OrderBy(e => e.CreatedUtc));
    }

    public HistoryEntry Add(Bitmap original, IEnumerable<Annotation> annotations, PixelRect? crop, DateTime nowUtc)
    {
        string id = nowUtc.Ticks.ToString() + "_" + _entries.Count;
        string orig = Path.Combine(Root, id + ".png");
        string thumb = Path.Combine(Root, id + "_thumb.png");
        original.Save(orig, System.Drawing.Imaging.ImageFormat.Png);
        SaveThumb(original, thumb);

        var entry = new HistoryEntry
        {
            Id = id, OriginalPngPath = orig, ThumbnailPngPath = thumb,
            Annotations = annotations.Select(AnnotationDto.From).ToList(),
            Crop = crop, CreatedUtc = nowUtc
        };
        _entries.Add(entry);
        while (_entries.Count > Max)
        {
            var old = _entries[0]; _entries.RemoveAt(0);
            TryDelete(old.OriginalPngPath); TryDelete(old.ThumbnailPngPath);
        }
        Persist();
        return entry;
    }

    private static void SaveThumb(Bitmap src, string path)
    {
        int w = 200, h = Math.Max(1, (int)(src.Height * (200.0 / src.Width)));
        using var t = new Bitmap(w, h);
        using (var g = Graphics.FromImage(t))
        { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(src, 0, 0, w, h); }
        t.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private void Persist() => File.WriteAllText(IndexPath, JsonSerializer.Serialize(_entries,
        new JsonSerializerOptions { WriteIndented = true }));

    private static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `cd windows && dotnet test --filter HistoryStoreTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/History windows/DMShot.Tests/HistoryStoreTests.cs
git commit -m "feat(win): persistent history store (last 10, JSON + thumbnails, tested)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 13: History sidebar in the editor

**Files:**
- Modify: `windows/DMShot/Editor/EditorWindow.xaml`(+`.cs`), `windows/DMShot/App.xaml.cs`

**Interfaces:**
- Consumes: `HistoryStore`. Produces: a left sidebar of thumbnails; clicking one reloads its image + annotations + crop into the editor. New captures push into the store.

- [ ] **Step 1: Add the sidebar to XAML**

In `EditorWindow.xaml`, wrap the canvas area: add `DockPanel.Dock="Left"` `ListBox`:
```xml
<ListBox x:Name="HistoryList" DockPanel.Dock="Left" Width="180" Background="#1A1A1F"
         SelectionChanged="HistorySelected" BorderThickness="0">
  <ListBox.ItemTemplate>
    <DataTemplate>
      <Image Source="{Binding Thumb}" Height="100" Margin="4" Stretch="Uniform"/>
    </DataTemplate>
  </ListBox.ItemTemplate>
</ListBox>
```

- [ ] **Step 2: Bind history in code-behind**

In `EditorWindow.xaml.cs`:
```csharp
public sealed record HistoryVM(string Id, System.Windows.Media.ImageSource Thumb);
public HistoryStore? Store { get; set; }

public void RefreshHistory()
{
    if (Store is null) return;
    HistoryList.ItemsSource = Store.Entries
        .OrderByDescending(e => e.CreatedUtc)
        .Select(e => new HistoryVM(e.Id, LoadFrozen(e.ThumbnailPngPath)))
        .ToList();
}

private static System.Windows.Media.ImageSource LoadFrozen(string path)
{
    var bi = new System.Windows.Media.Imaging.BitmapImage();
    bi.BeginInit();
    bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
    bi.UriSource = new Uri(path);
    bi.EndInit(); bi.Freeze();
    return bi;
}

private void HistorySelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
{
    if (Store is null || HistoryList.SelectedItem is not HistoryVM vm) return;
    var entry = Store.Entries.FirstOrDefault(x => x.Id == vm.Id);
    if (entry is null) return;
    using var bmp = new System.Drawing.Bitmap(entry.OriginalPngPath);
    LoadImage(bmp);
    foreach (var d in entry.Annotations) Canvas.Model.Add(d.To());
    if (entry.Crop is { } c) Canvas.Model.SetCrop(c);
}
```
Note: `LoadImage` resets the canvas to a fresh `EditorModel`? It does not — to fully reload, give `CanvasControl` a `Reset()` that clears the model before re-adding. Add to `CanvasControl`:
```csharp
public void Reset() { foreach (var a in Model.Annotations.ToList()) Model.Remove(a); Model.SetCrop(null); }
```
and call `Canvas.Reset()` at the top of `LoadImage`.

- [ ] **Step 3: Wire the store in App**

In `App.xaml.cs`, create the store once and push captures:
```csharp
using DMShot.History;
// field:
private HistoryStore _history = null!;
// in OnStartup:
_history = new HistoryStore(Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DMShot", "history"));
_history.Load();
// in OnImageCaptured, after showing the editor:
_history.Add(bmp, Array.Empty<Annotation>(), null, DateTime.UtcNow);
_editor!.Store = _history;
_editor.RefreshHistory();
```
(Capturing stores the raw image immediately; later "save edited to history" can be added, but storing the capture satisfies the "last 10 edited images" sidebar for v1.)

- [ ] **Step 4: Live-test**

Run: `cd windows && dotnet run --project DMShot`. Take several captures → thumbnails appear in the sidebar (newest first), capped at 10. Click an older thumbnail → it reloads into the editor. Restart the app → `Ctrl+Shift+2` once to open the editor; previous thumbnails are still there (persisted).

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Editor windows/DMShot/App.xaml.cs
git commit -m "feat(win): history sidebar (thumbnails, reload with annotations, persistent)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 14: System tray + hide-on-close

**Files:**
- Create: `windows/DMShot/Platform/ITrayIcon.cs`, `windows/DMShot/Platform/NotifyIconTray.cs`
- Modify: `windows/DMShot/App.xaml.cs`, `windows/DMShot/Editor/EditorWindow.xaml.cs`

**Interfaces:**
- Produces: `interface ITrayIcon : IDisposable { event Action OpenRequested, FullScreenRequested, AreaRequested, QuitRequested; void Show(); }` and `class NotifyIconTray : ITrayIcon` using `Hardcodet.Wpf.TaskbarNotification.TaskbarIcon`.
- Consumes: capture coordinator + editor (via App wiring).

- [ ] **Step 1: Interface + implementation**

`windows/DMShot/Platform/ITrayIcon.cs`:
```csharp
namespace DMShot.Platform;
public interface ITrayIcon : IDisposable
{
    event Action? OpenRequested;
    event Action? FullScreenRequested;
    event Action? AreaRequested;
    event Action? QuitRequested;
    void Show();
}
```

`windows/DMShot/Platform/NotifyIconTray.cs`:
```csharp
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
namespace DMShot.Platform;

public sealed class NotifyIconTray : ITrayIcon
{
    private readonly TaskbarIcon _icon;
    public event Action? OpenRequested;
    public event Action? FullScreenRequested;
    public event Action? AreaRequested;
    public event Action? QuitRequested;

    public NotifyIconTray()
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = "DM_Screenshot",
            IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Resources/AppIcon.ico"))
        };
        var menu = new ContextMenu();
        menu.Items.Add(Item("New Fullscreen Shot", () => FullScreenRequested?.Invoke()));
        menu.Items.Add(Item("New Area Shot", () => AreaRequested?.Invoke()));
        menu.Items.Add(Item("Open Editor", () => OpenRequested?.Invoke()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Quit", () => QuitRequested?.Invoke()));
        _icon.ContextMenu = menu;
        _icon.TrayMouseDoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    private static MenuItem Item(string header, Action onClick)
    { var m = new MenuItem { Header = header }; m.Click += (_, _) => onClick(); return m; }

    public void Show() => _icon.Visibility = System.Windows.Visibility.Visible;
    public void Dispose() => _icon.Dispose();
}
```
Note: until Task 17 ships `AppIcon.ico`, set a temporary built-in icon or wrap the `IconSource` line in try/catch so the tray still appears.

- [ ] **Step 2: Wire tray in App; hide editor on close**

In `App.xaml.cs` `OnStartup`:
```csharp
_tray = new NotifyIconTray();
_tray.FullScreenRequested += () => _coordinator.CaptureFullScreen();
_tray.AreaRequested += () => _coordinator.CaptureArea();
_tray.OpenRequested += ShowEditor;
_tray.QuitRequested += () => Shutdown();
_tray.Show();
```
Add `ShowEditor()`:
```csharp
private void ShowEditor()
{
    if (_editor is null || !_editor.IsLoaded) _editor = new EditorWindow { Store = _history };
    _editor.RefreshHistory();
    _editor.Show(); _editor.WindowState = WindowState.Normal; _editor.Activate();
}
```
Add field `private ITrayIcon _tray = null!;` and dispose it in `OnExit`.

In `EditorWindow.xaml.cs`, hide instead of close:
```csharp
protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
{ e.Cancel = true; Hide(); }
```

- [ ] **Step 3: Live-test**

Run: `cd windows && dotnet run --project DMShot`. A tray icon appears. Right-click → menu items work (Fullscreen/Area/Open/Quit). Double-click opens the editor. Closing the editor window hides it (app stays in tray, hotkeys still fire). Quit exits the process.

- [ ] **Step 4: Commit**

```bash
git add windows/DMShot/Platform/ITrayIcon.cs windows/DMShot/Platform/NotifyIconTray.cs windows/DMShot/App.xaml.cs windows/DMShot/Editor/EditorWindow.xaml.cs
git commit -m "feat(win): system tray (capture actions, open, quit) + hide-on-close

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 15: Settings (editable shortcuts + launch-at-login) — TDD core

**Files:**
- Create: `windows/DMShot/Settings/Settings.cs`, `windows/DMShot/Settings/SettingsStore.cs`, `windows/DMShot/Settings/SettingsWindow.xaml`(+`.cs`), `windows/DMShot/Settings/ShortcutRecorderControl.cs`
- Test: `windows/DMShot.Tests/SettingsTests.cs`
- Modify: `windows/DMShot/App.xaml.cs` (load settings, register hotkeys from them, re-register on change), tray menu (add "Settings…")

**Interfaces:**
- Produces:
  - `class Settings { string FullScreenHotkey = "Ctrl+Shift+1"; string AreaHotkey = "Ctrl+Shift+2"; bool LaunchAtLogin = false; }`
  - `class SettingsStore { string Path; Settings Load(); void Save(Settings s); }` (JSON at `%APPDATA%\DMShot\settings.json`).
  - `static class LaunchAtLogin { void Set(bool enabled); bool Get(); }` (registry `HKCU\...\Run`).
  - `SettingsWindow` with nav (Shortcuts / General / Updates / Language) and a `ShortcutRecorderControl`.
- Consumes: `HotkeySpec`, `IHotkeyManager`.

- [ ] **Step 1: Write failing settings round-trip test**

`windows/DMShot.Tests/SettingsTests.cs`:
```csharp
using DMShot.Settings;
using Xunit;

public class SettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "dmshot_settings_" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void SaveLoad_RoundTrips()
    {
        var store = new SettingsStore(_path);
        store.Save(new Settings { AreaHotkey = "Alt+A", LaunchAtLogin = true });
        var loaded = store.Load();
        Assert.Equal("Alt+A", loaded.AreaHotkey);
        Assert.True(loaded.LaunchAtLogin);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = new SettingsStore(_path);
        var s = store.Load();
        Assert.Equal("Ctrl+Shift+1", s.FullScreenHotkey);
        Assert.Equal("Ctrl+Shift+2", s.AreaHotkey);
    }

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `cd windows && dotnet test --filter SettingsTests`
Expected: FAIL.

- [ ] **Step 3: Implement Settings + SettingsStore + LaunchAtLogin**

`windows/DMShot/Settings/Settings.cs`:
```csharp
namespace DMShot.Settings;
public sealed class Settings
{
    public string FullScreenHotkey { get; set; } = "Ctrl+Shift+1";
    public string AreaHotkey { get; set; } = "Ctrl+Shift+2";
    public bool LaunchAtLogin { get; set; } = false;
}
```

`windows/DMShot/Settings/SettingsStore.cs`:
```csharp
using System.Text.Json;
namespace DMShot.Settings;

public sealed class SettingsStore
{
    public string Path { get; }
    public SettingsStore(string path) { Path = path; }
    public static SettingsStore Default() => new(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DMShot", "settings.json"));

    public Settings Load()
    {
        try { return File.Exists(Path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path)) ?? new() : new(); }
        catch { return new(); }
    }

    public void Save(Settings s)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public static class LaunchAtLogin
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "DM_Screenshot";
    public static void Set(bool enabled)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key)!;
        if (enabled) k.SetValue(Name, $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue(Name, false);
    }
    public static bool Get()
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValue(Name) != null;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `cd windows && dotnet test --filter SettingsTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Shortcut recorder control**

`windows/DMShot/Settings/ShortcutRecorderControl.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DMShot.Platform;
namespace DMShot.Settings;

public sealed class ShortcutRecorderControl : TextBox
{
    public string Hotkey { get; private set; } = "";
    public event Action<string>? HotkeyChanged;

    public ShortcutRecorderControl() { IsReadOnly = true; Focusable = true; Text = "Click and press keys…"; }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;

        var mods = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= HotkeyModifiers.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= HotkeyModifiers.Win;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        var spec = new HotkeySpec(mods, vk);
        Hotkey = spec.Format();
        Text = Hotkey;
        HotkeyChanged?.Invoke(Hotkey);
    }
}
```

- [ ] **Step 6: Settings window (DM-styled nav)**

`windows/DMShot/Settings/SettingsWindow.xaml`:
```xml
<Window x:Class="DMShot.Settings.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:s="clr-namespace:DMShot.Settings"
        Title="Settings" Width="640" Height="420" Background="#1F1F24">
  <Grid>
    <Grid.ColumnDefinitions><ColumnDefinition Width="180"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
    <ListBox x:Name="Nav" Grid.Column="0" Background="#1A1A1F" BorderThickness="0" Foreground="White"
             SelectionChanged="NavChanged" Margin="0">
      <ListBoxItem Content="Shortcuts" IsSelected="True"/>
      <ListBoxItem Content="General"/>
      <ListBoxItem Content="Updates"/>
      <ListBoxItem Content="Language"/>
    </ListBox>
    <Border Grid.Column="1" Padding="20">
      <StackPanel x:Name="Pane"/>
    </Border>
  </Grid>
</Window>
```
`windows/DMShot/Settings/SettingsWindow.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
namespace DMShot.Settings;

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly SettingsStore _store;
    public event Action<Settings>? Saved;

    public SettingsWindow(Settings settings, SettingsStore store)
    { InitializeComponent(); _settings = settings; _store = store; ShowShortcuts(); }

    private void NavChanged(object sender, SelectionChangedEventArgs e)
    {
        switch ((Nav.SelectedItem as ListBoxItem)?.Content)
        {
            case "Shortcuts": ShowShortcuts(); break;
            case "General": ShowGeneral(); break;
            case "Updates": ShowText("Updates", "Check github.com/m0nji/DM_Screenshot for new versions."); break;
            case "Language": ShowText("Language", "English (more languages later)."); break;
        }
    }

    private void ShowShortcuts()
    {
        Pane.Children.Clear();
        Pane.Children.Add(new TextBlock { Text = "Global Shortcuts", Foreground = System.Windows.Media.Brushes.White, FontSize = 16, Margin = new Thickness(0,0,0,12) });
        Pane.Children.Add(Row("Full screen", _settings.FullScreenHotkey, h => { _settings.FullScreenHotkey = h; Commit(); }));
        Pane.Children.Add(Row("Area selection", _settings.AreaHotkey, h => { _settings.AreaHotkey = h; Commit(); }));
    }

    private FrameworkElement Row(string label, string current, Action<string> onSet)
    {
        var rec = new ShortcutRecorderControl { Text = current, Width = 200 };
        rec.HotkeyChanged += onSet;
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,4,0,4) };
        sp.Children.Add(new TextBlock { Text = label, Foreground = System.Windows.Media.Brushes.White, Width = 140, VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(rec);
        return sp;
    }

    private void ShowGeneral()
    {
        Pane.Children.Clear();
        var cb = new CheckBox { Content = "Launch at login", Foreground = System.Windows.Media.Brushes.White, IsChecked = _settings.LaunchAtLogin };
        cb.Checked += (_, _) => { _settings.LaunchAtLogin = true; LaunchAtLogin.Set(true); Commit(); };
        cb.Unchecked += (_, _) => { _settings.LaunchAtLogin = false; LaunchAtLogin.Set(false); Commit(); };
        Pane.Children.Add(cb);
    }

    private void ShowText(string title, string body)
    {
        Pane.Children.Clear();
        Pane.Children.Add(new TextBlock { Text = title, Foreground = System.Windows.Media.Brushes.White, FontSize = 16, Margin = new Thickness(0,0,0,8) });
        Pane.Children.Add(new TextBlock { Text = body, Foreground = System.Windows.Media.Brushes.LightGray, TextWrapping = TextWrapping.Wrap });
    }

    private void Commit() { _store.Save(_settings); Saved?.Invoke(_settings); }
}
```

- [ ] **Step 7: Load settings in App; re-register hotkeys on save; add tray "Settings…"**

In `App.xaml.cs`:
```csharp
using DMShot.Settings;
// fields:
private Settings _settings = null!;
private SettingsStore _settingsStore = null!;
// in OnStartup, BEFORE registering hotkeys:
_settingsStore = SettingsStore.Default();
_settings = _settingsStore.Load();
// register from settings:
RegisterHotkeysFromSettings();
// add a method:
private void RegisterHotkeysFromSettings()
{
    _hotkeys.UnregisterAll();
    _hotkeys.Register(HK_FULL, HotkeySpec.Parse(_settings.FullScreenHotkey));
    _hotkeys.Register(HK_AREA, HotkeySpec.Parse(_settings.AreaHotkey));
}
// add tray Settings entry: extend ITrayIcon with event SettingsRequested and a menu item,
// then: _tray.SettingsRequested += OpenSettings;
private void OpenSettings()
{
    var w = new SettingsWindow(_settings, _settingsStore);
    w.Saved += s => { _settings = s; RegisterHotkeysFromSettings(); };
    w.Show();
}
```
Add `event Action? SettingsRequested;` to `ITrayIcon`/`NotifyIconTray` and a "Settings…" menu item that invokes it (mirror the existing items).

- [ ] **Step 8: Live-test**

Run: `cd windows && dotnet run --project DMShot`. Tray → Settings…. In Shortcuts, click a recorder and press e.g. `Ctrl+Shift+5` → it displays; close and press the new combo → capture fires (old one no longer does). General → toggle Launch at login → check `HKCU\...\Run` has/removes the `DM_Screenshot` value. Settings persist across restart.

- [ ] **Step 9: Commit**

```bash
git add windows/DMShot/Settings windows/DMShot/App.xaml.cs windows/DMShot/Platform/ITrayIcon.cs windows/DMShot/Platform/NotifyIconTray.cs windows/DMShot.Tests/SettingsTests.cs
git commit -m "feat(win): settings window — editable shortcuts + launch-at-login (tested core)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 16: DM theme (dark + orange accent)

**Files:**
- Create: `windows/DMShot/Theme/DmTheme.xaml`
- Modify: `windows/DMShot/App.xaml` (merge the resource dictionary), editor/settings windows (consume brushes)

**Interfaces:**
- Produces: app-wide resource dictionary with keys `DmBackground` (`#1F1F24`), `DmSurface` (`#26262C`), `DmSurfaceAlt` (`#1A1A1F`), `DmAccent` (`#C97B4A`), `DmAccentTint` (`#1FFF8A4C`), `DmText` (`#EDEDED`), plus default styles for `Button`, `Slider` (`Foreground`/track = accent), `ListBox`, `ToolBar` so highlights/selection use the accent.
- Consumes: nothing.

- [ ] **Step 1: Theme resource dictionary**

`windows/DMShot/Theme/DmTheme.xaml`:
```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <SolidColorBrush x:Key="DmBackground" Color="#1F1F24"/>
  <SolidColorBrush x:Key="DmSurface" Color="#26262C"/>
  <SolidColorBrush x:Key="DmSurfaceAlt" Color="#1A1A1F"/>
  <SolidColorBrush x:Key="DmAccent" Color="#C97B4A"/>
  <SolidColorBrush x:Key="DmAccentTint" Color="#FF8A4C" Opacity="0.12"/>
  <SolidColorBrush x:Key="DmText" Color="#EDEDED"/>

  <Style TargetType="Button">
    <Setter Property="Background" Value="{StaticResource DmSurface}"/>
    <Setter Property="Foreground" Value="{StaticResource DmText}"/>
    <Setter Property="BorderBrush" Value="#3A3A42"/>
    <Setter Property="Padding" Value="8,4"/>
    <Style.Triggers>
      <Trigger Property="IsMouseOver" Value="True">
        <Setter Property="BorderBrush" Value="{StaticResource DmAccent}"/>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style TargetType="Slider">
    <Setter Property="Foreground" Value="{StaticResource DmAccent}"/>
  </Style>

  <Style TargetType="TextBlock"><Setter Property="Foreground" Value="{StaticResource DmText}"/></Style>
</ResourceDictionary>
```

- [ ] **Step 2: Merge into App resources**

`windows/DMShot/App.xaml`:
```xml
<Application x:Class="DMShot.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Application.Resources>
    <ResourceDictionary>
      <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="Theme/DmTheme.xaml"/>
      </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
  </Application.Resources>
</Application>
```

- [ ] **Step 3: Apply accent to selection states**

In `EditorWindow.xaml`, set the history `ListBox` selected-item highlight to the accent: add to the `ListBox` an `ItemContainerStyle` whose `IsSelected` trigger sets `BorderBrush`/`Background` to `{StaticResource DmAccentTint}` with a `{StaticResource DmAccent}` border. Verify the area-overlay `SelRect` already uses `#C97B4A` (Task 6) and the default annotation color is the orange (Task 7) — consistent.

- [ ] **Step 4: Live-test**

Run: `cd windows && dotnet run --project DMShot`. Editor/settings show the dark surfaces; buttons highlight orange on hover; sliders are orange; the selected history thumbnail has an orange ring; overlay selection rectangle is orange. Matches the DM family look.

- [ ] **Step 5: Commit**

```bash
git add windows/DMShot/Theme windows/DMShot/App.xaml windows/DMShot/Editor/EditorWindow.xaml
git commit -m "feat(win): DM dark + orange theme applied app-wide

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 17: App + tray icon (DM squircle marquee)

**Files:**
- Create: `windows/DMShot/Resources/AppIcon.ico`
- Modify: `windows/DMShot/DMShot.csproj` (re-add `<ApplicationIcon>`), ensure `NotifyIconTray` icon path resolves.

**Interfaces:**
- Produces: a multi-resolution `.ico` (16/32/48/256) reusing the DM squircle + selection-marquee motif (orange `#c97b4a` marquee), used for the window icon, taskbar, and tray.

- [ ] **Step 1: Produce the .ico**

Reuse the macOS icon motif (`mac/Resources/AppIcon.svg`): dark squircle, white hairline rim, glassy tiles, an orange selection-marquee. Convert an SVG/PNG to a multi-size `.ico`. If ImageMagick is available:
```bash
# from a 1024px PNG export of the DM squircle+marquee:
magick convert appicon-1024.png -define icon:auto-resize=256,48,32,16 windows/DMShot/Resources/AppIcon.ico
```
If no SVG export tooling is present on the Windows box, generate a placeholder dark-squircle PNG with the orange marquee at 256px and convert it; flag in the commit that the final art may need a higher-fidelity export to fully match `mac/Resources/AppIcon.svg`.

- [ ] **Step 2: Reference the icon**

Re-add to `DMShot.csproj` `<PropertyGroup>`:
```xml
<ApplicationIcon>Resources\AppIcon.ico</ApplicationIcon>
```
Mark the ico as resource so the `pack://` URI in `NotifyIconTray` resolves — add to `DMShot.csproj`:
```xml
<ItemGroup>
  <Resource Include="Resources\AppIcon.ico"/>
</ItemGroup>
```
Set each window's icon: in `EditorWindow`/`SettingsWindow` XAML add `Icon="/Resources/AppIcon.ico"`.

- [ ] **Step 3: Live-test**

Run: `cd windows && dotnet run --project DMShot`. The tray icon, taskbar entry, and window title-bar icon all show the DM squircle. Remove any temporary try/catch fallback added in Task 14.

- [ ] **Step 4: Commit**

```bash
git add windows/DMShot/Resources/AppIcon.ico windows/DMShot/DMShot.csproj windows/DMShot/Editor/EditorWindow.xaml windows/DMShot/Settings/SettingsWindow.xaml windows/DMShot/Platform/NotifyIconTray.cs
git commit -m "feat(win): DM app + tray icon (squircle marquee)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

**✅ M2 complete:** history sidebar, tray, settings (editable shortcuts + launch-at-login), DM theming, icon. Feature parity with the macOS v1 app.

---

## Final verification (run before declaring done)

- [ ] `cd windows && dotnet test` — all unit tests pass (CropMath, HotkeySpec, EditorModel, Renderer, HistoryStore, Settings, capture smoke).
- [ ] `cd windows && dotnet build -c Release` — no warnings-as-errors, builds clean.
- [ ] Full manual pass on the live Windows machine:
  - `Ctrl+Shift+1` full screen, `Ctrl+Shift+2` area (per-monitor, DPI-correct), Esc cancels.
  - Every editor tool draws correctly and flattens correctly on Copy/Save.
  - History persists across a real app restart.
  - Tray actions + hide-on-close + Quit behave.
  - Settings change a shortcut live; launch-at-login toggles the registry value.
- [ ] On a multi-monitor + mixed-DPI setup (e.g. 100% + 150%): selection lands on the right pixels on both displays.

## M3 (out of scope — note for a later plan)

Installer (Inno Setup or MSIX) + `signtool` code-signing with a Windows certificate; optional GitHub Actions Windows job. Mirrors the macOS notarization split but with a Windows cert. Not built here.
