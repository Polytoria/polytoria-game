// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;
using System.Collections.Generic;

namespace Polytoria.Datamodel.Resources;

[Instantiable]
public partial class BuiltInCursorAsset : CursorAsset
{
	private BuiltInCursorPresetEnum _cursorPreset = BuiltInCursorPresetEnum.Arrow;
	private Image _cursorImage = null!;
	private Texture2D _cursorTex = null!;

	[Editable, ScriptProperty]
	public BuiltInCursorPresetEnum CursorPreset
	{
		get => _cursorPreset;
		set
		{
			_cursorPreset = value;
			LoadResource();
			OnPropertyChanged();
		}
	}

	private readonly Dictionary<BuiltInCursorPresetEnum, string> CursorMapping = new()
	{
		{ BuiltInCursorPresetEnum.Arrow, "arrow.png" },
		{ BuiltInCursorPresetEnum.Pointer, "click.png" },
		{ BuiltInCursorPresetEnum.Hand, "grab.png" },
		{ BuiltInCursorPresetEnum.Holding, "grabbing.png" },
		{ BuiltInCursorPresetEnum.Chevron, "chevron.png" },
		{ BuiltInCursorPresetEnum.Dot, "dot.png" },
		{ BuiltInCursorPresetEnum.Plus, "plus.png" },
		{ BuiltInCursorPresetEnum.VerticalCross, "crosshair-vertical.png" },
		{ BuiltInCursorPresetEnum.TacticalVerticalCross, "crosshair-vertical-locked.png" },
		{ BuiltInCursorPresetEnum.DiagonalCross, "crosshair-diagonal.png" },
		{ BuiltInCursorPresetEnum.TacticalDiagonalCross, "crosshair-diagonal-locked.png" },
		{ BuiltInCursorPresetEnum.X, "x.png" },
	};

	public static void RegisterAsset()
	{
		RegisterType<BuiltInCursorAsset>();
	}

	public override void LoadResource()
	{
		InvokeResourceLoaded(GD.Load<Image>(Globals.BuiltInCursorLocation.PathJoin(CursorMapping[_cursorPreset])));
		CursorImage = ApplyCursorScale();
	}

	[ScriptEnum]
	public enum BuiltInCursorPresetEnum
	{
		Arrow,
		Pointer,
		Hand,
		Holding,
		Chevron,
		Dot,
		Plus,
		VerticalCross,
		TacticalVerticalCross,
		DiagonalCross,
		TacticalDiagonalCross,
		X,
	};
}
