using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class LockHeight : MonoBehaviour
{
    public float fixedHeight = 0.6f;
    private CharacterController cc;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
    }

    void LateUpdate()
    {
        if (cc.height != fixedHeight)
        {
            cc.height = fixedHeight;
            cc.center = new Vector3(0f, fixedHeight / 2f, 0f);
        }
    }
}