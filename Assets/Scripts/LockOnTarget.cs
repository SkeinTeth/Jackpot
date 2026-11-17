using UnityEngine;

public class LockOnTarget : MonoBehaviour
{
    [Tooltip("Where the camera and reticle aim. Defaults to this transform. Use an empty child at chest height.")]
    [SerializeField] private Transform aimPoint;

    public Transform AimPoint => aimPoint != null ? aimPoint : transform;

    // Set to false when the enemy dies so lock-on moves on to someone else
    public bool IsTargetable { get; set; } = true;
}