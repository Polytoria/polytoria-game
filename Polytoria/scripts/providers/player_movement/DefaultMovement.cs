using Godot;
using Polytoria.Datamodel;
using Polytoria.Utils;

namespace Polytoria.Providers.PlayerMovement;

public class DefaultMovement : IPlayerMovement
{
	public Player Target { get; set; } = null!;

	public World Root { get; set; } = null!;

	public InputSnapshot SampleInput(double delta)
	{
		Camera? cam = Root.Environment.CurrentCamera;
		Vector3 moveDirection = Vector3.Zero;
		Quaternion camRotation = Quaternion.Identity;
		float forwardInput = 0f;
		bool jump = false;
		bool sprint = false;
		bool camLocked = false;

		if (cam != null && Root.Input.IsGameFocused && Target.CanMove && !Target.IsDead)
		{
			Basis facingRot = cam.Camera3D.GlobalBasis;
			camRotation = facingRot.Orthonormalized().GetRotationQuaternion();

			float forwardStrength = Input.GetActionStrength("forward");
			float backwardStrength = Input.GetActionStrength("backward");
			forwardInput = forwardStrength - backwardStrength;

			Quaternion verticalize = new(facingRot.Y, Target.Vertical);
			moveDirection = verticalize * ((facingRot.Z * -forwardInput) + (facingRot.X * (Input.GetActionStrength("rightward") - Input.GetActionStrength("leftward")))).LimitLength(1);

			bool initialSprintOverride = Target.SprintOverride;
			jump = Input.IsActionPressed("jump");
			sprint = Input.IsActionPressed("sprint") || initialSprintOverride;

			if (Target.SprintHoldAgain)
			{
				sprint = Target.SprintOverride = false;
				if (Input.IsActionJustReleased("sprint") || initialSprintOverride)
				{
					Target.SprintHoldAgain = false;
				}
			}

			switch (Target.RotationMode)
			{
				case Player.PlayerRotationModeEnum.Automatic:
					camLocked = cam.IsFirstPerson || cam.CtrlLocked;
					break;
				case Player.PlayerRotationModeEnum.CameraLocked:
					camLocked = true;
					break;
				case Player.PlayerRotationModeEnum.Movement:
					camLocked = false;
					break;
				case Player.PlayerRotationModeEnum.MovementCtrlLockOnly:
					camLocked = cam.IsFirstPerson;
					break;
			}
		}

		return new()
		{
			Delta = delta,
			MoveDirection = moveDirection,
			Jump = jump,
			Sprint = sprint,
			ForwardInput = forwardInput,
			CameraRotation = camRotation,
			CamLocked = camLocked
		};
	}

	public void ProcessInput(InputSnapshot snapshot)
	{
		bool isOnFloor = Target.CharBody3D.IsOnFloor();
		CharacterModel.CharacterModelStateEnum finalState = CharacterModel.CharacterModelStateEnum.Idle;

		double delta = snapshot.Delta;

		Vector3 vertical = Target.Vertical;

		Vector3 externalVelocity = Target.ExternalVelocity;
		bool hasExternalVelocity = externalVelocity.Slide(vertical) != Vector3.Zero;

		if (Target.CanMove && !Target.IsDead)
		{
			float gdWalkSpeed = Target.WalkSpeed;
			bool sprinting = snapshot.Sprint;

			Vector3 moveDirection = snapshot.MoveDirection;
			float forwardInput = snapshot.ForwardInput;

			// Handle jump
			if (snapshot.Jump)
			{
				Target.Jump();
			}

			// Sprint/Stamina
			if (sprinting && moveDirection != Vector3.Zero)
			{
				if (Target.Stamina > 0 || !Target.UseStamina)
				{
					gdWalkSpeed = Target.SprintSpeed;
				}
				else
				{
					sprinting = false;
					Target.SprintHoldAgain = true;
				}

				Target.RemoveStaminaTick(delta);
			}
			else
			{
				Target.AddStaminaTick(delta);
			}

			if (Target.IsClimbing)
			{
				float climbSpeed = forwardInput * gdWalkSpeed * Target.ClimbingTruss!.ClimbSpeed;

				// Lock to vertical only and add vertical velocity
				Target.CharacterVelocity = vertical * climbSpeed;

				finalState = CharacterModel.CharacterModelStateEnum.Climbing;
				Target.Character?.SetAnimSpeed(climbSpeed / 8);
			}
			else if (Target.JustFinishedClimbing)
			{
				Target.JustFinishedClimbing = false;
				Target.CharacterVelocity = Target.CharacterVelocity.Slide(vertical);
			}

			// Always rotate in first person
			if (snapshot.CamLocked)
			{
				Target.Quaternion = new Quaternion(vertical, Mathf.Pi) * (new Quaternion(snapshot.CameraRotation * Vector3.Up, vertical) * snapshot.CameraRotation);
			}

			Vector3 pushVelocity = hasExternalVelocity
				? externalVelocity.Slide(vertical)
				: Vector3.Zero;

			if (moveDirection != Vector3.Zero && !Target.IsClimbing)
			{
				Target.IsMoving = true;

				Target.CharacterVelocity = moveDirection * gdWalkSpeed + pushVelocity + Target.CharacterVelocity.Project(vertical);

				if (!snapshot.CamLocked)
				{
					// Apply rotation by move direction
					Vector3 a = new Quaternion(Target.Up, vertical) * Target.Forward;
					Vector3 b = Target.CharacterVelocity.Slide(vertical).Normalized();
					float angle = Mathf.Asin(a.Cross(b).Dot(vertical));
					if (a.Dot(b) < 0) angle = Mathf.Pi - angle;
					if (angle > Mathf.Pi) angle -= Mathf.Tau;
					Target.Quaternion = new Quaternion(vertical, angle * MathUtils.ExpDecay((float)delta, NPC.BodyRotateLerp)) * Target.Quaternion;
				}


				float animMoveAmount = Mathf.Max(Mathf.Clamp(moveDirection.Length(), 0f, 1f), 0.15f);
				if (sprinting && Target.SprintSpeed != Target.WalkSpeed)
				{
					finalState = CharacterModel.CharacterModelStateEnum.Running;
					Target.Character?.SetAnimSpeed(gdWalkSpeed / 20 * animMoveAmount);
				}
				else
				{
					finalState = CharacterModel.CharacterModelStateEnum.Walking;
					Target.Character?.SetAnimSpeed(gdWalkSpeed / 8 * animMoveAmount);
				}
			}
			else if (!Target.IsClimbing)
			{
				Target.IsMoving = false;

				if (hasExternalVelocity)
				{
					Target.CharacterVelocity = pushVelocity + Target.CharacterVelocity.Project(vertical);
				}
				else
				{
					// Stop horizontal movement when no input
					Target.CharacterVelocity = Target.CharacterVelocity.Slide(vertical).MoveToward(Vector3.Zero, gdWalkSpeed) + Target.CharacterVelocity.Project(vertical);
				}
				Target.Character?.SetAnimSpeed(1);
			}

			if (!isOnFloor && !Target.IsClimbing)
			{
				Target.Character?.SetAnimSpeed(1);
				finalState = CharacterModel.CharacterModelStateEnum.Jumping;
			}

			// Remove debounce if touched the ground
			if (Target.ClimbDebounce && isOnFloor)
			{
				Target.ClimbDebounce = false;
			}

			if (Target.IsClimbing && isOnFloor)
			{
				Target.EndClimb();
			}
		}
		else
		{
			Target.CharacterVelocity = Target.CharacterVelocity.Project(vertical);
		}

		Target.Character?.SetState(finalState);

		if (hasExternalVelocity)
		{
			float decay = Target.WalkSpeed * 60f * (float)delta;
			Target.ExternalVelocity = externalVelocity.Slide(vertical).MoveToward(Vector3.Zero, decay) + externalVelocity.Project(vertical);
		}

		Target.ApplyInternalVelocity(Target.CharacterVelocity);
		Target.CharBody3D.Velocity = Target.CharacterVelocity;
		Target.CharBody3D.MoveAndSlide();

		if (isOnFloor && Target.IsMoving && !Target.IsClimbing && !Target.IsSitting)
		{
			Target.TryStepUp();
		}
	}
}
