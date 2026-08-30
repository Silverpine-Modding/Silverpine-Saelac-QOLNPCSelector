#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace SilverpineMods.QOLNPCSelector;

public sealed partial class Plugin
{
    private const string DefaultEnabledListName = "enabled-npcs";

    private static string EnabledListDirectory =>
        Path.Combine(Paths.ConfigPath, PluginName, "Lists");

    [Serializable]
    private sealed class EnabledNpcListDocument
    {
        public int formatVersion = 1;
        public string exportedAtUtc = "";
        public List<string> enabledNpcs = new();
    }

    private static void AddSelectionToolsButton(
        GeneralCustomContentEditorUI editor,
        GameObject root,
        IReadOnlyList<GameObject> originalRows)
    {
        try
        {
            GameObject? backRow = null;
            foreach (GameObject candidate in originalRows)
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
                    backRow = candidate;
                    break;
                }
            }

            var item = new ListUIItem_Generic(
                "NPC Selection Tools",
                null,
                "Import / Export / Randomize",
                () => ShowSelectionTools(editor))
            {
                tooltip =
                    "Open enabled-NPC list import, export, and random "
                    + "selection tools."
            };

            GameObject instantiated = InstantiateListItem(root, item);
            if (backRow != null)
            {
                instantiated.transform.SetSiblingIndex(
                    backRow.transform.GetSiblingIndex());
            }
        }
        catch (Exception exception)
        {
            Log.LogError(
                "Could not add the NPC Selection Tools button.\n"
                + exception);
        }
    }

    private static void ShowSelectionTools(
        GeneralCustomContentEditorUI editor)
    {
        GameObject? root = EditorRootField?.GetValue(editor) as GameObject;
        if (root == null || GenericListUI.Instance == null)
        {
            Log.LogError(
                "Could not open NPC Selection Tools because the custom "
                + "content editor is unavailable.");
            return;
        }

        List<CustomContentDefinition_NPC> definitions =
            SnapshotNpcDefinitions();
        int enabledCount = 0;
        foreach (CustomContentDefinition_NPC definition in definitions)
        {
            if (definition.enabled)
                enabledCount++;
        }
        int listCount = GetEnabledListPaths().Count;

        var items = new List<ListUIItem>
        {
            new ListUIItem_Label(
                "Manage the exact set of enabled NPC definitions. Import "
                + "and random selection use the same validation-free "
                + "Enabled-only save path as the list checkboxes."),
            new ListUIItem_Generic(
                "Export Enabled NPC List",
                null,
                $"{enabledCount} enabled",
                () => ShowExportNameInput(editor))
            {
                tooltip =
                    "Prompts for a JSON filename, then exports the exact "
                    + "set of enabled NPC definitions to:\n"
                    + EnabledListDirectory
            },
            new ListUIItem_Generic(
                "Import Enabled NPC List",
                null,
                listCount == 1 ? "1 JSON list" : $"{listCount} JSON lists",
                () => ShowImportLists(editor))
            {
                tooltip =
                    "Choose a named JSON list from:\n"
                    + EnabledListDirectory
            },
            new ListUIItem_Generic(
                "Randomize Enabled NPCs",
                null,
                $"{definitions.Count} available",
                () => ShowRandomizeInput(editor))
            {
                tooltip =
                    "Choose a count, disable every loaded NPC definition, "
                    + "then enable that many definitions at random."
            },
            new ListUIItem_Line(),
            new ListUIItem_Generic(
                "Back",
                null,
                "",
                () => ShowLoadedDefinitions(editor))
        };

        GenericListUI.Instance.DrawIntoRoot(items, root);
    }

    private static void ShowExportNameInput(
        GeneralCustomContentEditorUI editor,
        string initialName = DefaultEnabledListName)
    {
        GameObject? root = EditorRootField?.GetValue(editor) as GameObject;
        if (root == null || GenericListUI.Instance == null)
        {
            Log.LogError(
                "Could not open the export-name input because the custom "
                + "content editor is unavailable.");
            return;
        }

        string requestedName = initialName;
        var items = new List<ListUIItem>
        {
            new ListUIItem_Label(
                "Name this enabled-NPC JSON preset. Add .json yourself or "
                + "leave it off; the extension is added automatically. "
                + "Spaces and any other characters valid in a Windows "
                + "filename are allowed. An existing preset with the same "
                + "name is replaced."),
            new ListUIItem_SmallInputField(
                "JSON Filename",
                InputFieldConstraint.ConstraintType.None,
                100,
                requestedName,
                (_, value) => requestedName = value),
            new ListUIItem_Generic(
                "Export",
                null,
                "Save named JSON",
                () => ExportEnabledList(editor, requestedName)),
            new ListUIItem_Generic(
                "Back",
                null,
                "",
                () => ShowSelectionTools(editor))
        };

        GenericListUI.Instance.DrawIntoRoot(items, root);
    }

    private static void ExportEnabledList(
        GeneralCustomContentEditorUI editor,
        string requestedName)
    {
        if (!TryResolveEnabledListPath(
                requestedName,
                out string path,
                out string filename,
                out string error))
        {
            ShowToolMessage(
                editor,
                error,
                () => ShowExportNameInput(editor, requestedName));
            return;
        }

        try
        {
            List<CustomContentDefinition_NPC> definitions =
                SnapshotNpcDefinitions();
            var document = new EnabledNpcListDocument
            {
                exportedAtUtc = DateTime.UtcNow.ToString("O")
            };

            foreach (CustomContentDefinition_NPC definition in definitions)
            {
                if (definition.enabled)
                    document.enabledNpcs.Add(definition.customContentName);
            }

            Directory.CreateDirectory(EnabledListDirectory);
            File.WriteAllText(
                path,
                JsonUtility.ToJson(document, prettyPrint: true),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Log.LogInfo(
                $"Exported {document.enabledNpcs.Count} enabled NPC "
                + $"definition(s) to {path}");
            ShowToolMessage(
                editor,
                $"Exported {document.enabledNpcs.Count} enabled NPC "
                + "definition"
                + (document.enabledNpcs.Count == 1 ? "" : "s")
                + " to:\n\n" + filename,
                () => ShowSelectionTools(editor));
        }
        catch (Exception exception)
        {
            Log.LogError("Could not export the enabled NPC list.\n" + exception);
            ShowToolMessage(
                editor,
                "Failed to export the enabled NPC list:\n\n"
                + exception.Message,
                () => ShowExportNameInput(editor, requestedName));
        }
    }

    private static void ShowImportLists(
        GeneralCustomContentEditorUI editor)
    {
        GameObject? root = EditorRootField?.GetValue(editor) as GameObject;
        if (root == null || GenericListUI.Instance == null)
        {
            Log.LogError(
                "Could not open the named JSON list picker because the "
                + "custom content editor is unavailable.");
            return;
        }

        List<string> paths = GetEnabledListPaths();
        var items = new List<ListUIItem>
        {
            new ListUIItem_Label(
                "Choose a named enabled-NPC JSON preset to import. The "
                + "preset becomes the exact enabled set among definitions "
                + "that are currently loaded.\n\n" + EnabledListDirectory)
        };

        if (paths.Count == 0)
        {
            items.Add(new ListUIItem_Label(
                "No JSON presets have been exported or copied into the "
                + "folder yet."));
        }
        else
        {
            foreach (string path in paths)
            {
                string capturedPath = path;
                string suffix;
                try
                {
                    EnabledNpcListDocument summary =
                        ReadEnabledListDocument(path);
                    int count = summary.enabledNpcs.Count;
                    suffix = count == 1 ? "1 enabled NPC" : $"{count} enabled NPCs";
                }
                catch
                {
                    suffix = "Invalid JSON";
                }

                items.Add(new ListUIItem_Generic(
                    Path.GetFileNameWithoutExtension(path),
                    null,
                    suffix,
                    () => ImportEnabledList(editor, capturedPath))
                {
                    tooltip = Path.GetFileName(path)
                });
            }
        }

        items.Add(new ListUIItem_Line());
        items.Add(new ListUIItem_Generic(
            "Refresh",
            null,
            "",
            () => ShowImportLists(editor)));
        items.Add(new ListUIItem_Generic(
            "Back",
            null,
            "",
            () => ShowSelectionTools(editor)));
        GenericListUI.Instance.DrawIntoRoot(items, root);
    }

    private static void ImportEnabledList(
        GeneralCustomContentEditorUI editor,
        string path)
    {
        try
        {
            EnabledNpcListDocument document = ReadEnabledListDocument(path);
            var requestedNames = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (string requestedName in document.enabledNpcs)
            {
                if (!string.IsNullOrWhiteSpace(requestedName))
                    requestedNames.Add(requestedName.Trim());
            }

            List<CustomContentDefinition_NPC> definitions =
                SnapshotNpcDefinitions();
            var definitionsByName =
                new Dictionary<string, CustomContentDefinition_NPC>(
                    StringComparer.OrdinalIgnoreCase);
            foreach (CustomContentDefinition_NPC definition in definitions)
            {
                definitionsByName[definition.customContentName] = definition;
            }

            var enabledNames = new HashSet<string>(StringComparer.Ordinal);
            var missingNames = new List<string>();
            foreach (string requestedName in requestedNames)
            {
                if (definitionsByName.TryGetValue(
                        requestedName,
                        out CustomContentDefinition_NPC definition))
                {
                    enabledNames.Add(definition.customContentName);
                }
                else
                {
                    // Missing definitions are intentionally non-fatal. Keep
                    // importing everything that is available and provide the
                    // requested warning in the BepInEx console.
                    missingNames.Add(requestedName);
                }
            }

            string filename = Path.GetFileName(path);
            missingNames.Sort(StringComparer.OrdinalIgnoreCase);
            if (missingNames.Count > 0)
            {
                Log.LogWarning(
                    "[Import Warning] \"" + filename + "\" ignored "
                    + missingNames.Count
                    + " NPC name(s) that are not currently loaded: "
                    + string.Join(", ", missingNames));
            }

            if (!TryApplyEnabledSet(
                    editor,
                    enabledNames,
                    "Imported enabled NPC list \"" + filename + "\"",
                    out int enabledCount))
            {
                return;
            }

            string missingNotice = missingNames.Count == 0
                ? ""
                : "\n\nIgnored " + missingNames.Count
                    + " missing NPC name"
                    + (missingNames.Count == 1 ? "" : "s")
                    + "; a warning was written to the BepInEx console.";
            ShowToolMessage(
                editor,
                "Imported \"" + filename + "\". " + enabledCount
                + " NPC definition"
                + (enabledCount == 1 ? " is" : "s are")
                + " enabled."
                + missingNotice,
                () => ShowSelectionTools(editor));
        }
        catch (Exception exception)
        {
            Log.LogError(
                "Could not import enabled NPC list \""
                + Path.GetFileName(path) + "\".\n" + exception);
            ShowToolMessage(
                editor,
                "Failed to import \"" + Path.GetFileName(path)
                + "\":\n\n" + exception.Message,
                () => ShowImportLists(editor));
        }
    }

    private static EnabledNpcListDocument ReadEnabledListDocument(
        string path)
    {
        string json = File.ReadAllText(path, Encoding.UTF8);
        EnabledNpcListDocument? document =
            JsonUtility.FromJson<EnabledNpcListDocument>(json);
        if (document == null)
            throw new InvalidDataException("The JSON document is empty.");
        if (document.formatVersion != 1)
        {
            throw new InvalidDataException(
                "Unsupported enabled-NPC list format version: "
                + document.formatVersion + ".");
        }

        document.enabledNpcs ??= new List<string>();
        return document;
    }

    private static List<string> GetEnabledListPaths()
    {
        var paths = new List<string>();
        try
        {
            if (!Directory.Exists(EnabledListDirectory))
                return paths;

            paths.AddRange(Directory.GetFiles(
                EnabledListDirectory,
                "*.json",
                SearchOption.TopDirectoryOnly));
            paths.Sort((left, right) =>
                StringComparer.OrdinalIgnoreCase.Compare(
                    Path.GetFileName(left),
                    Path.GetFileName(right)));
        }
        catch (Exception exception)
        {
            Log.LogError(
                "Could not enumerate enabled-NPC JSON presets in "
                + EnabledListDirectory + ".\n" + exception);
        }

        return paths;
    }

    private static bool TryResolveEnabledListPath(
        string requestedName,
        out string path,
        out string filename,
        out string error)
    {
        path = "";
        filename = requestedName.Trim();
        error = "";

        if (filename.Length == 0)
        {
            error = "Enter a JSON filename.";
            return false;
        }

        if (!filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            filename += ".json";

        if (Path.GetFileName(filename) != filename
            || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || string.IsNullOrWhiteSpace(
                Path.GetFileNameWithoutExtension(filename)))
        {
            error =
                "Enter a filename without a directory or invalid filename "
                + "characters.";
            return false;
        }

        try
        {
            string directory = Path.GetFullPath(EnabledListDirectory);
            path = Path.GetFullPath(Path.Combine(directory, filename));
            if (!string.Equals(
                    Path.GetDirectoryName(path),
                    directory,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "The JSON filename must stay inside the list folder.";
                path = "";
                return false;
            }
        }
        catch (Exception exception)
        {
            error = "Invalid JSON filename: " + exception.Message;
            path = "";
            return false;
        }

        return true;
    }

    private static void ShowRandomizeInput(
        GeneralCustomContentEditorUI editor)
    {
        GameObject? root = EditorRootField?.GetValue(editor) as GameObject;
        if (root == null || GenericListUI.Instance == null)
        {
            Log.LogError(
                "Could not open the random NPC count input because the "
                + "custom content editor is unavailable.");
            return;
        }

        int availableCount = SnapshotNpcDefinitions().Count;
        string requestedCount = Math.Min(1, availableCount).ToString();
        var items = new List<ListUIItem>
        {
            new ListUIItem_Label(
                "Enter how many NPC definitions should be enabled. No "
                + "changes are made until Apply Random Selection is "
                + "pressed. A value above the available count enables all "
                + "available NPCs; zero disables all of them."),
            new ListUIItem_SmallInputField(
                "Number of NPCs",
                InputFieldConstraint.ConstraintType.PositiveNumber,
                5,
                requestedCount,
                (_, value) => requestedCount = value),
            new ListUIItem_Generic(
                "Apply Random Selection",
                null,
                $"{availableCount} available",
                () => ApplyRandomSelection(
                    editor,
                    requestedCount,
                    availableCount)),
            new ListUIItem_Generic(
                "Back",
                null,
                "",
                () => ShowSelectionTools(editor))
        };

        GenericListUI.Instance.DrawIntoRoot(items, root);
    }

    private static void ApplyRandomSelection(
        GeneralCustomContentEditorUI editor,
        string requestedCount,
        int displayedAvailableCount)
    {
        if (!int.TryParse(requestedCount, out int count) || count < 0)
        {
            ShowToolMessage(
                editor,
                "Enter a non-negative whole number.",
                () => ShowRandomizeInput(editor));
            return;
        }

        List<CustomContentDefinition_NPC> definitions =
            SnapshotNpcDefinitions();
        int selectedCount = Math.Min(count, definitions.Count);
        var random = new System.Random(Guid.NewGuid().GetHashCode());

        for (int index = definitions.Count - 1; index > 0; index--)
        {
            int swapIndex = random.Next(index + 1);
            CustomContentDefinition_NPC temporary = definitions[index];
            definitions[index] = definitions[swapIndex];
            definitions[swapIndex] = temporary;
        }

        var enabledNames = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < selectedCount; index++)
            enabledNames.Add(definitions[index].customContentName);

        if (!TryApplyEnabledSet(
                editor,
                enabledNames,
                "Randomized enabled NPCs",
                out int enabledCount))
        {
            return;
        }

        Log.LogInfo(
            "Random selection enabled: "
            + (enabledNames.Count == 0
                ? "<none>"
                : string.Join(", ", enabledNames)));
        string availabilityNotice = displayedAvailableCount == definitions.Count
            ? ""
            : "\nThe loaded NPC count changed while the input was open.";
        ShowToolMessage(
            editor,
            $"Random selection complete. {enabledCount} of "
            + $"{definitions.Count} NPC definition"
            + $"{(definitions.Count == 1 ? "" : "s")} "
            + $"{(enabledCount == 1 ? "is" : "are")} enabled."
            + availabilityNotice,
            () => ShowSelectionTools(editor));
    }

    private static bool TryApplyEnabledSet(
        GeneralCustomContentEditorUI editor,
        ISet<string> enabledNames,
        string operation,
        out int enabledCount)
    {
        List<CustomContentDefinition_NPC> definitions =
            SnapshotNpcDefinitions();
        var originalStates =
            new Dictionary<CustomContentDefinition_NPC, bool>();

        enabledCount = 0;
        foreach (CustomContentDefinition_NPC definition in definitions)
        {
            bool desired = enabledNames.Contains(
                definition.customContentName);
            if (desired)
                enabledCount++;
            if (definition.enabled == desired)
                continue;

            originalStates.Add(definition, definition.enabled);
            definition.enabled = desired;
        }

        try
        {
            foreach (CustomContentDefinition_NPC definition
                     in originalStates.Keys)
            {
                // This is deliberately the low-level writer only. Batch
                // import/randomization performs no editor validation and no
                // definition unload/reload lifecycle.
                new SILCFile(definition).SaveToDefinitionsFolder();
            }

            Log.LogInfo(
                operation + ": changed " + originalStates.Count
                + " definition(s); " + enabledCount + " enabled.");
            return true;
        }
        catch (Exception exception)
        {
            foreach (KeyValuePair<CustomContentDefinition_NPC, bool> original
                     in originalStates)
            {
                original.Key.enabled = original.Value;
            }

            var rollbackFailures = new List<string>();
            foreach (CustomContentDefinition_NPC definition
                     in originalStates.Keys)
            {
                try
                {
                    new SILCFile(definition).SaveToDefinitionsFolder();
                }
                catch (Exception rollbackException)
                {
                    rollbackFailures.Add(
                        definition.customContentName + ": "
                        + rollbackException.Message);
                }
            }

            string rollbackMessage = rollbackFailures.Count == 0
                ? "All changed definitions were restored."
                : "Some on-disk definitions could not be restored: "
                    + string.Join("; ", rollbackFailures);
            Log.LogError(
                operation + " failed. " + rollbackMessage + "\n"
                + exception);
            ShowToolMessage(
                editor,
                operation + " failed:\n\n" + exception.Message
                + "\n\n" + rollbackMessage,
                () => ShowSelectionTools(editor));
            enabledCount = CountEnabledDefinitions();
            return false;
        }
    }

    private static List<CustomContentDefinition_NPC>
        SnapshotNpcDefinitions()
    {
        var definitions = new List<CustomContentDefinition_NPC>();
        foreach (CustomContentDefinition_NPC definition
                 in CustomContentDefinition_NPC.loaded.Values)
        {
            if (definition != null)
                definitions.Add(definition);
        }

        definitions.Sort((left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(
                left.customContentName,
                right.customContentName));
        return definitions;
    }

    private static int CountEnabledDefinitions()
    {
        int count = 0;
        foreach (CustomContentDefinition_NPC definition
                 in CustomContentDefinition_NPC.loaded.Values)
        {
            if (definition != null && definition.enabled)
                count++;
        }

        return count;
    }

    private static void ShowLoadedDefinitions(
        GeneralCustomContentEditorUI editor)
    {
        try
        {
            ShowLoadedDefinitionsMethod?.Invoke(editor, null);
        }
        catch (Exception exception)
        {
            Log.LogError(
                "Could not return to the loaded custom-content list.\n"
                + exception);
        }
    }

    private static GameObject InstantiateListItem(
        GameObject root,
        ListUIItem item)
    {
        GameObject? prefab = Resources.Load<GameObject>(
            "MiscPrefabs/ListUI/" + item.PrefabName);
        if (prefab == null)
        {
            throw new InvalidOperationException(
                "Could not load list item prefab " + item.PrefabName + ".");
        }

        GameObject instantiated = UnityEngine.Object.Instantiate(
            prefab,
            root.transform,
            worldPositionStays: false);
        if (item.tooltip != null)
            instantiated.AddComponent<Tooltip>().SetText(item.tooltip);
        item.instantiated = instantiated;
        item.HandleInstantiated(instantiated);
        return instantiated;
    }

    private static void ShowToolMessage(
        GeneralCustomContentEditorUI editor,
        string message,
        Action callback)
    {
        if (DisplayMessageMethod == null)
        {
            Log.LogWarning(message);
            callback();
            return;
        }

        try
        {
            DisplayMessageMethod.Invoke(
                editor,
                new object?[] { message, callback });
        }
        catch (Exception exception)
        {
            Log.LogError(
                "Could not display an NPC Selection Tools message.\n"
                + exception);
            callback();
        }
    }
}
