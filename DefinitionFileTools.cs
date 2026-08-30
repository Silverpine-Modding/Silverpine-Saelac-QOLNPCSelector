#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SilverpineMods.QOLNPCSelector;

public sealed partial class Plugin
{
    private enum DefinitionFileKind
    {
        Npc,
        PlayerCharacter
    }

    private sealed class DefinitionFileEntry
    {
        internal string Name = "";
        internal string FileName = "";
        internal string Path = "";
        internal bool IsEnabled;
        internal DefinitionFileKind Kind;
        internal string PlayerCharacterName = "";
        internal string DependencyReadError = "";

        internal string KindLabel => Kind == DefinitionFileKind.Npc
            ? "NPC"
            : "Player Character";

        internal string Identity => Kind + ":" + Name;
    }

    private sealed class FileMoveRecord
    {
        internal DefinitionFileEntry Entry = null!;
        internal string Source = "";
        internal string Destination = "";
    }

    private sealed class RuntimeDefinitionRecord
    {
        internal DefinitionFileEntry Entry = null!;
        internal CustomContentDefinition Definition = null!;
    }

    private sealed class DefinitionFileSearchRow
    {
        internal int RenderedIndex;
        internal string SearchText = "";
    }

    private static string DefinitionsDirectory =>
        System.IO.Path.Combine(Application.persistentDataPath, "Definitions");

    private static string DisabledDefinitionsDirectory =>
        System.IO.Path.Combine(DefinitionsDirectory, "Disabled");

    private static void AddDefinitionFileButtons(
        GeneralCustomContentEditorUI editor,
        GameObject root,
        IReadOnlyList<GameObject> originalRows)
    {
        try
        {
            GameObject? backRow = FindBackRow(originalRows);
            var singular = new ListUIItem_Generic(
                "Enable / Disable One SILC",
                null,
                "Selected file only",
                () => ShowDefinitionFilePicker(
                    editor,
                    includeConnected: false))
            {
                tooltip =
                    "Move exactly one selected NPC or Player Character SILC "
                    + "between Definitions and Definitions/Disabled."
            };
            var connected = new ListUIItem_Generic(
                "Enable / Disable Connected SILCs",
                null,
                "Dependency group",
                () => ShowDefinitionFilePicker(
                    editor,
                    includeConnected: true))
            {
                tooltip =
                    "Move the selected SILC and every connected NPC / "
                    + "Player Character dependency as one transaction."
            };

            InsertBeforeBack(root, singular, backRow);
            InsertBeforeBack(root, connected, backRow);
        }
        catch (Exception exception)
        {
            Log.LogError(
                "Could not add the definition-file enable/disable buttons.\n"
                + exception);
        }
    }

    private static void ShowDefinitionFilePicker(
        GeneralCustomContentEditorUI editor,
        bool includeConnected,
        string initialSearch = "")
    {
        GameObject? root = EditorRootField?.GetValue(editor) as GameObject;
        if (root == null || GenericListUI.Instance == null)
        {
            Log.LogError(
                "Could not open the definition-file picker because the "
                + "custom content editor is unavailable.");
            return;
        }

        List<DefinitionFileEntry> entries;
        try
        {
            entries = ScanDefinitionFiles();
        }
        catch (Exception exception)
        {
            Log.LogError(
                "Could not scan enabled and disabled SILC files.\n"
                + exception);
            ShowToolMessage(
                editor,
                "Could not scan definition files:\n\n" + exception.Message,
                () => ShowLoadedDefinitions(editor));
            return;
        }

        string modeDescription = includeConnected
            ? "Selecting a row toggles the entire connected dependency "
                + "group. NPC-to-Player-Character links are followed in "
                + "both directions, including sibling NPCs that use the "
                + "same Player Character SILC."
            : "Selecting a row force-toggles only that one SILC. This mode "
                + "does not move dependencies; use the connected mode to "
                + "keep linked NPC and Player Character files together.";
        string searchText = initialSearch ?? "";
        var renderedRows = new List<GameObject>();
        var searchableRows = new List<DefinitionFileSearchRow>();
        var items = new List<ListUIItem>
        {
            new ListUIItem_Label(
                modeDescription + "\n\nEnabled: " + DefinitionsDirectory
                + "\nDisabled: " + DisabledDefinitionsDirectory),
            new ListUIItem_SmallInputField(
                "Search SILCs",
                InputFieldConstraint.ConstraintType.None,
                100,
                searchText,
                (_, value) =>
                {
                    searchText = value ?? "";
                    ApplyDefinitionFileSearch(
                        renderedRows,
                        searchableRows,
                        searchText);
                })
        };

        if (entries.Count == 0)
        {
            items.Add(new ListUIItem_Label(
                "No NPC or Player Character SILC files were found."));
        }
        else
        {
            foreach (DefinitionFileEntry entry in entries)
            {
                DefinitionFileEntry captured = entry;
                string action = entry.IsEnabled ? "Disable " : "Enable ";
                string dependency = entry.Kind == DefinitionFileKind.Npc
                    && entry.PlayerCharacterName.Length > 0
                    ? "\nUses Player Character: "
                        + entry.PlayerCharacterName
                    : "";
                string readError = entry.DependencyReadError.Length == 0
                    ? ""
                    : "\nDependency inspection failed: "
                        + entry.DependencyReadError;
                int renderedIndex = items.Count;
                items.Add(new ListUIItem_Generic(
                    action + entry.Name,
                    null,
                    (entry.IsEnabled ? "Enabled · " : "Disabled · ")
                        + entry.KindLabel,
                    () => ToggleDefinitionFiles(
                        editor,
                        captured,
                        includeConnected,
                        searchText))
                {
                    tooltip = entry.Path + dependency + readError
                });
                searchableRows.Add(new DefinitionFileSearchRow
                {
                    RenderedIndex = renderedIndex,
                    SearchText = entry.Name + "\n"
                        + entry.FileName + "\n"
                        + entry.KindLabel + "\n"
                        + (entry.IsEnabled ? "enabled disable" : "disabled enable")
                        + "\n" + entry.PlayerCharacterName
                });
            }
        }

        items.Add(new ListUIItem_Line());
        items.Add(new ListUIItem_Generic(
            "Refresh",
            null,
            "",
            () => ShowDefinitionFilePicker(
                editor,
                includeConnected,
                searchText)));
        items.Add(new ListUIItem_Generic(
            "Back",
            null,
            "",
            () => ShowLoadedDefinitions(editor)));
        renderedRows = GenericListUI.Instance.DrawIntoRoot(items, root);
        ApplyDefinitionFileSearch(
            renderedRows,
            searchableRows,
            searchText);
    }

    private static void ApplyDefinitionFileSearch(
        List<GameObject> renderedRows,
        List<DefinitionFileSearchRow> searchableRows,
        string searchText)
    {
        string[] terms = (searchText ?? "").Split(
            new[] { ' ' },
            StringSplitOptions.RemoveEmptyEntries);
        foreach (DefinitionFileSearchRow searchable in searchableRows)
        {
            if (searchable.RenderedIndex < 0
                || searchable.RenderedIndex >= renderedRows.Count)
            {
                continue;
            }

            bool visible = true;
            foreach (string term in terms)
            {
                if (searchable.SearchText.IndexOf(
                        term,
                        StringComparison.OrdinalIgnoreCase) < 0)
                {
                    visible = false;
                    break;
                }
            }

            renderedRows[searchable.RenderedIndex].SetActive(visible);
        }
    }

    private static void ToggleDefinitionFiles(
        GeneralCustomContentEditorUI editor,
        DefinitionFileEntry selected,
        bool includeConnected,
        string searchText,
        bool dependencyWarningConfirmed = false)
    {
        try
        {
            List<DefinitionFileEntry> entries = ScanDefinitionFiles();
            DefinitionFileEntry? current = FindMatchingEntry(entries, selected);
            if (current == null)
            {
                throw new FileNotFoundException(
                    "The selected SILC moved or disappeared. Refresh the "
                    + "list and try again.",
                    selected.Path);
            }

            bool targetEnabled = !current.IsEnabled;
            List<DefinitionFileEntry> group = includeConnected
                ? GetConnectedEntries(current, entries)
                : new List<DefinitionFileEntry> { current };
            var toMove = new List<DefinitionFileEntry>();
            foreach (DefinitionFileEntry entry in group)
            {
                if (entry.IsEnabled != targetEnabled)
                    toMove.Add(entry);
            }

            if (toMove.Count == 0)
            {
                ShowToolMessage(
                    editor,
                    "Every selected definition is already "
                    + (targetEnabled ? "enabled." : "disabled."),
                    () => ShowDefinitionFilePicker(
                        editor,
                        includeConnected,
                        searchText));
                return;
            }

            List<string> warnings = GetDependencyWarnings(
                current,
                entries,
                group,
                targetEnabled,
                includeConnected);
            if (includeConnected
                && targetEnabled
                && HasMissingRequiredPlayerCharacter(group, entries,
                    out string missingDependency))
            {
                throw new InvalidOperationException(
                    "Cannot enable the connected group because required "
                    + "Player Character SILC \"" + missingDependency
                    + "\" was not found in either definition folder.");
            }

            if (!includeConnected
                && !targetEnabled
                && warnings.Count > 0
                && !dependencyWarningConfirmed)
            {
                ShowSingleDisableDependencyWarning(
                    editor,
                    current,
                    warnings,
                    searchText);
                return;
            }

            ExecuteDefinitionFileTransition(toMove, targetEnabled);

            string scope = includeConnected ? "connected " : "";
            string warningText = warnings.Count == 0
                ? ""
                : "\n\nWarning:\n" + string.Join("\n", warnings);
            string destination = targetEnabled
                ? DefinitionsDirectory
                : DisabledDefinitionsDirectory;
            Log.LogInfo(
                $"{(targetEnabled ? "Enabled" : "Disabled")} "
                + $"{toMove.Count} {scope}SILC file(s). Destination: "
                + destination);
            foreach (string warning in warnings)
                Log.LogWarning("[Dependency Warning] " + warning);

            ShowToolMessage(
                editor,
                $"{(targetEnabled ? "Enabled" : "Disabled")} "
                + $"{toMove.Count} {scope}SILC file"
                + (toMove.Count == 1 ? "." : "s.")
                + "\n\n" + destination
                + warningText
                + "\n\nReload an already-open save before relying on "
                + "world NPC spawn/despawn changes.",
                () => ShowDefinitionFilePicker(
                    editor,
                    includeConnected,
                    searchText));
        }
        catch (Exception exception)
        {
            Log.LogError(
                "Definition-file enable/disable operation failed.\n"
                + exception);
            ShowToolMessage(
                editor,
                "Definition-file operation failed. Any completed moves "
                + "were rolled back when possible:\n\n"
                + exception.Message,
                () => ShowDefinitionFilePicker(
                    editor,
                    includeConnected,
                    searchText));
        }
    }

    private static void ShowSingleDisableDependencyWarning(
        GeneralCustomContentEditorUI editor,
        DefinitionFileEntry selected,
        List<string> warnings,
        string searchText)
    {
        GameObject? root = EditorRootField?.GetValue(editor) as GameObject;
        if (root == null || GenericListUI.Instance == null)
        {
            Log.LogError(
                "Could not show the single-SILC dependency warning because "
                + "the custom content editor is unavailable.");
            return;
        }

        string warningText = "Disabling only \"" + selected.Name
            + "\" will leave broken dependencies:\n\n• "
            + string.Join("\n• ", warnings)
            + "\n\nUse Connected SILCs instead to disable linked files "
            + "together, or continue only if this broken state is intended.";
        var items = new List<ListUIItem>
        {
            new ListUIItem_Label(warningText),
            new ListUIItem_Generic(
                "Disable Anyway",
                null,
                selected.FileName,
                () => ToggleDefinitionFiles(
                    editor,
                    selected,
                    includeConnected: false,
                    searchText,
                    dependencyWarningConfirmed: true)),
            new ListUIItem_Generic(
                "Cancel",
                null,
                "Keep this SILC enabled",
                () => ShowDefinitionFilePicker(
                    editor,
                    includeConnected: false,
                    searchText))
        };

        GenericListUI.Instance.DrawIntoRoot(items, root);
    }

    private static void ExecuteDefinitionFileTransition(
        List<DefinitionFileEntry> entries,
        bool targetEnabled)
    {
        if (targetEnabled && MainMenuUI.Instance == null)
        {
            throw new InvalidOperationException(
                "The main menu is unavailable, so definitions cannot be "
                + "loaded safely right now.");
        }

        string destinationDirectory = targetEnabled
            ? DefinitionsDirectory
            : DisabledDefinitionsDirectory;
        Directory.CreateDirectory(destinationDirectory);

        var moves = new List<FileMoveRecord>();
        foreach (DefinitionFileEntry entry in entries)
        {
            string destination = System.IO.Path.Combine(
                destinationDirectory,
                entry.FileName);
            if (!File.Exists(entry.Path))
            {
                throw new FileNotFoundException(
                    "Source SILC no longer exists.",
                    entry.Path);
            }
            if (File.Exists(destination))
            {
                throw new IOException(
                    "A SILC with the same filename already exists at the "
                    + "destination: " + destination);
            }

            moves.Add(new FileMoveRecord
            {
                Entry = entry,
                Source = entry.Path,
                Destination = destination
            });
        }

        SortForRuntimeOrder(moves, enabling: targetEnabled);
        if (targetEnabled)
            EnableDefinitionFiles(moves);
        else
            DisableDefinitionFiles(moves);
    }

    private static void DisableDefinitionFiles(
        List<FileMoveRecord> moves)
    {
        var unloaded = new List<RuntimeDefinitionRecord>();
        var completedMoves = new List<FileMoveRecord>();
        try
        {
            // Moves are sorted NPC-first for disabling so dependents leave
            // the runtime dictionaries before their Player Character.
            foreach (FileMoveRecord move in moves)
            {
                CustomContentDefinition? definition =
                    FindLoadedDefinition(move.Entry);
                if (definition == null)
                    continue;

                var runtimeRecord = new RuntimeDefinitionRecord
                {
                    Entry = move.Entry,
                    Definition = definition
                };
                try
                {
                    definition.Unload();
                    unloaded.Add(runtimeRecord);
                }
                catch
                {
                    // Silverpine's Player Character Unload removes the
                    // runtime object before it notifies selection listeners.
                    // A listener can therefore throw after the unload has
                    // already happened. Track that partial success so the
                    // transaction can reload it during rollback.
                    if (!ReferenceEquals(
                            FindLoadedDefinition(move.Entry),
                            definition))
                    {
                        unloaded.Add(runtimeRecord);
                    }

                    throw;
                }
            }

            foreach (FileMoveRecord move in moves)
            {
                File.Move(move.Source, move.Destination);
                completedMoves.Add(move);
            }
        }
        catch (Exception exception)
        {
            var rollbackErrors = new List<string>();
            RollBackMoves(completedMoves, rollbackErrors);
            ReloadDefinitions(unloaded, rollbackErrors);
            throw BuildTransitionException(
                "Disabling SILC files failed",
                exception,
                rollbackErrors);
        }
    }

    private static void EnableDefinitionFiles(
        List<FileMoveRecord> moves)
    {
        var completedMoves = new List<FileMoveRecord>();
        var loaded = new List<RuntimeDefinitionRecord>();
        try
        {
            // Moves are sorted Player-Character-first for enabling so every
            // NPC dependency is registered before its NPC definition.
            foreach (FileMoveRecord move in moves)
            {
                File.Move(move.Source, move.Destination);
                completedMoves.Add(move);
            }

            foreach (FileMoveRecord move in moves)
            {
                CustomContentDefinition definition =
                    LoadDefinitionFromActivePath(move);
                loaded.Add(new RuntimeDefinitionRecord
                {
                    Entry = move.Entry,
                    Definition = definition
                });
            }
        }
        catch (Exception exception)
        {
            var rollbackErrors = new List<string>();
            UnloadDefinitions(loaded, rollbackErrors);
            RollBackMoves(completedMoves, rollbackErrors);
            throw BuildTransitionException(
                "Enabling SILC files failed",
                exception,
                rollbackErrors);
        }
    }

    private static CustomContentDefinition LoadDefinitionFromActivePath(
        FileMoveRecord move)
    {
        CustomContentDefinition definition =
            SILCFile.LoadFromPath(move.Destination).GetDefinition();
        if (definition is CustomContentDefinition_PlayerCharacter player)
        {
            SwitchSelectionUI selection =
                MainMenuUI.Instance.characterSelectionUI;
            player.characterFieldGameObject =
                selection.transform.GetChild(0).gameObject;
            player.characterSelectionUI = selection;
        }

        try
        {
            definition.Load();
            return definition;
        }
        catch
        {
            try
            {
                definition.Unload();
            }
            catch (Exception cleanupException)
            {
                Log.LogError(
                    "Could not clean up a partially loaded definition \""
                    + move.Entry.Name + "\".\n" + cleanupException);
            }

            throw;
        }
    }

    private static void RollBackMoves(
        List<FileMoveRecord> completedMoves,
        List<string> errors)
    {
        for (int index = completedMoves.Count - 1; index >= 0; index--)
        {
            FileMoveRecord move = completedMoves[index];
            try
            {
                if (File.Exists(move.Destination)
                    && !File.Exists(move.Source))
                {
                    File.Move(move.Destination, move.Source);
                }
            }
            catch (Exception exception)
            {
                errors.Add(
                    "Could not restore " + move.Entry.FileName + ": "
                    + exception.Message);
            }
        }
    }

    private static void ReloadDefinitions(
        List<RuntimeDefinitionRecord> definitions,
        List<string> errors)
    {
        definitions.Sort((left, right) =>
            RuntimeOrder(left.Entry, enabling: true)
                .CompareTo(RuntimeOrder(right.Entry, enabling: true)));
        foreach (RuntimeDefinitionRecord record in definitions)
        {
            try
            {
                record.Definition.Load();
            }
            catch (Exception exception)
            {
                errors.Add(
                    "Could not reload " + record.Entry.FileName + ": "
                    + exception.Message);
            }
        }
    }

    private static void UnloadDefinitions(
        List<RuntimeDefinitionRecord> definitions,
        List<string> errors)
    {
        for (int index = definitions.Count - 1; index >= 0; index--)
        {
            RuntimeDefinitionRecord record = definitions[index];
            try
            {
                record.Definition.Unload();
            }
            catch (Exception exception)
            {
                errors.Add(
                    "Could not unload " + record.Entry.FileName + ": "
                    + exception.Message);
            }
        }
    }

    private static Exception BuildTransitionException(
        string message,
        Exception cause,
        List<string> rollbackErrors)
    {
        string causeDetails = string.IsNullOrWhiteSpace(cause.Message)
            ? cause.GetType().Name
            : cause.GetType().Name + ": " + cause.Message;
        string rollback = rollbackErrors.Count == 0
            ? "Rollback completed."
            : "Rollback problems: " + string.Join("; ", rollbackErrors);
        return new InvalidOperationException(
            message + " (" + causeDetails + "). " + rollback,
            cause);
    }

    private static List<DefinitionFileEntry> ScanDefinitionFiles()
    {
        var entries = new List<DefinitionFileEntry>();
        AddDefinitionFilesFromDirectory(
            entries,
            DefinitionsDirectory,
            isEnabled: true);
        AddDefinitionFilesFromDirectory(
            entries,
            DisabledDefinitionsDirectory,
            isEnabled: false);

        foreach (DefinitionFileEntry entry in entries)
        {
            if (entry.Kind != DefinitionFileKind.Npc)
                continue;

            try
            {
                CustomContentDefinition_NPC? loaded = FindLoadedNpc(entry.Name);
                CustomContentDefinition_NPC? definition = loaded
                    ?? SILCFile.LoadFromPath(entry.Path).GetDefinition()
                        as CustomContentDefinition_NPC;
                if (definition == null)
                {
                    throw new InvalidDataException(
                        "The file does not contain an NPC definition.");
                }

                entry.PlayerCharacterName =
                    definition.customPlayerCharacterDefinitionName?.Trim()
                    ?? "";
            }
            catch (Exception exception)
            {
                entry.DependencyReadError = exception.Message;
                Log.LogWarning(
                    "Could not inspect NPC dependency for "
                    + entry.Path + ": " + exception.Message);
            }
        }

        entries.Sort(CompareDefinitionEntries);
        return entries;
    }

    private static void AddDefinitionFilesFromDirectory(
        List<DefinitionFileEntry> entries,
        string directory,
        bool isEnabled)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (string path in Directory.GetFiles(
                     directory,
                     "*.silc",
                     SearchOption.TopDirectoryOnly))
        {
            string fileName = System.IO.Path.GetFileName(path);
            if (!TryParseDefinitionFilename(
                    fileName,
                    out DefinitionFileKind kind,
                    out string name))
            {
                continue;
            }

            entries.Add(new DefinitionFileEntry
            {
                Name = name,
                FileName = fileName,
                Path = path,
                IsEnabled = isEnabled,
                Kind = kind
            });
        }
    }

    private static bool TryParseDefinitionFilename(
        string fileName,
        out DefinitionFileKind kind,
        out string name)
    {
        const string npcPrefix = "NPC_";
        const string playerPrefix = "PlayerCharacter_";
        string baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);
        if (baseName.StartsWith(npcPrefix, StringComparison.OrdinalIgnoreCase))
        {
            kind = DefinitionFileKind.Npc;
            name = baseName.Substring(npcPrefix.Length);
            return name.Length > 0;
        }
        if (baseName.StartsWith(
                playerPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            kind = DefinitionFileKind.PlayerCharacter;
            name = baseName.Substring(playerPrefix.Length);
            return name.Length > 0;
        }

        kind = default;
        name = "";
        return false;
    }

    private static List<DefinitionFileEntry> GetConnectedEntries(
        DefinitionFileEntry selected,
        List<DefinitionFileEntry> entries)
    {
        var connected = new List<DefinitionFileEntry>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<DefinitionFileEntry>();
        queue.Enqueue(selected);

        while (queue.Count > 0)
        {
            DefinitionFileEntry current = queue.Dequeue();
            string visitKey = current.Identity + ":" + current.IsEnabled;
            if (!visited.Add(visitKey))
                continue;

            connected.Add(current);
            if (current.Kind == DefinitionFileKind.Npc)
            {
                if (current.PlayerCharacterName.Length == 0)
                    continue;

                foreach (DefinitionFileEntry candidate in entries)
                {
                    if (candidate.Kind ==
                            DefinitionFileKind.PlayerCharacter
                        && string.Equals(
                            candidate.Name,
                            current.PlayerCharacterName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        queue.Enqueue(candidate);
                    }
                }
            }
            else
            {
                foreach (DefinitionFileEntry candidate in entries)
                {
                    if (candidate.Kind == DefinitionFileKind.Npc
                        && string.Equals(
                            candidate.PlayerCharacterName,
                            current.Name,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        queue.Enqueue(candidate);
                    }
                }
            }
        }

        return connected;
    }

    private static List<string> GetDependencyWarnings(
        DefinitionFileEntry selected,
        List<DefinitionFileEntry> allEntries,
        List<DefinitionFileEntry> group,
        bool targetEnabled,
        bool includeConnected)
    {
        var warnings = new List<string>();
        if (includeConnected)
        {
            foreach (DefinitionFileEntry entry in group)
            {
                if (entry.DependencyReadError.Length > 0)
                {
                    warnings.Add(
                        "Could not inspect every dependency for "
                        + entry.FileName + ".");
                }
            }

            return warnings;
        }

        if (!targetEnabled
            && selected.Kind == DefinitionFileKind.PlayerCharacter)
        {
            var dependents = new List<string>();
            foreach (DefinitionFileEntry entry in allEntries)
            {
                if (entry.IsEnabled
                    && entry.Kind == DefinitionFileKind.Npc
                    && string.Equals(
                        entry.PlayerCharacterName,
                        selected.Name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    dependents.Add(entry.Name);
                }
            }

            if (dependents.Count > 0)
            {
                warnings.Add(
                    "Enabled NPC SILCs still depend on this Player "
                    + "Character: " + string.Join(", ", dependents));
            }
        }
        else if (targetEnabled
                 && selected.Kind == DefinitionFileKind.Npc
                 && selected.PlayerCharacterName.Length > 0)
        {
            DefinitionFileEntry? player = FindPlayerCharacterEntry(
                allEntries,
                selected.PlayerCharacterName);
            if (player == null || !player.IsEnabled)
            {
                warnings.Add(
                    "This NPC depends on disabled or missing Player "
                    + "Character SILC \""
                    + selected.PlayerCharacterName + "\".");
            }
        }

        return warnings;
    }

    private static bool HasMissingRequiredPlayerCharacter(
        List<DefinitionFileEntry> group,
        List<DefinitionFileEntry> allEntries,
        out string missingName)
    {
        foreach (DefinitionFileEntry entry in group)
        {
            if (entry.Kind != DefinitionFileKind.Npc
                || entry.PlayerCharacterName.Length == 0)
            {
                continue;
            }

            if (FindPlayerCharacterEntry(
                    allEntries,
                    entry.PlayerCharacterName) == null)
            {
                missingName = entry.PlayerCharacterName;
                return true;
            }
        }

        missingName = "";
        return false;
    }

    private static DefinitionFileEntry? FindPlayerCharacterEntry(
        List<DefinitionFileEntry> entries,
        string name)
    {
        foreach (DefinitionFileEntry entry in entries)
        {
            if (entry.Kind == DefinitionFileKind.PlayerCharacter
                && string.Equals(
                    entry.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private static DefinitionFileEntry? FindMatchingEntry(
        List<DefinitionFileEntry> entries,
        DefinitionFileEntry selected)
    {
        foreach (DefinitionFileEntry entry in entries)
        {
            if (entry.IsEnabled == selected.IsEnabled
                && entry.Kind == selected.Kind
                && string.Equals(
                    entry.Name,
                    selected.Name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private static CustomContentDefinition? FindLoadedDefinition(
        DefinitionFileEntry entry)
    {
        return entry.Kind == DefinitionFileKind.Npc
            ? FindLoadedNpc(entry.Name)
            : FindLoadedPlayerCharacter(entry.Name);
    }

    private static CustomContentDefinition_NPC? FindLoadedNpc(string name)
    {
        foreach (KeyValuePair<string, CustomContentDefinition_NPC> pair
                 in CustomContentDefinition_NPC.loaded)
        {
            if (string.Equals(
                    pair.Key,
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static CustomContentDefinition_PlayerCharacter?
        FindLoadedPlayerCharacter(string name)
    {
        foreach (KeyValuePair<string, CustomContentDefinition_PlayerCharacter>
                 pair in CustomContentDefinition_PlayerCharacter.loaded)
        {
            if (string.Equals(
                    pair.Key,
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static int CompareDefinitionEntries(
        DefinitionFileEntry left,
        DefinitionFileEntry right)
    {
        int name = StringComparer.OrdinalIgnoreCase.Compare(
            left.Name,
            right.Name);
        if (name != 0)
            return name;

        int kind = left.Kind.CompareTo(right.Kind);
        if (kind != 0)
            return kind;
        return right.IsEnabled.CompareTo(left.IsEnabled);
    }

    private static void SortForRuntimeOrder(
        List<FileMoveRecord> moves,
        bool enabling)
    {
        moves.Sort((left, right) =>
        {
            int order = RuntimeOrder(left.Entry, enabling)
                .CompareTo(RuntimeOrder(right.Entry, enabling));
            return order != 0
                ? order
                : StringComparer.OrdinalIgnoreCase.Compare(
                    left.Entry.Name,
                    right.Entry.Name);
        });
    }

    private static int RuntimeOrder(
        DefinitionFileEntry entry,
        bool enabling)
    {
        bool player = entry.Kind == DefinitionFileKind.PlayerCharacter;
        if (enabling)
            return player ? 0 : 1;
        return player ? 1 : 0;
    }

    private static GameObject? FindBackRow(
        IReadOnlyList<GameObject> rows)
    {
        foreach (GameObject candidate in rows)
        {
            TMPro.TextMeshProUGUI[] labels =
                candidate.GetComponentsInChildren<TMPro.TextMeshProUGUI>(
                    includeInactive: true);
            if (labels.Length > 0
                && string.Equals(
                    labels[0].text,
                    "Back",
                    StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void InsertBeforeBack(
        GameObject root,
        ListUIItem item,
        GameObject? backRow)
    {
        GameObject instantiated = InstantiateListItem(root, item);
        if (backRow != null)
        {
            instantiated.transform.SetSiblingIndex(
                backRow.transform.GetSiblingIndex());
        }
    }
}
