---
name: ui-uitk
description: Implement, edit, review, and validate this Unity 6 project's runtime UI Toolkit presentation, including UXML, USS, UIElements C#, UIDocument, PanelSettings, responsive portrait layouts, Android Safe Area handling, localization, runtime binding, custom elements, and touch interactions. Use for any player-facing UI Toolkit or mobile layout change in this repository.
---

# TCG UI Toolkit

Build responsive portrait game UI without weakening this repository's release, testing, or workspace safeguards.

## Precedence

1. Follow the user's current request.
2. Follow `HANDOFF.md` and `.agents/skills/vibe-coding/SKILL.md` for scope, Git checkpoints, and verification.
3. Apply this skill for UI Toolkit implementation details.

This skill is adapted for a Unity-dependent project from Unity Technologies' `ui-uitk` skill at commit `af3648673f865e0a36e8a13dae1cf2e60d6afd3a`. See `LICENSE.md`.

## Workflow

1. Read the relevant UXML, USS, controller/presenter, PanelSettings, tests, and direct call sites before editing.
2. Read `references/mobile-runtime-contract.md` before changing layouts, interaction components, initialization states, or validation.
3. Preserve existing stable assets and shared styles. Make the smallest coherent slice; do not rewrite unrelated screens.
4. Put durable appearance in USS and structure in UXML. Use C# for behavior, runtime data, accessibility state, localization, and measured geometry such as Safe Area insets.
5. Initialize navigation, localized page structure, and recoverable states before asynchronous content loading.
6. Validate imports/compilation, focused tests, relevant PlayMode paths, layout contracts, and the actual diff. Build an Android candidate only when the changed boundary requires it.

## Structure and styling

- Use camelCase element names and kebab-case USS classes. Follow established project paths and naming when they differ.
- Reuse shared styles, icons, PanelSettings, and components before creating new ones.
- Keep one intentional root container per UXML document and link stylesheets explicitly.
- Do not use inline `style="..."` in UXML. Avoid `element.style.*` for durable appearance; permit it only for genuinely runtime-computed geometry or state that cannot be represented by class changes.
- Prefer `flex-grow`, `flex-shrink`, min/max constraints, wrapping, and scroll containers over fixed screen dimensions.
- Treat `1000 x 2000` only as a design reference. Never crop a Camera or constrain the root to force that aspect ratio.
- Let backgrounds extend edge-to-edge while keeping controls and essential text inside the shared Safe Area container.
- Explicitly enable wrapping for variable-length text. Test English, Chinese, and Japanese strings plus enlarged font settings.
- Keep primary touch targets at least approximately 48 dp and provide pressed, disabled, loading, and focus-visible feedback.

## USS constraints

Unity USS is not browser CSS. Do not use unsupported browser features such as:

- `gap`, `z-index`, `box-shadow`, `filter`, `outline`, or CSS gradient functions
- border shorthand, attribute selectors, or structural selectors such as `:nth-child`
- external URL assets or `UnityDefaultRuntimeTheme.tss`
- arbitrary values in `transition-property`; omit it or use only supported keywords

Use separate border properties, explicit classes, hierarchy order, nested visual elements, Painter2D/custom elements, and `project://database/Assets/...` references as appropriate. Put transitions on base selectors, not only pseudo-states.

## Android interaction rules

- For player-facing primary controls, prefer the project's proven `VisualElement + Label` interaction pattern over native `Button` when the Android renderer path is affected.
- Do not animate a root background/border through `:active` on the known-problematic path. Apply pressed feedback to a label or dedicated overlay and keep the root background stable.
- Route click sound and optional haptics through existing settings-aware services.
- Respect reduced-motion settings: replace large movement with a short fade or immediate state change.
- Verify repeated press/release, disabled/re-enabled, navigation away/back, and Android rendering whenever a shared button changes.

## Validation

Do not rely only on visual inspection or ask the user to validate changes that can be checked locally.

- Run Unity batch-mode compilation/import and focused EditMode or PlayMode tests.
- Cover narrow, standard, and tall portrait sizes plus representative Safe Area insets.
- Exercise zero-content, offline, loading, success, and recoverable-error states when the screen depends on content.
- Confirm localized long text wraps without hiding actions, and navigation remains usable during failures.
- Check Console/import logs, missing references, GUID/meta integrity, `git diff --check`, and scoped `git status`.
- For Safe Area, PanelSettings, Android renderer, or touch-path changes, verify with one Android candidate and current-device evidence before calling the mobile result complete.

Report exactly what was automated and what still requires a physical device.
