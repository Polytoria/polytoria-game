// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Interfaces;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class Folder : Instance, IGroup
{
	private Dynamic? _transformParent;

	public override Node CreateGDNode()
	{
		// Keep the spatial transform chain intact for Dynamic descendants.
		return new Node3D();
	}

	public override void EnterTree()
	{
		base.EnterTree();
		SubscribeToTransformParent();
	}

	public override void ExitTree()
	{
		UnsubscribeFromTransformParent();
		base.ExitTree();
	}

	private void SubscribeToTransformParent()
	{
		UnsubscribeFromTransformParent();

		// Only the first Folder after a Dynamic needs to forward the event.
		// Nested Folders are reached recursively by PropagateTransformChanged.
		_transformParent = Parent as Dynamic;
		if (_transformParent != null)
		{
			_transformParent.TransformChanged += OnTransformParentChanged;
		}
	}

	private void UnsubscribeFromTransformParent()
	{
		if (_transformParent != null)
		{
			_transformParent.TransformChanged -= OnTransformParentChanged;
			_transformParent = null;
		}
	}

	private void OnTransformParentChanged()
	{
		PropagateTransformChanged(this);
	}

	private static void PropagateTransformChanged(Instance parent)
	{
		foreach (Instance child in parent.GetChildren())
		{
			if (child is Dynamic dynamicChild)
			{
				dynamicChild.InvokeTransformChanged();
			}
			else
			{
				PropagateTransformChanged(child);
			}
		}
	}
}
