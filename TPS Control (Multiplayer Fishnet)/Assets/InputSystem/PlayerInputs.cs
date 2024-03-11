using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PlayerInputs : MonoBehaviour
{
	[Header("Character Input Values")]
	public Vector2 Move;
	public Vector2 Look;
	public bool Jump;
	public bool IsSprint;
	public bool IsCrouching;
	public bool IsProne;

	[Header("Movement Settings")]
	public bool AnalogMovement;

	[Header("Mouse Cursor Settings")]
	public bool cursorLocked = true;
	public bool mouseInputForLook = true;

	private ThirdPersonController controller;

	void Start()
	{
		controller = GetComponent<ThirdPersonController>();
	}

#if ENABLE_INPUT_SYSTEM
	public void OnMove(InputValue value)
	{
		MoveInput(value.Get<Vector2>());
	}

	public void OnLook(InputValue value)
	{
		if (mouseInputForLook)
		{
			LookInput(value.Get<Vector2>());
		}
	}

	public void OnJump(InputValue value)
	{
		JumpInput(value.isPressed);
	}

	public void OnSprint(InputValue value)
	{
		SprintInput(value.isPressed);
	}
#endif


	public void MoveInput(Vector2 newMoveDirection)
	{
		Move = newMoveDirection;
	}

	public void LookInput(Vector2 newLookDirection)
	{
		Look = newLookDirection;
	}

	public void JumpInput(bool newJumpState)
	{
		Jump = newJumpState;
	}

	public void SprintInput(bool newSprintState)
	{
		IsSprint = newSprintState;
		controller.SetSprintAnimation(IsSprint);
	}

	public void OnCrouch(InputValue value)
	{
		CrouchInput(value.isPressed);
	}
	public void OnProne(InputValue value)
	{
		ProneInput(value.isPressed);
	}


	public void CrouchInput(bool newJumpState)
	{
		if (newJumpState)
		{
			IsCrouching = !IsCrouching;
			IsProne = false;
			controller.Crouch(IsCrouching);
		}
	}

	public void ProneInput(bool newSprintState)
	{
		if (newSprintState)
		{
			IsProne = !IsProne;
			IsCrouching = false;
			controller.Prone(IsProne);
		}
	}


	private void OnApplicationFocus(bool hasFocus)
	{
		SetCursorState(cursorLocked);
	}

	private void SetCursorState(bool newState)
	{
		Cursor.lockState = newState ? CursorLockMode.Locked : CursorLockMode.None;
	}
}
