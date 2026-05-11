using Godot;
using Polytoria.Shared;
using System;
using System.Threading.Tasks;

namespace Polytoria.Creator.UI.TextEditor;

public partial class TextEditorTooltip : Control
{
	[Export(PropertyHint.None, "suffix:px")]
	private Vector2 _maxDimensions;

	[Export]
	private RichTextLabel _label = null!;

	[Export]
	private PanelContainer _tooltipPanel = null!;

	[Export]
	private float _widthOffset = 4.0f;

	[Export]
	private Vector2 _positionOffset = new(8, 8);

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		Visible = false;
		Position = Vector2.Zero;
	}

	// NOTE: If there is a more efficient way of doing this, I am open to discussions on it.
	/// <summary>
	/// Updates the size of the tooltip. This requires waiting for about 2 frames.
	/// </summary>
	/// <returns></returns>
	private async Task UpdateSize()
	{
		var stylebox = _tooltipPanel.GetThemeStylebox("panel");
		var marginL = stylebox.ContentMarginLeft;
		var marginR = stylebox.ContentMarginRight;
		var marginT = stylebox.ContentMarginTop;
		var marginB = stylebox.ContentMarginBottom;

		var maxTextWidth = _maxDimensions.X - marginL - marginR;
		var maxTextHeight = _maxDimensions.Y - marginT - marginB;

		_label.CustomMinimumSize = Vector2.Zero;
		_label.AutowrapMode = TextServer.AutowrapMode.Off;
		_label.ScrollToLine(0);

		_tooltipPanel.Size = Vector2.Zero;
		Size = Vector2.Zero;
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var rawWidth = _label.GetContentWidth();
		var targetWidth = MathF.Ceiling(Math.Min(rawWidth + _widthOffset, maxTextWidth));

		_label.CustomMinimumSize = new(targetWidth, 0);

		if (rawWidth > maxTextWidth)
		{
			_label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		}

		var wrappedHeight = _label.GetContentHeight();
		var targetHeight = MathF.Ceiling(Math.Min(wrappedHeight, maxTextHeight));
		bool needsScroll = wrappedHeight > maxTextHeight;

		if (needsScroll)
		{
			// Account for scroll bar's length
			targetWidth += _label.GetVScrollBar().Size.X;
		}

		_label.CustomMinimumSize = new(targetWidth, targetHeight);

		_tooltipPanel.Size = Vector2.Zero;
		Size = Vector2.Zero;
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		Size = _tooltipPanel.Size;
	}

	public async Task UpdateTooltip(string text)
	{
		Visible = false;

		if (string.IsNullOrWhiteSpace(text))
		{
			// Hide this node
			return;
		}

		Vector2 tooltipPos = GetGlobalMousePosition() + _positionOffset;

		_label.Text = text;
		await UpdateSize();

		// In case a new tooltip is being shown, we don't go any further.
		if (_label.Text != text)
		{
			return;
		}

		GlobalPosition = tooltipPos;
		Visible = true;
	}
}
