using Godot;
using Polytoria.Utils;

namespace Polytoria.Shared.Misc;

public partial class CharacterIKModifier : SkeletonModifier3D
{
	private const float StepSearchRadius = 0.5f;
	private const int StepSearchDirections = 8;
	private const float StationarySpeedThreshold = 0.3f;
	private const float MovingIKWeight = 0.3f;
	private const float TargetSmoothSpeed = 18f;
	private const float WeightSmoothSpeed = 8f;
	private const float StepHeightEpsilon = 0.05f;

	public bool IsActive = false;
	public string UpperLegBoneL = "";
	public string UpperLegBoneR = "";
	public string LowerLegBoneL = "";
	public string LowerLegBoneR = "";
	public float FootOffsetL = -1f;
	public float FootOffsetR = -1f;
	public float CurrentHorizontalSpeed = 0f;
	public Rid SelfExclude;

	private Vector3 _leftFootTarget;
	private Vector3 _rightFootTarget;
	private bool _hasLeftTarget;
	private bool _hasRightTarget;
	private float _currentWeight = 1f;
	private Skeleton3D? _skeleton;
	private int _boneIdxL = -1;
	private int _boneIdxR = -1;
	private int _upperBoneIdxL = -1;
	private int _upperBoneIdxR = -1;
	private float _upperLenL, _lowerLenL, _upperLenR, _lowerLenR;

	public override void _Ready()
	{
		_skeleton = GetParent<Skeleton3D>();
		if (_skeleton == null) return;

		_boneIdxL = _skeleton.FindBone(LowerLegBoneL);
		_boneIdxR = _skeleton.FindBone(LowerLegBoneR);
		_upperBoneIdxL = _skeleton.FindBone(UpperLegBoneL);
		_upperBoneIdxR = _skeleton.FindBone(UpperLegBoneR);

		CacheBoneLength(_upperBoneIdxL, _boneIdxL, out _upperLenL);
		CacheBoneLength(_boneIdxL, _boneIdxL, out _lowerLenL);
		CacheBoneLength(_upperBoneIdxR, _boneIdxR, out _upperLenR);
		CacheBoneLength(_boneIdxR, _boneIdxR, out _lowerLenR);

		if (FootOffsetL >= 0f) _lowerLenL = FootOffsetL;
		if (FootOffsetR >= 0f) _lowerLenR = FootOffsetR;
	}

	private void CacheBoneLength(int fromIdx, int toIdx, out float length)
	{
		length = 0.4f;
		if (_skeleton == null || fromIdx < 0) return;

		if (fromIdx == toIdx)
		{
			int[] children = _skeleton.GetBoneChildren(fromIdx);
			if (children.Length > 0)
			{
				Vector3 a = _skeleton.GetBoneGlobalPose(fromIdx).Origin;
				Vector3 b = _skeleton.GetBoneGlobalPose(children[0]).Origin;
				length = a.DistanceTo(b);
			}
			return;
		}

		Vector3 fromPos = _skeleton.GetBoneGlobalPose(fromIdx).Origin;
		Vector3 toPos = _skeleton.GetBoneGlobalPose(toIdx).Origin;
		length = fromPos.DistanceTo(toPos);
	}

	public override void _ProcessModification()
	{
		if (_skeleton == null) return;

		if (!IsActive)
		{
			_hasLeftTarget = _hasRightTarget = false;
			return;
		}

		float delta = (float)GetProcessDeltaTime();
		float speedBlend = Mathf.Clamp(CurrentHorizontalSpeed / StationarySpeedThreshold, 0f, 1f);
		float targetWeight = Mathf.Lerp(1f, MovingIKWeight, speedBlend);
		_currentWeight = Mathf.Lerp(_currentWeight, targetWeight, MathUtils.ExpDecay(delta, WeightSmoothSpeed));

		Vector3 rawLeft = GetFootTarget(_boneIdxL, _lowerLenL, out bool validL);
		Vector3 rawRight = GetFootTarget(_boneIdxR, _lowerLenR, out bool validR);

		_leftFootTarget = _hasLeftTarget && validL ? _leftFootTarget.Lerp(rawLeft, MathUtils.ExpDecay(delta, TargetSmoothSpeed)) : rawLeft;
		_rightFootTarget = _hasRightTarget && validR ? _rightFootTarget.Lerp(rawRight, MathUtils.ExpDecay(delta, TargetSmoothSpeed)) : rawRight;
		_hasLeftTarget = validL;
		_hasRightTarget = validR;

		if (validL) SolveTwoBoneIK(_upperBoneIdxL, _boneIdxL, _leftFootTarget, _upperLenL, _lowerLenL, _currentWeight);
		if (validR) SolveTwoBoneIK(_upperBoneIdxR, _boneIdxR, _rightFootTarget, _upperLenR, _lowerLenR, _currentWeight);
	}

	private Vector3 GetFootTarget(int lowerIdx, float lowerLen, out bool valid)
	{
		valid = false;
		if (lowerIdx < 0) return Vector3.Zero;

		Transform3D skelGlobal = _skeleton!.GlobalTransform;
		Vector3 bonePos = skelGlobal * _skeleton.GetBoneGlobalPose(lowerIdx).Origin;
		float reach = lowerLen * skelGlobal.Basis.Scale.Y + 0.5f;

		var spaceState = GetWorld3D().DirectSpaceState;
		Godot.Collections.Array<Rid> exclude = SelfExclude.IsValid ? [SelfExclude] : [];

		var straightDown = spaceState.IntersectRay(new PhysicsRayQueryParameters3D
		{
			From = bonePos + Vector3.Up * 0.3f,
			To = bonePos + Vector3.Down * reach,
			CollideWithBodies = true,
			CollideWithAreas = false,
			Exclude = exclude
		});

		Vector3 best = straightDown.Count > 0 ? straightDown["position"].AsVector3() : bonePos;
		bool foundAny = straightDown.Count > 0;

		for (int i = 0; i < StepSearchDirections; i++)
		{
			float angle = (Mathf.Tau / StepSearchDirections) * i;
			Vector3 offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * StepSearchRadius;

			var hit = spaceState.IntersectRay(new PhysicsRayQueryParameters3D
			{
				From = bonePos + offset + Vector3.Up * 0.3f,
				To = bonePos + offset + Vector3.Down * reach,
				CollideWithBodies = true,
				CollideWithAreas = false,
				Exclude = exclude
			});

			if (hit.Count == 0) continue;

			Vector3 hitPos = hit["position"].AsVector3();
			if (!foundAny)
			{
				best = hitPos; foundAny = true;
			}
			else if (hitPos.Y > best.Y + StepHeightEpsilon)
			{
				best = hitPos;
			}
		}

		valid = foundAny;
		return best;
	}

	private void SolveTwoBoneIK(int rootIdx, int midIdx, Vector3 targetWorld, float upperLen, float lowerLen, float weight)
	{
		if (rootIdx < 0 || midIdx < 0 || upperLen <= 0.001f || lowerLen <= 0.001f) return;

		Transform3D rootPose = _skeleton!.GetBoneGlobalPose(rootIdx);
		Transform3D midPose = _skeleton.GetBoneGlobalPose(midIdx);
		Vector3 targetSkel = _skeleton.GlobalTransform.AffineInverse() * targetWorld;
		Vector3 rootPos = rootPose.Origin;
		Vector3 midPos = midPose.Origin;

		float dist = Mathf.Clamp(rootPos.DistanceTo(targetSkel), 0.001f, upperLen + lowerLen - 0.001f);
		float cosRoot = (upperLen * upperLen + dist * dist - lowerLen * lowerLen) / (2f * upperLen * dist);
		float angleRoot = Mathf.Acos(Mathf.Clamp(cosRoot, -1f, 1f));

		Vector3 toTarget = (targetSkel - rootPos).Normalized();
		Vector3 bendAxis = toTarget.Cross(Vector3.Up).Normalized();
		if (bendAxis.LengthSquared() < 0.001f) bendAxis = toTarget.Cross(Vector3.Forward).Normalized();

		Vector3 newMidDir = toTarget.Rotated(bendAxis, angleRoot);
		Vector3 newMidPos = rootPos + newMidDir * upperLen;

		ApplyBoneDelta(rootIdx, rootPose.Basis, (midPos - rootPos).Normalized(), newMidDir, weight);
		ApplyBoneDelta(midIdx, midPose.Basis, (targetSkel - midPos).Normalized(), (targetSkel - newMidPos).Normalized(), weight);
	}

	private void ApplyBoneDelta(int boneIdx, Basis oldGlobalBasis, Vector3 oldDir, Vector3 newDir, float weight)
	{
		if (oldDir.IsEqualApprox(newDir) || weight <= 0.001f) return;

		Basis delta = new Basis(new Quaternion(oldDir, newDir));
		Basis localDelta = oldGlobalBasis.Inverse() * delta * oldGlobalBasis;
		Quaternion currentPose = _skeleton!.GetBonePoseRotation(boneIdx);
		Quaternion solvedPose = (new Basis(currentPose) * localDelta).GetRotationQuaternion();
		_skeleton.SetBonePoseRotation(boneIdx, currentPose.Slerp(solvedPose, Mathf.Clamp(weight, 0f, 1f)));
	}
}
