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

public class LuaDefinitionGenerator
{
	private const string CodeHintPath = "res://modules/codehint/luau/";
	private static readonly string[] SkippedMetamethods = ["__iter"];

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

		// Add PTSignal type definitions
		builder.AppendLine("declare class PTSignalConnection");
		builder.AppendLine("\tfunction Disconnect(self): ()");
		builder.AppendLine("end");
		builder.AppendLine();

		builder.AppendLine("export type PTSignal<T... = ...any> = {");
		builder.AppendLine("\tConnect: (self: PTSignal<T...>, callback: (T...) -> ()) -> PTSignalConnection,");
		builder.AppendLine("\tDisconnect: (self: PTSignal<T...>, callback: (T...) -> ()) -> nil,");
		builder.AppendLine("\tOnce: (self: PTSignal<T...>, callback: (T...) -> ()) -> PTSignalConnection,");
		builder.AppendLine("\tWait: (self: PTSignal<T...>) -> T...,");
		builder.AppendLine("}");
		builder.AppendLine();

		builder.AppendLine($"declare class Enum end");

		foreach (ScriptEnum e in refer.Enums)
		{
			builder.AppendLine($"declare class {e.Name} end");
			builder.AppendLine($"declare class {e.InternalName} extends Enum");
			foreach (string item in e.Options)
			{
				builder.AppendLine($"\t{item}:{e.Name}");
			}
			builder.AppendLine($"end");
		}

		builder.AppendLine($"type ENUM_LIST = {{");
		foreach (ScriptEnum e in refer.Enums)
		{
			builder.AppendLine($"\t{e.Name}:{e.InternalName},");
		}
		builder.AppendLine($"}} & {{ }}");
		builder.AppendLine($"declare Enums: ENUM_LIST");

		foreach (ScriptClass item in refer.Classes)
		{
			// Ignore PTSignal, already declared
			if (item.Name == "PTSignal") continue;

			builder.AppendLine(GenerateClass(item));
		}

		File.WriteAllText(atFolder.PathJoin("def.d.luau"), builder.ToString());
	}

	public static string GenerateClass(ScriptClass c)
	{
		StringBuilder builder = new();

		if (c.IsInstantiable)
		{
			// Add new to instantiatables
			c.Methods.Add(new()
			{
				IsStatic = true,
				Name = "New",
				Parameters = [
					new() {
						Name = "parent",
						Type = "NetworkedObject",
						IsOptional = true,
						DefaultValue = null
					}
				],
				ReturnType = c.Name
			});
		}

		bool hasStatic = false;

		string baseType = c.BaseType != null ? $" extends {c.BaseType}" : "";
		builder.AppendLine($"declare class {c.Name}{baseType}");

		foreach (ScriptProperty p in c.Properties)
		{
			if (p.IsObsolete) continue;
			if (p.IsStatic) { hasStatic = true; continue; }
			builder.AppendLine($"\t{p.Name} : {ProcessType(p.Type ?? "nil")}");
		}

		foreach (ScriptEvent e in c.Events)
		{
			if (e.Parameters != null && e.Parameters.Count > 0)
			{
				string typeParams = string.Join(", ", e.Parameters.Select(p => ProcessType(p.Type ?? "nil")));
				builder.AppendLine($"\t{e.Name} : PTSignal<{typeParams}>");
			}
			else
			{
				builder.AppendLine($"\t{e.Name} : PTSignal");
			}
		}

		foreach (ScriptMethod m in c.Methods)
		{
			if (m.IsObsolete) continue;
			if (SkippedMetamethods.Contains(m.Name)) continue;
			if (m.IsStatic && !m.Name.StartsWith("__")) { hasStatic = true; continue; }
			List<string> args = [];

			foreach (ScriptParameter param in m.Parameters)
			{
				if (param.Type == null) continue;
				args.Add($"{param.Name}: {ProcessType(param.Type) + (param.IsOptional ? "?" : "")}");
			}

			args.Insert(0, "self");

			builder.AppendLine($"\tfunction {m.Name}({string.Join(", ", args)}): {ProcessType(m.ReturnType ?? "")}");
		}

		builder.AppendLine($"end");

		if (hasStatic)
		{
			builder.AppendLine(GenerateStaticClass(c));
		}

		return builder.ToString();
	}

	public static string GenerateStaticClass(ScriptClass c)
	{
		StringBuilder builder = new();

		builder.AppendLine($"declare {c.Name}: {{");

		foreach (ScriptProperty p in c.Properties)
		{
			if (!p.IsStatic) continue;
			builder.AppendLine($"\t{p.Name} : {ProcessType(p.Type ?? "nil")},");
		}

		foreach (ScriptMethod m in c.Methods)
		{
			if (m.IsObsolete) continue;
			if (!m.IsStatic) continue;
			// Ignore metamethods
			if (m.Name.StartsWith("__")) continue;
			List<string> args = [];

			foreach (ScriptParameter param in m.Parameters)
			{
				if (param.Type == null) continue;
				args.Add($"{ProcessType(param.Type) + (param.IsOptional ? "?" : "")}");
			}

			builder.AppendLine($"{m.Name}: ({string.Join(", ", args)}) -> ({ProcessType(m.ReturnType ?? "")}),");
		}

		builder.AppendLine($"}}");

		return builder.ToString();
	}

	private static string ProcessType(string t)
	{
		if (t == "function")
		{
			return "() -> nil";
		}
		else if (t == "table")
		{
			return "{ any }";
		}
		return t;
	}
}
