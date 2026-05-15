// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;

namespace Polytoria.Datamodel.Resources;

[Abstract]
public partial class MeshAnimationAsset : ResourceAsset
{
	private MeshAnimationTypeEnum _animationType = MeshAnimationTypeEnum.Normal;

	[Editable, ScriptProperty]
	public MeshAnimationTypeEnum AnimationType
	{
		get => _animationType;
		set
		{
			_animationType = value;
			OnPropertyChanged();
		}
	}

	[ScriptEnum]
	public enum MeshAnimationTypeEnum
	{
		Normal,
		Looped,
		PingPong,
		OneShot,
		OneShotImpluse
	}
}
