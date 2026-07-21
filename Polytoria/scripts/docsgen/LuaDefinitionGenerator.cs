// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using static Polytoria.DocsGen.APIReferenceGenerator;

namespace Polytoria.DocsGen;

public static class LuaDefinitionGenerator
{
	private const string CodeHintPath = "res://modules/creator/codehint/luau/";

	public static void GenerateDocFiles(string atFolder)
	{
		// Clear old lua folder
		string[] files = Directory.GetFiles(atFolder);

		APIReferenceRoot refer = GenerateReferences();

		foreach (string file in files)
		{
			File.Delete(file);
		}

		StringBuilder builder = new();

		foreach (string item in DirAccess.GetFilesAt(CodeHintPath))
		{
			string pathTo = CodeHintPath.PathJoin(item);
			if (pathTo.EndsWith(".luau"))
			{
				string content = Godot.FileAccess.GetFileAsString(pathTo);
				builder.AppendLine(content);
			}
		}

		File.WriteAllText(atFolder.PathJoin("def.json"), JsonSerializer.Serialize(refer, APIRefGenerationContext.Default.APIReferenceRoot));

		foreach (ScriptEnum e in refer.Enums)
		{
			builder.AppendLine($"declare extern type {e.Name} extends Enum with end");
			builder.AppendLine();
		}

		builder.AppendLine("declare Enums: {");
		foreach (ScriptEnum e in refer.Enums)
		{
			builder.AppendLine($"\t{e.Name}: {{");
			foreach (string item in e.Options)
			{
				builder.AppendLine($"\t\t{item}: {e.Name},");
			}
			builder.AppendLine("\t},");
		}
		builder.AppendLine("}");

		for (int i = 0; i < refer.Classes.Length; i++)
		{
			ScriptClass c = refer.Classes[i];
			// Ignore already declared types
			if (c.Name == "PTSignal" || c.Name == "PTSignalConnection") continue;

			builder.AppendLine();
			AppendClass(builder, c);
			builder.AppendLine();
		}

		File.WriteAllText(atFolder.PathJoin("def.d.luau"), builder.ToString());
	}

	private static void AppendClass(StringBuilder builder, ScriptClass c)
	{
		bool hasStatic = false;

		builder.Append($"declare extern type {c.Name}");
		if (c.BaseType != null)
		{
			builder.Append($" extends {c.BaseType}");
		}
		builder.AppendLine(" with");

		foreach (ScriptProperty p in c.Properties)
		{
			if (p.IsStatic)
			{
				hasStatic = true;
				continue;
			}
			// properties cannot be marked with attributes
			if (p.ObsoletionInfo.HasValue)
			{
				builder.AppendLine($"\t{p.ObsoletionInfo.Value.GetWarningComment()}");
			}
			builder.Append('\t');
			if (p.IsReadOnly)
			{
				builder.Append("read ");
			}
			builder.AppendLine(p.ToString());
		}

		foreach (ScriptEvent e in c.Events)
		{
			builder.Append($"\tread {e.Name}: PTSignal");
			if (e.Parameters.Length > 0)
			{
				builder.Append($"<{string.Join(", ", e.Parameters.Select(p => p.Type))}>");
			}
			builder.AppendLine();
		}

		bool hasIndexer = false;
		List<string> indexerIndexes = [];
		List<string> indexerValues = [];

		foreach (ScriptMethod m in c.Methods)
		{
			if (m.IsMetamethod)
			{
				// if static, the first parameter must be this class (self)
				// we can skip explicit metamethod operator overloads since the type
				// checker will blatantly assume that they already exist
				if (m.IsStatic && (m.Parameters.Length == 0 || m.Parameters[0].Type != c.Name)) continue;
				// the type checker will not consider a class's __index or
				// __newindex metamethods when checking for keys, so they must be
				// converted to a `[K]: V` field
				bool isNewIndex = m.Name == "__newindex";
				if (isNewIndex || m.Name == "__index")
				{
					indexerIndexes.Add(m.Parameters[m.IsStatic ? 1 : 0].Type);
					indexerValues.Add(isNewIndex ? m.Parameters[m.IsStatic ? 2 : 1].Type : (m.ReturnType ?? "nil"));
					hasIndexer = true;
					continue;
				}
			}
			else if (m.IsStatic)
			{
				hasStatic = true;
				if (!m.IsSemiStatic) continue;
			}
			IEnumerable<string> iter = m.Parameters.Select(p => p.ToString());
			if ((m.IsMetamethod && m.IsStatic) || m.IsSemiStatic)
			{
				// overwrite first parameter with self
				iter = iter.Skip(1);
			}
			if (m.ObsoletionInfo.HasValue)
			{
				builder.AppendLine($"\t{m.ObsoletionInfo.Value.GetAttribute()}");
			}
			builder.Append($"\tfunction {m.Name}({string.Join(", ", iter.Prepend("self"))})");
			if (m.ReturnType != null)
			{
				builder.Append($": {m.ReturnType}");
			}
			builder.AppendLine();
		}

		if (hasIndexer)
		{
			builder.AppendLine($"\t[{string.Join(" | ", indexerIndexes.Distinct())}]: {string.Join(" | ", indexerValues.Distinct())}");
		}

		builder.Append("end");

		if (hasStatic)
		{
			builder.AppendLine();
			builder.AppendLine();
			AppendStaticClass(builder, c);
		}
	}

	private static void AppendStaticClass(StringBuilder builder, ScriptClass c)
	{
		builder.AppendLine($"declare {c.Name}: {{");

		foreach (ScriptProperty p in c.Properties)
		{
			if (!p.IsStatic) continue;
			// properties cannot be marked with attributes
			if (p.ObsoletionInfo.HasValue)
			{
				builder.AppendLine($"\t{p.ObsoletionInfo.Value.GetWarningComment()}");
			}
			builder.AppendLine($"\t{p},");
		}

		// function types cannot be marked with attributes nor documented

		IEnumerable<IGrouping<string, string>> functionTypes = c.Methods
			.Where(m => m.IsStatic && !(m.IsMetamethod || m.ObsoletionInfo.HasValue))
			.GroupBy(
				m => m.Name,
				m => $"({string.Join(", ", m.Parameters.Select(p => p.ToString(true)))}) -> {m.ReturnType ?? "()"}"
			);
		foreach (IGrouping<string, string> g in functionTypes)
		{
			builder.AppendLine($"\t{g.Key}: {(g.Count() > 1 ? $"({string.Join(") & (", g)})" : g.First())},");
		}

		builder.Append('}');
	}
}
