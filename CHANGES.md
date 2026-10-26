# CHANGES.md — branch `gamepad-fill-all`

## Upstream situation
- `upstream` currently points to https://github.com/war3i4i/AzuCraftyBoxes (HEAD v1.2.5),
  the only reachable mirror. The canonical repo https://github.com/AzumattDev/AzuCraftyBoxes
  now returns 404 (deleted or made private).
- Consequence: `git diff upstream/HEAD...HEAD` spans the ENTIRE Azumatt 1.3–1.8.19 line
  plus our work. Our own changes sit on top of Azumatt v1.8.19 (commit 76d6857); the
  per-feature sections below are derived from `76d6857..HEAD` (~+1200/−204, 30 files):
  `bde2453` (gamepad fill-all), `482ba0a` (merge Azumatt 1.8.21), `1e8f084` (WIP save).

## Gamepad fill-all (bde2453)
- Fill-all modifier bound to Valheim's alternate-function gamepad bind; pulling
  everything from nearby containers works with a controller chord (Plugin.cs,
  PlayerPatches.cs, MiscFunctions.cs, GamepadButton plumbing).
- New icon `images/pullingmineicon.png` for the pull-mode indicator.

## CraftyToggle (Patches/CraftyTogglePatches.cs, new, ~218 lines)
- Hotkey (config `chestToggleKey`) toggles "crafty access" per container while looking
  at it: vanilla Containers via `RPC_UpdateCrafty`, API containers via their ZNetView.
- Honors guardstone/private-area and container access checks; hover-text status line
  (`chestToggleHoverText`); `MineOnly` pull mode restricts toggling to containers whose
  ZDO `s_creator` is you (`PullModeAllows`).
- Support touched in ChestPatches, CookingStationPatches, FermenterPatches,
  FireplacePatches, ShieldGeneratorPatches, SmelterPatches, TurretPatches, SE_ContainerPull.

## Pull-build-items (Patches/PullBuildItemsPatches.cs, new, ~200 lines)
- Keyboard (`pullItemsKey`) or gamepad (AlternateFunction + `pullItemsGamepadKey`) while
  InPlaceMode with a selected piece: pulls that piece's build requirements from nearby
  containers into the inventory.
- Gamepad chord suppresses vanilla A/jump handling while armed (`SuppressJump`).
- New `IContainers/BowsBeforeHoesQuiver.cs` adapter (quiver as a source container;
  TBD: exact upstream mod it adapts).

## Version / packaging
- Thunderstore manifest 1.8.19 -> 1.8.27 across the WIP series (current 1.8.27);
  README + CHANGELOG updated accordingly.
- `Libs/ItemDataManager.dll` bumped by the Azumatt 1.8.21 merge.

## Other edits in the WIP merge/save (per-file deltas, reasons TBD for the user)
- Plugin.cs +159/−: config wiring for the new keys/options, pull-mode handling.
- Util/Functions/Boxes.cs +215/−, MiscFunctions, UiItemBank, PlayerExtensions,
  APIs/MkzItemDrawers_API + kg_ItemDrawersAPI: TBD (carry-over from Azumatt 1.8.20/1.8.21
  merge vs our edits — not yet separated commit-by-commit).
- `.gitignore` extended: release zips (`Thunderstore/*.zip`, `Nexus/`), `.build-out/`,
  `.decompile-tools/`, `*.dll` untracked from here on.
