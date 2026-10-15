using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Add to the <b>XR Origin</b> (or any parent of your hand rigs). When
/// <see cref="XRDirectInteractor.attachTransform"/> is unset, assigns a sensible child —
/// joystick, lever, and other grabs use this for hand pose and for <see cref="HOTASJoystick"/> grab line.
/// Does not change flight code; only XR grab wiring.
/// <para><b>Scene template:</b> If stick grab feels wrong while the same prefab is fine elsewhere,
/// try a <b>Standard</b> (3D) scene instead of Unity’s <b>Basic</b> template — Basic can ship extra
/// objects / defaults that interfere with XR interaction.</para>
/// </summary>
[DefaultExecutionOrder(-450)]
public class XRHandAttachAutoSetup : MonoBehaviour
{
    [Tooltip("Tried in order as descendants of each XRDirectInteractor (e.g. LDirectInteractor / RDirectInteractor).")]
    public string[] attachNameHints = { "Attach", "Palm", "HandAttach", "FakeHand", "Grip", "Pinch" };

    [Tooltip("If no name matches, use the interactor's first child (often the hand / proxy mesh root).")]
    public bool fallbackToFirstChild = true;

    [Header("Hands")]
    [Tooltip("Hides tracked/controller visuals while grabbing (stick, lever, etc.). Requires a valid XR controller on the interactor.")]
    public bool hideControllerOnSelect = true;

    [Header("Debug")]
    public bool logAssignments;

    void Awake()
    {
        WireAttachTransforms();
        ApplyHideControllerOnSelect();
    }

    void ApplyHideControllerOnSelect()
    {
        if (!hideControllerOnSelect) return;
        var interactors = GetComponentsInChildren<XRDirectInteractor>(true);
        foreach (var ix in interactors)
        {
            if (ix == null) continue;
#pragma warning disable CS0618 // XRI 3.0: prefer Select Enter/Exit + controller model toggles when migrating off legacy controller path
            ix.hideControllerOnSelect = true;
#pragma warning restore CS0618
        }
    }

    void WireAttachTransforms()
    {
        var interactors = GetComponentsInChildren<XRDirectInteractor>(true);
        foreach (var ix in interactors)
        {
            if (ix == null || ix.attachTransform != null)
                continue;

            Transform at = FindAttachCandidate(ix.transform);
            if (at != null)
            {
                ix.attachTransform = at;
                if (logAssignments)
                    Debug.Log("[XRHandAttachAutoSetup] " + ix.gameObject.name + " → attachTransform = " + at.name, this);
            }
            else
            {
                Debug.LogWarning(
                    "[XRHandAttachAutoSetup] No attach candidate for '" + ix.gameObject.name +
                    "'. Assign Attach Transform on the XR Direct Interactor (palm / fake hand) so lever & stick line up.",
                    ix);
            }
        }
    }

    Transform FindAttachCandidate(Transform interactorRoot)
    {
        if (attachNameHints != null)
        {
            foreach (var hint in attachNameHints)
            {
                if (string.IsNullOrEmpty(hint)) continue;
                var t = FindChildRecursive(interactorRoot, hint);
                if (t != null) return t;
            }
        }

        if (fallbackToFirstChild && interactorRoot.childCount > 0)
            return interactorRoot.GetChild(0);

        return null;
    }

    static Transform FindChildRecursive(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var f = FindChildRecursive(root.GetChild(i), name);
            if (f != null) return f;
        }
        return null;
    }
}
