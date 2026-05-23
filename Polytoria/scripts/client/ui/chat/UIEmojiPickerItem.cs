// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Client.UI.Chat;

public partial class UIEmojiPickerItem : Button
{
	public string EmojiName { get; private set; } = "";

	private TextureRect _iconRect = null!;
	private RichTextLabel _nameLabel = null!;

	private static readonly StyleBoxFlat _selectedStyle = new()
	{
		BgColor = new Color(0, 0.592f, 1, 0.25f),
		CornerRadiusTopLeft = 6,
		CornerRadiusTopRight = 6,
		CornerRadiusBottomRight = 6,
		CornerRadiusBottomLeft = 6,
		CornerDetail = 5,
	};

	private bool _isSelected;
	public bool IsSelected
	{
		get => _isSelected;
		set
		{
			_isSelected = value;
			if (value)
				AddThemeStyleboxOverride("normal", _selectedStyle);
			else
				RemoveThemeStyleboxOverride("normal");
		}
	}

	public void Initialize(string emojiName, string texturePath, int emojiSize)
	{
		_iconRect = GetNode<TextureRect>("VBox/Icon");
		_nameLabel = GetNode<RichTextLabel>("VBox/Name");
		EmojiName = emojiName;
		_iconRect.Texture = GD.Load<Texture2D>(texturePath);
		TooltipText = $":{emojiName}:";
		_nameLabel.Visible = false;
		CustomMinimumSize = new Vector2(56, 56);
	}
}
