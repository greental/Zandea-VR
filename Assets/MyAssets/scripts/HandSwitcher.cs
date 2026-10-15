using System.Collections.Generic;
using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class HandSwitcher_NoGrabInteractable : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Resolved from the selecting player's hand when Auto Detect is enabled. Manual fallback otherwise.")]
    public GameObject realHandModel;
    public GameObject fakeGrabHandModel;
    public XRBaseInteractable joystick;
    public bool autoDetectPlayerHand = true;

    sealed class Visibility { public int users; public bool wasEnabled; }
    static readonly Dictionary<Renderer,Visibility> hiddenRenderers = new Dictionary<Renderer,Visibility>();
    readonly Dictionary<IXRSelectInteractor,List<Renderer>> selections = new Dictionary<IXRSelectInteractor,List<Renderer>>();

    void Awake()
    {
        if (!joystick) joystick=GetComponent<XRBaseInteractable>();
        if (fakeGrabHandModel && joystick && fakeGrabHandModel.transform.IsChildOf(joystick.transform))
            for(var t=fakeGrabHandModel.transform;t&&t!=joystick.transform;t=t.parent)
                foreach(var behaviour in t.GetComponents<MonoBehaviour>())
                    if(behaviour is IGrabPoseOverride)fakeGrabHandModel=t.gameObject;
        if(fakeGrabHandModel)fakeGrabHandModel.SetActive(false);
    }
    void OnEnable()
    {
        if(!joystick)return;
        joystick.selectEntered.AddListener(OnGrab);
        joystick.selectExited.AddListener(OnRelease);
    }
    void OnDisable()
    {
        if(joystick){joystick.selectEntered.RemoveListener(OnGrab);joystick.selectExited.RemoveListener(OnRelease);}
        foreach(var renderers in selections.Values)Restore(renderers);
        selections.Clear();ShowFake(false);
    }
    void OnGrab(SelectEnterEventArgs args)
    {
        var hand=args.interactorObject;
        if(selections.ContainsKey(hand))return;
        var visual=autoDetectPlayerHand?ResolveHand(hand):realHandModel;
        if(!visual)visual=realHandModel;
        var renderers=new List<Renderer>();
        if(visual && visual!=fakeGrabHandModel)
        {
            realHandModel=visual;
            foreach(var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if(fakeGrabHandModel && renderer.transform.IsChildOf(fakeGrabHandModel.transform))continue;
                if(!hiddenRenderers.TryGetValue(renderer,out var state))
                {state=new Visibility{wasEnabled=renderer.enabled};hiddenRenderers.Add(renderer,state);}
                state.users++;renderer.enabled=false;renderers.Add(renderer);
            }
        }
        selections.Add(hand,renderers);ShowFake(true);
    }
    void OnRelease(SelectExitEventArgs args)
    {
        if(!selections.TryGetValue(args.interactorObject,out var renderers))return;
        Restore(renderers);selections.Remove(args.interactorObject);
        ShowFake(selections.Count>0);
    }
    static void Restore(List<Renderer> renderers)
    {
        foreach(var renderer in renderers)
        {
            if(!hiddenRenderers.TryGetValue(renderer,out var state))continue;
            if(--state.users>0)continue;
            if(renderer)renderer.enabled=state.wasEnabled;
            hiddenRenderers.Remove(renderer);
        }
    }
    GameObject ResolveHand(IXRSelectInteractor hand)
    {
        var rig=hand.transform.GetComponentInParent<XROrigin>();
        foreach(var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            if(!(behaviour is IPlayerHandVisual visual) || !visual.HandController || !visual.HandVisual)continue;
            // Posed copies on controls implement the same animation interface;
            // they are never the player's actual tracked glove.
            if(behaviour.GetComponentInParent<XRBaseInteractable>() || visual.HandVisual==fakeGrabHandModel)continue;
            bool matches=hand.transform==visual.HandController || hand.transform.IsChildOf(visual.HandController);
            if(!matches && rig && visual.HandController.GetComponentInParent<XROrigin>()==rig)
                matches=(hand.handedness==InteractorHandedness.Left && visual.IsLeftHand) ||
                    (hand.handedness==InteractorHandedness.Right && !visual.IsLeftHand);
            if(matches)return visual.HandVisual;
        }
        // Standard controller-hand fallback, stopping before reaching both hands
        // at the rig root. Hide the mesh/Animator only, never the XR interactor.
        for(var node=hand.transform;node && (!rig || node!=rig.transform);node=node.parent)
        {
            var inputs=node.GetComponentsInChildren<AnimateControllerHand>(true);
            if(inputs.Length>1)break;
            if(inputs.Length==1 && inputs[0].handAnimator)return inputs[0].handAnimator.gameObject;
        }
        return null;
    }
    void ShowFake(bool grabbed)
    {
        if(!fakeGrabHandModel)return;
        // Set the pose override before activation so tracking cannot move the posed hand.
        foreach(var behaviour in fakeGrabHandModel.GetComponentsInChildren<MonoBehaviour>(true))
            if(behaviour is IGrabPoseOverride poseOverride)poseOverride.SetGrabPoseOverride(grabbed);
        fakeGrabHandModel.SetActive(grabbed);
    }
}
