using Cinemachine;
using FishNet.Example.Scened;
using FishNet.Object;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM 
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
[RequireComponent(typeof(PlayerInput))]
#endif
public class ThirdPersonController : NetworkBehaviour
{
    [Header("Player")]
    [Tooltip("Move speed of the character in m/s")]
    public float MoveSpeed = 2.0f;

    [Tooltip("Sprint speed of the character in m/s")]
    public float SprintSpeed = 5.335f;

    [Tooltip("How fast the character turns to face movement direction")]
    [Range(0.0f, 0.3f)]
    public float RotationSmoothTime = 0.12f;

    [Tooltip("Acceleration and deceleration")]
    public float SpeedChangeRate = 10.0f;


    [Space(10)]
    [Tooltip("The height the player can jump")]
    public float JumpHeight = 1.2f;

    [Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
    public float Gravity = -15.0f;

    [Space(10)]
    [Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
    public float JumpTimeout = 0.50f;

    [Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
    public float FallTimeout = 0.15f;

    [Header("Player Grounded")]
    [Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
    public bool Grounded = true;

    [Tooltip("Useful for rough ground")]
    public float GroundedOffset = -0.14f;

    [Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
    public float GroundedRadius = 0.28f;

    [Tooltip("What layers the character uses as ground")]
    public LayerMask GroundLayers;

    [Header("Cinemachine")]
    [Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
    public GameObject CinemachineCameraTarget;

    [Tooltip("How far in degrees can you move the camera up")]
    public float TopClamp = 70.0f;

    [Tooltip("How far in degrees can you move the camera down")]
    public float BottomClamp = -30.0f;

    [Tooltip("Additional degress to override the camera. Useful for fine tuning camera position when locked")]
    public float CameraAngleOverride = 0.0f;

    [Tooltip("For locking the camera position on all axis")]
    public bool LockCameraPosition = false;

    // cinemachine
    private float cinemachineTargetYaw;
    private float cinemachineTargetPitch;

    // player
    private float speed;
    private float animationBlend;
    private float targetMoveRotation = 0.0f;
    private float rotationVelocity;
    private float verticalVelocity;
    private float terminalVelocity = 53.0f;

    private float jumpTimeoutDelta;
    private float fallTimeoutDelta;

#if ENABLE_INPUT_SYSTEM
    private PlayerInput playerInput;
#endif
    private Animator animator;
    private Transform model;
    private CharacterController controller;
    [SerializeField] private PlayerInputs input;
    private GameObject mainCamera;

    private const float threshold = 0.01f;

    private bool hasAnimator;

    public bool IsForceLookByUI;

    private bool IsCurrentDeviceMouse
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return playerInput.currentControlScheme == "KeyboardMouse";
#else
    				return false;
#endif
        }
    }


    private void Awake()
    {
        // get a reference to our main camera
        if (mainCamera == null)
        {
            mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
        }
        controller = GetComponent<CharacterController>();
        model = transform.Find("Model");
        input = GetComponent<PlayerInputs>();
        playerInput = GetComponent<PlayerInput>();
    }

    private void Start()
    {
        cinemachineTargetYaw = CinemachineCameraTarget.transform.rotation.eulerAngles.y;

        hasAnimator = TryGetComponent(out animator);

        // reset our timeouts on start
        jumpTimeoutDelta = JumpTimeout;
        fallTimeoutDelta = FallTimeout;
    }

    private void Update()
    {
        hasAnimator = animator != null;

        JumpAndGravity();
        GroundedCheck();
        Move();
    }

    private void LateUpdate()
    {
        CameraRotation();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (IsOwner)
        {
            var uiInput = FindObjectOfType<UICanvasControllerInput>();
            uiInput.playerInputs = input;

            var cinemachineVirtualCamera = FindObjectOfType<CinemachineVirtualCamera>();
            cinemachineVirtualCamera.Follow = CinemachineCameraTarget.transform;
        }
        else
        {
            // gameObject.GetComponent<PlayerController>().enabled = false;
        }
    }

    private void GroundedCheck()
    {
        // set sphere position, with offset
        Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset,
            transform.position.z);
        Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers,
            QueryTriggerInteraction.Ignore);

        // update animator if using character
        if (hasAnimator)
        {
            animator.SetBool(AnimationType.Grounded.ToString(), Grounded);
        }
    }

    private void CameraRotation()
    {
        // if there is an input and camera position is not fixed
        if (!LockCameraPosition)
        {
            if (input.Look.sqrMagnitude >= threshold)
            {
                float deltaTimeMultiplier = IsCurrentDeviceMouse && !IsForceLookByUI ? 1.0f : Time.deltaTime;

                cinemachineTargetYaw += input.Look.x * deltaTimeMultiplier;
                cinemachineTargetPitch += input.Look.y * deltaTimeMultiplier;
            }
        }

        // clamp our rotations so our values are limited 360 degrees
        cinemachineTargetYaw = ClampAngle(cinemachineTargetYaw, float.MinValue, float.MaxValue);
        cinemachineTargetPitch = ClampAngle(cinemachineTargetPitch, BottomClamp, TopClamp);

        // Cinemachine will follow this target
        CinemachineCameraTarget.transform.rotation = Quaternion.Euler(cinemachineTargetPitch + CameraAngleOverride, cinemachineTargetYaw, 0.0f);
    }

    private void Move()
    {
        float targetSpeed = GetCurrentSpeed();

        if (input.Move == Vector2.zero) targetSpeed = 0.0f;

        float currentHorizontalSpeed = new Vector3(controller.velocity.x, 0.0f, controller.velocity.z).magnitude;

        float speedOffset = 0.1f;
        float inputMagnitude = input.AnalogMovement ? input.Move.magnitude : 1f;

        // accelerate or decelerate to target speed
        if (currentHorizontalSpeed < targetSpeed - speedOffset ||
            currentHorizontalSpeed > targetSpeed + speedOffset)
        {
            speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude,
                Time.deltaTime * SpeedChangeRate);

            // round speed to 3 decimal places
            speed = Mathf.Round(speed * 1000f) / 1000f;
        }
        else
        {
            speed = targetSpeed;
        }

        animationBlend = Mathf.Lerp(animationBlend, targetSpeed, Time.deltaTime * SpeedChangeRate); //TODO
        if (animationBlend < 0.01f) animationBlend = 0f;

        // normalized input direction
        Vector3 inputDirection = new Vector3(input.Move.x, 0.0f, input.Move.y);//.normalized

        // note: Vector2's != operator uses approximation so is not floating point error prone, and is cheaper than magnitude
        // if there is a move input rotate player when the player is moving
        if (input.Move != Vector2.zero)
        {
            targetMoveRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg +
                    mainCamera.transform.eulerAngles.y;

            var modelRotation = mainCamera.transform.eulerAngles.y;
            float rotation = Mathf.SmoothDampAngle(model.transform.eulerAngles.y, modelRotation, ref rotationVelocity, RotationSmoothTime);
            model.transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
        }


        Vector3 targetDirection = Quaternion.Euler(0.0f, targetMoveRotation, 0.0f) * Vector3.forward;

        // move the player
        controller.Move(targetDirection.normalized * (speed * Time.deltaTime) +
                         new Vector3(0.0f, verticalVelocity, 0.0f) * Time.deltaTime);

        // update animator if using character
        if (hasAnimator)
        {
            animator.SetFloat(AnimationType.Speed.ToString(), animationBlend);
            animator.SetFloat(AnimationType.Vertical.ToString(), inputDirection.z);
            animator.SetFloat(AnimationType.Horizontal.ToString(), inputDirection.x);
            animator.SetFloat(AnimationType.MotionSpeed.ToString(), inputMagnitude);
        }
    }

    private float GetCurrentSpeed()
    {
        float targetSpeed;
        // targetSpeed = input.IsSprint ? SprintSpeed : MoveSpeed;
        if (animator.GetBool(AnimationType.Crunch.ToString()))
        {
            targetSpeed = MoveSpeed;
            if (input.IsSprint)
            {
                targetSpeed = MoveSpeed * 2; 
            }
        }
        else if (animator.GetBool(AnimationType.Prone.ToString()))
        {
            targetSpeed = MoveSpeed / 2; 
        }
        else
        {
            targetSpeed = input.IsSprint ? SprintSpeed : MoveSpeed; 
        }

        return targetSpeed;
    }


    private void JumpAndGravity()
    {
        if (Grounded)
        {
            // reset the fall timeout timer
            fallTimeoutDelta = FallTimeout;

            // update animator if using character
            if (hasAnimator)
            {
                animator.SetBool(AnimationType.Jump.ToString(), false);
                animator.SetBool(AnimationType.FreeFall.ToString(), false);
            }

            // stop our velocity dropping infinitely when grounded
            if (verticalVelocity < 0.0f)
            {
                verticalVelocity = -2f;
            }

            // Jump
            if (input.Jump && jumpTimeoutDelta <= 0.0f)
            {
                // the square root of H * -2 * G = how much velocity needed to reach desired height
                verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);

                // update animator if using character
                if (hasAnimator)
                {
                    animator.SetBool(AnimationType.Jump.ToString(), true);
                }
            }

            // jump timeout
            if (jumpTimeoutDelta >= 0.0f)
            {
                jumpTimeoutDelta -= Time.deltaTime;
            }
        }
        else
        {
            // reset the jump timeout timer
            jumpTimeoutDelta = JumpTimeout;

            // fall timeout
            if (fallTimeoutDelta >= 0.0f)
            {
                fallTimeoutDelta -= Time.deltaTime;
            }
            else
            {
                // update animator if using character
                if (hasAnimator)
                {
                    animator.SetBool(AnimationType.FreeFall.ToString(), true);
                }
            }

            // if we are not grounded, do not jump
            input.Jump = false;
        }

        // apply gravity over time if under terminal (multiply by delta time twice to linearly speed up over time)
        if (verticalVelocity < terminalVelocity)
        {
            verticalVelocity += Gravity * Time.deltaTime;
        }
    }

    private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
    {
        if (lfAngle < -360f) lfAngle += 360f;
        if (lfAngle > 360f) lfAngle -= 360f;
        return Mathf.Clamp(lfAngle, lfMin, lfMax);
    }

    public void Crouch(bool isCrouching)
    {
        if (isCrouching)
        {
            controller.center = new Vector3(0, 0.5f, 0);
            controller.height = 1.0f;
        }
        else
        {
            controller.center = new Vector3(0, 1, 0);
            controller.height = 2.0f;
        }
        animator.SetBool(AnimationType.Crunch.ToString(), isCrouching);
        animator.SetBool(AnimationType.Prone.ToString(), false);
    }

    public void Prone(bool isProne)
    {
        if (isProne)
        {
            controller.center = new Vector3(0, 0.25f, 0);
            controller.height = 0.5f;
        }
        else
        {
            controller.center = new Vector3(0, 1, 0);
            controller.height = 2.0f;
        }
        animator.SetBool(AnimationType.Prone.ToString(), isProne);
        animator.SetBool(AnimationType.Crunch.ToString(), false);
    }

    public void SetSprintAnimation(bool run)
    {
        animator.SetBool(AnimationType.Run.ToString(), run);
    }

    private void OnFootstep(AnimationEvent animationEvent)
    {
        //TODO
    }

    private void OnLand(AnimationEvent animationEvent)
    {
        //TODO
    }

}

public enum AnimationType
{
    Speed,
    Vertical,
    Horizontal,
    Run,
    Grounded,
    Jump,
    FreeFall,
    MotionSpeed,
    Crunch,
    Prone
}