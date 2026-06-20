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
end
