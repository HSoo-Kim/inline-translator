# Inline Translator settings

## Purpose and platform

Windows Forms tray utility, used locally and over remote desktop. Apply the
Apple-design principles of clear grouping, predictable response, agency and
flexibility using the existing Windows controls. Preserve translation behavior.
No framework migration, glass effects, looping motion, or artificial delays.

## Information architecture

Three equal navigation tabs stay visible above the content:

- Translation: My language, writing hotkeys and destinations, tone, reading hotkey.
- Engines: primary and fallback models, sign-in state, per-engine test results.
- General: whole-input apps, bubble appearance/position, interface language,
  startup and updates.

Default tab is Translation. Switching tabs keeps unsaved fields intact.
Compact settings title replaces the large repeated logo/tagline. Long usage
instructions become short, local helper text beside the relevant controls.

## Layout and responsive contract

Resize/maximize and working-area fitting from 0.1.9 remain. Fixed top navigation
and bottom action bar, one vertical scroll region in each tab. No horizontal
scroll at 640x480, 800x600, 1024x768 or 1280x720 working areas at current 200% DPI.
Available width drives content and wrapping; cards and rows cannot impose a
larger minimum width than their viewport. Wide rows align labels and fields;
narrow rows stack labels and fields. Long options must get a full-width field,
natural wrapping or an explicit readable detail, not unexplained ellipses.

## Existing design tokens and primitives

Reuse Ui.Canvas, Card, Ink, Muted, Line, Accent, Soft, Red and Green, Segoe UI and
Segoe UI Semibold, existing rounded panels, buttons, toggles and dropdowns.
Use Ui.GreenInk (#15803d) for readable success/status text; Green stays the dot color.
Use 8/12/16/20 spacing and restrained 10/14 corner radii. Primary action is Save,
secondary Cancel, optional Update. Tabs have an explicit selected state and
keyboard focus. No emoji decoration. Do not shrink text to fit.

## Interaction and feedback

Press feedback starts on pointer-down. Navigation and input never wait for an
animation. Remote use favors immediate changes; no spring is necessary here.
Inline hotkey errors update when the user edits/enables a hotkey, including
duplicates across the reading hotkey and writing presets. Disable Save while
invalid. OS registration failures returned by save stay visible next to their
hotkeys. Engine tests show progress/success/failure inside their own engine
section and do not overwrite the other engine's result.

## State and persistence

Save starts disabled, enables only for a changed, valid draft (including Windows
startup), disables when changes are reverted. Test/account status and tab
selection are not settings changes. All editable fields survive tab switching.
Keep existing save/cancel semantics and interface-language behavior unless a
change is required to avoid losing edits; never silently discard draft values.

## Accessibility and localization

All new text has ko/en/zh-CN/ja translations. Use native keyboard navigation and
visible focus, name controls for automation, pair color with text. Wrap long
labels and helper paragraphs, preserve complete hotkeys and model identifiers.
No transparency or motion dependence. Preserve Windows high contrast behavior
where existing primitives already support it; broader theming is out of scope.

## Verification

Pure settings validation/change-comparison tests, private-desktop interaction
QA for dirty/revert/duplicates/tab retention and per-row test state, all three
tabs in four languages at desktop and small widths with scroll-end captures.
Verify footer visibility and zero horizontal overflow, then independently
review fresh screenshots. Never inject keys into the user's active desktop.
