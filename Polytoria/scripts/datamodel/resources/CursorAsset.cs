// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Scripting;
using System;

namespace Polytoria.Datamodel.Resources;

[Abstract]
public partial class CursorAsset : ResourceAsset
{
	const int MAX_CURSOR_SIZE = 128;

	// Cursor image with applied scale!
	// Use this to load instead of Resource!
	public Image? CursorImage { get; private set; } = null;
	public event Action<Image?>? CursorLoaded;
	private Vector2 _hotspot = new(0, 0);
	private int _scale = 0;
	private Input.CursorShape? _cursorTarget = null;
	internal PTSignal CursorAdjustInternal { get; private set; } = new();

	[Editable, ScriptProperty]
	public Vector2 Hotspot
	{
		get => _hotspot;
		set
		{
			_hotspot = value;
			CursorAdjustInternal.Invoke();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public int Scale
	{
		get => _scale;
		set
		{
			_scale = Math.Clamp(value, 0, 128);
			ReloadImage();
			CursorAdjustInternal.Invoke();
			OnPropertyChanged();
		}
	}

	public override void PreDelete()
	{
		CursorAdjustInternal.DisconnectAll();
		base.PreDelete();
	}

	protected void ReloadImage()
	{
		if (Resource is DpiTexture dpiTex)
		{
			DpiTexture scaledTex = (DpiTexture)dpiTex.Duplicate();
			float largestBound = Math.Max(scaledTex.GetWidth(), scaledTex.GetHeight());

			if (Scale > 0)
				scaledTex.BaseScale = Scale / largestBound;
			else if (largestBound > MAX_CURSOR_SIZE)
				scaledTex.BaseScale = MAX_CURSOR_SIZE / largestBound;

			CursorImage = scaledTex.GetImage();
		}
		else if (Resource is Texture2D tex)
		{
			Image scaledImage = tex.GetImage();
			Vector2 imgSize = new(scaledImage.GetWidth(), scaledImage.GetHeight());
			float largestBound = Math.Max(imgSize.X, imgSize.Y);

			if (Scale > 0)
			{
				imgSize = (imgSize / largestBound) * Scale;
				scaledImage.Resize((int)imgSize.X, (int)imgSize.Y);
			}
			else if (largestBound > MAX_CURSOR_SIZE)
			{
				imgSize = (imgSize / largestBound) * MAX_CURSOR_SIZE;
				scaledImage.Resize((int)imgSize.X, (int)imgSize.Y);
			}

			CursorImage = scaledImage;
		}
		else
		{
			CursorImage = null;
		}

		CursorLoaded?.Invoke(CursorImage);
	}
}
