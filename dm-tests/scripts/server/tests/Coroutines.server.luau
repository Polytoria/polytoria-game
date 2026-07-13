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

	do
		local continued = false
		local co = coroutine.create(function()
			wait()
			continued = true
		end)

		assert(coroutine.resume(co))
		assert(coroutine.close(co))
		wait()
		wait()
		assert(continued == false)
	end

	do
		local completed = 0
		for _ = 1, 25 do
			coroutine.resume(coroutine.create(function()
				wait()
				completed += 1
			end))
		end

		wait()
		wait()
		assert(completed == 25)
	end

	do
		local co = coroutine.create(function(a, b)
			local resumedWith = coroutine.yield(a + b, "yielded")
			return resumedWith, "finished"
		end)

		local ok, sum, marker = coroutine.resume(co, 20, 22)
		assert(ok and sum == 42 and marker == "yielded")

		local finished, value, finalMarker = coroutine.resume(co, "resumed")
		assert(finished and value == "resumed" and finalMarker == "finished")
	end

	do
		local co = coroutine.create(function()
			error("expected coroutine failure")
		end)

		local ok, message = coroutine.resume(co)
		assert(ok == false)
		assert(string.find(message, "expected coroutine failure") ~= nil)
	end

	do
		local continued = false
		local co = coroutine.create(function()
			pcall(function()
				wait()
				continued = true
			end)
		end)

		assert(coroutine.resume(co))
		assert(coroutine.close(co))
		wait()
		wait()
		assert(continued == false)
	end

	do
		local completed = 0
		for _ = 1, 50 do
			spawn(function()
				wait()
				completed += 1
			end)
		end

		while completed < 50 do
			wait()
		end
		assert(completed == 50)
	end
end
