using Godot;
using Polytoria.Shared;
using System;
using System.Threading.Tasks;

namespace Polytoria.Creator.UI.TextEditor;

public partial class TextEditorTooltip : Control
{
	/// <summary>
	/// Stores the maximum allowed dimensions for the associated element, in pixels.
	/// </summary>
	private static readonly Vector2 MaxDimensions = new(600, 300);

	/// <summary>
	/// Gets or sets the number of pixels to offset the width. Only change this if required.
	/// </summary>
	private const float WidthOffset = 0;

	/// <summary>
	/// Gets or sets the offset applied to the position, in pixels.
	/// By default, the tooltip is shown at the bottom left of the hovered character.
	/// </summary>
	private static readonly Vector2 PositionOffset = new(-8, 0);

	[Export]
	private RichTextLabel _label = null!;

	[Export]
	private PanelContainer _tooltipPanel = null!;

	public override void _Ready()
	{
		Visible = false;
		Position = Vector2.Zero;
	}

	private async Task UpdatePanelSize()
	{
		_tooltipPanel.Size = Vector2.Zero;
		Size = Vector2.Zero;
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	// NOTE: If there is a more efficient way of doing this, I am open to discussions on it.
	/// <summary>
	/// Updates the size of the tooltip. This requires waiting for about 3 frames.
	/// </summary>
	private async Task UpdateSize()
	{
		var stylebox = _tooltipPanel.GetThemeStylebox("panel");
		var marginL = stylebox.ContentMarginLeft;
		var marginR = stylebox.ContentMarginRight;
		var marginT = stylebox.ContentMarginTop;
		var marginB = stylebox.ContentMarginBottom;

		var maxTextWidth = MaxDimensions.X - marginL - marginR;
		var maxTextHeight = MaxDimensions.Y - marginT - marginB;

		_label.CustomMinimumSize = Vector2.Zero;
		_label.AutowrapMode = TextServer.AutowrapMode.Off;
		_label.ScrollToLine(0);
		await UpdatePanelSize();

		var rawWidth = _label.GetContentWidth();
		var targetWidth = MathF.Ceiling(Math.Min(rawWidth + WidthOffset, maxTextWidth));

		_label.CustomMinimumSize = new(targetWidth, 0);

		if (rawWidth > maxTextWidth)
		{
			_label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		}

		await UpdatePanelSize();

		var wrappedHeight = _label.GetContentHeight();
		var targetHeight = MathF.Ceiling(Math.Min(wrappedHeight, maxTextHeight));
		bool needsScroll = wrappedHeight > maxTextHeight;

		if (needsScroll)
		{
			// Account for scroll bar's length
			targetWidth += _label.GetVScrollBar().Size.X;
		}

		_label.CustomMinimumSize = new(targetWidth, targetHeight);
		await UpdatePanelSize();

		Size = _tooltipPanel.Size;
	}

	/// <summary>
	/// Updates the tooltip text and repositions the tooltip relative to the specified character rectangle.
	/// </summary>
	/// <param name="text">The text to display in the tooltip. If null, empty, or whitespace, the tooltip will be hidden.</param>
	/// <param name="charGlobalRect">The global rectangle representing the character's position and size, used to position the tooltip.</param>
	public async void UpdateTooltip(string text, Rect2 charGlobalRect)
	{
		Visible = false;

		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}

		_label.Text = text;
		await UpdateSize();

		// In case a new tooltip is being shown, we don't go any further.
		if (_label.Text != text)
		{
			return;
		}

		Vector2 tooltipPos = charGlobalRect.Position + new Vector2(0, charGlobalRect.Size.Y) + PositionOffset;
		Vector2 windowBounds = GetViewportRect().Size;

		// If the tooltip goes beyond the screen, cap the position to be within the screen bounds
		if (tooltipPos.X + Size.X > windowBounds.X)
		{
			tooltipPos.X = windowBounds.X - Size.X;
		}

		if (tooltipPos.Y + Size.Y > windowBounds.Y)
		{
			tooltipPos.Y = windowBounds.Y - Size.Y;
		}

		GlobalPosition = tooltipPos;
		Visible = true;
	}
}
