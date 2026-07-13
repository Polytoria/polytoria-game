function Entry()
	local children = getmetatable(Environment).__namecall(Environment, "GetChildren")
	assert(type(children) == "table")
	local root = getmetatable(Environment).__namecall(Environment, "FindChild", "ParentChildRoot")
	assert(root == Environment.ParentChildRoot)

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
end
