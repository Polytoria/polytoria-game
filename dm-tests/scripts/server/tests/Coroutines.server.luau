function Entry()
	do
		local c1 = false
		local c2 = false
		local c3 = false
		local c4 = false

		local co = coroutine.create(function()
			c2 = true
			coroutine.yield()
			c4 = true
		end)

		c1 = true
		coroutine.resume(co)
		c3 = true
		coroutine.resume(co)

		assert(c1, "c1 is false")
		assert(c2, "c2 is false")
		assert(c3, "c3 is false")
		assert(c4, "c4 is false")
	end

	do
		local co = coroutine.create(function()
		end)

		coroutine.resume(co)

		local ok, err = coroutine.resume(co)
		assert(ok == false)
		assert(type(err) == "string")
	end

	do
		local c1 = false
		local c2 = false
		local c3 = false

		local co = coroutine.create(function()
			c1 = true
			wait()
			c2 = true
		end)

		coroutine.resume(co)
		assert(c1, "c1 is false")
		assert(c2 == false)
		wait()
		assert(c2, "c2 is false")
		c3 = true
		assert(c3, "c3 is false")
	end

	do
		local c1 = false
		local c2 = false
		local c3 = false
		local c4 = false

		local f = coroutine.wrap(function()
			c2 = true
			coroutine.yield()
			c4 = true
		end)

		c1 = true
		f()
		c3 = true
		f()

		assert(c1, "c1 is false")
		assert(c2, "c2 is false")
		assert(c3, "c3 is false")
		assert(c4, "c4 is false")
	end

	do
		local c1 = false
		local c2 = false

		local f = coroutine.wrap(function()
			c1 = true
			wait()
			c2 = true
		end)

		f()
		assert(c1, "c1 is false")
		assert(c2 == false)
		wait()
		assert(c2, "c2 is false")
	end
end
