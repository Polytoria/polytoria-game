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
		-- wait yields no immediate values to the coroutine caller, then returns
		-- the elapsed time when the scheduler resumes the waiting thread.
		local elapsed = nil
		local co = coroutine.create(function()
			elapsed = wait()
		end)

		local resumeResults = table.pack(coroutine.resume(co))
		assert(resumeResults.n == 1, "wait leaked a value while yielding")
		assert(resumeResults[1] == true, "coroutine failed to start")

		wait()
		assert(type(elapsed) == "number", "wait did not return elapsed time")
		assert(elapsed >= 0, "wait returned a negative elapsed time")
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
		local c1 = false
		local c2 = false
		local c3 = false
		local value: IntValue = Instance.New("IntValue")

		value.Changed:Connect(function()
			spawn(function()
				c1 = true
				wait()
				c2 = true
			end)
			c3 = true
		end)

		value.Value = 1
		assert(c1, "c1 is false")
		assert(c2 == false)
		assert(c3, "c3 is false")
		wait()
		assert(c2, "c2 is false")
	end

	do
		local co = coroutine.create(function(a)
			local b = coroutine.yield(a + 1)
			return b + 1
		end)

		local ok, result = coroutine.resume(co, 4)
		assert(ok)
		assert(result == 5)

		ok, result = coroutine.resume(co, 9)
		assert(ok)
		assert(result == 10)
	end
end
