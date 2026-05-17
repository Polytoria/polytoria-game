function Entry()
	do
		local v1 = Vector3.New(0.3, -2.25, 1.89)
		local v2 = Vector3.New(3.32, 1.77, 0.2)

		assert(Vector3.Dot(v1, v2) == v1:Dot(v2))
		assert(Vector3.Lerp(v1, v2, 0.5) == v1:Lerp(v2, 0.5))
		assert(Vector3.Round(v1) == v1:Round())
	end

	do
		local v1 = Vector2.New(-4.95, 9.26)
		local v2 = Vector2.New(5.54, 6.21)

		assert(Vector2.Cross(v1, v2) == v1:Cross(v2))
		assert(Vector2.Clamp(v1, Vector2.Zero, Vector2.One) == v1:Clamp(Vector2.Zero, Vector2.One))
		assert(Vector2.Floor(v1) == v1:Floor())
	end

	do
		local c1 = Color.Random()
		local c2 = Color.Random()

		assert(Color.ToHex(c1) == c1:ToHex())
		assert(Color.Lerp(c1, c2, 0.5) == c1:Lerp(c2, 0.5))
	end

	do
		local q1 = Quaternion.Euler(0, math.pi, math.pi / 2)
		local q2 = Quaternion.Euler(math.pi, math.pi / 2, 0)

		assert(Quaternion.Angle(q1, q2) == q1:Angle(q2))
		assert(Quaternion.Inverse(q1) == q1:Inverse())
		assert(Quaternion.Slerp(q1, q2, 0.5) == q1:Slerp(q2, 0.5))
	end
end
