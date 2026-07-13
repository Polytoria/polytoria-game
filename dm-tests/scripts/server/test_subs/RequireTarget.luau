wait()

local module = {
	a = 1,
}

function module:Hello()
	assert(_SERVER == true)
	assert(_CLIENT == false)
	assert(script == ScriptService.Tests.Require.RequireTarget)
	return 2
end

return module
