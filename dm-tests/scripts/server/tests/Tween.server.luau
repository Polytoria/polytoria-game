function Entry()
	local tw = Tween:NewTween()

	tw:TweenNumber(0, 1, 0.5, function(a0: number) end)

	Tween:NewTween()
		:TweenColor(Color.New(0), Color.New(1), 0.5, function(c: Color) end)
		:TweenVector2(Vector2.New(-5), Vector2.New(5, 3), 0.5, function(v2: Vector2) end)
		:Chain()
		:Interval(0.5)
		:TweenQuaternion(Quaternion.Euler(45, 90, 15), Quaternion.Euler(-15, -24, -32), 0.5, function(q) end)
end
