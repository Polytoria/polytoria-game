function Entry()
	local bindable: BindableEvent = BindableEvent.New(Environment)
	local passes = false

	bindable.Invoked:Connect(function(...)
		assert(select("#", ...) == 3)
		local a, b, c = ...
		assert(a == "hi")
		assert(b == 123)
		assert(c == Vector3.New(1, 4, 2))
		passes = true
	end)
	bindable:Invoke("hi", 123, Vector3.New(1, 4, 2))

	wait(0)
	assert(passes)
end
