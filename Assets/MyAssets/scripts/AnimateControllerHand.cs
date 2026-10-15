using UnityEngine;
using UnityEngine.InputSystem;

public class AnimateControllerHand : MonoBehaviour
{
    
    public InputActionProperty triggerValue;
    public InputActionProperty gripValue;

    public Animator handAnimator;

    void Update()
    {
        if (handAnimator == null) return;

        float trigger = triggerValue.action.ReadValue<float>();
        float grip = gripValue.action.ReadValue<float>();
        
        handAnimator.SetFloat("Trigger", trigger);
        handAnimator.SetFloat("Grip", grip);
        
    }
}
