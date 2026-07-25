using Godot;
using Polytoria.Datamodel;
using Polytoria.Utils;

namespace Polytoria.Providers.PlayerMovement;

public class DefaultMovement : IPlayerMovement
{
	public Player Target { get; set; } = null!;

	public World Root { get; set; } = null!;

	private const float ClimbStuckDelay = 0.15f;
	private const float ClimbStuckPositionEpsilon = 0.02f;
	private bool _wasClimbing = false;
	private Vector3 _lastClimbCheckPosition;
	private float _climbStuckTimer = 0f;

	public InputSnapshot SampleInput(double delta)
	{
		Camera? cam = Root.Environment.CurrentCamera;
		Vector3 moveDirection = Vector3.Zero;
		Vector3 camRotation = Vector3.Zero;
		float forwardInput = 0f;
		bool jump = false;
		bool sprint = false;
		bool camLocked = false;

		if (cam != null && Root.Input.IsGameFocused && Target.CanMove && !Target.IsDead)
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
		bool isOnFloor = Target.CharBody3D.IsOnFloor();
		CharacterModel.CharacterModelStateEnum finalState = CharacterModel.CharacterModelStateEnum.Idle;

		double delta = snapshot.Delta;

		Vector3 externalVelocity = Target.ExternalVelocity;
		bool hasExternalVelocity = externalVelocity.X != 0 || externalVelocity.Z != 0;
		bool shouldDrainStamina = false;

		float gdWalkSpeed = Target.WalkSpeed;
		bool sprinting = false;
		Vector3 moveDirection = Vector3.Zero;
		float forwardInput = 0f;

		if (Target.CanMove && !Target.IsDead)
		{
			gdWalkSpeed = Target.WalkSpeed;
			sprinting = snapshot.Sprint;

			moveDirection = snapshot.MoveDirection;
			forwardInput = snapshot.ForwardInput;

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

				shouldDrainStamina = true;
			}
			else
			{
				Target.AddStaminaTick(delta);
			}

			if (Target.IsClimbing)
			{
				if (!_wasClimbing)
				{
					_lastClimbCheckPosition = Target.GetGlobalPosition();
					_climbStuckTimer = 0f;
				}
				_wasClimbing = true;

				// Reset all vectors, lock to Y only
				Target.CharacterVelocity.X = 0;
				Target.CharacterVelocity.Z = 0;

				float climbSpeed = forwardInput * gdWalkSpeed * Target.ClimbingTruss!.ClimbSpeed;

				// Add y velocity
				Target.CharacterVelocity.Y = climbSpeed;

				finalState = CharacterModel.CharacterModelStateEnum.Climbing;
				Target.Character?.SetAnimSpeed(1);

				// If the player ain't actually moving (stuck), pause the climb animation.
				Vector3 currentPos = Target.GetGlobalPosition();
				if (currentPos.DistanceTo(_lastClimbCheckPosition) < ClimbStuckPositionEpsilon)
				{
					_climbStuckTimer += (float)delta;
				}
				else
				{
					_climbStuckTimer = 0f;
					_lastClimbCheckPosition = currentPos;
				}
				bool stuck = _climbStuckTimer >= ClimbStuckDelay;
				Target.Character?.SetBlendValue(CharacterModel.CharacterModelBlendEnum.ClimbSpeed, stuck ? 0f : climbSpeed / 8);
			}
			else if (Target.JustFinishedClimbing)
			{
				_wasClimbing = false;
				Target.JustFinishedClimbing = false;
			}

			// Always rotate in first person
			if (snapshot.CamLocked)
			{
				Target.Rotation = Target.Rotation with { Y = 180 + Mathf.RadToDeg(snapshot.CameraRotation.Y) };
			}

			Vector3 pushVelocity = hasExternalVelocity
				? externalVelocity with { Y = 0 }
				: Vector3.Zero;

			if (moveDirection != Vector3.Zero && !Target.IsClimbing)
			{
				Target.IsMoving = true;

				Target.CharacterVelocity.X = (moveDirection.X * gdWalkSpeed) + pushVelocity.X;
				Target.CharacterVelocity.Z = (moveDirection.Z * gdWalkSpeed) + pushVelocity.Z;

				Vector3 localMove = moveDirection.Rotated(Vector3.Up, -Mathf.DegToRad(Target.Rotation.Y));
				float strafeX = Mathf.Clamp(localMove.X, -1f, 1f);
				if (forwardInput < 0f) // Flips input so backwardsLeft/backwardsRight displays correctly
				{
					strafeX = -strafeX;
				}
				Target.Character?.SetBlendValue(CharacterModel.CharacterModelBlendEnum.StrafeX, strafeX);
				float strafeY = snapshot.CamLocked ? Mathf.Clamp(forwardInput, -1f, 1f) : 1f;
				Target.Character?.SetBlendValue(CharacterModel.CharacterModelBlendEnum.StrafeY, strafeY);

				if (!snapshot.CamLocked)
				{
					// Apply rotation by move direction
					Target.Rotation = Target.Rotation with
					{
						Y = Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(Target.Rotation.Y), Mathf.Atan2(Target.CharacterVelocity.X, Target.CharacterVelocity.Z), MathUtils.ExpDecay((float)delta, NPC.BodyRotateLerp)))
					};
				}
			}
			else if (!Target.IsClimbing)
			{
				Target.IsMoving = false;
				Target.Character?.SetBlendValue(CharacterModel.CharacterModelBlendEnum.StrafeX, 0f);

				if (hasExternalVelocity)
				{
					Target.CharacterVelocity.X = pushVelocity.X;
					Target.CharacterVelocity.Z = pushVelocity.Z;
				}
				else
				{
					// Stop horizontal movement when no input
					Target.CharacterVelocity.X = Mathf.MoveToward(Target.CharacterVelocity.X, 0, gdWalkSpeed);
					Target.CharacterVelocity.Z = Mathf.MoveToward(Target.CharacterVelocity.Z, 0, gdWalkSpeed);
				}
				Target.Character?.SetAnimSpeed(1);
			}

			if (!isOnFloor && !Target.IsClimbing)
			{
				Target.Character?.SetAnimSpeed(1);
				finalState = CharacterModel.CharacterModelStateEnum.Jumping;
			}

			if (Target.IsClimbing && isOnFloor)
			{
				Target.EndClimb();
			}

			// Handle jump
			if (snapshot.Jump)
			{
				Target.Jump();
			}
		}
		else
		{
			Target.CharacterVelocity = new Vector3(0, Target.CharacterVelocity.Y, 0);
		}

		if (hasExternalVelocity)
		{
			float decay = Target.WalkSpeed * 60f * (float)delta;
			Target.ExternalVelocity = new Vector3(
				Mathf.MoveToward(externalVelocity.X, 0, decay),
				externalVelocity.Y,
				Mathf.MoveToward(externalVelocity.Z, 0, decay)
			);
		}

		Target.ApplyInternalVelocity(Target.CharacterVelocity);
		Target.ConsumeEjectMomentum((float)delta);

		// Split velocity into horizontal/vertical components and merge them
		// after MoveAndSlide, otherwise gravity fights StepUp.
		Vector3 fullVelocity = Target.CharacterVelocity;

		Target.CharBody3D.Velocity = new Vector3(fullVelocity.X, 0f, fullVelocity.Z);
		Target.CharBody3D.MoveAndSlide();

		Vector3 afterHorizontal = Target.CharBody3D.Velocity;

		const float StoppedSpeedThreshold = 0.05f;
		float actualSpeed = new Vector2(afterHorizontal.X, afterHorizontal.Z).Length();
		if (moveDirection != Vector3.Zero && !Target.IsClimbing && isOnFloor && actualSpeed > StoppedSpeedThreshold)
		{
			float animMoveAmount = Mathf.Max(Mathf.Clamp(moveDirection.Length(), 0f, 1f), 0.15f);
			bool suppressRunBackward = snapshot.CamLocked && forwardInput < 0;

			if (sprinting && Target.SprintSpeed != Target.WalkSpeed && !suppressRunBackward)
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

		if (shouldDrainStamina && actualSpeed > StoppedSpeedThreshold)
		{
			Target.RemoveStaminaTick(delta);
		}
		else if (shouldDrainStamina)
		{
			Target.AddStaminaTick(delta);
		}

		Target.Character?.SetState(finalState);

		float snapLength = Target.CharBody3D.FloorSnapLength;
		if (isOnFloor && Target.IsMoving && !Target.IsClimbing && !Target.IsSitting)
		{
			Target.CharBody3D.FloorSnapLength = 0f;
			Target.TryStepUp();
			Target.CharBody3D.FloorSnapLength = snapLength;
		}

		Target.CharBody3D.Velocity = new Vector3(0f, fullVelocity.Y, 0f);
		Target.CharBody3D.MoveAndSlide();

		Target.CharacterVelocity = new Vector3(afterHorizontal.X, Target.CharBody3D.Velocity.Y, afterHorizontal.Z);
		Target.TickPanicFall(isOnFloor, (float)delta);
		Target.TickFootsteps(isOnFloor, (float)delta);
	}
}
