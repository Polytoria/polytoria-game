// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class UIViewport : UIField
{
	private SubViewport _subViewport = null!;
	private WorldEnvironment _worldEnv = null!;
	private DirectionalLight3D _skyLight = null!;
	private readonly Godot.Sky _sky = new();
	private SunLight? _sun;
	private bool _findSun = true;
	private bool _environmentDirty;

	public override Node CreateGDNode()
	{
		SubViewportContainer container = new() { FocusMode = Control.FocusModeEnum.None };
		_subViewport = new() { HandleInputLocally = false, TransparentBg = true, OwnWorld3D = true };

		_worldEnv = new();
		_subViewport.AddChild(_worldEnv);

		_skyLight = new() { SkyMode = DirectionalLight3D.SkyModeEnum.SkyOnly, ShadowEnabled = false, Visible = false };
		_subViewport.AddChild(_skyLight, @internal: Node.InternalMode.Front);

		container.Stretch = true;
		container.AddChild(_subViewport);
		return container;
	}

	public override void InitGDNode()
	{
		SlotNode = _subViewport;
		base.InitGDNode();
	}

	public override void Init()
	{
		Root.Lighting.PropertyChanged.Connect(MarkEnvironmentDirty);
		Root.Lighting.environment.Changed += MarkEnvironmentDirty;
		Root.Lighting.ChildAdded.Connect(OnLightingChildAdded);
		RenderingServer.FramePreDraw += OnFramePreDraw;
		SyncEnvironment();
		base.Init();
	}

	public override void PreDelete()
	{
		Root?.Lighting?.PropertyChanged.Disconnect(MarkEnvironmentDirty);
		Root?.Lighting?.environment.Changed -= MarkEnvironmentDirty;
		Root?.Lighting?.ChildAdded.Disconnect(OnLightingChildAdded);
		RenderingServer.FramePreDraw -= OnFramePreDraw;
		base.PreDelete();
	}

	private void MarkEnvironmentDirty()
	{
		_environmentDirty = true;
	}

	private void OnLightingChildAdded(Instance child)
	{
		if (child is SunLight) _findSun = true;
	}

	private void OnFramePreDraw()
	{
		if (!GodotObject.IsInstanceValid(NodeControl) || !NodeControl.IsVisibleInTree()) return;

		if (_environmentDirty)
		{
			_environmentDirty = false;
			SyncEnvironment();
		}
		SyncSkyLight();
	}

	private void SyncSkyLight()
	{
		if (!GodotObject.IsInstanceValid(_skyLight)) return;

		if (_sun != null && _sun.IsDeleted)
		{
			_sun = null;
			_findSun = true;
		}

		if (_sun == null && _findSun)
		{
			_findSun = false;
			_sun = Root?.Lighting?.FindChild<SunLight>("SunLight");
		}

		Light3D? sun = _sun?.GDLight;
		bool visible = sun != null && GodotObject.IsInstanceValid(sun) && sun.IsVisibleInTree();
		if (_skyLight.Visible != visible) _skyLight.Visible = visible;
		if (!visible) return;

		if (_skyLight.GlobalTransform != sun!.GlobalTransform) _skyLight.GlobalTransform = sun.GlobalTransform;
		if (_skyLight.LightColor != sun.LightColor) _skyLight.LightColor = sun.LightColor;
		if (_skyLight.LightEnergy != sun.LightEnergy) _skyLight.LightEnergy = sun.LightEnergy;
		if (_skyLight.LightSize != sun.LightSize) _skyLight.LightSize = sun.LightSize;
		if (_skyLight.LightNegative != sun.LightNegative) _skyLight.LightNegative = sun.LightNegative;
	}

	private void SyncEnvironment()
	{
		Godot.Environment environment = (Godot.Environment)Root.Lighting.environment.Duplicate();

		if (environment.Sky != null)
		{
			_sky.SkyMaterial = environment.Sky.SkyMaterial;
			_sky.ProcessMode = environment.Sky.ProcessMode;
			_sky.RadianceSize = environment.Sky.RadianceSize;
			environment.Sky = _sky;
		}

		environment.SsaoEnabled = false;
		environment.SsrEnabled = false;
		environment.SsilEnabled = false;
		environment.SdfgiEnabled = false;
		_worldEnv.Environment = environment;
	}
}
