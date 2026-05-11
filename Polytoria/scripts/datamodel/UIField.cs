// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
#if CREATOR
using Polytoria.Datamodel.Creator;
#endif
using Polytoria.Scripting;
using Polytoria.Shared;
using Polytoria.Utils;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class UIField : Instance
{
	internal Control NodeControl = null!;
	private Vector2 _positionOffset = new(0, 0);
	private Vector2 _positionRelative = new(0.5f, 0.5f);
	private Vector2 _sizeOffset = new(100, 100);
	private Vector2 _sizeRelative = new(0, 0);
	private Vector2 _pivotPoint = new(0.5f, 0.5f);
	private Vector2 _scale = new(1f, 1f);
	private float _rotation = 0;
	private bool _clipDescendants = false;
	private bool _queuedRecomputeTransform = false;
	private MaskModeEnum _maskModeEnum = MaskModeEnum.Disabled;
	private bool _ignoreMouse = false;
	private int _zIndex = 0;

	internal bool OverrideAbsSize;
	internal Vector2 OverrideAbsSizeTo;
	internal bool OverrideParentCheck = false;

	private bool _visible = true;
	[Editable, ScriptProperty]
	public Vector2 PositionOffset
	{
		get => _positionOffset;
		set
		{
			_positionOffset = value;
			QueueRecomputeTransform();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector2 PositionRelative
	{
		get => _positionRelative;
		set
		{
			_positionRelative = value;
			QueueRecomputeTransform();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float Rotation
	{
		get => _rotation;
		set
		{
			_rotation = value;
			QueueRecomputeTransform();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector2 SizeOffset
	{
		get => _sizeOffset;
		set
		{
			_sizeOffset = value;
			QueueRecomputeTransform();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector2 SizeRelative
	{
		get => _sizeRelative;
		set
		{
			_sizeRelative = value;
			QueueRecomputeTransform();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool ClipDescendants
	{
		get => _clipDescendants;
		set
		{
			_clipDescendants = value;
			NodeControl.ClipContents = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector2 PivotPoint
	{
		get => _pivotPoint;
		set
		{
			_pivotPoint = value;
			QueueRecomputeTransform();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Vector2 Scale
	{
		get => _scale;
		set
		{
			_scale = value;
			NodeControl.Scale = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool Visible
	{
		get => _visible;
		set
		{
			_visible = value;
			RecomputeVisible();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public MaskModeEnum MaskMode
	{
		get => _maskModeEnum;
		set
		{
			_maskModeEnum = value;
			NodeControl.ClipChildren = value switch
			{
				MaskModeEnum.Disabled => Control.ClipChildrenMode.Disabled,
				MaskModeEnum.ClipOnly => Control.ClipChildrenMode.Only,
				MaskModeEnum.ClipAndDraw => Control.ClipChildrenMode.AndDraw,
				_ => Control.ClipChildrenMode.Disabled,
			};
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool IgnoreMouse
	{
		get => _ignoreMouse;
		set
		{
			_ignoreMouse = value;
			OnPropertyChanged();
			NodeControl.MouseFilter = value ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop;
		}
	}

	[Editable, ScriptProperty]
	public int ZIndex
	{
		get => _zIndex;
		set
		{
			_zIndex = value;
			NodeControl.ZIndex = value;
			OnPropertyChanged();
		}
	}

	[ScriptProperty] public Vector2 AbsolutePosition => NodeControl.GlobalPosition;
	[ScriptProperty] public Vector2 AbsoluteSize => OverrideAbsSize ? OverrideAbsSizeTo : NodeControl.Size;

	[ScriptProperty] public PTSignal MouseEnter { get; private set; } = new();
	[ScriptProperty] public PTSignal MouseExit { get; private set; } = new();

	[ScriptProperty] public PTSignal MouseDown { get; private set; } = new();
	[ScriptProperty] public PTSignal MouseUp { get; private set; } = new();
	[ScriptProperty] public PTSignal TransformChanged { get; private set; } = new();
	[ScriptProperty] public PTSignal VisibilityChanged { get; private set; } = new();

	[ScriptProperty] public bool IsVisibleInTree => NodeControl.IsVisibleInTree();

	internal bool IsParentedToUI = false;
	internal bool IsParentedToCreatorGUI = false;

	private Rect2 _oldRect;
	private bool _oldVisible;

	public override Node CreateGDNode()
	{
		return Globals.LoadNetworkedObjectScene(ClassName)!;
	}

	public override void InitGDNode()
	{
		NodeControl = (Control)GDNode;
		base.InitGDNode();
	}

	public override void EnterTree()
	{
		// Hide if not in GUI related class
		if (Parent is not PlayerGUI and not UIField and not GUI and not GUI3D)
		{
			IsParentedToUI = false;
		}
		else
		{
			IsParentedToUI = true;
		}
#if CREATOR
		IsParentedToCreatorGUI = IsDescendantOfClass<CreatorGUI>();
		if (!IsParentedToCreatorGUI)
		{
			NodeControl.MouseFilter = Control.MouseFilterEnum.Pass;
			NodeControl.FocusMode = Control.FocusModeEnum.Click;
		}
#endif
		QueueRecomputeTransform();
		RecomputeVisible();
		base.EnterTree();
	}

	public override void Init()
	{
		NodeControl.MouseEntered += OnMouseEntered;
		NodeControl.MouseExited += OnMouseExited;
		NodeControl.GuiInput += GuiInput;

		NodeControl.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		NodeControl.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;

		IgnoreMouse = false;

		base.Init();
		SetProcess(true);
	}

	public override void PreDelete()
	{
		NodeControl.MouseEntered -= OnMouseEntered;
		NodeControl.MouseExited -= OnMouseExited;
		NodeControl.GuiInput -= GuiInput;
		base.PreDelete();
	}

	public override void Ready()
	{
		Callable.From(() =>
		{
			RecomputeTransform();
			RecomputeVisible();
		}).CallDeferred();
		base.Ready();
	}

	public override void Process(double delta)
	{
		if (_queuedRecomputeTransform)
		{
			_queuedRecomputeTransform = false;
			RecomputeTransform();
			//SetProcess(false);
		}
		base.Process(delta);
	}

	private void GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton btn && btn.ButtonIndex == MouseButton.Left)
		{
			if (btn.Pressed)
			{
				MouseDown.Invoke();
#if CREATOR
				if (!IsParentedToCreatorGUI)
				{
					Root.CreatorContext?.Selections.SelectOnly(this);
				}
#endif
			}
			else
			{
				MouseUp.Invoke();
			}
		}
	}

	internal void QueueRecomputeTransform()
	{
		_queuedRecomputeTransform = true;
	}

	private void OnMouseEntered()
	{
		MouseEnter.Invoke();
	}
	private void OnMouseExited()
	{
		if (NodeControl.HasFocus())
		{
			NodeControl.ReleaseFocus();
		}
		MouseExit.Invoke();
	}

	internal void RecomputeTransform()
	{
		Vector2 parentSize = new(0, 0);

		if (Parent is UIField field)
		{
			parentSize = field.AbsoluteSize;
		}
		else if (Parent is GUI gui)
		{
			parentSize = gui.AbsoluteSize;
		}
		else if (Parent is GUI3D g3D)
		{
			parentSize = g3D.AbsoluteSize;
		}

		Vector2 size = _sizeOffset + (parentSize * _sizeRelative);

		NodeControl.CustomMinimumSize = size;

		OverrideAbsSizeTo = size;
		OverrideAbsSize = true;

		PreRecomputeChildTransforms();

		NodeControl.Size = size;
		NodeControl.PivotOffsetRatio = new(_pivotPoint.X, _pivotPoint.Y);

		if (Parent is not UIContainer)
		{
			Vector2 selfSize = AbsoluteSize;
			Vector2 computedPos = new Vector2(_positionOffset.X, _positionOffset.Y) + (parentSize * new Vector2(_positionRelative.X, _positionRelative.Y)) - (new Vector2(_pivotPoint.X, _pivotPoint.Y) * selfSize);

			NodeControl.Position = computedPos;
			NodeControl.Rotation = Mathf.DegToRad(_rotation);
		}

		Rect2 curTransform = NodeControl.GetGlobalRect();

		if (_oldRect != curTransform)
		{
			_oldRect = curTransform;
			TransformChanged.Invoke();
		}

		RecomputeChildTransforms();
	}

	protected void RecomputeChildTransforms()
	{
		foreach (Instance item in GetChildren())
		{
			if (item is UIField uifield)
			{
				uifield.RecomputeTransform();
			}
		}
	}

	internal void PreRecomputeChildTransforms()
	{
		foreach (Instance item in GetChildren())
		{
			if (item is UIField uifield)
			{
				// process children by deepest first
				uifield.PreRecomputeChildTransforms();
				uifield.RecomputeTransform();
			}
		}
	}

	internal void RecomputeVisible()
	{
		if (!IsHidden && (IsParentedToUI || OverrideParentCheck))
		{
			NodeControl.Visible = _visible;
		}
		else
		{
			NodeControl.Visible = false;
		}
		if (!NodeControl.Visible && NodeControl.HasFocus())
		{
			NodeControl.ReleaseFocus();
		}

		if (_oldVisible != NodeControl.Visible)
		{
			_oldVisible = NodeControl.Visible;
			VisibilityChanged.Invoke();
		}
	}

	public override void HiddenChanged(bool to)
	{
		RecomputeVisible();
		base.HiddenChanged(to);
	}

	public enum MaskModeEnum
	{
		Disabled,
		ClipOnly,
		ClipAndDraw
	}
}
