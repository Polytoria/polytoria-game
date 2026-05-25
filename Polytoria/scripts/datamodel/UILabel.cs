// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;
using Polytoria.Enums;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class UILabel : UIView
{
	private readonly Label _label = new();
	private readonly RichTextLabel _richLabel = new();

	private string _text = "";
	private Color _textColor;
	private float _fontSize;
	private bool _autoSize;
	private bool _useRichText;
	private TextHorizontalAlignmentEnum _justify;
	private TextVerticalAlignmentEnum _verticalAlign;
	private BuiltInFontAsset.BuiltInTextFontPresetEnum _fontPreset;
	private FontAsset? _fontAsset;
	private float _outlineWidth;
	private Color _outlineColor;
	private TextTrimmingEnum _textTrimming;
	private bool _textWrapped;

	public const float FontScaleConversion = 1.35f;

	[Editable, ScriptProperty]
	public string Text
	{
		get => _text;
		set
		{
			_text = value;
			_richLabel.Text = _text;
			_label.Text = _text;
			if (_autoSize) UpdateAutosize();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Color TextColor
	{
		get => _textColor;
		set
		{
			_textColor = value;
			_label.AddThemeColorOverride("font_color", _textColor);
			_richLabel.AddThemeColorOverride("default_color", _textColor);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float OutlineWidth
	{
		get => _outlineWidth;
		set
		{
			_outlineWidth = value;
			_label.AddThemeConstantOverride("outline_size", (int)value);
			_richLabel.AddThemeConstantOverride("outline_size", (int)value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Color OutlineColor
	{
		get => _outlineColor;
		set
		{
			_outlineColor = value;
			_label.AddThemeColorOverride("font_outline_color", value);
			_richLabel.AddThemeColorOverride("font_outline_color", value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public TextHorizontalAlignmentEnum HorizontalAlignment
	{
		get => _justify;
		set
		{
			_justify = value;

			switch (value)
			{
				case TextHorizontalAlignmentEnum.Left:
					_label.HorizontalAlignment = Godot.HorizontalAlignment.Left;
					break;
				case TextHorizontalAlignmentEnum.Right:
					_label.HorizontalAlignment = Godot.HorizontalAlignment.Right;
					break;
				case TextHorizontalAlignmentEnum.Center:
					_label.HorizontalAlignment = Godot.HorizontalAlignment.Center;
					break;
			}

			_richLabel.HorizontalAlignment = _label.HorizontalAlignment;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public TextVerticalAlignmentEnum VerticalAlignment
	{
		get => _verticalAlign;
		set
		{
			_verticalAlign = value;

			switch (value)
			{
				case TextVerticalAlignmentEnum.Top:
					_label.VerticalAlignment = Godot.VerticalAlignment.Top;
					break;
				case TextVerticalAlignmentEnum.Middle:
					_label.VerticalAlignment = Godot.VerticalAlignment.Center;
					break;
				case TextVerticalAlignmentEnum.Bottom:
					_label.VerticalAlignment = Godot.VerticalAlignment.Bottom;
					break;
			}
			;

			_richLabel.VerticalAlignment = _label.VerticalAlignment;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float FontSize
	{
		get => _fontSize;
		set
		{
			_fontSize = value;
			if (!_autoSize) SetTextSize(_fontSize);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool AutoSize
	{
		get => _autoSize;
		set
		{
			_autoSize = value;
			if (_autoSize)
			{
				NodeControl.Resized += UpdateAutosize;
				UpdateAutosize();
			}
			else
			{
				NodeControl.Resized -= UpdateAutosize;
				SetTextSize(_fontSize);
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool UseRichText
	{
		get => _useRichText;
		set
		{
			_useRichText = value;
			_label.Visible = !_useRichText;
			_richLabel.Visible = _useRichText;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, Export]
	public FontAsset? FontAsset
	{
		get => _fontAsset;
		set
		{
			if (_fontAsset != null && _fontAsset != value)
			{
				_fontAsset.ResourceLoaded -= OnFontLoaded;
				_fontAsset.UnlinkFrom(this);
			}
			_fontAsset = value;
			if (_fontAsset != null)
			{
				_fontAsset.LinkTo(this);
				_fontAsset.ResourceLoaded += OnFontLoaded;

				if (_fontAsset.IsResourceLoaded && _fontAsset.Resource != null)
				{
					OnFontLoaded(_fontAsset.Resource);
				}
				else
				{
					_fontAsset.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use FontAsset instead"), CloneIgnore]
	public BuiltInFontAsset.BuiltInTextFontPresetEnum Font
	{
		get => _fontPreset;
		set
		{
			_fontPreset = value;
			FontAsset = new BuiltInFontAsset()
			{
				FontPreset = _fontPreset
			};
		}
	}

	[Editable, ScriptProperty, DefaultValue(TextTrimmingEnum.None)]
	public TextTrimmingEnum TextTrimming
	{
		get => _textTrimming;
		set
		{
			_textTrimming = value;
			_label.TextOverrunBehavior = value switch
			{
				TextTrimmingEnum.None => TextServer.OverrunBehavior.NoTrimming,
				TextTrimmingEnum.Character => TextServer.OverrunBehavior.TrimChar,
				TextTrimmingEnum.Word => TextServer.OverrunBehavior.TrimWord,
				TextTrimmingEnum.CharacterEllipsis => TextServer.OverrunBehavior.TrimEllipsis,
				TextTrimmingEnum.WordEllipsis => TextServer.OverrunBehavior.TrimWordEllipsis,
				_ => TextServer.OverrunBehavior.NoTrimming,
			};
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool TextWrapped
	{
		get => _textWrapped;
		set
		{
			_textWrapped = value;

			_label.AutowrapMode = _richLabel.AutowrapMode = _textWrapped ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off;

			if (_autoSize) UpdateAutosize();
			OnPropertyChanged();
		}
	}

	private void UpdateAutosize()
	{
		Font font = _label.GetThemeFont("font");
		Vector2 bounds = NodeControl.Size;
		int lo = 1, hi = 512, result = 0;

		while (lo <= hi)
		{
			int mid = (lo + hi) / 2;
			int scaled = (int)(mid * FontScaleConversion);
			Vector2 textBounds;
			if (_textWrapped)
			{
				textBounds = font.GetMultilineStringSize(
					text: _text,
					alignment: _label.HorizontalAlignment,
					width: bounds.X,
					fontSize: scaled
				);
			}
			else textBounds = font.GetStringSize(_text, _label.HorizontalAlignment, -1, scaled);

			if (textBounds.X <= bounds.X && textBounds.Y <= bounds.Y)
			{
				result = mid;
				lo = mid + 1;
			}
			else hi = mid - 1;
		}

		SetTextSize(result);
	}

	private void SetTextSize(float size)
	{
		int setto = (int)(size * FontScaleConversion);
		_label.AddThemeFontSizeOverride("font_size", setto);
		_richLabel.AddThemeFontSizeOverride("normal_font_size", setto);
		_richLabel.AddThemeFontSizeOverride("bold_font_size", setto);
		_richLabel.AddThemeFontSizeOverride("bold_italics_font_size", setto);
		_richLabel.AddThemeFontSizeOverride("italics_font_size", setto);
		_richLabel.AddThemeFontSizeOverride("mono_font_size", setto);
	}

	private void OnFontLoaded(Resource resource)
	{
		_label.AddThemeFontOverride("font", (Font)resource);
		_richLabel.AddThemeFontOverride("normal_font", (Font)resource);
		_richLabel.AddThemeFontOverride("bold_fonte", (Font)resource);
		_richLabel.AddThemeFontOverride("bold_italics_font", (Font)resource);
		_richLabel.AddThemeFontOverride("italics_font", (Font)resource);
		_richLabel.AddThemeFontOverride("mono_font", (Font)resource);
		if (_autoSize) UpdateAutosize();
	}

	public override void Init()
	{
		GDNode.AddChild(_label, false, @internal: Node.InternalMode.Front);
		GDNode.AddChild(_richLabel, false, @internal: Node.InternalMode.Front);
		_richLabel.Text = "";
		_richLabel.BbcodeEnabled = true;
		_richLabel.ScrollActive = false;
		_richLabel.ShortcutKeysEnabled = false;
		_richLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
		_richLabel.ClipContents = false;
		_richLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		_label.ClipContents = false;
		_label.AddThemeConstantOverride("line_spacing", 0);

		Text = "Text";
		TextColor = new(0, 0, 0);
		FontSize = 16;
		HorizontalAlignment = TextHorizontalAlignmentEnum.Center;
		VerticalAlignment = TextVerticalAlignmentEnum.Middle;
		UseRichText = false;
		OutlineWidth = 0;
		OutlineColor = new(0, 0, 0);

		base.Init();
	}
}
