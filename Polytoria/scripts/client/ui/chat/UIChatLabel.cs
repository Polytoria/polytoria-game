// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;

namespace Polytoria.Client.UI.Chat;

public partial class UIChatLabel : RichTextLabel
{
	private bool _isPending = false;
	private bool _isDeclined = false;
	private string _content = "";

	public string Content
	{
		get => _content;
		set
		{
			_content = value;
			UpdateContent();
		}
	}

	public Color NameColor = new(1, 1, 1);
	public string AuthorName = null!;
	public Player? AuthorPlayer;
	public bool ChatColorsEnabled = true;
	public string FontPath = "";
	public int FontSize;

	public bool IsPending
	{
		get => _isPending;
		set
		{
			_isPending = value;

			SelfModulate = _isPending ? new Color(1, 1, 1, 0.3f) : new Color(1, 1, 1, 1);
		}
	}
	public bool IsDeclined
	{
		get => _isDeclined;
		set
		{
			_isDeclined = value;
			Visible = !_isDeclined;
		}
	}

	public override void _Ready()
	{
		UpdateContent();
	}

	private string GetBadgeBBCode(int fontSize)
	{
		if (AuthorPlayer == null)
			return "";

		string badgePath = Player.GetBadgeIconPath(AuthorPlayer);
		if (badgePath.Length == 0)
			return "";

		int size = fontSize > 0 ? Mathf.Max(fontSize, 16) : 16;
		return $"[img={size}x{size}]{badgePath}[/img]";
	}

	private void UpdateContent()
	{
		string badgeBBCode = GetBadgeBBCode(FontSize);
		string text;
		if (AuthorName == "")
		{
			text = Content;
		}
		else
		{
			string nameText = ChatColorsEnabled
				? $"[color={NameColor.ToHtml(false)}]{AuthorName}[/color]"
				: AuthorName;
			text = $"{badgeBBCode}{(badgeBBCode.Length > 0 ? " " : "")}{nameText}: {Content}";
		}

		if (!string.IsNullOrEmpty(FontPath))
			text = $"[font={FontPath}]{text}[/font]";

		if (FontSize > 0)
			text = $"[font_size={FontSize}]{text}[/font_size]";

		Text = text;
	}
}
