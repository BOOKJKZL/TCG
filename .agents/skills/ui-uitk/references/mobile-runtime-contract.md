# Mobile runtime contract

Read this checklist before implementing or reviewing runtime UI Toolkit changes in this repository.

## Layout

- Render through the full device viewport; do not modify `Camera.rect` to enforce a design aspect.
- Separate the edge-to-edge background layer from the Safe Area content layer.
- Search for the shared Safe Area component before adding screen-specific code. If none exists, create one reusable, injectable component in the P0-D slice; do not duplicate inset math in each controller.
- Apply current `Screen.safeArea` insets after panel geometry is valid and refresh them after geometry/orientation changes.
- Avoid assuming screen pixels equal panel points or share the same origin. Convert safe-area corners through the active runtime panel mapping (for example `RuntimePanelUtils.ScreenToPanel`) and test the top/bottom inversion explicitly.
- Keep essential controls reachable with display cutouts, gesture/navigation bars, and enlarged text.
- Prefer flexible content plus bounded scroll views. Avoid nested unconstrained scroll views.

Minimum layout cases:

| Case | Viewport | Insets to exercise |
|---|---:|---:|
| Narrow | 720 x 1600 | top 48, bottom 72 |
| Standard | 1080 x 2160 | top 80, bottom 96 |
| Tall | 1080 x 2400 | top 96, bottom 120 |
| Cutout asymmetry | 1080 x 2400 | left 36, top 120, right 0, bottom 96 |

Use project test helpers rather than baking these values into production code.

Treat 48 panel logical units at the reference scale as the automated minimum for a primary touch target. Physical-device verification remains necessary because PanelSettings scaling and device density determine the final Android size.

## Initialization and content states

Build the persistent shell before loading a catalog:

1. Resolve or request application language.
2. Create the page structure, navigation, localized labels, and error subscriptions.
3. Show a loading or first-run state.
4. Load local/remote content asynchronously.
5. Switch to ready, empty, offline, or recoverable-error content without destroying navigation.

A missing local content directory on a clean install is an empty/first-run state. Never surface absolute paths, exception text, stack traces, credentials, or internal URLs to players.

Every recoverable state needs a clear action chosen from retry, manage/download content, use offline data, return home, or cancel. Do not download the complete catalog payload automatically.

## Interaction and motion

- Use a dedicated state class for pressed/disabled/loading feedback where practical.
- Keep Android control backgrounds stable on paths where native `Button`, `:active`, or background/border transitions have disappeared after input.
- Trigger click/status audio and optional haptics only through existing preference-aware services.
- Cancel or detach callbacks, scheduled items, and image leases when a screen is disposed or rebound.
- When reduced motion is enabled, avoid large transforms and repeated ambient motion.

## Localization and accessibility

- Application locale (`en`, `zh`, `ja`) must not change card printing locale (`en`, `ja`, `zh-cn`).
- Do not hide language-independent actions because a localized label is longer.
- Use the existing TextCore fallback configuration; do not substitute TMP font assets into UI Toolkit.
- Preserve semantic names/tooltips and visible focus or selected states where the current input path supports them.

## Verification evidence

For each UI slice, retain current-source evidence for:

- Unity import and compile success
- focused tests for state/model behavior
- PlayMode coverage for actual UXML/controller composition
- viewport/inset layout cases
- localized wrapping and zero-content/failure paths
- no missing scripts/assets or unexpected `.meta` changes

Safe Area or Android rendering claims require an Android candidate plus a physical-device check. Emulator or Editor screenshots alone are not final evidence.
