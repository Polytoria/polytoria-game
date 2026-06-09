using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class UIAspectRatioRestraint : Instance
{
	private DominantAxisEnum _dominantAxis = default;
	private AspectRatioScaleType _scaleType = default;
	private float _aspectRatio = 1;
	private UIField? oldParent = null;

	[Editable, ScriptProperty]
	public DominantAxisEnum DominantAxis
	{
		get => _dominantAxis;
		set
		{
			_dominantAxis = value;
			OnPropertyChanged();
			UpdateParentSize();
		}
	}
	[Editable, ScriptProperty]
	public AspectRatioScaleType ScaleType
	{
		get => _scaleType;
		set
		{
			_scaleType = value;
			OnPropertyChanged();
			UpdateParentSize();
		}
	}
	[Editable, ScriptProperty]
	public float AspectRatio
	{
		get => _aspectRatio;
		set
		{
			if (value == _aspectRatio) return;
			_aspectRatio = value;
			UpdateParentSize();
		}
	}

	private void UpdateParentSize()
	{
		if (Parent is not UIField field || field.NodeControl == null) return;
		field.RecomputeTransform();
	}

	public override void EnterTree()
	{
		UpdateParentSize();
		if (oldParent == null && Parent is UIField parentField)
		{
			oldParent = parentField;
		}
		base.EnterTree();
	}


	public override void PostReparent()
	{
		if (oldParent is UIField field && field.NodeControl != null)
		{
			field.RecomputeTransform();
		}
		if (oldParent is UIField parentField)
		{
			oldParent = parentField;
		}
		base.PostReparent();
	}
}

[ScriptEnum]
public enum DominantAxisEnum
{
	Width = 0,
	Height = 1
}
[ScriptEnum]
public enum AspectRatioScaleType
{
	FitContainer = 0,
	FitMaxSize = 1,
	NoLimit = 2
}
