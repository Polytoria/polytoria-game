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
		{ BuiltInCursorPresetEnum.Arrow, "arrow.svg" },
		{ BuiltInCursorPresetEnum.Pointer, "click.svg" },
		{ BuiltInCursorPresetEnum.Hand, "grab.svg" },
		{ BuiltInCursorPresetEnum.Holding, "grabbing.svg" },
		{ BuiltInCursorPresetEnum.Chevron, "chevron.svg" },
		{ BuiltInCursorPresetEnum.Dot, "dot.svg" },
		{ BuiltInCursorPresetEnum.Plus, "plus.svg" },
		{ BuiltInCursorPresetEnum.VerticalCross, "crosshair-vertical.svg" },
		{ BuiltInCursorPresetEnum.TacticalVerticalCross, "crosshair-vertical-dot.svg" },
		{ BuiltInCursorPresetEnum.DiagonalCross, "crosshair-tactical.svg" },
		{ BuiltInCursorPresetEnum.TacticalDiagonalCross, "crosshair-tactical-dot.svg" },
		{ BuiltInCursorPresetEnum.X, "x.svg" },
	};

	public static void RegisterAsset()
	{
		RegisterType<BuiltInCursorAsset>();
	}

	public override void LoadResource()
	{
		InvokeResourceLoaded(GD.Load<DpiTexture>(Globals.BuiltInCursorLocation.PathJoin(CursorMapping[_cursorPreset])));
		ReloadImage();
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
