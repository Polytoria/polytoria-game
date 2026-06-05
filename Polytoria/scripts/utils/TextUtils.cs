// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Utils;

public static class TextUtils
{
	public static int BoundsToTextSize(Font font, string text, Vector2 bounds, bool isWrapped)
	{
		int lo = 1, hi = 512, result = 0;

		while (lo <= hi)
		{
			int mid = (lo + hi) / 2;
			Vector2 textBounds;
			if (isWrapped)
			{
				textBounds = font.GetMultilineStringSize(
					text: text,
					alignment: HorizontalAlignment.Center,
					width: bounds.X,
					fontSize: mid
				);
			}
			else textBounds = font.GetStringSize(text, HorizontalAlignment.Center, -1, mid);

			if (textBounds.X <= bounds.X && textBounds.Y <= bounds.Y)
			{
				result = mid;
				lo = mid + 1;
			}
			else hi = mid - 1;
		}

		return result;
	}
}
