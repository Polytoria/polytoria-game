// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;
using Polytoria.Enums;
using Polytoria.Scripting;
using Polytoria.Utils;
using System;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class UITextInput : UIView
{
	private readonly TextEdit _textEdit = new();
	private readonly LineEdit _lineEdit = new();

	private Color _textColor;
	private float _fontSize;
	private bool _autoSize;
	private float _maxAutoSize;
	private TextHorizontalAlignmentEnum _justify;
	private bool _multiLine;
	private string _placeholder = "";
	private Color _placeholderColor;
	private Color _readOnlyColor;
	private bool _readOnly;
	private BuiltInFontAsset.BuiltInTextFontPresetEnum _fontPreset;
	private FontAsset? _fontAsset;

	[Editable, ScriptProperty]
	public string Text
	{
		get
		{
			if (_lineEdit.Visible)
			{
				return _lineEdit.Text;
			}
			else if (_textEdit.Visible)
			{
				return _textEdit.Text;
			}
			return "";
		}
		set
		{
			_lineEdit.Text = value;
			_textEdit.Text = value;
			UpdateTextSize();
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
			_textEdit.AddThemeColorOverride("font_color", _textColor);
			_lineEdit.AddThemeColorOverride("font_color", _textColor);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public TextHorizontalAlignmentEnum JustifyText
	{
		get => _justify;
		set
		{
			_justify = value;

			switch (value)
			{
				case TextHorizontalAlignmentEnum.Left:
					_lineEdit.Alignment = Godot.HorizontalAlignment.Left;
					break;
				case TextHorizontalAlignmentEnum.Right:
					_lineEdit.Alignment = Godot.HorizontalAlignment.Right;
					break;
				case TextHorizontalAlignmentEnum.Center:
					_lineEdit.Alignment = Godot.HorizontalAlignment.Center;
					break;
			}
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
			UpdateTextSize();
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
			if (_autoSize) NodeControl.Resized += UpdateTextSize;
			else NodeControl.Resized -= UpdateTextSize;
			UpdateTextSize();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float MaxAutoSize
	{
		get => _maxAutoSize;
		set
		{
			_maxAutoSize = value;
			UpdateTextSize();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool MultiLine
	{
		get => _multiLine;
		set
		{
			_multiLine = value;
			_textEdit.Visible = _multiLine;
			_lineEdit.Visible = !_multiLine;
			UpdateTextSize();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public string Placeholder
	{
		get => _placeholder;
		set
		{
			_placeholder = value;
			_textEdit.PlaceholderText = _placeholder;
			_lineEdit.PlaceholderText = _placeholder;
			UpdateTextSize();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Color PlaceholderColor
	{
		get => _placeholderColor;
		set
		{
			_placeholderColor = value;
			_textEdit.AddThemeColorOverride("font_placeholder_color", _placeholderColor);
			_lineEdit.AddThemeColorOverride("font_placeholder_color", _placeholderColor);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Color ReadOnlyColor
	{
		get => _readOnlyColor;
		set
		{
			_readOnlyColor = value;
			_textEdit.AddThemeColorOverride("font_readonly_color", _readOnlyColor);
			_lineEdit.AddThemeColorOverride("font_uneditable_color", _readOnlyColor);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool ReadOnly
	{
		get => _readOnly;
		set
		{
			_readOnly = value;
			_textEdit.Editable = !_readOnly;
			_lineEdit.Editable = !_readOnly;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
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

	[ScriptProperty] public PTSignal<string> Submitted { get; private set; } = new();
	[ScriptProperty] public PTSignal<string> Changed { get; private set; } = new();
	[ScriptProperty] public PTSignal FocusEnter { get; private set; } = new();
	[ScriptProperty] public PTSignal FocusExit { get; private set; } = new();

	private void OnFontLoaded(Resource resource)
	{
		_textEdit.AddThemeFontOverride("font", (Font)resource);
		_lineEdit.AddThemeFontOverride("font", (Font)resource);
		UpdateTextSize();
	}

	private void UpdateTextSize()
	{
		if (_autoSize)
		{
			string textDisplayed = !string.IsNullOrEmpty(Text) ? Text : _placeholder;
			float autoSize = TextUtils.BoundsToTextSize(_textEdit.GetThemeFont("font"), textDisplayed, NodeControl.Size, _multiLine) / UILabel.FontScaleConversion;
			if (_maxAutoSize > 0 && autoSize > _maxAutoSize) SetTextSize(_maxAutoSize);
			else SetTextSize(autoSize);
		}
		else SetTextSize(_fontSize);
	}

	private void SetTextSize(float size)
	{
		int setto = Convert.ToInt32(size * UILabel.FontScaleConversion);
		_textEdit.AddThemeFontSizeOverride("font_size", setto);
		_lineEdit.AddThemeFontSizeOverride("font_size", setto);
	}

	public override void Init()
	{
		GDNode.AddChild(_textEdit, false, Node.InternalMode.Front);
		GDNode.AddChild(_lineEdit, false, Node.InternalMode.Front);
		_textEdit.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_lineEdit.SetAnchorsPreset(Control.LayoutPreset.FullRect);

		_textEdit.ContextMenuEnabled = false;
		_lineEdit.ContextMenuEnabled = false;

		StyleBoxEmpty empty = new();

		_textEdit.AddThemeStyleboxOverride("normal", empty);
		_textEdit.AddThemeStyleboxOverride("read_only", empty);
		_textEdit.AddThemeStyleboxOverride("focus", empty);
		_lineEdit.AddThemeStyleboxOverride("normal", empty);
		_lineEdit.AddThemeStyleboxOverride("read_only", empty);
		_lineEdit.AddThemeStyleboxOverride("focus", empty);
		_textEdit.MouseDefaultCursorShape = _lineEdit.MouseDefaultCursorShape = Control.CursorShape.Ibeam;
		_textEdit.AddThemeConstantOverride("line_spacing", 0);

		// Set pass
		_textEdit.MouseFilter = Control.MouseFilterEnum.Pass;
		_lineEdit.MouseFilter = Control.MouseFilterEnum.Pass;

		// Register focus enter/exit
		_textEdit.FocusEntered += OnTextEditFocusEntered;
		_textEdit.FocusExited += OnTextEditFocusExited;
		_lineEdit.FocusEntered += OnLineEditFocusEntered;
		_lineEdit.FocusExited += OnLineEditFocusExited;

		// Register text changed
		_textEdit.TextChanged += OnTextEditTextChanged;
		_lineEdit.TextChanged += OnLineEditTextChanged;
		_lineEdit.TextSubmitted += OnLineEditTextSubmitted;

		Text = "";
		Placeholder = "Type here...";
		TextColor = new(0, 0, 0);
		PlaceholderColor = new(0, 0, 0, 0.5f);
		ReadOnlyColor = new(0, 0, 0, 0.2f);
		MultiLine = false;
		FontSize = 16;
		AutoSize = false;
		ReadOnly = false;

		base.Init();
	}

	public override void PreDelete()
	{
		_textEdit.FocusEntered -= OnTextEditFocusEntered;
		_textEdit.FocusExited -= OnTextEditFocusExited;
		_lineEdit.FocusEntered -= OnLineEditFocusEntered;
		_lineEdit.FocusExited -= OnLineEditFocusExited;

		_textEdit.TextChanged -= OnTextEditTextChanged;
		_lineEdit.TextChanged -= OnLineEditTextChanged;
		_lineEdit.TextSubmitted -= OnLineEditTextSubmitted;

		base.PreDelete();
	}

	[ScriptMethod]
	public void Focus()
	{
		if (_textEdit.Visible) { _textEdit.GrabFocus(); }
		if (_lineEdit.Visible) { _lineEdit.GrabFocus(); }
	}

	private void OnTextEditFocusEntered()
	{
		FocusEnter.Invoke();
	}

	private void OnTextEditFocusExited()
	{
		FocusExit.Invoke();
	}

	private void OnLineEditFocusEntered()
	{
		FocusEnter.Invoke();
	}

	private void OnLineEditFocusExited()
	{
		FocusExit.Invoke();
	}

	private void OnTextEditTextChanged()
	{
		Changed.Invoke(_textEdit.Text);
		UpdateTextSize();
	}

	private void OnLineEditTextChanged(string txt)
	{
		Changed.Invoke(txt);
		UpdateTextSize();
	}

	private void OnLineEditTextSubmitted(string str)
	{
		Submitted.Invoke(str);
	}
}
