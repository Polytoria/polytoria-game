// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class Accessory : Dynamic
{
	private CharacterModel? _targetCharacter;
	private PolytorianModel.CharacterAttachmentEnum _targetAttachment;
	private RemoteTransform3D? remoteTransform;

	[Editable, ScriptProperty]
	public PolytorianModel.CharacterAttachmentEnum TargetAttachment
	{
		get => _targetAttachment;
		set
		{
			_targetAttachment = value;
			RefreshAttachment();
			OnPropertyChanged();
		}
	}

	private void RefreshAttachment()
	{
		if (_targetCharacter is not PolytorianModel ptm || !GDNode.IsInsideTree()) { return; }
		remoteTransform?.QueueFree();
		Dynamic attachment = ptm.GetAttachment(TargetAttachment);
		remoteTransform = new()
		{
			UseGlobalCoordinates = true,
			UpdatePosition = true,
			UpdateRotation = true,
			UpdateScale = false
		};
		attachment.GDNode.AddChild(remoteTransform, @internal: Node.InternalMode.Back);
		remoteTransform.RemotePath = remoteTransform.GetPathTo(GDNode);
	}

	public override void EnterTree()
	{
		base.EnterTree();
		if (Parent is CharacterModel c)
		{
			_targetCharacter = c;
		}
		RefreshAttachment();
	}

	public override void ExitTree()
	{
		base.ExitTree();
		_targetCharacter = null;
		remoteTransform?.QueueFree();
	}
}
