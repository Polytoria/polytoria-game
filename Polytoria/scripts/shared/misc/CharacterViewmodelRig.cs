using Godot;
using System;

namespace Polytoria.Shared.Misc;

public partial class CharacterViewmodelRig : SkeletonModifier3D
{
	private const int ArmsLayerBit = Polytoria.Datamodel.PolytorianModel.ViewmodelArmsLayerBit;
	private const int BodyLayerBit = Polytoria.Datamodel.PolytorianModel.ViewmodelBodyLayerBit;
	private static readonly Vector3 ArmsOffsetPosition = new(0f, 0.5f, 0.5f);
	private static readonly Vector3 ArmsOffsetRotation = new(-45f, 0f, 0f);
	private static readonly Vector3 BodyOffsetPosition = new(0f, 1f, -0.25f);
	private static readonly Vector3 BodyOffsetRotation = new(0f, 0f, 0f);

	private const float NearClip = 0.01f;
	private const float Fov = 70f;

	public bool IsActive = false;
	public string UpperArmBoneL = "";
	public string UpperArmBoneR = "";
	public string HeadBoneName = "";
	public float CameraPitchDegrees = 0f;

	private SubViewport _armsViewport = null!;
	private SubViewport _bodyViewport = null!;
	private Camera3D _armsCamera = null!;
	private Camera3D _bodyCamera = null!;
	private CanvasLayer _armsCompositeLayer = null!;
	private CanvasLayer _bodyCompositeLayer = null!;
	private Skeleton3D? _skeleton;
	private int _boneIdxL = -1;
	private int _boneIdxR = -1;
	private int _headBoneIdx = -1;

	public override void _Ready()
	{
		_skeleton = GetParent<Skeleton3D>();
		if (_skeleton != null)
		{
			_boneIdxL = _skeleton.FindBone(UpperArmBoneL);
			_boneIdxR = _skeleton.FindBone(UpperArmBoneR);
			_headBoneIdx = _skeleton.FindBone(HeadBoneName);
		}

		(_bodyViewport, _bodyCamera) = BuildViewportCamera(1u << (BodyLayerBit - 1));
		(_armsViewport, _armsCamera) = BuildViewportCamera(1u << (ArmsLayerBit - 1));

		_bodyCompositeLayer = BuildCompositeLayer(_bodyViewport, 5);
		_armsCompositeLayer = BuildCompositeLayer(_armsViewport, 6);
		AddChild(_bodyCompositeLayer);
		AddChild(_armsCompositeLayer);

		GetViewport().SizeChanged += OnMainViewportResized;
	}

	private (SubViewport, Camera3D) BuildViewportCamera(uint cullMask)
	{
		SubViewport viewport = new()
		{
			TransparentBg = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
			World3D = GetViewport().World3D,
			Size = (Vector2I)GetViewport().GetVisibleRect().Size
		};
		AddChild(viewport);

		Camera3D camera = new()
		{
			Fov = Fov,
			Near = NearClip,
			CullMask = cullMask,
			Current = true,
			Environment = viewport.World3D?.Environment
		};
		viewport.AddChild(camera);

		return (viewport, camera);
	}

	private static CanvasLayer BuildCompositeLayer(SubViewport viewport, int layerIndex)
	{
		TextureRect rect = new()
		{
			Texture = viewport.GetTexture(),
			StretchMode = TextureRect.StretchModeEnum.Scale,
			AnchorRight = 1f,
			AnchorBottom = 1f
		};
		CanvasLayer layer = new() { Layer = layerIndex };
		layer.AddChild(rect);
		return layer;
	}

	private void OnMainViewportResized()
	{
		Vector2I size = (Vector2I)GetViewport().GetVisibleRect().Size;
		_armsViewport.Size = size;
		_bodyViewport.Size = size;
	}

	public override void _ProcessModification()
	{
		if (_skeleton == null || _boneIdxL < 0 || _boneIdxR < 0 || !IsActive) return;

		Quaternion worldPitchDelta = new(Vector3.Right, Mathf.DegToRad(-CameraPitchDegrees));
		ApplyPitch(_boneIdxL, worldPitchDelta);
		ApplyPitch(_boneIdxR, worldPitchDelta);
	}

	private void ApplyPitch(int boneIdx, Quaternion worldPitchDelta)
	{
		Basis boneGlobalBasis = _skeleton!.GetBoneGlobalPose(boneIdx).Basis;
		Basis localDelta = boneGlobalBasis.Inverse() * new Basis(worldPitchDelta) * boneGlobalBasis;

		Quaternion currentPose = _skeleton.GetBonePoseRotation(boneIdx);
		_skeleton.SetBonePoseRotation(boneIdx, currentPose * localDelta.GetRotationQuaternion());
	}

	private static Vector3 LocalOffsetToWorld(Transform3D camTransform, Vector3 localOffset)
	{
		Vector3 right = camTransform.Basis.X;
		Vector3 up = camTransform.Basis.Y;
		Vector3 forward = -camTransform.Basis.Z;
		return (right * localOffset.X) + (up * localOffset.Y) + (forward * localOffset.Z);
	}

	private static Transform3D BuildOffsetTransform(Transform3D camTransform, Vector3 headWorldPos, Vector3 positionOffset, Vector3 rotationOffset)
	{
		Basis rotDelta = Basis.FromEuler(new Vector3(
			Mathf.DegToRad(rotationOffset.X),
			Mathf.DegToRad(rotationOffset.Y),
			Mathf.DegToRad(rotationOffset.Z)
		));

		Transform3D result = camTransform;
		result.Basis = camTransform.Basis * rotDelta;
		result.Origin = headWorldPos + LocalOffsetToWorld(camTransform, positionOffset);
		return result;
	}

	public void SyncToCamera(Camera3D mainCam)
	{
		Transform3D camTransform = mainCam.GlobalTransform;

		Vector3 headWorldPos = camTransform.Origin;
		if (_skeleton != null && _headBoneIdx >= 0)
		{
			Transform3D skelGlobal = _skeleton.GlobalTransform;
			headWorldPos = skelGlobal * _skeleton.GetBoneGlobalPose(_headBoneIdx).Origin;
		}

		_armsCamera.GlobalTransform = BuildOffsetTransform(camTransform, headWorldPos, ArmsOffsetPosition, ArmsOffsetRotation);
		_bodyCamera.GlobalTransform = BuildOffsetTransform(camTransform, headWorldPos, BodyOffsetPosition, BodyOffsetRotation);
	}

	public void SetActive(bool active)
	{
		SubViewport.UpdateMode mode = active ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
		_armsViewport.RenderTargetUpdateMode = mode;
		_bodyViewport.RenderTargetUpdateMode = mode;
		_armsCompositeLayer.Visible = active;
		_bodyCompositeLayer.Visible = active;
	}
}
