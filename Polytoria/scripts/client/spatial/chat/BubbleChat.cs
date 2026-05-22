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
	public const float BubbleHeightMinus = 2f;
	private readonly List<BubbleItem> _activeBubbles = [];

	[Export] private Control _itemContainer = null!;
	public Player TargetPlayer = null!;

	public override void _EnterTree()
	{
		TargetPlayer.Chatted.Connect(OnPlayerChatted);
		base._EnterTree();
	}

	public override void _ExitTree()
	{
		TargetPlayer.Chatted.Disconnect(OnPlayerChatted);
		base._ExitTree();
	}

	private void OnPlayerChatted(string msg)
	{
		Aabb? bounds = TargetPlayer.CalculateBounds();

		if (bounds.HasValue)
		{
			int additional = 0;
			if (TargetPlayer.IsLocal)
			{
				additional = 1;
			}
			Position = new Vector3(0, bounds.Value.Size.Y - BubbleHeightMinus - additional, 0);
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
