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
		Vector3 camRotation = Vector3.Zero;
		float forwardInput = 0f;
		bool jump = false;
		bool sprint = false;
		bool camLocked = false;

		if (cam != null && Root.Input.IsGameFocused)
		{
			Vector3 facingRot = cam.Camera3D.GlobalRotation;
			camRotation = facingRot;

			float forwardStrength = Input.GetActionStrength("forward");
			float backwardStrength = Input.GetActionStrength("backward");
			forwardInput = forwardStrength - backwardStrength;

			moveDirection.X = Input.GetActionStrength("rightward") - Input.GetActionStrength("leftward");
			moveDirection.Z = backwardStrength - forwardStrength;
			moveDirection = moveDirection.Rotated(Vector3.Up, facingRot.Y).LimitLength(1);

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
		if (Target == null) return;
		CharacterModel TargetCharacter = Target.Character!;
		if (TargetCharacter == null) return;

		bool isOnFloor = TargetCharacter.CharBody3D.IsOnFloor();
		CharacterModel.CharacterModelStateEnum finalState = CharacterModel.CharacterModelStateEnum.Idle;

		double delta = snapshot.Delta;

		Vector3 externalVelocity = TargetCharacter.ExternalVelocity;
		bool hasExternalVelocity = externalVelocity.X != 0 || externalVelocity.Z != 0;

		if (TargetCharacter.CanMove && !TargetCharacter.IsDead)
		{
			float gdWalkSpeed = TargetCharacter.WalkSpeed;
			bool sprinting = snapshot.Sprint;

			Vector3 moveDirection = snapshot.MoveDirection;
			float forwardInput = snapshot.ForwardInput;

			// Handle jump
			if (snapshot.Jump)
			{
				TargetCharacter.Jump();
			}

			// Sprint/Stamina
			if (sprinting && moveDirection != Vector3.Zero)
			{
				if (TargetCharacter.Stamina > 0 || !TargetCharacter.UseStamina)
				{
					gdWalkSpeed = TargetCharacter.SprintSpeed;
				}
				else
				{
					sprinting = false;
					Target.SprintHoldAgain = true;
				}

				TargetCharacter.RemoveStaminaTick(delta);
			}
			else
			{
				TargetCharacter.AddStaminaTick(delta);
			}

			if (TargetCharacter.IsClimbing)
			{
				// Reset all vectors, lock to Y only
				TargetCharacter.CharacterVelocity.X = 0;
				TargetCharacter.CharacterVelocity.Z = 0;

				float climbSpeed = forwardInput * gdWalkSpeed * TargetCharacter.ClimbingTruss!.ClimbSpeed;

				// Add y velocity
				TargetCharacter.CharacterVelocity.Y = climbSpeed;

				finalState = CharacterModel.CharacterModelStateEnum.Climbing;
				TargetCharacter.SetAnimSpeed(climbSpeed / 8);
			}
			else if (TargetCharacter.JustFinishedClimbing)
			{
				TargetCharacter.JustFinishedClimbing = false;
				TargetCharacter.CharacterVelocity.Y = 0;
			}

			// Always rotate in first person
			if (snapshot.CamLocked)
			{
				TargetCharacter.Rotation = TargetCharacter.Rotation with { Y = 180 + Mathf.RadToDeg(snapshot.CameraRotation.Y) };
			}

			Vector3 pushVelocity = hasExternalVelocity
				? externalVelocity with { Y = 0 }
				: Vector3.Zero;

			if (moveDirection != Vector3.Zero && !TargetCharacter.IsClimbing)
			{
				Target.IsMoving = true;

				TargetCharacter.CharacterVelocity.X = (moveDirection.X * gdWalkSpeed) + pushVelocity.X;
				TargetCharacter.CharacterVelocity.Z = (moveDirection.Z * gdWalkSpeed) + pushVelocity.Z;

				if (!snapshot.CamLocked)
				{
					// Apply rotation by move direction
					TargetCharacter.Rotation = TargetCharacter.Rotation with
					{
						Y = Mathf.RadToDeg(
							Mathf.LerpAngle(
								Mathf.DegToRad(TargetCharacter.Rotation.Y),
								Mathf.Atan2(
									TargetCharacter.CharacterVelocity.X,
									TargetCharacter.CharacterVelocity.Z
								),
								MathUtils.ExpDecay((float)delta, NPC.BodyRotateLerp)
							)
						)
					};
				}


				float animMoveAmount = Mathf.Max(Mathf.Clamp(moveDirection.Length(), 0f, 1f), 0.15f);
				if (sprinting && TargetCharacter.SprintSpeed != TargetCharacter.WalkSpeed)
				{
					finalState = CharacterModel.CharacterModelStateEnum.Running;
					TargetCharacter.SetAnimSpeed(gdWalkSpeed / 20 * animMoveAmount);
				}
				else
				{
					finalState = CharacterModel.CharacterModelStateEnum.Walking;
					TargetCharacter.SetAnimSpeed(gdWalkSpeed / 8 * animMoveAmount);
				}
			}
			else if (!TargetCharacter.IsClimbing)
			{
				Target.IsMoving = false;

				if (hasExternalVelocity)
				{
					TargetCharacter.CharacterVelocity.X = pushVelocity.X;
					TargetCharacter.CharacterVelocity.Z = pushVelocity.Z;
				}
				else
				{
					// Stop horizontal movement when no input
					TargetCharacter.CharacterVelocity.X = Mathf.MoveToward(TargetCharacter.CharacterVelocity.X, 0, gdWalkSpeed);
					TargetCharacter.CharacterVelocity.Z = Mathf.MoveToward(TargetCharacter.CharacterVelocity.Z, 0, gdWalkSpeed);
				}
				TargetCharacter.SetAnimSpeed(1);
			}

			if (!isOnFloor && !TargetCharacter.IsClimbing)
			{
				TargetCharacter.SetAnimSpeed(1);
				finalState = CharacterModel.CharacterModelStateEnum.Jumping;
			}

			// Remove debounce if touched the ground
			if (TargetCharacter.ClimbDebounce && isOnFloor)
			{
				TargetCharacter.ClimbDebounce = false;
			}

			if (TargetCharacter.IsClimbing && isOnFloor)
			{
				TargetCharacter.EndClimb();
			}
		}
		else
		{
			TargetCharacter.CharacterVelocity = new Vector3(0, TargetCharacter.CharacterVelocity.Y, 0);
		}

		TargetCharacter.SetState(finalState);

		if (hasExternalVelocity)
		{
			float decay = TargetCharacter.WalkSpeed * 60f * (float)delta;
			TargetCharacter.ExternalVelocity = new Vector3(
				Mathf.MoveToward(externalVelocity.X, 0, decay),
				externalVelocity.Y,
				Mathf.MoveToward(externalVelocity.Z, 0, decay)
			);
		}

		TargetCharacter.ApplyInternalVelocity(TargetCharacter.CharacterVelocity);
		TargetCharacter.CharBody3D.Velocity = TargetCharacter.CharacterVelocity;
		TargetCharacter.CharBody3D.MoveAndSlide();

		if (isOnFloor && Target.IsMoving && !TargetCharacter.IsClimbing && !TargetCharacter.IsSitting)
		{
			TargetCharacter.TryStepUp();
		}
	}
}
