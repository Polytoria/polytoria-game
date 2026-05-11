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
	[Export(PropertyHint.None, "suffix:px")]
	private Vector2 _maxDimensions;

	[Export]
	private RichTextLabel _label = null!;

	[Export]
	private PanelContainer _tooltipPanel = null!;

	/// <summary>
	/// Gets or sets the number of pixels to offset the width.
	/// If, at some point, the tooltip causes unwanted line breaks, increasing this value may help.
	/// It is best to keep this value as low as possible, as it adds extra space in the tooltip.
	/// </summary>
	[Export(PropertyHint.None, "suffix:px")]
	private float _widthOffset = 0f;

	/// <summary>
	/// Gets or sets the offset applied to the position, in pixels.
	/// The tooltip is shown at the bottom left of the hovered character,
	/// so a positive X value moves it to the right, and a positive Y value moves it down.
	/// </summary>
	[Export(PropertyHint.None, "suffix:px")]
	private Vector2 _positionOffset = new(0, 0);

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

	/// <summary>
	/// Updates the tooltip text and repositions the tooltip relative to the specified character rectangle.
	/// </summary>
	/// <remarks>
	/// If the tooltip text is changed during the update, the method will not reposition or show the
	/// tooltip. The tooltip is automatically hidden if the provided text is null, empty, or consists only of
	/// whitespace.
	/// </remarks>
	/// <param name="text">The text to display in the tooltip. If null, empty, or whitespace, the tooltip will be hidden.</param>
	/// <param name="charGlobalRect">The global rectangle representing the character's position and size, used to position the tooltip.</param>
	/// <returns>A task that represents the asynchronous operation.</returns>
	public async Task UpdateTooltip(string text, Rect2 charGlobalRect)
	{
		Visible = false;

		if (string.IsNullOrWhiteSpace(text))
		{
			// Hide this node
			return;
		}

		_label.Text = text;
		await UpdateSize();

		// In case a new tooltip is being shown, we don't go any further.
		if (_label.Text != text)
		{
			return;
		}

		Vector2 tooltipPos = charGlobalRect.Position + new Vector2(0, charGlobalRect.Size.Y) + _positionOffset;
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
