using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Workaround for XRIT 3.x: <see cref="XRDirectInteractor"/> can call StartCoroutine/StopCoroutine with a null
/// IEnumerator if OnEnable runs before Awake finishes assigning it. Unity warns if you edit the package in Library/.
/// This guard runs first and assigns the private enumerator via reflection when still null.
/// Add to the same GameObject as <see cref="XRDirectInteractor"/> (e.g. hand ray / direct interactor roots).
/// </summary>
/// Runs before default (0) so <see cref="OnEnable"/> runs before <see cref="XRDirectInteractor"/>'s OnEnable,
/// after both components' Awake phases (XR assigns the coroutine field in Awake).
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(XRDirectInteractor))]
public sealed class XRDirectInteractorCoroutineGuard : MonoBehaviour
{
    static FieldInfo s_Field;
    static MethodInfo s_Method;
    static bool s_Cached;

    XRDirectInteractor _interactor;

    void Awake()
    {
        CacheReflection();
        _interactor = GetComponent<XRDirectInteractor>();
    }

    void OnEnable()
    {
        EnsureEnumerator();
    }

    static void CacheReflection()
    {
        if (s_Cached) return;
        s_Cached = true;
        var t = typeof(XRDirectInteractor);
        s_Field = t.GetField("m_UpdateCollidersAfterTriggerStay", BindingFlags.Instance | BindingFlags.NonPublic);
        s_Method = t.GetMethod("UpdateCollidersAfterOnTriggerStay", BindingFlags.Instance | BindingFlags.NonPublic);
    }

    void EnsureEnumerator()
    {
        if (_interactor == null || s_Field == null || s_Method == null) return;
        if (s_Field.GetValue(_interactor) != null) return;

        var en = (IEnumerator)s_Method.Invoke(_interactor, null);
        if (en != null)
            s_Field.SetValue(_interactor, en);
    }
}
