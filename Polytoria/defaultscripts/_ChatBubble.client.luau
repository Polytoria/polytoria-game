

local name = "_ChatBubble"

Chat.NewChatMessage:Connect(function(plr : Player, msg : string)
	if plr.UseBubbleChat then
		plr:FindChild(name):Speak(msg)
	end
end)

local lp = Players.LocalPlayer
local cb = lp:FindChild(name)
local cam = Environment.CurrentCamera

local locallocalpos = Vector3.New(0,3,0)

cb.LocalPosition = locallocalpos

if cam then
	cam.FirstPersonEntered:Connect(function()
		cb.Visible = false
	end)
	cam.FirstPersonExited:Connect(function()
		cb.Visible = true
	end)
end

