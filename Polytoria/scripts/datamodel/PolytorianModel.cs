// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;
using Polytoria.Networking;
using Polytoria.Schemas.API;
using Polytoria.Scripting;
using Polytoria.Shared;
using Polytoria.Shared.Misc;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class PolytorianModel : CharacterModel
{
	private const double NetLookBlendUpdateInterval = 0.1;
	private double _lastNetUpdateTime = 0.0;

	private static readonly BoxShape3D _collisionBox = new() { Size = new(2f, 5.8f, 1f) };
	internal Node3D? CollisionPivot;
	internal CollisionShape3D? CollisionShape;
	private Physical? _oldPhyParent;

	internal MeshInstance3D HeadMeshInstance = null!;
	internal MeshInstance3D TorsoMeshInstance = null!;
	internal MeshInstance3D LeftArmMeshInstance = null!;
	internal MeshInstance3D RightArmMeshInstance = null!;
	internal MeshInstance3D LeftLegMeshInstance = null!;
	internal MeshInstance3D RightLegMeshInstance = null!;
	internal Node3D Pivot = null!;

	private const float BlendSpeed = 5f;
	private const float LookBlendSpeed = 15f;
	private const string DefaultBodyColor = "#FFFFFF";

	private int _loadAppearanceCount = 0;

	internal Skeleton3D Skeleton = null!;
	internal AnimationTree AnimTree = null!;

	private ImageAsset? _faceImage;
	private MeshAsset? _bodyMesh;
	private readonly StandardMaterial3D _headMat = new();
	private readonly StandardMaterial3D _faceMat = new();
	private readonly StandardMaterial3D _torsoMat = new();
	private readonly StandardMaterial3D _leftArmMat = new();
	private readonly StandardMaterial3D _rightArmMat = new();
	private readonly StandardMaterial3D _leftLegMat = new();
	private readonly StandardMaterial3D _rightLegMat = new();
	private readonly StandardMaterial3D[] _shirtMats = new StandardMaterial3D[3];
	private readonly StandardMaterial3D[] _pantsMats = new StandardMaterial3D[2];
	private PhysicalBoneSimulator3D _ragdollBoneSim = null!;
	private PhysicalBoneSimulator3D? _lastPhysicalBoneSim = null!;
	private readonly Dictionary<string, float> _blendTargets = [];
	private int _toBeLoadedCount = 0;
	private bool _faceLoaded = false;
	private float _lastLookBlendX = 0;
	private float _lastLookBlendY = 0;
	private bool _faceOverrided = false;
	private bool _bodyOverrided = false;
	private CharacterAnimHelper _helper = null!;
	private readonly Dictionary<CharacterAttachmentEnum, Dynamic> _attachmentEnumToDyn = [];
	private PackedScene? _bodyPkScene;
	private bool _updateClothDirty = false;

	public PhysicalBone3D? VelocityPhysicalBone;

	[Editable, ScriptProperty, Export, SyncVar]
	public Color HeadColor
	{
		get => _headMat.AlbedoColor;
		set
		{
			_headMat.AlbedoColor = value;
			_faceMat.AlbedoColor = new(1, 1, 1, value.A);
			MatApplyAlpha(_headMat, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Color TorsoColor
	{
		get => _torsoMat.AlbedoColor;
		set
		{
			_torsoMat.AlbedoColor = value;
			_shirtMats[1].AlbedoColor = new(1, 1, 1, value.A);
			MatApplyAlpha(_torsoMat, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Color LeftArmColor
	{
		get => _leftArmMat.AlbedoColor;
		set
		{
			_leftArmMat.AlbedoColor = value;
			_shirtMats[0].AlbedoColor = new(1, 1, 1, value.A);
			MatApplyAlpha(_leftArmMat, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Color RightArmColor
	{
		get => _rightArmMat.AlbedoColor;
		set
		{
			_rightArmMat.AlbedoColor = value;
			_shirtMats[2].AlbedoColor = new(1, 1, 1, value.A);
			MatApplyAlpha(_rightArmMat, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Color LeftLegColor
	{
		get => _leftLegMat.AlbedoColor;
		set
		{
			_leftLegMat.AlbedoColor = value;
			_pantsMats[0].AlbedoColor = new(1, 1, 1, value.A);
			MatApplyAlpha(_leftLegMat, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Color RightLegColor
	{
		get => _rightLegMat.AlbedoColor;
		set
		{
			_rightLegMat.AlbedoColor = value;
			_pantsMats[1].AlbedoColor = new(1, 1, 1, value.A);
			MatApplyAlpha(_rightLegMat, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use FaceImage instead"), CloneIgnore]
	public int FaceID
	{
		get => (int)((_faceImage is PTImageAsset polyImg) ? polyImg.ImageID : 0);
		set
		{
			if (value == 0) { FaceImage = null; return; }
			PTImageAsset imgAsset = new();
			FaceImage = imgAsset;
			imgAsset.ImageID = (uint)value;
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public ImageAsset? FaceImage
	{
		get => _faceImage;
		set
		{
			if (_faceImage != null && _faceImage != value)
			{
				_faceImage.ResourceLoaded -= OnFaceLoaded;
				_faceImage.UnlinkFrom(this);
			}
			_faceImage = value;

			_faceMat.AlbedoTexture = null;
			if (_faceImage != null)
			{
				_faceOverrided = true;
				_faceLoaded = false;
				AddLoadCount();
				_faceImage.LinkTo(this);
				_faceImage.ResourceLoaded += OnFaceLoaded;

				if (_faceImage.IsResourceLoaded && _faceImage.Resource != null)
				{
					OnFaceLoaded(_faceImage.Resource);
				}
				else
				{
					_faceImage.QueueLoadResource();
				}
			}
			else
			{
				// Set to default face
				_faceMat.AlbedoTexture = GD.Load<Texture2D>("res://assets/textures/client/character/DefaultFace.png");
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public MeshAsset? BodyMesh
	{
		get => _bodyMesh;
		set
		{
			if (_bodyMesh != null && _bodyMesh != value)
			{
				_bodyMesh.ResourceLoaded -= OnBodyLoaded;
				_bodyMesh.UnlinkFrom(this);
			}
			OnBodyLoaded(null);
			_bodyMesh = value;
			if (_bodyMesh != null)
			{
				AddLoadCount();
				_bodyOverrided = true;
				_bodyMesh.LinkTo(this);
				_bodyMesh.ResourceLoaded += OnBodyLoaded;
				if (_bodyMesh.IsResourceLoaded && _bodyMesh.Resource != null)
				{
					OnBodyLoaded(_bodyMesh.Resource);
				}
				else
				{
					_bodyMesh.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}

	[ScriptProperty] public bool Ragdolling { get; private set; } = false;
	[ScriptProperty] public Vector3 RagdollPosition => VelocityPhysicalBone == null ? Vector3.Zero : VelocityPhysicalBone.GlobalPosition;
	[ScriptProperty] public Vector3 RagdollRotation => VelocityPhysicalBone == null ? Vector3.Zero : VelocityPhysicalBone.GlobalRotationDegrees.FlipEuler();

	// These two's not reliable yet, as it doesn't wait for mesh to load. TODO: Come back and fix
	public bool IsAvatarLoaded { get; private set; } = false;
	public event Action? AvatarLoaded;

	[ScriptProperty] public PTSignal RagdollStarted { get; private set; } = new();
	[ScriptProperty] public PTSignal RagdollStopped { get; private set; } = new();

	public override void Init()
	{
		FaceImage = null;
		_headMat.NextPass = _faceMat;

		_shirtMats[0] = new() { Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, RenderPriority = 1 };
		_shirtMats[1] = new() { Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, RenderPriority = 1 };
		_shirtMats[2] = new() { Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, RenderPriority = 1 };

		_pantsMats[0] = new() { Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, RenderPriority = 1 };
		_pantsMats[1] = new() { Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, RenderPriority = 1 };

		_faceMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;

		_helper = new() { Name = "CharacterHelper", Target = this };
		Globals.Singleton.AddChild(_helper, true);

		Skeleton = GDNode.GetNode<Skeleton3D>("Character/Poly/Skeleton3D");
		Skeleton.ShowRestOnly = false;
		_ragdollBoneSim = GDNode.GetNode<PhysicalBoneSimulator3D>("Character/Poly/Skeleton3D/RagdollBone");
		HeadMeshInstance = GDNode.GetNode<MeshInstance3D>("Character/Poly/Skeleton3D/Head");
		TorsoMeshInstance = GDNode.GetNode<MeshInstance3D>("Character/Poly/Skeleton3D/Torso");
		LeftArmMeshInstance = GDNode.GetNode<MeshInstance3D>("Character/Poly/Skeleton3D/LeftArm");
		RightArmMeshInstance = GDNode.GetNode<MeshInstance3D>("Character/Poly/Skeleton3D/RightArm");
		LeftLegMeshInstance = GDNode.GetNode<MeshInstance3D>("Character/Poly/Skeleton3D/LeftLeg");
		RightLegMeshInstance = GDNode.GetNode<MeshInstance3D>("Character/Poly/Skeleton3D/RightLeg");
		Pivot = GDNode.GetNode<Node3D>("Character/Poly");

		Pivot.Scale = NodeSize;

		HeadMeshInstance.MaterialOverride = _headMat;
		TorsoMeshInstance.MaterialOverride = _torsoMat;
		LeftArmMeshInstance.MaterialOverride = _leftArmMat;
		RightArmMeshInstance.MaterialOverride = _rightArmMat;
		LeftLegMeshInstance.MaterialOverride = _leftLegMat;
		RightLegMeshInstance.MaterialOverride = _rightLegMat;

		AnimTree = GDNode.GetNode<AnimationTree>("AnimationTree");
		AnimTree.Active = true;

		base.Init();
		SetProcess(true);
	}

	public override void PreDelete()
	{
		// Free helper
		_helper?.QueueFree();

		// Free body part materials
		_headMat.Dispose();
		_faceMat.Dispose();
		_torsoMat.Dispose();
		_leftArmMat.Dispose();
		_rightArmMat.Dispose();
		_leftLegMat.Dispose();
		_rightLegMat.Dispose();

		// Free materials
		_shirtMats[0].Dispose();
		_shirtMats[1].Dispose();
		_shirtMats[2].Dispose();
		_pantsMats[0].Dispose();
		_pantsMats[1].Dispose();

		base.PreDelete();
	}

	public override Node CreateGDNode()
	{
		return Globals.LoadNetworkedObjectScene(ClassName)!;
	}

	public override void EnterTree()
	{
		if (Parent is Physical phy)
		{
			_oldPhyParent = phy;

			// Configure default collision shape for PolytorianModel
			CollisionPivot = new()
			{
				Scale = NodeSize
			};
			CollisionShape = new()
			{
				Shape = _collisionBox
			};
			Physical.SetRemoteLinkOffset(CollisionShape, new(0, 3f - 0.1f, 0));
			Physical.SetRemoteLinkTarget(CollisionShape, CollisionPivot);
			GDNode.AddChild(CollisionPivot);
			CollisionPivot.Position = new(0, -3f, 0);

			phy.GDNode.AddChild(CollisionShape);
			phy.AddCollisionShape(CollisionShape);
			phy.UpdateCollision();
		}
		base.EnterTree();
	}

	public override void ExitTree()
	{
		if (_oldPhyParent != null)
		{
			_oldPhyParent.RemoveCollisionShape(CollisionShape!);
			if (Node.IsInstanceValid(CollisionPivot))
			{
				CollisionPivot.QueueFree();
			}

			CollisionPivot = null;
			CollisionShape = null;
		}
		base.ExitTree();
	}

	public override async void Ready()
	{
		if (Root == null)
		{
			// Create default character on null root (eg. loading screens/mobile)
			Animator = New<Animator>();
			Animator.Name = "Animator";
			Animator.Parent = this;
		}

		Animator = await WaitChild<Animator>("Animator", 5);

		if (Animator == null) return;

		AnimTree.AdvanceExpressionBaseNode = _helper.GetPath();

		Animator.SetNetworkAuthority(NetworkAuthority);

		Animator.AnimationTree = AnimTree;
		Animator.AnimatorInit();
		Animator.ImportAnimationRaw("emote_dance", "Dance");
		Animator.ImportAnimationRaw("emote_helicopter", "Helicopter");
		Animator.ImportAnimationRaw("emote_sit", "Sit");

		Animator.ImportOneShotAnimationRaw("emote_wave", "Wave");
		Animator.ImportOneShotAnimationRaw("emote_point", "Point");
		/*
		Animator.ImportOneShotAnimationRaw("poly_welcome", "polytorian_2/welcome");
		Animator.ImportOneShotAnimationRaw("avataredit_pose1", "polytorian_2/pose1");
		Animator.ImportOneShotAnimationRaw("avataredit_pose2", "polytorian_2/pose2");
		Animator.ImportOneShotAnimationRaw("avataredit_pose3", "polytorian_2/pose3");
		*/

		Animator.ImportOneShotAnimationRaw("slash", "ToolSlash", true);
		Animator.ImportOneShotAnimationRaw("eat", "ToolEat", true);
		Animator.ImportOneShotAnimationRaw("drink", "ToolDrink", true);
	}

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		Pivot?.Scale = newSize;
		CollisionPivot?.Scale = newSize;
		base.OnNodeSizeChanged(newSize);
	}

	public override void Process(double delta)
	{
		base.Process(delta);

		if (_updateClothDirty)
		{
			_updateClothDirty = false;
			UpdateClothMaterials();
		}

		foreach (KeyValuePair<string, float> kvp in _blendTargets)
		{
			string propName = kvp.Key;
			float target = kvp.Value;
			float current = (float)AnimTree.Get(propName);

			float targetBlendSpeed = BlendSpeed;
			float newValue;

			if (propName.Contains("Look"))
			{
				targetBlendSpeed = LookBlendSpeed;

				newValue = Mathf.Lerp(current, target, (float)delta * targetBlendSpeed);
			}
			else
			{
				newValue = Mathf.MoveToward(current, target, (float)delta * targetBlendSpeed);
			}

			AnimTree.Set(propName, newValue);
		}
	}

	private void UpdateClothMaterials()
	{
		StandardMaterial3D? BuildClothChain()
		{
			StandardMaterial3D? head = null;
			StandardMaterial3D? tail = null;
			foreach (var c in GetChildrenOfClass<Clothing>())
			{
				// Skip unloaded ones
				if (c.ClothTexture == null) continue;
				StandardMaterial3D m = new()
				{
					AlbedoTexture = c.ClothTexture,
					Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
					RenderPriority = 1
				};
				if (head == null) { head = m; tail = m; }
				else { tail!.NextPass = m; tail = m; }
			}
			return head;
		}

		StandardMaterial3D? DuplicateWithColor(StandardMaterial3D? source, StandardMaterial3D? previous)
		{
			if (source == null) return null;
			var dup = (StandardMaterial3D)source.Duplicate(true);
			if (previous != null) dup.AlbedoColor = previous.AlbedoColor;
			return dup;
		}

		var head = BuildClothChain();

		_leftArmMat.NextPass = DuplicateWithColor(head, _shirtMats[0]);
		_torsoMat.NextPass = DuplicateWithColor(head, _shirtMats[1]);
		_rightArmMat.NextPass = DuplicateWithColor(head, _shirtMats[2]);
		_leftLegMat.NextPass = DuplicateWithColor(head, _pantsMats[0]);
		_rightLegMat.NextPass = DuplicateWithColor(head, _pantsMats[1]);

		if (head != null)
		{
			_shirtMats[0] = (StandardMaterial3D)_leftArmMat.NextPass!;
			_shirtMats[1] = (StandardMaterial3D)_torsoMat.NextPass!;
			_shirtMats[2] = (StandardMaterial3D)_rightArmMat.NextPass!;
			_pantsMats[0] = (StandardMaterial3D)_leftLegMat.NextPass!;
			_pantsMats[1] = (StandardMaterial3D)_rightLegMat.NextPass!;
		}
	}

	private void OnFaceLoaded(Resource tex)
	{
		_faceMat.AlbedoTexture = (Texture2D)tex;
		if (!_faceLoaded)
		{
			_faceLoaded = true;
			AssetLoadCheckout();
		}
	}

	private void AddLoadCount()
	{
		IsAvatarLoaded = false;
		_toBeLoadedCount++;
	}

	private void AssetLoadCheckout()
	{
		_toBeLoadedCount--;
		if (_toBeLoadedCount < 0)
		{
			_toBeLoadedCount = 0;
		}
		if (!IsAvatarLoaded && _toBeLoadedCount == 0)
		{
			IsAvatarLoaded = true;
			AvatarLoaded?.Invoke();
		}
	}

	private void OnBodyLoaded(Resource? resource)
	{
		if (resource is PackedScene scene)
		{
			if (_bodyPkScene == scene) return;
			_bodyPkScene = scene;

			Node n = scene.Instantiate();

			ApplyBodyPart(n, HeadMeshInstance, "Head");
			ApplyBodyPart(n, LeftArmMeshInstance, "LeftArm");
			ApplyBodyPart(n, RightArmMeshInstance, "RightArm");
			ApplyBodyPart(n, LeftLegMeshInstance, "LeftLeg");
			ApplyBodyPart(n, RightLegMeshInstance, "RightLeg");
			ApplyBodyPart(n, TorsoMeshInstance, "Torso");

			n.QueueFree();
		}
		else if (resource == null)
		{
			_bodyPkScene = null;
			ApplyDefaultBodyPart(HeadMeshInstance, "Head");
			ApplyDefaultBodyPart(LeftArmMeshInstance, "LeftArm");
			ApplyDefaultBodyPart(RightArmMeshInstance, "RightArm");
			ApplyDefaultBodyPart(LeftLegMeshInstance, "LeftLeg");
			ApplyDefaultBodyPart(RightLegMeshInstance, "RightLeg");
			ApplyDefaultBodyPart(TorsoMeshInstance, "Torso");
		}
	}

	private static void ApplyDefaultBodyPart(MeshInstance3D m3d, string k)
	{
		m3d.Mesh = GD.Load<Godot.Mesh>($"res://assets/models/bodyparts/default/{k}.tres");
	}

	private static void ApplyBodyPart(Node source, MeshInstance3D target, string sourceName)
	{
		if (source.GetNodeOrNull($"Poly/Skeleton3D/{sourceName}") is MeshInstance3D m3d)
		{
			target.Mesh = m3d.Mesh;
		}
		else
		{
			throw new Exception("Invalid Body Mesh");
		}
	}

	[ScriptMethod]
	public void StartRagdoll(Vector3? force = null)
	{
		force ??= Vector3.Zero;
		Rpc(nameof(NetStartRagdoll), force.Value);
	}

	[ScriptMethod]
	public void StopRagdoll()
	{
		Rpc(nameof(NetStopRagdoll));
	}

	[NetRpc(AuthorityMode.Authority, CallLocal = true, TransferMode = TransferMode.Reliable)]
	private async void NetStartRagdoll(Vector3 force)
	{
		if (_lastPhysicalBoneSim != null) return;

		// need duplicates cuz godot won't adapt dynamically to bones
		PhysicalBoneSimulator3D s = (PhysicalBoneSimulator3D)_ragdollBoneSim.Duplicate();

		VelocityPhysicalBone = s.GetNode<PhysicalBone3D>("Physical Bone UpperTorso");

		Skeleton.AddChild(s);

		s.Active = true;
		s.PhysicalBonesStartSimulation();

		_lastPhysicalBoneSim = s;

		VelocityPhysicalBone.LinearVelocity = force / VelocityPhysicalBone.GravityScale;
		Ragdolling = true;
		RagdollStarted.Invoke();
	}

	[NetRpc(AuthorityMode.Authority, CallLocal = true, TransferMode = TransferMode.Reliable)]
	private void NetStopRagdoll()
	{
		if (_lastPhysicalBoneSim == null) return;

		_lastPhysicalBoneSim.PhysicalBonesStopSimulation();
		_lastPhysicalBoneSim.Active = false;
		_lastPhysicalBoneSim.QueueFree();
		_lastPhysicalBoneSim = null;

		Ragdolling = false;
		RagdollStopped.Invoke();
	}

	[ScriptMethod]
	public override Dynamic GetAttachment(CharacterAttachmentEnum attachmentEnum)
	{
		if (!_attachmentEnumToDyn.TryGetValue(attachmentEnum, out Dynamic? dyn))
		{
			Node3D a = GetNode3DAttachment(attachmentEnum);
			dyn = New<Dynamic>();
			dyn.OverrideGDNode(a);
		}

		return dyn;
	}

	public Node3D GetNode3DAttachment(CharacterAttachmentEnum attachmentEnum)
	{
		Node3D result = attachmentEnum switch
		{
			CharacterAttachmentEnum.Head => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_Head/HeadAttachment"),
			CharacterAttachmentEnum.UpperTorso => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_UpperTorso/UpperTorsoAttachment"),
			CharacterAttachmentEnum.LowerTorso => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_LowerTorso/LowerTorsoAttachment"),
			CharacterAttachmentEnum.ShoulderLeft => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_UpperArm_L/ShoulderLeftAttachment"),
			CharacterAttachmentEnum.ShoulderRight => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_UpperArm_R/RightShoulderAttachment"),
			CharacterAttachmentEnum.ElbowLeft => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_LowerArm_L/LeftElbowAttachment"),
			CharacterAttachmentEnum.ElbowRight => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_LowerArm_R/RightElbowAttachment"),
			CharacterAttachmentEnum.HandLeft => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_Hand_L/LeftHandAttachment"),
			CharacterAttachmentEnum.HandRight => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_Hand_R/RightHandAttachment"),
			CharacterAttachmentEnum.LegLeft => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_UpperLeg_L/LeftLegAttachment"),
			CharacterAttachmentEnum.LegRight => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_UpperLeg_R/RightLegAttachment"),
			CharacterAttachmentEnum.KneeLeft => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_LowerLeg_L/LeftKneeAttachment"),
			CharacterAttachmentEnum.KneeRight => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_LowerLeg_R/RightKneeAttachment"),
			_ => throw new NotImplementedException(),
		};

		return result;
	}

	public override void RecvBlendValue(CharacterModelBlendEnum blendName, float blendValue)
	{
		string propName = "";
		switch (blendName)
		{
			case CharacterModelBlendEnum.Sitting:
				propName = "parameters/Sit/blend_amount";
				break;
			case CharacterModelBlendEnum.ToolHoldLeft:
				propName = "parameters/GearHold_L/blend_amount";
				break;
			case CharacterModelBlendEnum.ToolHoldRight:
				propName = "parameters/GearHold_R/blend_amount";
				break;
			case CharacterModelBlendEnum.LookX:
				propName = "parameters/LookXAdd/add_amount";
				break;
			case CharacterModelBlendEnum.LookY:
				propName = "parameters/LookYAdd/add_amount";
				break;
		}

		if (propName != "")
		{
			_blendTargets[propName] = blendValue;
		}
	}

	public override void RecvSpeedValue(float speedValue)
	{
		if (AnimTree == null) return;
		AnimTree.Set("parameters/TimeScale/scale", speedValue);
	}

	public override void ApplyCameraModifier(Camera camera)
	{
		Camera3D cam3D = camera.Camera3D;
		Transform3D camTransform = cam3D.GlobalTransform;
		Transform3D charTransform = GetGlobalTransform();

		Vector3 camForward = -camTransform.Basis.Z.Normalized();

		Vector3 localForward = charTransform.Basis.Inverse() * camForward;
		localForward = localForward.Normalized();

		float lookY = Mathf.Clamp(localForward.Y, -1f, 1f);
		float lookX = -localForward.X;

		if (lookX != _lastLookBlendX)
		{
			_lastLookBlendX = lookX;
		}

		if (lookY != _lastLookBlendY)
		{
			_lastLookBlendY = lookY;
		}

		NetRecvLookBlend(lookY, lookX);

		if (Time.GetTicksMsec() / 1000.0 >= _lastNetUpdateTime + NetLookBlendUpdateInterval)
		{
			_lastNetUpdateTime = Time.GetTicksMsec() / 1000.0;
			Rpc(nameof(NetRecvLookBlend), lookY, lookX);
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.UnreliableOrdered)]
	private void NetRecvLookBlend(float lookYBlend, float lookXBlend)
	{
		RecvBlendValue(CharacterModelBlendEnum.LookX, lookXBlend);
		RecvBlendValue(CharacterModelBlendEnum.LookY, lookYBlend);
	}

	[ScriptMethod]
	public void LoadAppearance(int userID, bool loadTool = true)
	{
		ClearAppearance();
		_ = InternalLoadAppearance(userID, loadTool);
	}

	[ScriptMethod]
	public void ClearAppearance()
	{
		HeadColor = Color.FromString(DefaultBodyColor, new Color());
		TorsoColor = Color.FromString(DefaultBodyColor, new Color());
		LeftArmColor = Color.FromString(DefaultBodyColor, new Color());
		RightArmColor = Color.FromString(DefaultBodyColor, new Color());
		LeftLegColor = Color.FromString(DefaultBodyColor, new Color());
		RightLegColor = Color.FromString(DefaultBodyColor, new Color());
		FaceImage = null;
		_faceOverrided = false;
		_bodyOverrided = false;

		foreach (Instance item in GetChildren())
		{
			if (item is Accessory or Clothing)
			{
				item.Delete();
			}
		}
	}

	private static void MatApplyAlpha(StandardMaterial3D m, Color a)
	{
		m.Transparency = a.A == 1 ? BaseMaterial3D.TransparencyEnum.Disabled : BaseMaterial3D.TransparencyEnum.Alpha;
	}

	internal async Task<AvatarLoadResponse> InternalLoadAppearance(int userID, bool loadTool = false, bool loadToolNpc = false)
	{
		_loadAppearanceCount++;

		// Prevent reloading
		int myCount = _loadAppearanceCount;

		APIAvatarResponse avatarData = await PolyAPI.GetUserAvatarFromID(userID);
		if (myCount != _loadAppearanceCount) throw new OperationCanceledException("The avatar is cancelled");

		if (IsDeleted)
		{
			throw new OperationCanceledException("The avatar is deleted");
		}

		// Apply body color
		HeadColor = Color.FromString(avatarData.Colors.Head, new Color());
		TorsoColor = Color.FromString(avatarData.Colors.Torso, new Color());
		LeftArmColor = Color.FromString(avatarData.Colors.LeftArm, new Color());
		RightArmColor = Color.FromString(avatarData.Colors.RightArm, new Color());
		LeftLegColor = Color.FromString(avatarData.Colors.LeftLeg, new Color());
		RightLegColor = Color.FromString(avatarData.Colors.RightLeg, new Color());

		bool hasTool = false;

		foreach (APIAvatarAsset asset in avatarData.Assets)
		{
			if (asset.Type == "clothing")
			{
				PTImageAsset txt = New<PTImageAsset>();
				txt.ImageID = (uint)asset.ID;
				Clothing c = New<Clothing>();
				c.Name = asset.Name;
				c.Image = txt;
				c.Parent = this;
			}
			else if (asset.Type == "face")
			{
				if (_faceOverrided) continue;
				PTImageAsset face = New<PTImageAsset>();
				face.ImageID = (uint)asset.ID;
				FaceImage = face;
			}
			else if (asset.Type == "body")
			{
				if (_bodyOverrided) continue;
				var body = New<PTMeshAsset>();
				body.AssetID = (uint)asset.ID;
				BodyMesh = body;
			}
			else if (asset.Type == "hat")
			{
				try
				{
					Accessory? accessory = await Root.Insert.AccessoryAsync(asset.ID);
					if (myCount != _loadAppearanceCount) { accessory?.Delete(); throw new OperationCanceledException("The avatar is cancelled"); }
					if (IsDeleted)
					{
						accessory?.Delete();
						throw new OperationCanceledException("The avatar is deleted");
					}
					accessory?.Parent = this;
				}
				catch (Exception ex)
				{
					PT.PrintErr(ex);
				}
			}
			else if (asset.Type == "tool")
			{
				if (Parent is Player plr && loadTool)
				{
					hasTool = true;
					try
					{
						Tool? tool = await Root.Insert.ToolAsync(asset.ID);
						if (myCount != _loadAppearanceCount) { tool?.Delete(); throw new OperationCanceledException("The avatar is cancelled"); }
						if (IsDeleted)
						{
							tool?.Delete();
							throw new OperationCanceledException("The avatar is deleted");
						}
						tool?.Parent = plr.Inventory;
					}
					catch (Exception ex)
					{
						PT.PrintErr(ex);
					}
				}
				else if (Parent is NPC npc && loadToolNpc)
				{
					hasTool = true;
					try
					{
						Tool? tool = await Root.Insert.ToolAsync(asset.ID);
						if (myCount != _loadAppearanceCount) { tool?.Delete(); throw new OperationCanceledException("The avatar is cancelled"); }
						if (IsDeleted)
						{
							tool?.Delete();
							throw new OperationCanceledException("The avatar is deleted");
						}
						if (tool != null)
							npc.EquipTool(tool);
					}
					catch (Exception ex)
					{
						PT.PrintErr(ex);
					}
				}
			}
		}

		AssetLoadCheckout();

		return new() { HasTool = hasTool };
	}

	internal async Task WaitForAppearanceLoad()
	{
		if (FaceImage != null && !FaceImage.IsResourceLoaded)
		{
			await FaceImage.ResourceLoadedInternal.Wait();
		}
		if (BodyMesh != null && !BodyMesh.IsResourceLoaded)
		{
			await BodyMesh.ResourceLoadedInternal.Wait();
		}

		Instance checkOn = this;

		// Check on NPC for loading tools
		if (Parent is NPC)
		{
			checkOn = Parent;
		}

		foreach (var item in checkOn.GetDescendants())
		{
			if (item is Mesh m)
			{
				if (m.Loading)
				{
					await m.Loaded.Wait();
				}
			}
			else if (item is Clothing c)
			{
				if (c.Image != null && !c.Image.IsResourceLoaded)
				{
					await c.Image.ResourceLoadedInternal.Wait();
				}
			}
		}
	}

	internal void QueueRenderCloth()
	{
		_updateClothDirty = true;
	}

	public void SetAnimationOverrideTo(bool to)
	{
		AnimTree.Active = !to;
	}

	internal struct AvatarLoadResponse()
	{
		public bool HasTool = false;
	}
}
