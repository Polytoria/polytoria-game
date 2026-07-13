function Entry()
	local first
	local second
	spawn(function()
		first = require(script.RequireTarget)
	end)
	spawn(function()
		second = require(script.RequireTarget)
	end)

	while first == nil or second == nil do
		wait()
	end

	assert(first == second)
	local requirez = first
	assert(requirez:Hello() == 2)
	assert(requirez.a == 1)
	requirez.a = 2

	local requirez2 = require(script.RequireTarget)
	assert(requirez2.a == 2)
end
