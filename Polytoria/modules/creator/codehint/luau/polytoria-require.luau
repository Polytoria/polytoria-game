local MAP_FILE_NAME = "polytoria-module-map.tsv"

type ModuleLookup = { [string]: string | false }
type SourceContext = {
	sourceWorldPath: string,
	modules: ModuleLookup,
}

local function normalizePath(path: string): string
	local normalized = string.gsub(path, "\\", "/")
	normalized = string.gsub(normalized, "/+$", "")
	return normalized
end

local function splitPath(path: string, separatorPattern: string): { string }
	local parts = {}
	for part in string.gmatch(path, separatorPattern) do
		table.insert(parts, part)
	end
	return parts
end

local function getProjectRelativePath(context: PluginContext): string?
	local rootPath = normalizePath(lsp.workspace.getRootUri().fsPath)
	local filePath = normalizePath(context.filePath)
	local prefix = rootPath .. "/"

	if string.lower(string.sub(filePath, 1, #prefix)) ~= string.lower(prefix) then
		return nil
	end

	return string.sub(filePath, #prefix + 1)
end

local function readModuleContexts(context: PluginContext): ({ SourceContext }?, string?)
	local sourceFile = getProjectRelativePath(context)
	if sourceFile == nil then
		return nil, nil
	end

	local rootUri = lsp.workspace.getRootUri()
	local mapUri = rootUri:joinPath(".poly", "luau", MAP_FILE_NAME)
	local ok, mapContent = pcall(function()
		return lsp.fs.readFile(mapUri)
	end)

	if not ok or type(mapContent) ~= "string" then
		return nil, nil
	end

	local sourceKey = string.lower(normalizePath(sourceFile))
	local sourcePathsByWorld: { [string]: { string } } = {}
	local modulesByWorld: { [string]: ModuleLookup } = {}

	for line in string.gmatch(mapContent, "[^\r\n]+") do
		if string.sub(line, 1, 1) ~= "#" then
			local sourceWorldId, mappedSource, sourceWorldPath = string.match(
				line,
				"^S\t([^\t]*)\t([^\t]*)\t([^\t]*)$"
			)

			if sourceWorldId ~= nil and string.lower(normalizePath(mappedSource)) == sourceKey then
				local sourcePaths = sourcePathsByWorld[sourceWorldId]
				if sourcePaths == nil then
					sourcePaths = {}
					sourcePathsByWorld[sourceWorldId] = sourcePaths
				end
				table.insert(sourcePaths, sourceWorldPath)
			else
				local moduleWorldId, moduleWorldPath, moduleFile = string.match(
					line,
					"^M\t([^\t]*)\t([^\t]*)\t([^\t]*)$"
				)

				if moduleWorldId ~= nil then
					local modules = modulesByWorld[moduleWorldId]
					if modules == nil then
						modules = {}
						modulesByWorld[moduleWorldId] = modules
					end

					local previous = modules[moduleWorldPath]
					if previous == nil or previous == moduleFile then
						modules[moduleWorldPath] = moduleFile
					else
						-- Duplicate world paths with different linked files are ambiguous.
						modules[moduleWorldPath] = false
					end
				end
			end
		end
	end

	local contexts: { SourceContext } = {}
	for worldId, sourcePaths in sourcePathsByWorld do
		local modules = modulesByWorld[worldId]
		if modules ~= nil then
			for _, sourceWorldPath in sourcePaths do
				table.insert(contexts, {
					sourceWorldPath = sourceWorldPath,
					modules = modules,
				})
			end
		end
	end

	if #contexts == 0 then
		return nil, nil
	end

	return contexts, sourceFile
end

local function resolveScriptExpression(sourceWorldPath: string, expression: string): string?
	if expression == "world" or string.sub(expression, 1, 6) == "world." then
		return expression
	end

	if expression ~= "script" and string.sub(expression, 1, 7) ~= "script." then
		return nil
	end

	local resolved = splitPath(sourceWorldPath, "[^.]+")
	local suffix = string.sub(expression, 7)
	if string.sub(suffix, 1, 1) == "." then
		suffix = string.sub(suffix, 2)
	end

	if suffix == "" then
		return table.concat(resolved, ".")
	end

	for segment in string.gmatch(suffix, "[^.]+") do
		if segment == "Parent" then
			if #resolved <= 1 then
				return nil
			end
			table.remove(resolved)
		else
			table.insert(resolved, segment)
		end
	end

	return table.concat(resolved, ".")
end

local function removeLuauExtension(path: string): string
	if string.sub(path, -5) == ".luau" then
		return string.sub(path, 1, -6)
	end
	if string.sub(path, -4) == ".lua" then
		return string.sub(path, 1, -5)
	end
	return path
end

local function getDirectory(path: string): string
	local slashIndex = string.match(path, "^.*()/")
	if slashIndex == nil then
		return ""
	end
	return string.sub(path, 1, slashIndex - 1)
end

local function makeRelativeRequirePath(sourceFile: string, moduleFile: string): string
	local sourceDirectory = normalizePath(getDirectory(sourceFile))
	local targetPath = normalizePath(removeLuauExtension(moduleFile))
	local sourceParts = splitPath(sourceDirectory, "[^/]+")
	local targetParts = splitPath(targetPath, "[^/]+")

	local common = 0
	while common < #sourceParts and common < #targetParts do
		if string.lower(sourceParts[common + 1]) ~= string.lower(targetParts[common + 1]) then
			break
		end
		common += 1
	end

	local relativeParts = {}
	for _ = common + 1, #sourceParts do
		table.insert(relativeParts, "..")
	end
	for index = common + 1, #targetParts do
		table.insert(relativeParts, targetParts[index])
	end

	local relative = table.concat(relativeParts, "/")
	if relative == "" then
		relative = "./" .. (targetParts[#targetParts] or "")
	elseif string.sub(relative, 1, 1) ~= "." then
		relative = "./" .. relative
	end

	return relative
end

local function resolveRequire(expression: string, contexts: { SourceContext }, sourceFile: string): string?
	local resolvedFile: string? = nil

	for _, sourceContext in contexts do
		local moduleWorldPath = resolveScriptExpression(sourceContext.sourceWorldPath, expression)
		if moduleWorldPath ~= nil then
			local moduleFile = sourceContext.modules[moduleWorldPath]
			if type(moduleFile) == "string" then
				if resolvedFile == nil then
					resolvedFile = moduleFile
				elseif resolvedFile ~= moduleFile then
					return nil
				end
			end
		end
	end

	if resolvedFile == nil then
		return nil
	end

	local relativePath = makeRelativeRequirePath(sourceFile, resolvedFile)
	relativePath = string.gsub(relativePath, "\\", "\\\\")
	relativePath = string.gsub(relativePath, '"', '\\"')
	return '"' .. relativePath .. '"'
end

local function isIdentifierByte(byte: number?): boolean
	return byte ~= nil and (
		(byte >= 48 and byte <= 57)
		or (byte >= 65 and byte <= 90)
		or (byte >= 97 and byte <= 122)
		or byte == 95
	)
end

local function isExpressionByte(byte: number?): boolean
	return isIdentifierByte(byte) or byte == 46
end

local function isWhitespaceByte(byte: number?): boolean
	return byte == 9 or byte == 10 or byte == 13 or byte == 32
end

local function skipWhitespace(source: string, index: number): number
	while index <= #source and isWhitespaceByte(string.byte(source, index)) do
		index += 1
	end
	return index
end

local function skipQuotedString(source: string, index: number, quoteByte: number): number
	index += 1
	while index <= #source do
		local byte = string.byte(source, index)
		if byte == 92 then
			index += 2
		elseif byte == quoteByte then
			return index + 1
		else
			index += 1
		end
	end
	return index
end

local function skipLongString(source: string, index: number): number
	local closeStart = string.find(source, "]]", index + 2, true)
	if closeStart == nil then
		return #source + 1
	end
	return closeStart + 2
end

local function getPosition(source: string, byteIndex: number): (number, number)
	local line = 1
	local lineStart = 1

	for index = 1, byteIndex - 1 do
		if string.byte(source, index) == 10 then
			line += 1
			lineStart = index + 1
		end
	end

	return line, byteIndex - lineStart + 1
end

return {
	transformSource = function(source: string, context: PluginContext): { TextEdit }?
		local contexts, sourceFile = readModuleContexts(context)
		if contexts == nil or sourceFile == nil then
			return nil
		end

		local edits = {}
		local index = 1

		while index <= #source do
			local byte = string.byte(source, index)
			local nextByte = string.byte(source, index + 1)

			if byte == 34 or byte == 39 or byte == 96 then
				index = skipQuotedString(source, index, byte)
			elseif byte == 91 and nextByte == 91 then
				index = skipLongString(source, index)
			elseif byte == 45 and nextByte == 45 then
				if string.sub(source, index + 2, index + 3) == "[[" then
					index = skipLongString(source, index + 2)
				else
					local newline = string.find(source, "\n", index + 2, true)
					index = newline == nil and (#source + 1) or (newline + 1)
				end
			elseif string.sub(source, index, index + 6) == "require"
				and not isIdentifierByte(string.byte(source, index - 1))
				and string.byte(source, index - 1) ~= 46
				and string.byte(source, index - 1) ~= 58
				and not isIdentifierByte(string.byte(source, index + 7)) then
				local cursor = skipWhitespace(source, index + 7)
				if string.byte(source, cursor) == 40 then
					cursor = skipWhitespace(source, cursor + 1)
					local expressionStart = cursor
					while cursor <= #source and isExpressionByte(string.byte(source, cursor)) do
						cursor += 1
					end

					local expressionEnd = cursor - 1
					cursor = skipWhitespace(source, cursor)
					if expressionEnd >= expressionStart and string.byte(source, cursor) == 41 then
						local expression = string.sub(source, expressionStart, expressionEnd)
						local replacement = resolveRequire(expression, contexts, sourceFile)
						if replacement ~= nil then
							local startLine, startColumn = getPosition(source, expressionStart)
							local endLine, endColumn = getPosition(source, expressionEnd + 1)
							table.insert(edits, {
								startLine = startLine,
								startColumn = startColumn,
								endLine = endLine,
								endColumn = endColumn,
								newText = replacement,
							})
						end
						index = cursor + 1
					else
						index += 1
					end
				else
					index += 1
				end
			else
				index += 1
			end
		end

		if #edits == 0 then
			return nil
		end

		return edits
	end,
} :: PluginApi
