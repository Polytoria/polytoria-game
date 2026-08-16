// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Services;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mesh = Polytoria.Datamodel.Mesh;

namespace Polytoria.Renderer;

public partial class RendererViewport : SubViewport
{
	private const string EnvironmentScene = "res://scenes/renderer/env.tscn";
	public World Root = null!;
	public NetworkService NetworkService = null!;

	public RendererViewport()
	{
		World3D = new();

		TransparentBg = true;
		Msaa3D = Msaa.Msaa8X;
		Size = new(800, 800);

		Root = Globals.LoadInstance<World>();
		DatamodelBridge bridge = new()
		{
			Name = "DatamodelBridge"
		};
		AddChild(bridge, true);

		NetworkService networkService = new()
		{
			Name = "NetworkService",
		};
		NetworkService = networkService;

		Root.SessionType = World.SessionTypeEnum.Renderer;

		networkService.Attach(Root);
		networkService.IsServer = true;
		networkService.NetworkMode = NetworkService.NetworkModeEnum.Renderer;
		networkService.NetworkParent = Root;

		AddChild(Root.GDNode, true);
		Root.Root = Root;
		Root.World3D = World3D;
		Root.InitEntry();

		bridge.Attach(Root);
	}

	public void Setup()
	{
		Root.Setup();

		Root.Lighting.SunBrightness = 0;

		Node n = Globals.CreateInstanceFromScene<Node>(EnvironmentScene);
		Root.GDNode.AddChild(n);
		Root.World3D.Environment = n.GetNode<WorldEnvironment>("WorldEnvironment").Environment;
	}

	public async Task AddAvatar(int id, AvatarPhotoTypeEnum photoType = AvatarPhotoTypeEnum.FullAvatar)
	{
		Camera cam = Root.Environment.CurrentCamera!;
		Camera3D c3d = cam.Camera3D;

		PolytorianModel ptm = Root.Insert.DefaultNPCCharacter();
		ptm.Parent = Root.Environment;
		ptm.GDNode3D.RotationDegrees = new(0, 15, 0);
		ptm.UseNametag = false;

		ptm.SetAnimationOverrideTo(true);
		AnimationPlayer ply = ptm.AnimTree.GetNode<AnimationPlayer>(ptm.AnimTree.AnimPlayer);
		PolytorianModel.AvatarLoadResponse loadRes = await ptm.InternalLoadAppearance(id, loadToolNpc: true);

		if (loadRes.HasTool)
		{
			ply.Play("ToolHoldR");
		}

		await ptm.WaitForAppearanceLoad();

		switch (photoType)
		{
			case AvatarPhotoTypeEnum.FullAvatar:
				{
					c3d.GlobalPosition = new(0, 1.75f, 5);
					c3d.GlobalRotationDegrees = new(-15, 0, 0);
					break;
				}
			case AvatarPhotoTypeEnum.Headshot:
				{
					c3d.GlobalPosition = new(-0.05f, 1.7f, 2.5f);
					c3d.GlobalRotationDegrees = new(0, 0, 0);
					break;
				}
		}
	}

	public async Task AddAccessory(int id)
	{
		Camera cam = Root.Environment.CurrentCamera!;
		Camera3D c3d = cam.Camera3D;

		Accessory? accessory = await Root.Insert.AccessoryAsync(id);

		if (accessory != null)
		{
			accessory.Parent = Root.Environment;

			foreach (Instance item in accessory.GetDescendants())
			{
				if (item is Mesh m)
				{
					if (m.Loading)
					{
						await m.Loaded.Wait();
					}
				}
			}
			FocusToBounds(accessory.GDNode3D, c3d);
		}
	}

	public async Task<byte[]> SavePng()
	{
		RenderTargetClearMode = ClearMode.Once;
		RenderTargetUpdateMode = UpdateMode.Once;
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Image img = GetTexture().GetImage();
		img.FixAlphaEdges();
		return img.SavePngToBuffer();
	}

	private static void FocusToBounds(Node3D target, Camera3D cam, float yawDeg = 20f, float pitchDeg = -15f, float padding = 0.01f, Vector3? up = null)
	{
		if (target == null || cam == null)
		{
			GD.PushError("Target or camera is null.");
			return;
		}

		if (!TryGetWorldAabb(target, out var worldMin, out var worldMax))
			return;

		var upVec = up ?? Vector3.Up;

		Vector3 size = worldMax - worldMin;
		Vector3 center = worldMin + size * 0.5f;
		float radius = size.Length() * 0.5f;

		float vFov = Mathf.DegToRad(cam.Fov);
		var vp = cam.GetViewport();
		float aspect = 1.0f;

		if (vp != null)
		{
			var r = vp.GetVisibleRect();
			aspect = r.Size.Y != 0 ? r.Size.X / r.Size.Y : 1.0f;
		}

		float hFov = 2f * Mathf.Atan(Mathf.Tan(vFov * 0.5f) * aspect);

		float halfMinFov = Mathf.Min(vFov, hFov) * 0.5f;
		float paddedR = radius * (1f + MathF.Max(0f, padding));
		float distance = paddedR / MathF.Tan(MathF.Max(0.001f, halfMinFov));

		float yaw = Mathf.DegToRad(yawDeg);
		float pitch = Mathf.DegToRad(pitchDeg);

		Basis yawB = new(upVec, yaw);
		Vector3 right = yawB * Vector3.Right;
		Basis pitchB = new(right, pitch);
		Basis viewBasis = pitchB * yawB;

		Vector3 forward = viewBasis.Z * -1f;
		forward = forward.Normalized();

		Vector3 camPos = center - forward * distance;
		cam.GlobalTransform = new Transform3D(Basis.Identity, camPos).LookingAt(center, upVec);

		cam.Near = MathF.Max(0.01f, distance - paddedR * 2f);
		cam.Far = MathF.Max(cam.Near + 1f, distance + paddedR * 4f);

	}

	private static bool TryGetWorldAabb(Node3D root, out Vector3 min, out Vector3 max)
	{
		min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
		max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
		bool found = false;

		var stack = new Stack<Node>();
		stack.Push(root);

		while (stack.Count > 0)
		{
			var n = stack.Pop();
			foreach (var child in n.GetChildren())
			{
				stack.Push(child);
			}

			if (n is MeshInstance3D mi && mi.Mesh != null)
			{
				var a = mi.Mesh.GetAabb();
				EncapsulateTransformedAabb(ref min, ref max, a, mi.GlobalTransform);
				found = true;
			}
		}
		return found;
	}

	private static void EncapsulateTransformedAabb(ref Vector3 min, ref Vector3 max, Aabb local, Transform3D xf)
	{
		Vector3 p = local.Position;
		Vector3 s = local.Size;
		Span<Vector3> corners = [
			new Vector3(p.X,       p.Y,       p.Z),
			new Vector3(p.X+s.X,   p.Y,       p.Z),
			new Vector3(p.X,       p.Y+s.Y,   p.Z),
			new Vector3(p.X,       p.Y,       p.Z+s.Z),
			new Vector3(p.X+s.X,   p.Y+s.Y,   p.Z),
			new Vector3(p.X+s.X,   p.Y,       p.Z+s.Z),
			new Vector3(p.X,       p.Y+s.Y,   p.Z+s.Z),
			new Vector3(p.X+s.X,   p.Y+s.Y,   p.Z+s.Z),
		];

		for (int i = 0; i < 8; i++)
		{
			Vector3 w = xf * corners[i];
			if (w.X < min.X) min.X = w.X; if (w.Y < min.Y) min.Y = w.Y; if (w.Z < min.Z) min.Z = w.Z;
			if (w.X > max.X) max.X = w.X; if (w.Y > max.Y) max.Y = w.Y; if (w.Z > max.Z) max.Z = w.Z;
		}
	}


	public enum AvatarPhotoTypeEnum
	{
		FullAvatar,
		Headshot
	}
}
