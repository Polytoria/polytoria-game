function Entry()
	local target = script.ScriptCallTarget

	assert(target.DaBall == "Ye")
	assert(target.a == nil)
	target.DaBall = 67
	assert(target.DaBall == 67)
	target.TestTarget()

	local grabbable = Grabbable.New(Environment)
	grabbable.PermissionPredicate = function(a, b)
		wait()
		return a + b, "done"
	end

	local result, marker = grabbable.PermissionPredicate(19, 23)
	assert(result == 42 and marker == "done")

	grabbable.PermissionPredicate = function(value)
		return value * 2
	end
	assert(grabbable.PermissionPredicate(21) == 42)
	grabbable:Delete()
end
