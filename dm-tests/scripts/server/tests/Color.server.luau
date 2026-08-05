function Entry()
	assert(Color.Black.R == 0 and Color.Black.G == 0 and Color.Black.B == 0)
	assert(Color.White.R == 1 and Color.White.G == 1 and Color.White.B == 1)
	assert(Color.Red.R == 1 and Color.Red.G == 0 and Color.Red.B == 0)
	assert(Color.Green.R == 0 and Color.Green.G == 1 and Color.Green.B == 0)
	assert(Color.Blue.R == 0 and Color.Blue.G == 0 and Color.Blue.B == 1)
	assert(Color.Yellow.R == 1 and Color.Yellow.G == 1 and Color.Yellow.B == 0)
	assert(Color.Cyan.R == 0 and Color.Cyan.G == 1 and Color.Cyan.B == 1)
	assert(Color.Magenta.R == 1 and Color.Magenta.G == 0 and Color.Magenta.B == 1)
	assert(Color.Gray.R == 0.5 and Color.Gray.G == 0.5 and Color.Gray.B == 0.5)

	-- Everything is opaque except Clear
	assert(Color.Red.A == 1)
	assert(Color.White.A == 1)
	assert(Color.Clear.A == 0)

	-- A constant equals the same colour built by hand
	assert(Color.Red == Color.New(1, 0, 0))
	assert(Color.Clear == Color.New(0, 0, 0, 0))

	-- Constants hand out a new value on every read. Writing to one must not
	-- change what the constant means for the next reader.
	local mutated = Color.Red
	mutated.R = 0
	mutated.G = 1
	assert(Color.Red.R == 1 and Color.Red.G == 0)
end
