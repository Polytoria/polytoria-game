function Entry()
	assert(Vector2.One + Vector2.One == Vector2.One * 2)
	assert(Vector3.One + Vector3.One == Vector3.One * 2)

	assert(Vector2.One - Vector2.One == Vector2.Zero)
	assert(Vector3.One - Vector3.One == Vector3.Zero)

	assert(Color.FromHSV(0.5, 0.3, 1) == Color.FromHSV(0.5, 0.3, 1))
	Color.FromHSV(0.2, 0.4, 1, 0.5)

	assert(Color.FromHex("#F1F1F1") == Color.FromHex("#F1F1F1"))

	local slerpA = Vector3.New(2, 0, 0)
	local slerpB = Vector3.New(0, 4, 0)
	assert((Vector3.Slerp(slerpA, slerpB, 0) - slerpA).Magnitude < 0.001)
	assert((Vector3.Slerp(slerpA, slerpB, 1) - slerpB).Magnitude < 0.001)
	assert(math.abs(Vector3.Slerp(slerpA, slerpB, 0.5).Magnitude - 3) < 0.001)
	assert(math.abs(slerpA:Slerp(slerpB, 0.5).Magnitude - 3) < 0.001)
	assert((Vector3.Slerp(Vector3.New(30, 30, 30), Vector3.New(90, 90, 90), 0.5) - Vector3.New(60, 60, 60)).Magnitude < 0.001)
end
