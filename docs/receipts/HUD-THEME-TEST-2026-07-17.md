# HUD-THEME-TEST receipt — 2026-07-17

**Lane:** HUD-THEME-TEST (branch `test/hud-theme-resolution`, from `origin/master`)
**Closes the hole named in the HUD-THEMES-GEN receipt:** "No dedicated theme-loading test
exists; theme texture resolution falls back to the Default set by engine design" — i.e. a
missing/misnamed theme file silently fell back to Default and nothing noticed.

## How HUD theme resolution ACTUALLY works (tribal knowledge, now written down)

Two prototype families, paired **by ID**, plus one engine fallback rule:

1. **`hudTheme`** (`HudThemePrototype`, `Resources/Prototypes/hud.yml`) is *dropdown-only*
   data: `Content.Client/Options/UI/Tabs/MiscTab.xaml.cs` lists them (sorted by `order`) and
   the selected entry's **ID** is written into the engine CVar `interface.theme`
   (`CVars.InterfaceTheme`, client-only, archived). Its `path` field is not used by the
   resolver at all — it only documents the directory (the test asserts it agrees with the
   paired uiTheme anyway).

2. **`uiTheme`** (engine `UITheme` prototype, `Resources/Prototypes/themes.yml`) is the
   actual theme. The engine's `UserInterfaceManager.Themes` watches `interface.theme` and
   calls `SetThemeOrPrevious(value)` — looking the value up **among uiTheme IDs**. If no
   uiTheme has that exact ID, **the selection is a silent no-op** (previous theme kept).
   Hence hudTheme/uiTheme IDs must pair 1:1. The engine itself ships one extra uiTheme,
   `Default` (`RobustToolbox/Resources/EnginePrototypes/UserInterface/uiThemes.yml`), which
   is the boot theme until `Content.Client/Entry/EntryPoint.cs` promotes `SS14DefaultTheme`
   via `SetDefaultTheme`.

3. **Texture lookup** (`UITheme.TryResolveTexture`, RobustToolbox
   `Robust.Client/UserInterface/Themes/UiTheme.cs`): given a *relative* name (`.png`
   appended if absent), try `{uiTheme.path}/{name}.png`, then fall back to the **hardcoded**
   `UITheme.DefaultThemePath` = `/Textures/Interface/Default/{name}.png`, then treat as an
   absolute path. So theme directories are **overlays over Default**:
   - Themes legitimately ship fewer files (Minimalist 28 pngs vs Ashen 43) — per-file
     fallback to Default **is the designed behavior**, not a bug.
   - But a *typo'd or deleted* override falls back by the exact same mechanism, invisibly.
   - `Default` is the resolver of last resort; a code-requested name missing there renders
     the engine's error texture.
   - The VFS **unions** engine and content resource roots: `/Textures/Interface/Default`
     at runtime = content's 56 pngs **+ the engine's `cross.png`**
     (`RobustToolbox/Resources/Textures/Interface/Default/cross.png`, requested as
     `"cross"` by the engine's `DefaultStylesheet` for window close buttons).

4. **Who requests relative (theme-overridable) names:** `SlotControl` (+`SlotButton`,
   `HandButton`) — `slot_highlight`, `blocked`, `Slots/back`, `Slots/toggle`,
   `Slots/hand_{l,m,r}`; inventory template prototypes (data-driven) —
   `Slots/{slotTexture}` and `fullTextureName` per slot; `ActionButton` — `SlotBackground`;
   `ItemStatusPanel` — `item_status_*`; `StorageWindow`/`ItemGridPiece` — `Storage/*`;
   relative `TexturePath=` XAML attributes — `{filled_,}left/right_arrow.svg.192dpi`
   (`ActionPageButtons.xaml`, `LobbyGui.xaml`). Absolute-path uses (e.g. Strippable's
   `/Textures/Interface/Default/Slots/camo.png`) bypass theming entirely.

## What the test pins

`Content.IntegrationTests/Tests/_Solreign/HudThemeResolutionTest.cs` (5 tests, real
client/server pair, real `IPrototypeManager` + `IResourceManager` + `UITheme` resolver):

1. **DropdownWiring** — hudTheme IDs == uiTheme IDs ∪ {engine `Default`} exactly; each
   hudTheme's `path` agrees with its uiTheme's resolve directory; every directory exists
   and is non-empty. Kills the "dropdown entry that silently does nothing" class.
2. **SelectingEveryTheme_ViaTheCVar** — end-to-end: sets `interface.theme` to each of the
   7 hudTheme IDs on the live client and asserts `IUserInterfaceManager.CurrentTheme`
   actually switched (and that boot lands on `SS14DefaultTheme`).
3. **EveryThemeFile_IsReachableByResolutionAndShadowsDefault** — every non-Default theme
   png must have a same-relative-path Default counterpart (resolution tries the *same*
   relative path in both, so a png without a Default twin is a typo'd override silently
   falling back, or orphan dead weight in the ACZ). Also whitelists non-png content
   (`meta.json`, `*.svg` sources, `*.png.yml` sidecars whose base png must exist).
4. **EveryCodeRequestedTextureName_Resolves...** — the harvested request contract (static
   call-site list, kept in the test with call-site comments + inventory-template names
   harvested live from prototypes) must (a) all exist in Default (universal fallback
   guarantee — no error texture possible) and (b) resolve via the real
   `UITheme.TryResolveTexture` for **every** theme.
5. **ThemeDirectories_ExactlyMatchTheirRecordedManifests** — *the snapshot*: each theme
   dir's png set must equal its `meta.json` `states` list **exactly** (the manifest doubles
   as the CC0 license receipt from the HUD regen). Any add/delete/rename fails loudly and
   must be acknowledged by editing the manifest in the same commit. Engine-shipped
   `cross` is pinned separately (`EngineShippedDefaultTextures`) and must NOT be claimed
   by the content license record.

**Falsification check:** renaming `Ashen/Slots/head.png` → `heda.png` trips test 3
("unreachable ... typo'd override or dead weight: Slots/heda.png") AND test 5 (manifest
records `Slots/head` missing + `Slots/heda` unrecorded). Restored after.

## Data fixes shipped alongside (found by writing the test)

- **Deleted 2 genuine orphans:** `Minimalist/Slots/ears_headset.png` and
  `Ashen/Slots/ears_headset.png` — no Default counterpart, no code or YAML requests
  `Slots/ears_headset` anywhere (upstream leftover); they were shipping dead in the ACZ.
  Their meta.json state entries removed to match.
- **Rewrote `Default/meta.json` `states`** to the actual 56-png folder contents (it was a
  stale wave-6 slot-glyph list: claimed `ears_headset`/`block`/`hand_active`/`invtoggle`/
  `inventory` which don't exist, missed all `Storage/*`, `Slots/` prefixes, arrows, etc.).
  License/copyright fields untouched. The 6 regenerated theme manifests already matched
  their folders exactly (HUD-THEMES-GEN wrote them correctly).
- `Default/Slots/{hand_l,hand_r}_no_letter.png` are requested by no current code path but
  are kept (Default is the master art set; pinned by the manifest either way).

## Verification (Release, blocking)

- Build: `dotnet build Content.IntegrationTests -c Release` — 0 errors.
- New fixture: **5/5 green** (`FullyQualifiedName~HudThemeResolutionTest`).
- Full `~Solreign` integration suite: green — see final lane message for counts
  (baseline 313/1skip + 5 new).
- YAML linter (`dotnet run --project Content.YAMLLinter -c Release`): "No errors found".
- Mutation/falsification run performed and reverted (see above).
