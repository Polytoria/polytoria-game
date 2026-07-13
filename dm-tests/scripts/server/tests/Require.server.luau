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

	local yielded = require(script.YieldingRequireTarget)
	assert(yielded.ready == true)

	local noReturnOk, noReturnError = pcall(function()
		require(script.NoReturnRequireTarget)
	end)
	assert(noReturnOk == false)
	assert(string.find(noReturnError, "must return a value") ~= nil)

	local failed, failureMessage = pcall(function()
		require(script.FailingRequireTarget)
	end)
	assert(failed == false)
	assert(string.find(failureMessage, "expected module failure") ~= nil)

	local retryOk, retryError = pcall(function()
		require(script.FailingRequireTarget)
	end)
	assert(retryOk == false)
	assert(string.find(retryError, "expected module failure") ~= nil)

	local cycleOk, cycleError = pcall(function()
		require(script.CycleA)
	end)
	assert(cycleOk == false)
	assert(string.find(cycleError, "circular module dependency") ~= nil)

	local target = script.RequireTarget
	local originalLink = target.LinkedScript
	target.LinkedScript = nil
	target.Source = "return { revision = 1 }"
	local revisionOne = require(target)
	assert(revisionOne.revision == 1)

	target.Source = "return { revision = 2 }"
	local revisionTwo = require(target)
	assert(revisionTwo.revision == 2)
	assert(revisionTwo ~= revisionOne)

	target.LinkedScript = originalLink
	local restored = require(target)
	assert(restored:Hello() == 2)
end
