# Accessibility

MasterSafe Authenticator aims to meet **WCAG 2.2 level AA** and the accessibility parts of EN 301 549 for a desktop program.
This page says what is in place, how it is checked, and what is still missing. It is a self-assessment, not a certification.

## What is in place (1.5)

| Need | What the app does |
| --- | --- |
| Screen readers | Every input, button, progress bar and list has an accessible name, headings are marked with their level, status messages are announced politely and the automatic-lock warning assertively. A helper (`AccessibilityNamer`) names inputs from their visible label, and a test fails if any control on any page has no name |
| Keyboard only | Every function can be used without a mouse: access keys on the navigation (Alt and the underlined letter), F1 help, Ctrl+L lock, Ctrl+F / Ctrl+N / Ctrl+B / Ctrl+C / Ctrl+U / Delete on the Passwords page. Drag and drop always has a button alternative |
| Low vision | A **Text and window size** setting (100 to 200%) and Ctrl and plus / minus / 0 at any time. The layout reflows instead of clipping |
| Contrast | Light, dark and a **High contrast** theme (also chosen automatically when Windows high contrast is on and the theme is "Follow the system"). The colour pairs of the light and dark themes are checked against the 4.5:1 ratio by a test. Colour is never the only signal: errors, the strength bar and the recycle bin also use words and icons |
| Time limits | The automatic lock shows a warning 30 seconds ahead with a "Keep unlocked" button, and every keypress or click counts as activity. The sign-in pause is shown in words and counts down in text |
| Motion | The app has no animation apart from a thin progress bar for the code timer |
| Cognitive load | Plain language, one task per screen, confirmations for destructive actions with an undo (recycle bin), a step-by-step first-run guide in Help, and errors that say what to do |
| Motor | No action needs a double-click, a drag or a long press. Targets are at least the size of standard controls |

## How it is checked

- **Automatic:** `AccessibilityTests` opens every page in a headless copy of the real window and fails when an interactive
  control has no accessible name, a page has no heading, or a list or progress bar is unnamed. Contrast ratios of the theme
  colours are calculated in tests.
- **By hand, before each release** (see [Documentation.md](Documentation.md), section 8): Narrator and NVDA through every page,
  keyboard-only use of every function, 200% text size, Windows High Contrast, and Voice Access (saying the names of buttons).

## Known gaps

- Orca (Linux screen reader) has not been tried; Avalonia's Linux accessibility support is less complete than on Windows.
- Languages: English, Spanish and Finnish. Some messages built in code from changing parts may still appear in English.
- No dyslexia-friendly font option or line-spacing setting yet.
- The first-run guide is a Help topic and reminders, not yet a step-by-step wizard.

To report an accessibility problem, open an issue at <https://github.com/GabrielVergara/MasterSafeAuthenticator/issues> and say
which assistive technology you use. These reports are treated like bugs.
