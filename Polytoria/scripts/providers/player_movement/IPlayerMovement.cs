using Godot;
using Polytoria.Datamodel;

namespace Polytoria.Providers.PlayerMovement;

public interface IPlayerMovement
{
	Player Target { get; set; }
	World Root { get; set; }
	InputSnapshot SampleInput(double delta);
	void ProcessInput(InputSnapshot snapshot);
}

public struct InputSnapshot
{
	public uint SequenceNumber;
	public double Delta;
	public Vector3 MoveDirection;
	public Quaternion CameraRotation;
	public bool Jump;
	public bool Sprint;
	public float ForwardInput;
	public bool CamLocked;
}
