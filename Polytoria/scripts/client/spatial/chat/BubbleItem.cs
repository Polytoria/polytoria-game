// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Polytoria.Client.UI.Chat;

public partial class BubbleItem : Control
{
	private const float BubbleTimeLength = 5;
	private const int BigEmojiSize = 64;
	private const int InlineEmojiSize = 42;
	private const int EmojiPadding = 16;
	private const int EmojiColumns = 4;
	private const int EmojiSpacing = 10;

	private static readonly Regex EmojiOnlyRegex = new(@"^\s*(\[img=\d+x\d+\][^\[]*\[/img\]\s*)+$", RegexOptions.Compiled);
	private static readonly Regex ImgSizeRegex = new(@"\[img=\d+x\d+\]", RegexOptions.Compiled);

	private AnimationPlayer _animPlay = null!;
	public string Content = null!;

	public override async void _Ready()
	{
		Visible = false;
		_animPlay = GetNode<AnimationPlayer>("AnimationPlayer");
		Label testLabel = GetNode<Label>("TestLabel");
		RichTextLabel textLabel = GetNode<RichTextLabel>("Pivot/Layout/Container/RichTextLabel");
		Control bubbleLayout = GetNode<Control>("Pivot/Layout");

		await ApplyLabelContent(textLabel, testLabel);

		// Wait for layout to settle
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		float targetHeight = bubbleLayout.Size.Y;
		CustomMinimumSize = new Vector2(0, targetHeight);

		// Wait one more frame for the container to reflow
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

		Tween tween = CreateTween();
		tween.TweenProperty(this, "custom_minimum_size", new Vector2(0, targetHeight), 0.4f)
			.SetEase(Tween.EaseType.Out)
			.SetTrans(Tween.TransitionType.Back);
		tween.Play();

		_animPlay.Play("appear");
		Visible = true;

		await ToSignal(GetTree().CreateTimer(BubbleTimeLength), Timer.SignalName.Timeout);
		Disappear();
	}

	private async Task ApplyLabelContent(RichTextLabel textLabel, Label testLabel)
	{
		string trimmedContent = Content.Trim();

		int emojiCount = ImgSizeRegex.Matches(trimmedContent).Count;
		bool isEmojiOnly = EmojiOnlyRegex.IsMatch(trimmedContent);

		if (isEmojiOnly && emojiCount is > 0 and <= EmojiColumns)
		{
			textLabel.Text = ImgSizeRegex.Replace(Content, $"[img={BigEmojiSize}x{BigEmojiSize}]");
			testLabel.Visible = false;

			await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

			int width = emojiCount * BigEmojiSize + (emojiCount - 1) * EmojiSpacing + EmojiPadding;
			textLabel.CustomMinimumSize = new(width, 0);
		}
		else
		{
			textLabel.Text = ImgSizeRegex.Replace(Content, $"[img={InlineEmojiSize}x{InlineEmojiSize}]");
			testLabel.Text = Content;

			await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

			testLabel.Visible = false;

			// Apply size based on test label
			textLabel.CustomMinimumSize = new(Mathf.Clamp(testLabel.Size.X + 48, 48, 320), 0);
		}

		// Wait for the layout to fully recompute with the updated minimum size
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
	}

	public async void Disappear()
	{
		if (!IsInsideTree())
		{
			return;
		}

		_animPlay.Play("disappear");
		await ToSignal(_animPlay, AnimationPlayer.SignalName.AnimationFinished);
		QueueFree();
	}
}
