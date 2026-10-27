# QOLNPCSelector review — 2026-09-05

Reviewed the 1.3.0 source against Silverpine's editor, native list rendering,
SILC loading/writing, and NPC/Player Character registration code. The local
update is version 1.3.2.

## Fixed

- **Menu placement:** Both SILC tools and NPC Selection Tools used the bottom
  Back row as their insertion anchor. They now occupy positions 0, 1, and 2
  above the definitions every time the loaded-definition page is rebuilt.
- **Outgoing rows:** Silverpine's list renderer calls deferred `Destroy`, so
  rows from the previous screen still exist during the plugin's postfix. A
  Harmony prefix now captures those rows, and the postfix hides/excludes them
  before replacing the freshly drawn NPC rows with checkboxes.
- **Scroll offset:** A component owned by the first tool row resets the editor
  list to the top after deferred destruction/layout completes. Navigating away
  destroys that row and cancels its pending reset. It does not need the
  short-lived BepInEx bootstrap host to remain alive.
- **Status search:** Disabled rows previously included the action word
  `enable` in their search text. Searches now use the actual file state.

## Registration cleanup — corrected in 1.3.2

**Original finding:** Failed duplicate registration could remove an existing
loaded NPC. `LoadDefinitionFromActivePath` called `definition.Unload()` after
any `Load()` exception. Native NPC `Load()` uses
`loaded.Add(name, this)`, while `Unload()` removes by name without checking
object identity. If an NPC is already loaded but its file was moved externally
into Disabled during the session, enabling that file moves it back, fails with
a duplicate-key exception, then removes the existing runtime NPC during cleanup.
File rollback moves the file back to Disabled, but does not restore that prior
runtime registration. The same name-based cleanup risk exists for Player
Character definitions.

The correction checks the whole batch for existing runtime registrations
before moving files, then checks again using the actual definition type/name
immediately before calling native Load. Duplicate rejection happens outside
the cleanup block, so no native lifecycle method runs for that attempted load.
Cleanup and rollback refuse to unload when a different instance owns the
registration. An unregistered, partially loaded Player Character can still
release its own UI through native Unload. Cleanup errors retain both the load
and cleanup exceptions instead of being logged and omitted from the result.
Rollback reloads use the same guarded load path.

Regression tests exercise the production guard with native-style dictionary
registration and simulated Player Character UI allocation. All 16 scenarios
pass: duplicate objects, repeated loads of the same object, successful rollback,
failures before/after registration, foreign registrations during cleanup,
replacement registrations during rollback, and cleanup failures, for both NPCs
and Player Characters. These are isolated tests, not live Unity UI tests.

## Verification

- Release build against the installed game/Unity/BepInEx assemblies: zero
  warnings and zero errors.
- `git diff --check` passed.
- Reviewed the initial-open, character-discard/back, tools-back, and rapid
  same-frame redraw paths in source. All rebuild the same loaded-definition
  method, where the position/scroll fix is applied.
- No live UI navigation or user definition mutations were used for verification.
- Registration regression tests: 16 passed. Run with
  `dotnet run --project tests/RegistrationGuard.Tests -c Release`.

After launching the update, check that the three controls remain first after opening
and discarding an NPC editor, after returning from each tool, and after
repeating those actions while the list is scrolled down.

Backup: `Plugin Backups/QOLNPCSelector/2026-09-05-020213-before-editor-menu-fix`
under the game directory.

Additional pre-1.3.2 backup:
`Plugin Backups/QOLNPCSelector/2026-09-05-021520-before-registration-guard`.
