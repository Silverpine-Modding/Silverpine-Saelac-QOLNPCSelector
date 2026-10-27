#nullable enable

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SilverpineMods.QOLNPCSelector;

public sealed partial class Plugin
{
    private static void InsertMenuItemAtTop(
        GameObject root,
        ListUIItem item,
        int index)
    {
        GameObject row = InstantiateListItem(root, item);
        row.transform.SetSiblingIndex(index);
        if (index == 0)
            row.AddComponent<EditorMenuScrollReset>().Initialize(root);
    }
}

// Owned by the new menu row, not the BepInEx bootstrap host. Leaving this
// screen destroys the row and cancels its pending scroll reset automatically.
internal sealed class EditorMenuScrollReset : MonoBehaviour
{
    private GameObject listRoot = null!;

    internal void Initialize(GameObject root)
    {
        listRoot = root;
    }

    private IEnumerator Start()
    {
        // Allow native deferred row destruction and layout to finish before
        // resetting the inherited scroll offset from the character editor.
        yield return null;

        if (listRoot == null || !gameObject.activeInHierarchy)
            yield break;

        ScrollRect? scroll = listRoot.GetComponentInParent<ScrollRect>();
        if (scroll == null || scroll.content == null
            || (listRoot.transform != scroll.content
                && !listRoot.transform.IsChildOf(scroll.content)))
        {
            yield break;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        Canvas.ForceUpdateCanvases();
        scroll.StopMovement();
        scroll.verticalNormalizedPosition = 1f;
    }
}
