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

namespace Polytoria.Datamodel;

[Instantiable]
public partial class UIField : Instance
{
	internal Control NodeControl = null!;
	internal StyleBoxFlat _styleBox = new() { AntiAliasing = true, AntiAliasingSize = 2 };
	private Panel? _bgPanel;
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

	internal ControllerState? _controllerState;

	private const string CornerRadiusPropName = "CornerRadius";

	internal void OnCornerControllerEnter()
	{
		_controllerState ??= new();
		if (_controllerState.CornerCount++ == 0)
		{
			_controllerState.SavedCorners[0] = _styleBox.CornerRadiusTopLeft;
			_controllerState.SavedCorners[1] = _styleBox.CornerRadiusTopRight;
			_controllerState.SavedCorners[2] = _styleBox.CornerRadiusBottomLeft;
			_controllerState.SavedCorners[3] = _styleBox.CornerRadiusBottomRight;
		}
	}

	internal void OnCornerControllerExit()
	{
		if (_controllerState == null) return;
		if (--_controllerState.CornerCount > 0) return;
		_styleBox.CornerRadiusTopLeft = _controllerState.SavedCorners[0];
		_styleBox.CornerRadiusTopRight = _controllerState.SavedCorners[1];
		_styleBox.CornerRadiusBottomLeft = _controllerState.SavedCorners[2];
		_styleBox.CornerRadiusBottomRight = _controllerState.SavedCorners[3];
		SyncCornerPanel();
		OnPropertyChanged(CornerRadiusPropName, syncToNet: false);
	}

	private const string BorderWidthPropName = "BorderWidth";
	private const string BorderColorPropName = "BorderColor";

	internal void OnStrokeControllerEnter()
	{
		_controllerState ??= new();
		if (_controllerState.StrokeCount++ == 0)
		{
			_controllerState.SavedBorderWidths[0] = _styleBox.BorderWidthTop;
			_controllerState.SavedBorderWidths[1] = _styleBox.BorderWidthBottom;
			_controllerState.SavedBorderWidths[2] = _styleBox.BorderWidthLeft;
			_controllerState.SavedBorderWidths[3] = _styleBox.BorderWidthRight;
			_controllerState.SavedBorderColor = _styleBox.BorderColor;
		}
	}

	internal void OnStrokeControllerExit()
	{
		if (_controllerState == null) return;
		if (--_controllerState.StrokeCount > 0) return;
		_styleBox.BorderWidthTop = _controllerState.SavedBorderWidths[0];
		_styleBox.BorderWidthBottom = _controllerState.SavedBorderWidths[1];
		_styleBox.BorderWidthLeft = _controllerState.SavedBorderWidths[2];
		_styleBox.BorderWidthRight = _controllerState.SavedBorderWidths[3];
		_styleBox.BorderColor = _controllerState.SavedBorderColor;
		OnPropertyChanged(BorderWidthPropName, syncToNet: false);
		OnPropertyChanged(BorderColorPropName, syncToNet: false);
	}

	internal int CornerControllerCount => _controllerState?.CornerCount ?? 0;
	internal int StrokeControllerCount => _controllerState?.StrokeCount ?? 0;

	internal int[] SavedCorners
	{
		get { _controllerState ??= new(); return _controllerState.SavedCorners; }
	}

	internal int[] SavedBorderWidths
	{
		get { _controllerState ??= new(); return _controllerState.SavedBorderWidths; }
	}

	internal Color SavedBorderColor
	{
		get => _controllerState?.SavedBorderColor ?? default;
		set { _controllerState ??= new(); _controllerState.SavedBorderColor = value; }
	}

	internal void InternalSetRotation(float degrees)
	{
		_rotation = degrees;
		NodeControl.Rotation = Mathf.DegToRad(degrees);
	}

	internal (float TopLeft, float TopRight, float BottomLeft, float BottomRight) InternalGetCorners()
		=> (_styleBox.CornerRadiusTopLeft, _styleBox.CornerRadiusTopRight,
			_styleBox.CornerRadiusBottomLeft, _styleBox.CornerRadiusBottomRight);

	internal void InternalSetAllCorners(float tl, float tr, float bl, float br)
	{
		_styleBox.CornerRadiusTopLeft = Mathf.RoundToInt(tl);
		_styleBox.CornerRadiusTopRight = Mathf.RoundToInt(tr);
		_styleBox.CornerRadiusBottomLeft = Mathf.RoundToInt(bl);
		_styleBox.CornerRadiusBottomRight = Mathf.RoundToInt(br);
		SyncCornerPanel();
		OnPropertyChanged(CornerRadiusPropName, syncToNet: false);
	}

	internal void InternalSetStroke(int width, Color color)
	{
		_styleBox.BorderWidthTop = width;
		_styleBox.BorderWidthBottom = width;
		_styleBox.BorderWidthLeft = width;
		_styleBox.BorderWidthRight = width;
		_styleBox.BorderColor = color;
		OnPropertyChanged(BorderWidthPropName, syncToNet: false);
		OnPropertyChanged(BorderColorPropName, syncToNet: false);
	}

	internal static Panel CreateOverlayPanel()
	{
		return new Panel
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ShowBehindParent = true,
			AnchorLeft = 0,
			AnchorRight = 1,
			AnchorTop = 0,
			AnchorBottom = 1,
		};
	}

	private void SyncCornerPanel()
	{
		if (NodeControl == null) return;
		if (NodeControl is Panel or TextureRect) return;

		bool hasCorners = _styleBox.CornerRadiusTopLeft > 0
			|| _styleBox.CornerRadiusTopRight > 0
			|| _styleBox.CornerRadiusBottomLeft > 0
			|| _styleBox.CornerRadiusBottomRight > 0;

		if (hasCorners && _bgPanel == null)
		{
			_bgPanel = CreateOverlayPanel();
			_bgPanel.AddThemeStyleboxOverride("panel", _styleBox);
			NodeControl.AddChild(_bgPanel);
			NodeControl.MoveChild(_bgPanel, 0);
		}
		else if (!hasCorners && _bgPanel != null)
		{
			_bgPanel.QueueFree();
			_bgPanel = null;
		}
	}

	public override void Init()
	{
		if (NodeControl is Panel)
			NodeControl.AddThemeStyleboxOverride("panel", _styleBox);

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
		_bgPanel?.QueueFree();
		_bgPanel = null;
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
			SetProcess(false);
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
					if (!IsMouseOverChildUIField())
					{
						if (Root.CreatorContext?.Selections.HasSelected(this) != true)
						{
							Root.CreatorContext?.Selections.SelectOnly(this);
						}
						NodeControl.AcceptEvent();
					}
				}
#endif
			}
			else
			{
				MouseUp.Invoke();
			}
		}

		if (@event is InputEventScreenTouch touch)
		{
			if (touch.Pressed)
			{
				MouseDown.Invoke();
			}
			else
			{
				MouseUp.Invoke();
			}

			NodeControl.AcceptEvent();
			return;
		}
	}

	private bool IsMouseOverChildUIField()
	{
		if (NodeControl == null) return false;
		Vector2 mousePos = NodeControl.GetGlobalMousePosition();
		foreach (Instance child in GetChildren())
		{
			if (child is UIField { IsHidden: false } uiChild)
			{
				if (uiChild.NodeControl.GetGlobalRect().HasPoint(mousePos))
					return true;
			}
		}
		return false;
	}

	internal void QueueRecomputeTransform()
	{
		_queuedRecomputeTransform = true;
		SetProcess(true);
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


		UIAspectRatioRestraint? aspectRatioConstraint = (UIAspectRatioRestraint?)FindChildByClass("UIAspectRatioRestraint");
		if (aspectRatioConstraint != null)
		{
			Vector2? maxSize;
			switch (aspectRatioConstraint.ScaleType)
			{
				case AspectRatioScaleTypeEnum.FitContainer:
					maxSize = parentSize;
					break;
				case AspectRatioScaleTypeEnum.FitMaxSize:
					maxSize = size;
					break;
				default:
					maxSize = null;
					break;
			}

			if (aspectRatioConstraint.DominantAxis == DominantAxisEnum.Width)
			{
				size.Y = size.X / aspectRatioConstraint.AspectRatio;
			}
			else
			{
				size.X = size.Y / aspectRatioConstraint.AspectRatio;
			}

			if (maxSize != null && (maxSize.Value.X < size.X || maxSize.Value.Y < size.Y))
			{
				Vector2 ratio;
				float subordinateAxis = 1 / aspectRatioConstraint.AspectRatio;
				float dominantAxis = 1;
				float higherAxis = Mathf.Max(dominantAxis, subordinateAxis);
				dominantAxis /= higherAxis;
				subordinateAxis /= higherAxis;

				if (aspectRatioConstraint.DominantAxis == DominantAxisEnum.Width)
				{
					ratio = new Vector2(dominantAxis, subordinateAxis);
				}
				else
				{
					ratio = new Vector2(subordinateAxis, dominantAxis);
				}
				size = ratio * Mathf.Min(maxSize.Value.X, maxSize.Value.Y);
			}
		}

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

	[ScriptEnum("UIMaskMode")]
	public enum MaskModeEnum
	{
		Disabled,
		ClipOnly,
		ClipAndDraw
	}

	internal sealed class ControllerState
	{
		public int CornerCount;
		public int StrokeCount;
		public int[] SavedCorners = new int[4];
		public int[] SavedBorderWidths = new int[4];
		public Color SavedBorderColor;
	}
}
