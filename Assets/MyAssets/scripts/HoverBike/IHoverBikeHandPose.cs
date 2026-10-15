using UnityEngine;

// Implemented by the avatar in Assembly-CSharp. MyAssets cannot reference
// Assembly-CSharp directly, so the bike communicates through this contract.
public interface IHoverBikeHandPose
{
    void SetHoverBikeGrip(Transform interactor, Transform grip);
    void ClearHoverBikeGrip(Transform grip);
}

public interface IGrabPoseOverride
{
    void SetGrabPoseOverride(bool grabbed);
}

// Hand visuals can live outside the controller hierarchy (for example full-body gloves).
public interface IPlayerHandVisual
{
    Transform HandController { get; }
    GameObject HandVisual { get; }
    bool IsLeftHand { get; }
}
