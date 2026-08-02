// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Scripting;
using Polytoria.Shared;
using Polytoria.Client.UI.Chat;
using Polytoria.Networking;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class VoiceBox : Part
{
	private BubbleText _bubbleText = null!;
	public const string BubbleChatScene = "res://scenes/client/spatial/chat/bubble_text.tscn";
	public PTSignal<string> CreateChatBubble { get; private set; } = new();

	[ScriptMethod]
	public void Speak(string msg)
	{
		if (Root.Network.IsServer)
		{
			Rpc(nameof(SpeakInternal), msg);
		}
		else
		{
			CreateChatBubble.Invoke(msg);
		}
	}
	[NetRpc(AuthorityMode.Authority, CallLocal = true, TransferMode = TransferMode.Reliable)]
	private async void SpeakInternal(string msg)
	{
		CreateChatBubble.Invoke(msg);
	}
	public override void Init()
	{
		base.Init();

		_bubbleText = Globals.CreateInstanceFromScene<BubbleText>(BubbleChatScene);
		_bubbleText.TargetBox = this;
		_bubbleText.Visible = true;
		GDNode.AddChild(_bubbleText, @internal: Node.InternalMode.Back);
		excludedBoundNodes.Add(_bubbleText);
	}
}
