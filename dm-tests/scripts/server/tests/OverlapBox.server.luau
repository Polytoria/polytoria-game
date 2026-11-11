function Entry()
	local suite: Dynamic = Environment.OverlapSuite
	local redCheck: Part = suite["RedCheck"]
	local blueCheck: Part = suite["BlueCheck"]

	local redParts =
		Environment:OverlapBox(redCheck.Position, redCheck.Size, redCheck.Rotation, { Players, redCheck, blueCheck })
	for _, part in redParts do
		assert(part:HasTag("OverlapTargetR"))
	end

	local blueParts =
		Environment:OverlapBox(blueCheck.Position, blueCheck.Size, blueCheck.Rotation, { Players, redCheck, blueCheck })
	for _, part in blueParts do
		assert(part:HasTag("OverlapTargetB"))
	end
end
