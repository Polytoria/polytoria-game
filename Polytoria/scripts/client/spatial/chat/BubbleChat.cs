// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Shared;
using System.Collections.Generic;

namespace Polytoria.Client.UI.Chat;

public partial class BubbleChat : Node3D
{
	public string BubbleItemPath = "res://scenes/client/spatial/chat/bubble_item.tscn";
	private const int BubbleCountLimit = 5;
	public const float BubbleHeightPlus = 4f;
	private readonly List<BubbleItem> _activeBubbles = [];

	[Export] private Control _itemContainer = null!;
	private Player? TargetPlayer = null;

	public void SetTarget(Player? plr)
	{
		if (TargetPlayer != null) TargetPlayer.Chatted.Disconnect(OnPlayerChatted);
		TargetPlayer = plr;
		if (TargetPlayer != null) TargetPlayer.Chatted.Connect(OnPlayerChatted);
	}

	public override void _EnterTree()
	{
		if (TargetPlayer != null) TargetPlayer.Chatted.Connect(OnPlayerChatted);
		base._EnterTree();
	}

	public override void _ExitTree()
	{
		if (TargetPlayer != null) TargetPlayer.Chatted.Disconnect(OnPlayerChatted);
		base._ExitTree();
	}

	private void OnPlayerChatted(string msg)
	{
		if (TargetPlayer!.Character != null)
		{
			Aabb? bounds = (TargetPlayer.Character.Head ?? TargetPlayer.Character).CalculateBounds();
			if (bounds.HasValue)
			{
				int decrease = 0;
				if (TargetPlayer.IsLocal)
				{
					decrease = 1;
				}
				Position = new Vector3(0, bounds.Value.Size.Y + BubbleHeightPlus - decrease, 0);
			}
		}
		BubbleItem item = Globals.CreateInstanceFromScene<BubbleItem>(BubbleItemPath);
		item.Content = msg;
		_itemContainer.AddChild(item);

		_activeBubbles.Add(item);

		if (_activeBubbles.Count > BubbleCountLimit)
		{
			BubbleItem oldest = _activeBubbles[0];
			_activeBubbles.RemoveAt(0);
			if (IsInstanceValid(oldest))
			{
				oldest.Disappear();
			}
		}
	}
}
