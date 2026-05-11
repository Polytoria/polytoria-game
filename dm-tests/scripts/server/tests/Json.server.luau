function Entry()
	assert(json.isNull(nil))
	assert(json.isNull(json.null()))

	assert(json.serialize(nil) == "null")
	assert(json.serialize(json.null()) == "null")
	assert(json.serialize({ a = json.null() }) == '{"a":null}')

	local p = json.parse('{"x":null}')
	assert(json.isNull(p.x))
	assert(json.serialize(p) == '{"x":null}')

	local t1 = { a = "hello", b = { c = "hi" } }
	local reparse = json.parse(json.serialize(t1))
	assert(reparse.a == t1.a)
	assert(reparse.b.c == t1.b.c)
end
