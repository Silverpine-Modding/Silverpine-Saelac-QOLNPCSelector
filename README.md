# QOLNPCSelector

A BepInEx plugin for Silverpine 1.7.3 that adds an enable checkbox to every
NPC row on **Show Loaded Custom Content Definitions**.

The **Enable / Disable One SILC**, **Enable / Disable Connected SILCs**, and
**NPC Selection Tools** buttons appear above all character definitions, in that
order, each time the page is opened. Returning from a character or tool screen
also resets the list to the top after the menu finishes rebuilding.

Each NPC uses Silverpine's complete native toggle row so it participates in
the base list's normal layout and render order. Selecting the NPC name still
opens the full custom NPC editor. Selecting the checkbox instead changes the
loaded definition's `enabled` value and immediately writes the `.silc`
definition.
The definition does not need to be unloaded or reloaded because the checkbox
changes the same object held in Silverpine's loaded-definition dictionary.

Silverpine reconciles the spawned custom NPC roster when character creation
finishes or a save finishes loading. If a world is already loaded, reload that
save for a disabled NPC to be removed or a newly enabled NPC to be spawned.

## NPC Selection Tools

The loaded-definition page also contains **NPC Selection Tools**, a secondary
screen with:

- **Export Enabled NPC List**, which prompts for any valid filename and writes
  the currently enabled NPC names as formatted JSON;
- **Import Enabled NPC List**, which displays all named JSON presets and
  restores the selected preset's exact enabled set;
- **Randomize Enabled NPCs**, which prompts for a count, disables the rest,
  and enables up to that many randomly selected NPC definitions.

Named JSON presets are stored at:

```text
BepInEx/config/QOLNPCSelector/Lists/<user-selected-name>.json
```

An imported name whose NPC definition is not loaded is ignored. Import keeps
processing every available name, reports the ignored count in the editor, and
writes an `[Import Warning]` with the missing names to the BepInEx console.
Import and randomization use the same validation-free, Enabled-only persistence
path as the individual checkboxes. No states change until the random-count
screen's **Apply Random Selection** button is pressed.

## SILC file enable/disable

The loaded custom-content page has two additional file-management buttons:

- **Enable / Disable One SILC** force-moves only the selected file.
- **Enable / Disable Connected SILCs** follows NPC ↔ Player Character
  dependencies in both directions and moves the entire connected group.

Both SILC pickers include a live search field. Search terms match definition
names, filenames, enabled/disabled state, definition type, and linked Player
Character names. Multiple terms can be combined, and the active search is
retained after refreshing or toggling a file.

Disabling moves files from Silverpine's `Definitions` folder into:

```text
Definitions/Disabled
```

Enabling moves them back into the top-level `Definitions` folder. Silverpine
only scans that top-level folder, so disabled files are excluded on the next
startup. The plugin also unloads or loads successfully moved definitions in
the current main-menu session. Reload an already-open save before relying on
NPC spawn/despawn changes.

Connected mode recognizes `CustomContentDefinition_NPC` references through
`customPlayerCharacterDefinitionName`. Disabling a Player Character SILC also
disables every NPC using it. Starting from an NPC also includes its Player
Character and sibling NPCs using that same character definition. Enabling uses
the same dependency group in reverse. File moves and runtime registration are
transactional; completed work is rolled back if a later step fails. The
single-file mode intentionally does not move dependencies and warns when it
leaves a broken link.

If a SILC being enabled is already registered in memory (for example, after
moving its file outside the game), the entire enable operation stops before
moving files. Existing runtime definitions are preserved. Failed-load cleanup
and transaction rollback also check registration ownership before unloading.
Restart Silverpine to reconcile externally moved files before retrying.

Before single-file mode disables a Player Character that still has enabled NPC
dependents, it displays the broken links and requires an explicit **Disable
Anyway** choice. **Cancel** returns to the filtered picker without changing any
files.

QOLCharSelector 1.0.1 or newer is recommended when both plugins are installed.
That release safely refreshes its character rows when this plugin unloads a
Player Character SILC at runtime.

This quick path intentionally skips the full editor Save workflow. It does not:

- clone or validate the definition;
- check names, descriptions, relationships, or routines;
- show an overwrite confirmation;
- unload and reload the definition.

If the `.silc` write fails, the checkbox and in-memory value are restored to
their previous state and the editor displays the error.

## Installation

Copy `QOLNPCSelector.dll` into:

```text
BepInEx/plugins/QOLNPCSelector
```

No ModdingTools runtime API is needed because this feature extends
Silverpine's native custom-content editor. The plugin follows ModdingTools'
Silverpine lifecycle contract: Harmony patches are installed during `Awake`
and deliberately remain installed when Silverpine destroys the bootstrap host
during the main-menu-to-game transition.
