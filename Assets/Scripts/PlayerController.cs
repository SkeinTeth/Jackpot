using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference jumpAction;
    [SerializeField] private InputActionReference dashAction;

    [Header("References")]
    [SerializeField] private LockOnSystem lockOn;

    [Header("Movement")]
    [SerializeField] private float maxSpeed = 8f;
    [SerializeField] private float acceleration = 60f;
    [SerializeField] private float deceleration = 90f;
    [SerializeField, Range(0f, 1f)] private float airControl = 0.6f;
    [SerializeField] private float turnSpeed = 900f; // degrees per second

    [Header("Gravity")]
    [SerializeField] private float gravity = -30f;
    [SerializeField] private float maxFallSpeed = 30f;
    [SerializeField] private float groundedStickForce = -2f;

    [Header("Jump")]
    [SerializeField] private float jumpHeight = 2.2f;
    [SerializeField] private int maxAirJumps = 1;
    [SerializeField] private float coyoteTime = 0.1f;
    [SerializeField] private float jumpBufferTime = 0.12f;
    [SerializeField, Range(0f, 1f)] private float jumpCutMultiplier = 0.5f;

    [Header("Dash")]
    [SerializeField] private float dashDistance = 6f;
    [SerializeField] private float airDashDistance = 5f;
    [SerializeField] private float dashDuration = 0.18f;
    [SerializeField] private float dashCooldown = 0.2f;
    [SerializeField] private float dashInvincibleTime = 0.15f;
    [SerializeField] private int maxAirDashes = 1;

    // Public state (other systems like Health and Attack will read these later)
    public bool IsGrounded { get; private set; }
    public bool IsDashing { get; private set; }
    public bool IsInvincible => Time.time < invincibleUntil;
    public Vector3 Velocity => horizontalVelocity + Vector3.up * verticalVelocity;

    private CharacterController controller;
    private Transform cam;

    private Vector3 horizontalVelocity;
    private float verticalVelocity;

    private float lastGroundedTime = float.NegativeInfinity;
    private float lastJumpPressedTime = float.NegativeInfinity;
    private int airJumpsUsed;

    private Vector3 dashDirection;
    private float dashSpeed;
    private float dashEndTime;
    private float nextDashTime;
    private bool dashIsAerial;
    private int airDashesUsed;
    private float invincibleUntil;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        cam = Camera.main.transform;
    }

    private void OnEnable()
    {
        moveAction.action.Enable();
        jumpAction.action.Enable();
        dashAction.action.Enable();
    }

    private void OnDisable()
    {
        moveAction.action.Disable();
        jumpAction.action.Disable();
        dashAction.action.Disable();
    }

    private void Update()
    {
        // Ground state
        IsGrounded = controller.isGrounded;
        if (IsGrounded)
        {
            lastGroundedTime = Time.time;
            airJumpsUsed = 0;
            airDashesUsed = 0;
        }

        // Input
        Vector2 input = moveAction.action.ReadValue<Vector2>();
        Vector3 moveDir = GetCameraRelativeDirection(input);

        if (jumpAction.action.WasPressedThisFrame())
            lastJumpPressedTime = Time.time;

        // Dash
        if (IsDashing && Time.time >= dashEndTime)
            EndDash();

        if (!IsDashing && Time.time >= nextDashTime && dashAction.action.WasPressedThisFrame())
            StartDash(moveDir);

        // Jump
        HandleJump();

        // Horizontal velocity
        if (IsDashing)
        {
            horizontalVelocity = dashDirection * dashSpeed;
        }
        else
        {
            Vector3 targetVelocity = moveDir * maxSpeed;
            float rate = moveDir.sqrMagnitude > 0.01f ? acceleration : deceleration;
            if (!IsGrounded) rate *= airControl;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, rate * Time.deltaTime);
        }

        // Vertical velocity (air dash hovers)
        if (IsDashing && dashIsAerial)
        {
            verticalVelocity = 0f;
        }
        else
        {
            if (IsGrounded && verticalVelocity < 0f)
                verticalVelocity = groundedStickForce;
            verticalVelocity = Mathf.Max(verticalVelocity + gravity * Time.deltaTime, -maxFallSpeed);
        }

        // One Move call per frame
        controller.Move(Velocity * Time.deltaTime);

        // Rotation
        if (lockOn != null && lockOn.HasTarget)
        {
            Vector3 toTarget = lockOn.TargetPosition - transform.position;
            toTarget.y = 0f;
            RotateToward(toTarget.normalized);
        }
        else if (IsDashing)
        {
            RotateToward(dashDirection);
        }
        else
        {
            RotateToward(moveDir);
        }
    }

    private void HandleJump()
    {
        bool buffered = Time.time - lastJumpPressedTime <= jumpBufferTime;
        if (buffered)
        {
            bool canGroundJump = Time.time - lastGroundedTime <= coyoteTime;
            if (canGroundJump)
            {
                Jump();
                lastGroundedTime = float.NegativeInfinity; // consume coyote time
            }
            else if (airJumpsUsed < maxAirJumps)
            {
                Jump();
                airJumpsUsed++;
            }
        }

        // Variable jump height: release early to cut the jump short
        if (jumpAction.action.WasReleasedThisFrame() && verticalVelocity > 0f)
            verticalVelocity *= jumpCutMultiplier;
    }

    private void Jump()
    {
        verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        lastJumpPressedTime = float.NegativeInfinity; // consume buffer
        if (IsDashing) EndDash(); // jump cancels dash
    }

    private void StartDash(Vector3 moveDir)
    {
        bool aerial = !IsGrounded;
        if (aerial && airDashesUsed >= maxAirDashes) return;

        Vector3 dir;
        if (moveDir.sqrMagnitude > 0.01f)
            dir = moveDir.normalized;
        else if (lockOn != null && lockOn.HasTarget)
            dir = -transform.forward; // backstep away from the target
        else
            dir = transform.forward;

        dir.y = 0f;
        dashDirection = dir.normalized;
        dashSpeed = (aerial ? airDashDistance : dashDistance) / dashDuration;
        dashEndTime = Time.time + dashDuration;
        dashIsAerial = aerial;
        IsDashing = true;
        invincibleUntil = Time.time + dashInvincibleTime;

        if (aerial)
        {
            airDashesUsed++;
            verticalVelocity = 0f;
        }
    }

    private void EndDash()
    {
        IsDashing = false;
        nextDashTime = Time.time + dashCooldown;
        horizontalVelocity = Vector3.ClampMagnitude(horizontalVelocity, maxSpeed);
    }

    private Vector3 GetCameraRelativeDirection(Vector2 input)
    {
        Vector3 forward = cam.forward;
        Vector3 right = cam.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        return Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);
    }

    private void RotateToward(Vector3 dir)
    {
        if (dir.sqrMagnitude < 0.01f) return;
        Quaternion target = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
    }
}