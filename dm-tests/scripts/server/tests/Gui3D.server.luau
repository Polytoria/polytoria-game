function Entry()
	local gui = Instance.New("GUI3D")
	gui.Parent = Environment
	gui.FaceCamera = true
	wait(0.1)

	local gui2 = Instance.New("GUI3D")
	gui2.Parent = Environment
	gui2.FaceCamera = true

	gui:Destroy()
	wait(0.1)

	assert(gui2.FaceCamera == true)
	gui2.FaceCamera = false
	assert(gui2.FaceCamera == false)

	local field = Instance.New("UIField")
	field.Parent = Environment
	field.SizeOffset = Vector2.New(120, 60)
	wait(0.1)
	assert(field.SizeOffset.X == 120 and field.SizeOffset.Y == 60)
	field:Destroy()
	gui2:Destroy()
end
