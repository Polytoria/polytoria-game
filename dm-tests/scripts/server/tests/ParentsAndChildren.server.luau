function Entry()
	local Root = Environment.ParentChildRoot
	assert(Root ~= nil)

	local Child = Root:FindChild("One")
	local Invalid = Root:FindChild("Invalid")
	assert(Child ~= nil)
	assert(Invalid == nil)

	local Three = Root:FindDescendant("One.Two.Three")
	local Five = Root:FindDescendant("Three.Five")
	local Seven = Root:FindDescendant("One.Four.Seven")
	assert(Three ~= nil)
	assert(Five == nil)
	assert(Seven == nil)

	local part = Instance.New("Part")
	part.Parent = Environment
	local texture = Instance.New("PartTexture")
	texture.Parent = part
	assert(texture.Parent == part)

	local other = Instance.New("Part")
	other.Parent = Environment
	texture.Parent = other
	assert(texture.Parent == other)
	assert(part:FindChild("PartTexture") == nil)

	assert(texture.Mode == Enums.TextureMode.Stretch)
	texture.Mode = Enums.TextureMode.Tile
	assert(texture.Mode == Enums.TextureMode.Tile)
	texture.TileSize = Vector2.New(4, 2)
	assert(texture.TileSize.X == 4 and texture.TileSize.Y == 2)
	texture.Offset = Vector2.New(1, 0.5)
	assert(texture.Offset.X == 1 and texture.Offset.Y == 0.5)

	texture:Destroy()
	other:Destroy()
	part:Destroy()
end
