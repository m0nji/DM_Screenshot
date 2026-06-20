# Localization (German / English) — Design

**Status:** Approved design — ready for implementation plan
**Date:** 2026-06-20
**Branch / worktree:** `feat/localization` (`../dmshot-localization`)
**Platforms:** macOS (`mac/`) **and** Windows (`windows/`) — both in this change (parity).

## Goal

Add an in-app interface language switch with **English (default)** and **German**.
Switching is **live** (no restart): all open windows, tooltips, status-bar / tray
menus, and alerts update immediately. English remains the default on first run,
regardless of the OS system language. Going forward, **every user-facing string
must exist in both languages**, enforced by tooling (compiler on macOS, unit test
on Windows).

## Non-goals

- No third language (structure must not preclude one, but only en/de ship now).
- No first-run OS-language auto-detection (always default to English).
- No per-document / per-window language; one app-wide setting.
- No translation of file names, log output, or developer-facing strings.

## Decisions (from brainstorming)

| Question | Decision |
|---|---|
| Live switch vs. restart | **Live** — UI updates in place, no restart. |
| String organization | **Typed keys** — one key per string, both languages required. |
| First-run default | **Always English** — ignore OS language. |
| Platform scope | **Both macOS + Windows now** (Windows build/verify is the user's step). |

## Shared architecture

Identical concept on both platforms; only the language/framework binding differs.

- **`Language` enum**: `english` / `german`. Persisted `rawValue` = `"en"` / `"de"`.
  Display names `"English"` / `"Deutsch"` (display names are **not** translated —
  each language is shown in its own name).
- **Persistence**: existing settings store. macOS `AppSettingsStore` → UserDefaults
  key `"language"`. Windows `Settings.Language` in `settings.json`. Default `"en"`
  on first run; unknown/missing value falls back to `english`.
- **Central string provider with typed keys**: each string is one key resolving to
  an English **and** a German value. A missing translation must fail before runtime
  (macOS: non-exhaustive `switch` ⇒ compile error; Windows: key-parity unit test).
- **Live switching**: the provider is observable. Changing the language in Settings
  notifies all consumers. Declarative UI (SwiftUI / XAML bindings) re-renders
  automatically; imperatively-built UI (AppKit menu, WPF code-behind panes, tray
  menu) is rebuilt via an explicit change subscription.

Data flow:

```
Settings picker ──sets──> Store.language ──notify──> StringProvider.current
                                                            │
            ┌────────────────────────────────────────────────┤
            ▼                       ▼                         ▼
     SwiftUI / XAML          AppKit / tray menus        tooltips / alerts
     (auto re-render)        (rebuilt on change)        (read live)
```

The two pre-existing stray German strings (macOS `CanvasView.swift`:
"Text eingeben" / "Abbrechen"; Windows `TextPromptWindow`) are folded into the
system and thereby become correctly bilingual.

## macOS implementation

New file `Localization.swift`:

- `enum L` — one case per user-facing string (grouped by area with comments).
- `final class Localizer: ObservableObject` with `@Published var language: Language`,
  a shared instance, and `func tr(_ key: L) -> String` whose body is
  `switch language { case .english: …; case .german: … }`. The per-key value is
  resolved by a non-exhaustive `switch key` over `L` — **adding a case without both
  language values fails to compile**.
- `Localizer` is seeded from and writes back to `AppSettingsStore.language`
  (single source of truth for persistence; `Localizer` is the observable surface).

Wiring:

- **SwiftUI views** (`Settings.swift`, `EditorView.swift`, `QuickEditToolbar.swift`,
  `EditorControls.swift`, `VideoPreviewWindow.swift`, `GIFViewerWindow.swift`,
  `RecordingControlWindow.swift`): inject `Localizer` as `@EnvironmentObject` /
  `@ObservedObject`. Replace literals with `tr(.copy)` etc.; `.help(...)` tooltips
  likewise. Views re-render on language change automatically.
- **Tool specs** (`EditorView.swift`, `QuickEditToolbar.swift`): the `help` field
  becomes an `L` key, resolved at render time rather than a fixed string.
- **AppKit status-bar menu** (`App.swift`): extend the existing `updateMenuTitles()`
  (already sets dynamic shortcut suffixes) to pull localized base titles, and call
  it on language change via a Combine subscription to `Localizer`.
- **Alerts** (Screen-Recording permission in `App.swift`; text prompt in
  `CanvasView.swift`): read localized strings at presentation time.
- **Settings "Language" pane** (`Settings.swift`): replace the stub with a `Picker`
  over `Language.allCases` bound to `AppSettingsStore.language`, showing
  "English" / "Deutsch". Remove the "More languages will be added later." caption.

## Windows implementation (WPF)

The Windows port is code-behind-heavy with no MVVM, so live switching needs an
observable provider plus explicit rebuilds for imperatively-built UI.

New file `Localization/Loc.cs`:

- `sealed class Loc : INotifyPropertyChanged` singleton `Loc.Instance`, with
  `Language Current` (set on language change) and a string **indexer**
  `this[string key]` returning the current language's value.
- Keys as `const string` (or `enum` + helper); values in two `Dictionary<string,string>`
  (`En`, `De`).
- On language change: set `Current`, raise `PropertyChanged("Item[]")` (updates all
  bound XAML) and a `LanguageChanged` event (for code-behind / tray rebuilds).

Wiring:

- **XAML strings** (`SettingsWindow.xaml`, `EditorWindow.xaml`,
  `TextPromptWindow.xaml`, etc.): a markup extension `{loc:Tr Copy}` expands to
  `Binding Path=[Copy], Source={x:Static loc:Loc.Instance}` for `Content`,
  `Text`, `ToolTip`, `Header`, `Title`. Live-updates via the indexer notification.
- **Imperatively-built UI** (`SettingsWindow.xaml.cs` `Show*()` panes;
  `Platform/NotifyIconTray.cs` context menu + tooltip): subscribe to
  `Loc.LanguageChanged` and re-run the existing build method.
- **`Settings.cs`**: add `public string Language { get; set; } = "en";`, persisted by
  the existing JSON serialization. Map `"en"`/`"de"` ↔ `Language`.
- **Settings "Language" pane**: `ComboBox` with "English" / "Deutsch", writes
  `Settings.Language` and sets `Loc.Instance.Current`.

Building and verifying on Windows is the user's step (no .NET toolchain here).

## Testing

- **macOS** (`swift test`):
  - For **every** `L` case, both languages resolve to a non-empty string.
  - `AppSettingsStore`: `language` round-trips through UserDefaults; default is
    `.english` when unset/unknown.
- **Windows** (`DMShot.Tests`):
  - `En` and `De` dictionaries have **identical key sets**, no empty values
    (this replaces macOS's compile-time guarantee).
  - `Settings` round-trips `Language` through JSON; default `"en"`.

## Future-proofing — "all future text in both languages"

- Rule: no user-facing string literal may live directly in a view / menu / tooltip /
  alert — it must go through `L` (macOS) / `Loc` (Windows).
- Enforcement: macOS compiler (non-exhaustive `switch`), Windows key-parity unit
  test. Adding a string in one language without the other breaks the build/tests.
- Document the rule in `CLAUDE.md` (new short "Localization" note) and add a
  `Localization` row to `docs/PARITY.md`'s feature map.

## Out of scope / follow-ups

- A third language would extend the `Language` enum and add a `case` arm / dictionary;
  no structural change required.
- Optional future: OS-language-based first-run default (explicitly deferred).
