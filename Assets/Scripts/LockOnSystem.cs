using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public class LockOnSystem : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputActionReference lockAction;      // Button
    [SerializeField] private InputActionReference switchAction;    // Vector2 (right stick / Q,E composite)

    [Header("Targeting")]
    [SerializeField] private LayerMask enemyLayers;
    [SerializeField] private LayerMask obstacleMask;               // walls/environment only, NOT enemies or player
    [SerializeField] private float maxRange = 20f;
    [SerializeField] private float breakRange = 25f;
    [SerializeField] private float maxAngle = 60f;                 // from camera forward
    [SerializeField] private float distanceWeight = 1f;            // degrees of score per metre
    [SerializeField] private float eyeHeight = 1.2f;

    [Header("Switching")]
    [SerializeField] private float switchThreshold = 0.7f;
    [SerializeField] private float switchCooldown = 0.3f;

    [Header("Camera & UI")]
    [SerializeField] private CinemachineCamera lockCamera;         // starts disabled, higher priority than the normal camera
    [SerializeField] private Transform reticle;                    // optional world-space marker

    public bool HasTarget => target != null;
    public Vector3 TargetPosition => target.AimPoint.position;
    public LockOnTarget Target => target;
    public event System.Action<LockOnTarget> OnTargetChanged;

    private LockOnTarget target;
    private bool wasLocked;
    private bool flickHeld;
    private float nextSwitchTime;
    private Transform cam;
    private readonly List<LockOnTarget> candidates = new List<LockOnTarget>();

    private void Awake()
    {
        cam = Camera.main.transform;
        if (lockCamera != null) lockCamera.gameObject.SetActive(false);
        if (reticle != null) reticle.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        lockAction.action.Enable();
        switchAction.action.Enable();
    }

    private void OnDisable()
    {
        lockAction.action.Disable();
        switchAction.action.Disable();
    }

    private void Update()
    {
        if (lockAction.action.WasPressedThisFrame())
            SetTarget(target != null ? null : FindBest());

        // Target was destroyed
        if (wasLocked && target == null)
        {
            SetTarget(null);
            return;
        }

        if (target == null) return;

        // Target died: auto-pick the next best one (or release if none)
        if (!target.IsTargetable)
        {
            SetTarget(FindBest());
            if (target == null) return;
        }
        else if (Vector3.Distance(transform.position, target.AimPoint.position) > breakRange)
        {
            SetTarget(null);
            return;
        }

        HandleSwitchInput();
    }

    private void LateUpdate()
    {
        if (target != null && reticle != null)
        {
            reticle.position = target.AimPoint.position;
            reticle.rotation = cam.rotation;
        }
    }

    private void HandleSwitchInput()
    {
        Vector2 look = switchAction.action.ReadValue<Vector2>();
        bool flick = Mathf.Abs(look.x) > switchThreshold;

        if (flick && !flickHeld && Time.time >= nextSwitchTime)
        {
            TrySwitch(Mathf.Sign(look.x));
            nextSwitchTime = Time.time + switchCooldown;
        }
        flickHeld = flick;
    }

    private void TrySwitch(float direction)
    {
        GatherCandidates();

        float currentX = cam.InverseTransformPoint(target.AimPoint.position).x;
        LockOnTarget best = null;
        float bestDelta = float.MaxValue;

        foreach (var t in candidates)
        {
            if (t == target) continue;
            float delta = (cam.InverseTransformPoint(t.AimPoint.position).x - currentX) * direction;
            if (delta > 0.1f && delta < bestDelta)
            {
                bestDelta = delta;
                best = t;
            }
        }

        if (best != null) SetTarget(best);
    }

    private LockOnTarget FindBest()
    {
        GatherCandidates();

        LockOnTarget best = null;
        float bestScore = float.MaxValue;

        foreach (var t in candidates)
        {
            float angle = Vector3.Angle(cam.forward, t.AimPoint.position - cam.position);
            float dist = Vector3.Distance(transform.position, t.AimPoint.position);
            float score = angle + dist * distanceWeight;
            if (score < bestScore)
            {
                bestScore = score;
                best = t;
            }
        }
        return best;
    }

    private void GatherCandidates()
    {
        candidates.Clear();
        Collider[] hits = Physics.OverlapSphere(transform.position, maxRange, enemyLayers, QueryTriggerInteraction.Ignore);

        foreach (var hit in hits)
        {
            var t = hit.GetComponentInParent<LockOnTarget>();
            if (t == null || !t.IsTargetable || candidates.Contains(t)) continue;

            Vector3 toTarget = t.AimPoint.position - cam.position;
            if (Vector3.Angle(cam.forward, toTarget) > maxAngle) continue;
            if (!HasLineOfSight(t)) continue;

            candidates.Add(t);
        }
    }

    private bool HasLineOfSight(LockOnTarget t)
    {
        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        return !Physics.Linecast(origin, t.AimPoint.position, obstacleMask, QueryTriggerInteraction.Ignore);
    }

    private void SetTarget(LockOnTarget newTarget)
    {
        target = newTarget;
        wasLocked = newTarget != null;

        if (lockCamera != null)
        {
            lockCamera.gameObject.SetActive(newTarget != null);
            if (newTarget != null) lockCamera.LookAt = newTarget.AimPoint;
        }

        if (reticle != null)
            reticle.gameObject.SetActive(newTarget != null);

        OnTargetChanged?.Invoke(newTarget);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, maxRange);
        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawWireSphere(transform.position, breakRange);

        if (target != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position + Vector3.up * eyeHeight, target.AimPoint.position);
            Gizmos.DrawWireSphere(target.AimPoint.position, 0.3f);
        }
    }
}