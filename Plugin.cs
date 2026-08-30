#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SilverpineMods.QOLNPCSelector;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed partial class Plugin : BaseUnityPlugin
{
    public const string PluginGuid =
        "renegadex.silverpine.qolnpcselector";
    public const string PluginName = "QOLNPCSelector";
    public const string PluginVersion = "1.3.0";

    private const string TogglePrefabPath =
        "MiscPrefabs/ListUI/ListUIItemPrefab_Toggle";
    private const string InjectedToggleRowName =
        "CustomNPCEnableToggleRow";

    private static readonly FieldInfo? EditorRootField =
        AccessTools.Field(
            typeof(GeneralCustomContentEditorUI),
            "rootGameObject");

    private static readonly MethodInfo? DisplayMessageMethod =
        AccessTools.Method(
            typeof(GeneralCustomContentEditorUI),
            "DisplayMessage",
            new[] { typeof(string), typeof(Action) });

    private static readonly MethodInfo? EditDefinitionMethod =
        AccessTools.Method(
            typeof(GeneralCustomContentEditorUI),
            "EditCustomContentDefinition",
            new[] { typeof(CustomContentDefinition), typeof(Action) });

    private static readonly MethodInfo? ShowLoadedDefinitionsMethod =
        AccessTools.Method(
            typeof(GeneralCustomContentEditorUI),
            "ShowLoadedCustomContentDefinitions");

    internal static ManualLogSource Log { get; private set; } = null!;

    private void Awake()
    {
        Log = Logger;

        if (EditorRootField == null
            || EditDefinitionMethod == null
            || ShowLoadedDefinitionsMethod == null)
        {
            Logger.LogError(
                "Could not find the custom-content editor root. The "
                + "installed Silverpine version is incompatible; no "
                + "patches were applied.");
            return;
        }

        try
        {
            Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, PluginGuid);
            Logger.LogInfo(
                $"{PluginName} {PluginVersion} loaded. The loaded custom "
                + "content page now has direct NPC enable checkboxes.");
        }
        catch (Exception exception)
        {
            Logger.LogError(
                "Could not patch the loaded custom-content page. The "
                + "installed Silverpine version may be incompatible.\n"
                + exception);
        }
    }

    private void OnDestroy()
    {
        // Silverpine destroys the initial BepInEx host while transitioning
        // away from the main menu. This is not a dependable plugin-unload
        // event, so the process-lifetime UI patch deliberately remains.
        Logger.LogInfo(
            "Plugin host destroyed during Silverpine bootstrap; the custom "
            + "NPC checkbox patch remains installed for the process "
            + "lifetime.");
    }

    internal static void AddEnableCheckboxes(
        GeneralCustomContentEditorUI editor)
    {
        GameObject? root = EditorRootField?.GetValue(editor) as GameObject;
        if (root == null)
        {
            Log.LogError(
                "Could not obtain the custom-content list root; NPC enable "
                + "checkboxes were not added.");
            return;
        }

        GameObject? togglePrefab = Resources.Load<GameObject>(
            TogglePrefabPath);
        if (togglePrefab == null)
        {
            Log.LogError(
                "Could not load Silverpine's native list toggle prefab; "
                + "NPC enable checkboxes were not added.");
            return;
        }

        var definitions = new Dictionary<string, CustomContentDefinition_NPC>(
            CustomContentDefinition_NPC.loaded,
            StringComparer.Ordinal);
        var originalRows = new List<GameObject>();
        foreach (Transform child in root.transform)
            originalRows.Add(child.gameObject);

        int added = 0;

        foreach (GameObject row in originalRows)
        {
            TextMeshProUGUI[] labels =
                row.GetComponentsInChildren<TextMeshProUGUI>(
                    includeInactive: true);

            // Native loaded-definition rows use [name, type] labels. Limit
            // injection to rows whose type is exactly NPC, leaving player
            // character definitions and navigation actions untouched.
            if (labels.Length < 2
                || !string.Equals(
                    labels[1].text,
                    "NPC",
                    StringComparison.Ordinal)
                || !definitions.TryGetValue(
                    labels[0].text,
                    out CustomContentDefinition_NPC definition))
            {
                continue;
            }

            try
            {
                ReplaceWithEnableRow(
                    editor,
                    root,
                    row,
                    togglePrefab,
                    definition);
                added++;
            }
            catch (Exception exception)
            {
                Log.LogError(
                    "Could not add an enable checkbox for custom NPC \""
                    + definition.customContentName + "\".\n" + exception);
            }
        }

        if (added != definitions.Count)
        {
            Log.LogWarning(
                $"Added {added} custom NPC enable checkbox(es), but "
                + $"{definitions.Count} NPC definition(s) are loaded.");
        }

        AddDefinitionFileButtons(editor, root, originalRows);
        AddSelectionToolsButton(editor, root, originalRows);
    }

    private static void ReplaceWithEnableRow(
        GeneralCustomContentEditorUI editor,
        GameObject root,
        GameObject row,
        GameObject togglePrefab,
        CustomContentDefinition_NPC definition)
    {
        int siblingIndex = row.transform.GetSiblingIndex();
        GameObject toggleRow = UnityEngine.Object.Instantiate(
            togglePrefab,
            root.transform,
            worldPositionStays: false);
        toggleRow.name = InjectedToggleRowName;

        try
        {
            TextMeshProUGUI? nameLabel =
                toggleRow.GetComponentInChildren<TextMeshProUGUI>(
                    includeInactive: true);
            Toggle? toggle = toggleRow.GetComponentInChildren<Toggle>(
                includeInactive: true);
            if (nameLabel == null || toggle == null)
            {
                throw new MissingComponentException(
                    "Silverpine's native toggle row is missing its label "
                    + "or Toggle component.");
            }

            nameLabel.text = definition.customContentName;

            toggle.onValueChanged.RemoveAllListeners();
            toggle.SetIsOnWithoutNotify(definition.enabled);
            toggle.onValueChanged.AddListener(enabled =>
                SetEnabled(editor, definition, toggle, enabled));

            // The complete native toggle prefab is now a direct child of the
            // list root, so the base list's layout and render order apply to
            // it normally. Make only the name text open the full editor; the
            // checkbox continues to own its separate pointer target.
            nameLabel.raycastTarget = true;
            Button editButton = nameLabel.gameObject.AddComponent<Button>();
            editButton.targetGraphic = nameLabel;
            editButton.transition = Selectable.Transition.None;
            editButton.onClick.AddListener(() =>
                OpenDefinition(editor, definition));

            toggleRow.AddComponent<Tooltip>().SetText(
                "Click the NPC name to edit its full definition. This "
                + "checkbox changes only Enabled and writes the definition "
                + "immediately; it does not run the editor's normal Save "
                + "checks. The new definition state is immediate, but an "
                + "already-loaded save must be reloaded before its spawned "
                + "NPC roster changes.");

            toggleRow.transform.SetSiblingIndex(siblingIndex);
            row.SetActive(false);
            UnityEngine.Object.Destroy(row);
        }
        catch
        {
            UnityEngine.Object.Destroy(toggleRow);
            throw;
        }
    }

    private static void OpenDefinition(
        GeneralCustomContentEditorUI editor,
        CustomContentDefinition_NPC definition)
    {
        if (EditDefinitionMethod == null
            || ShowLoadedDefinitionsMethod == null)
        {
            return;
        }

        try
        {
            Action discardCallback = () =>
                ShowLoadedDefinitionsMethod.Invoke(editor, null);
            EditDefinitionMethod.Invoke(
                editor,
                new object[] { definition, discardCallback });
        }
        catch (Exception exception)
        {
            Log.LogError(
                "Could not open the custom NPC editor for \""
                + definition.customContentName + "\".\n" + exception);
        }
    }

    private static void SetEnabled(
        GeneralCustomContentEditorUI editor,
        CustomContentDefinition_NPC definition,
        Toggle toggle,
        bool enabled)
    {
        bool previous = definition.enabled;
        if (previous == enabled)
            return;

        try
        {
            // Mutate the definition already held in the loaded dictionary so
            // runtime lookups see the new value immediately. Persist through
            // the low-level SILC writer only: deliberately do not clone,
            // validate, confirm overwrite, unload, or reload the definition.
            definition.enabled = enabled;
            new SILCFile(definition).SaveToDefinitionsFolder();

            Log.LogInfo(
                $"{(enabled ? "Enabled" : "Disabled")} custom NPC "
                + $"definition \"{definition.customContentName}\" without "
                + "running full editor save validation.");
        }
        catch (Exception exception)
        {
            definition.enabled = previous;
            toggle.SetIsOnWithoutNotify(previous);

            Log.LogError(
                "Could not persist the Enabled state for custom NPC \""
                + definition.customContentName + "\". The in-memory state "
                + "was restored.\n" + exception);
            ShowFailure(editor, definition, exception);
        }
    }

    private static void ShowFailure(
        GeneralCustomContentEditorUI editor,
        CustomContentDefinition_NPC definition,
        Exception exception)
    {
        if (DisplayMessageMethod == null)
            return;

        try
        {
            DisplayMessageMethod.Invoke(
                editor,
                new object?[]
                {
                    "Failed to update Enabled for custom NPC \""
                    + definition.customContentName + "\":\n\n"
                    + exception.Message,
                    null
                });
        }
        catch (Exception displayException)
        {
            Log.LogError(
                "Could not display the custom NPC checkbox failure.\n"
                + displayException);
        }
    }
}

[HarmonyPatch(
    typeof(GeneralCustomContentEditorUI),
    "ShowLoadedCustomContentDefinitions")]
internal static class LoadedCustomContentCheckboxPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void AddCheckboxes(
        GeneralCustomContentEditorUI __instance)
    {
        try
        {
            Plugin.AddEnableCheckboxes(__instance);
        }
        catch (Exception exception)
        {
            // The original list has already drawn successfully. A checkbox
            // failure must never make the native editor unusable.
            Plugin.Log.LogError(
                "Failed while adding custom NPC enable checkboxes.\n"
                + exception);
        }
    }
}
