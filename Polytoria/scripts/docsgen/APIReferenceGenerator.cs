// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Services;
using Polytoria.Scripting;
using Polytoria.Shared;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Polytoria.DocsGen.APIReferenceGenerator;

namespace Polytoria.DocsGen;

public static class APIReferenceGenerator
{
	public static APIReferenceRoot GenerateReferences()
	{
		Assembly assembly = Assembly.GetExecutingAssembly();
#pragma warning disable IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code
		Type[] types = assembly.GetTypes();
#pragma warning restore IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code

		List<ScriptEnum> enums = [];
		List<string> instanceClasses = [];
		List<Type> missingEnums = [];
		Dictionary<Type, ScriptClass> classMap = [];

		foreach (Type type in types)
		{
			if (!type.IsAssignableTo(typeof(IScriptObject))) continue;
			if (type.IsEnum || type.IsInterface) continue;
			if (type.IsDefined(typeof(InternalAttribute))) continue;
			if (type.FullName == null) continue;
			if (type.FullName.Contains("Polytoria.Scripting.Extensions")) continue;
			if (type.FullName.Contains("Polytoria.Scripting.Libraries")) continue;
			if (type.IsGenericType) continue;

			string name = ProcessClassName(type);
			if (type.IsAssignableTo(typeof(Instance)))
			{
				instanceClasses.Add(name);
			}

#pragma warning disable IL2075 // Datamodel types has the reflections needed
			PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
			MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
#pragma warning restore IL2075

			List<ScriptProperty> propertiesDef = [];
			List<ScriptMethod> methodsDef = [];
			List<ScriptEvent> eventsDef = [];

			foreach (PropertyInfo property in properties)
			{
				bool isScriptProperty = property.IsDefined(typeof(ScriptPropertyAttribute));
				bool isEditable = property.IsDefined(typeof(EditableAttribute));

				if (!isScriptProperty && !isEditable) continue;

				Type propertyType = property.PropertyType;
				if (propertyType == typeof(PTSignal) ||
					(propertyType.IsGenericType &&
					 propertyType.GetGenericTypeDefinition().Name.StartsWith(nameof(PTSignal))))
				{
					eventsDef.Add(new(
						property.Name,
						propertyType.IsGenericType
							? [.. propertyType.GetGenericArguments().Select(a => new ScriptParameter(Type: ProcessOptionalTypeName(a)))]
							: []
					));
				}
				else
				{
					if (propertyType.IsEnum && !ScriptService.EnumMap.ContainsValue(propertyType))
					{
						missingEnums.Add(propertyType);
					}

					string? typeName = ProcessOptionalTypeName(propertyType);
					if (typeName == null) continue;

					Attributes.ObsoleteAttribute? obsoleteAttribute = property.GetCustomAttribute<Attributes.ObsoleteAttribute>();
					propertiesDef.Add(new(
						property.Name,
						typeName,
						isEditable || isScriptProperty,
						isScriptProperty && property.GetSetMethod(false) == null,
						property.GetGetMethod(true)?.IsStatic ?? false,
						obsoleteAttribute != null ? new(obsoleteAttribute) : null
					));
				}
			}

			foreach (MethodInfo method in methods)
			{
				ScriptMethodAttribute? methodAttribute = method.GetCustomAttribute<ScriptMethodAttribute>();
				ScriptMetamethodAttribute? metaMethodAttribute = method.GetCustomAttribute<ScriptMetamethodAttribute>();

				if (methodAttribute == null && metaMethodAttribute == null) continue;
				if (method.IsDefined(typeof(HandlesLuaStateAttribute))) continue;

				bool asyncFunc = false;
				Type returnType = method.ReturnType;

				if (returnType == typeof(Task))
				{
					asyncFunc = true;
				}
				else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
				{
					asyncFunc = true;
					returnType = returnType.GetGenericArguments()[0];
				}

				if (returnType == typeof(Node)) continue;

				List<ScriptParameter> paramsDef = [];

				foreach (ParameterInfo item in method.GetParameters())
				{
					if (item.ParameterType == typeof(Node)) continue;
					if (item.IsDefined(typeof(ScriptingCallerAttribute))) continue;

					bool isVarArg = item.IsDefined(typeof(ParamArrayAttribute));
					Type? paramType = isVarArg ? item.ParameterType.GetElementType() : item.ParameterType;
					if (paramType == null) continue;

					Type? underlying = Nullable.GetUnderlyingType(paramType);
					string? typeName = ProcessTypeName(underlying ?? paramType);
					if (typeName == null) continue;

					paramsDef.Add(new(
						isVarArg ? "..." : item.Name,
						underlying != null || item.HasDefaultValue ? typeName + '?' : typeName,
						item.HasDefaultValue ? item.DefaultValue?.ToString() : null
					));
				}

				Attributes.ObsoleteAttribute? obsoleteAttribute = method.GetCustomAttribute<Attributes.ObsoleteAttribute>();
				methodsDef.Add(new(
					metaMethodAttribute != null ? GetMetamethodIndexer(metaMethodAttribute.Metamethod) : methodAttribute?.MethodName ?? method.Name,
					ProcessOptionalTypeName(returnType),
					[.. paramsDef],
					asyncFunc,
					method.IsStatic,
					method.IsStatic && (methodAttribute?.SemiStatic ?? false),
					obsoleteAttribute != null ? new(obsoleteAttribute) : null
				));
			}

			// __index & __newindex for Instance
			if (type == typeof(Instance))
			{
				methodsDef.Add(new(
					"__index",
					"any",
					[
						new("index", "any"),
					]
				));
				methodsDef.Add(new(
					"__newindex",
					null,
					[
						new("index", "any"),
						new("value", "any"),
					]
				));
			}

			bool isInstantiable = type.IsDefined(typeof(InstantiableAttribute), false);
			if (isInstantiable)
			{
				methodsDef.Add(new(
					"New",
					name,
					[
						new("parent", nameof(NetworkedObject) + '?')
					],
					IsStatic: true
				));
			}

			StaticAttribute? staticA = type.GetCustomAttribute<StaticAttribute>();
			classMap[type] = new(
				name,
				((type.BaseType != null && type.BaseType.IsAssignableTo(typeof(Node))) || type.BaseType == typeof(object) || type.BaseType == typeof(ValueType)) ? null : type.BaseType?.Name,
				[.. propertiesDef],
				[.. methodsDef],
				[.. eventsDef],
				staticA != null,
				type.IsDefined(typeof(AbstractAttribute), false),
				isInstantiable,
				staticA?.Alias
			);
		}

		// Order classes by inheritance hierarchy
		List<ScriptClass> classes = OrderClassesByInheritance(classMap);

		foreach ((string key, Type enumType) in ScriptService.EnumMap)
		{
			enums.Add(new(key, enumType.Name, Enum.GetNames(enumType)));
		}

		if (Globals.IsInGDEditor)
		{
			// Display enum map missing warnings
			PT.Print("APIREF Generation Complete");
			PT.Print("Missing enums: ", missingEnums.Count);
			foreach (Type item in missingEnums)
			{
				PT.PrintErr("Enum Missing ", item.Name);
			}
		}

		return new(
			Globals.AppVersion,
			[.. classes],
			[.. enums],
			[.. instanceClasses]
		);
	}

	private static string GetMetamethodIndexer(ScriptObjectMetamethod metamethod)
	{
		return metamethod switch
		{
			ScriptObjectMetamethod.Add => "__add",
			ScriptObjectMetamethod.Sub => "__sub",
			ScriptObjectMetamethod.Call => "__call",
			ScriptObjectMetamethod.Concat => "__concat",
			ScriptObjectMetamethod.Div => "__div",
			ScriptObjectMetamethod.Eq => "__eq",
			ScriptObjectMetamethod.Iter => "__iter",
			ScriptObjectMetamethod.Le => "__le",
			ScriptObjectMetamethod.Len => "__len",
			ScriptObjectMetamethod.Lt => "__lt",
			ScriptObjectMetamethod.Mod => "__mod",
			ScriptObjectMetamethod.Mul => "__mul",
			ScriptObjectMetamethod.Pow => "__pow",
			ScriptObjectMetamethod.ToString => "__tostring",
			ScriptObjectMetamethod.Unm => "__unm",
			ScriptObjectMetamethod.Index => "__index",
			ScriptObjectMetamethod.NewIndex => "__newindex",
			_ => ""
		};
	}

	private static List<ScriptClass> OrderClassesByInheritance(Dictionary<Type, ScriptClass> classMap)
	{
		List<ScriptClass> result = [];
		HashSet<Type> processed = [];
		Dictionary<Type, List<Type>> children = [];

		// Build parent-child relationships
		foreach (Type type in classMap.Keys)
		{
			Type? baseType = type.BaseType;

			// Find the actual base type
			while (baseType != null &&
					baseType != typeof(object) &&
					baseType != typeof(ValueType) &&
					!baseType.IsAssignableTo(typeof(Node)))
			{
				if (classMap.ContainsKey(baseType))
				{
					if (!children.TryGetValue(baseType, out List<Type>? value))
					{
						value = [];
						children[baseType] = value;
					}

					value.Add(type);
					break;
				}
				baseType = baseType.BaseType;
			}
		}

		// Recursive function to add type and its children in order
		void AddTypeAndChildren(Type type)
		{
			if (processed.Contains(type)) return;
			if (!classMap.TryGetValue(type, out ScriptClass v)) return;

			// Ensure parent is added
			Type? baseType = type.BaseType;
			while (baseType != null &&
					baseType != typeof(object) &&
					baseType != typeof(ValueType) &&
					!baseType.IsAssignableTo(typeof(Node)))
			{
				if (classMap.ContainsKey(baseType) && !processed.Contains(baseType))
				{
					AddTypeAndChildren(baseType);
					break;
				}
				baseType = baseType.BaseType;
			}

			processed.Add(type);
			result.Add(v);

			// Add children
			if (children.TryGetValue(type, out List<Type>? value))
			{
				foreach (Type child in value)
				{
					AddTypeAndChildren(child);
				}
			}
		}

		// Find root types
		List<Type> roots = [];
		foreach (Type type in classMap.Keys)
		{
			Type? baseType = type.BaseType;
			bool hasParentInSet = false;

			while (baseType != null &&
					baseType != typeof(object) &&
					baseType != typeof(ValueType) &&
					!baseType.IsAssignableTo(typeof(Node)))
			{
				if (classMap.ContainsKey(baseType))
				{
					hasParentInSet = true;
					break;
				}
				baseType = baseType.BaseType;
			}

			if (!hasParentInSet)
			{
				roots.Add(type);
			}
		}

		// Process all roots
		foreach (Type root in roots)
		{
			AddTypeAndChildren(root);
		}

		return result;
	}

	public static void GenerateRefFile()
	{
		string docData = JsonSerializer.Serialize(GenerateReferences(), APIRefGenerationContext.Default.APIReferenceRoot);
		using FileAccess file = FileAccess.Open("res://apiref.json", FileAccess.ModeFlags.Write);
		file.StoreString(docData);
		file.Close();
	}

	private static string ProcessClassName(Type type)
	{
		if (type.IsAssignableTo(typeof(IScriptGDObject)))
		{
			return type.Name.TrimPrefix("PT");
		}
		return type.Name;
	}

	private static string? ProcessTypeName(Type? type)
	{
		if (type == null ||
			type == typeof(void) ||
			type == typeof(Task) ||
			type == typeof(Nullable) ||
			type == typeof(ValueType))
		{
			return null;
		}

		if (type == typeof(byte) ||
			type == typeof(sbyte) ||
			type == typeof(short) ||
			type == typeof(ushort) ||
			type == typeof(int) ||
			type == typeof(uint) ||
			type == typeof(long) ||
			type == typeof(ulong) ||
			type == typeof(float) ||
			type == typeof(double) ||
			type == typeof(decimal))
		{
			return "number";
		}

		if (type == typeof(string))
		{
			return "string";
		}

		if (type == typeof(bool))
		{
			return "boolean";
		}

		if (type == typeof(object))
		{
			return "any";
		}

		if (type == typeof(byte[]))
		{
			return "buffer";
		}

		// TODO: make PTFunction and PTCallback generic so these aren't garbage types
		if (type == typeof(PTCallback))
		{
			return "(...any) -> ()";
		}

		if (type == typeof(PTFunction))
		{
			return "(...any) -> ...any";
		}

		// --- Proxies --- //

		if (type == typeof(Aabb))
		{
			return "Bounds";
		}

		// --------------- //

		if (type.IsAssignableTo(typeof(ITuple)))
		{
			return $"({string.Join(", ", type.GetGenericArguments().Select(arg => ProcessOptionalTypeName(arg) ?? "nil"))})";
		}

		if (type.IsAssignableTo(typeof(IScriptGDObject)))
		{
			return ProcessClassName(type);
		}

		if (type.IsGenericType)
		{
			Type genericType = type.GetGenericTypeDefinition();
			if (genericType == typeof(Task<>))
			{
				return ProcessOptionalTypeName(type.GetGenericArguments()[0]);
			}
			if (genericType == typeof(IEnumerable<>))
			{
				return $"((any) -> {ProcessOptionalTypeName(type.GetGenericArguments()[0])}, nil, nil)";
			}
		}

		if (type.IsArray)
		{
			return $"{{ {ProcessOptionalTypeName(type.GetElementType())} }}";
		}

		if (type.IsAssignableTo(typeof(IDictionary)))
		{
			Type[] args = type.GetGenericArguments();
			if (args.Length == 2)
			{
				string? indexType = ProcessOptionalTypeName(args[0]);
				string? valueType = ProcessOptionalTypeName(args[1]);
				if (indexType != null && valueType != null)
				{
					return $"{{ [{indexType}]: {valueType} }}";
				}
			}
			return "{  }";
		}

		if (type.IsEnum)
		{
			// Find the Enum's external name
			string name = ScriptService.EnumMap.FirstOrDefault(x => x.Value == type).Key;
			if (!string.IsNullOrEmpty(name))
				return name;
		}

		return type.Name;
	}

	private static string? ProcessOptionalTypeName(Type? type)
	{
		if (type == null) return null;

		Type? underlying = Nullable.GetUnderlyingType(type);
		if (underlying != null)
		{
			string? typeName = ProcessTypeName(underlying);
			return typeName != null ? typeName + '?' : typeName;
		}

		return ProcessTypeName(type);
	}

	public readonly record struct ScriptParameter(string? Name = null, string? Type = null, string? DefaultValue = null)
	{
		public readonly string Type = Type ?? "nil";

		public readonly string ToString(bool inFuncType = false) => Name switch
		{
			null => Type,
			// must use variadic type pack ('...' Type) in function types
			"..." when inFuncType => $"...{Type}",
			_ => $"{Name}: {Type}",
		};
	}

	public readonly record struct ScriptObsoletionInfo(string? Reason = null, string? UseInstead = null)
	{
		public ScriptObsoletionInfo(Attributes.ObsoleteAttribute obsoleteAttribute) : this(obsoleteAttribute.Reason, obsoleteAttribute.UseInstead) { }

		public override readonly string ToString()
		{
			if (UseInstead != null)
			{
				string result = $"Use `{UseInstead}` instead";
				return Reason != null ? $"{result}. {Reason}" : result;
			}
			return Reason ?? "";
		}

		/// <summary>
		/// Generates a Luau <c>@deprecated</c> attribute for functions:
		/// <see href="https://luau.org/attributes/#deprecated" />
		/// </summary>
		public readonly string GetAttribute()
		{
			List<string> args = [];
			if (Reason != null)
			{
				args.Add($"reason = \"{Reason}\"");
			}
			if (UseInstead != null)
			{
				args.Add($"use = \"{UseInstead}\"");
			}
			if (args.Count > 0)
			{
				return $"@[deprecated {{ {string.Join(", ", args)} }}]";
			}
			return "@deprecated";
		}

		/// <summary>
		/// Generates a moonwave <c>@deprecated</c> comment for non-functions:
		/// <see href="https://eryn.io/moonwave/docs/TagList/#deprecated" />
		/// </summary>
		public readonly string GetWarningComment()
		{
			string content = ToString();
			return content.Length != 0 ? $"--- @deprecated -- {content}" : "--- @deprecated";
		}
	}

	public readonly record struct ScriptMethod(string Name, string? ReturnType, ScriptParameter[] Parameters, bool IsAsync = false, bool IsStatic = false, bool IsSemiStatic = false, ScriptObsoletionInfo? ObsoletionInfo = null)
	{
		[JsonIgnore]
		public readonly bool IsMetamethod => Name.StartsWith("__");
	}

	public readonly record struct ScriptProperty(string Name, string Type, bool IsAccessibleByScripts, bool IsReadOnly, bool IsStatic, ScriptObsoletionInfo? ObsoletionInfo = null)
	{
		public readonly override string ToString() => $"{Name}: {Type}";
	}

	public readonly record struct ScriptEvent(string Name, ScriptParameter[] Parameters);

	public readonly record struct ScriptEnum(string Name, string InternalName, string[] Options);

	public readonly record struct ScriptClass(string Name, string? BaseType, ScriptProperty[] Properties, ScriptMethod[] Methods, ScriptEvent[] Events, bool IsStatic, bool IsAbstract, bool IsInstantiable, string? StaticAlias);

	public readonly record struct APIReferenceRoot(string Version, ScriptClass[] Classes, ScriptEnum[] Enums, string[] InstanceClasses);
}

[JsonSourceGenerationOptions(IncludeFields = true)]
[JsonSerializable(typeof(APIReferenceRoot))]
[JsonSerializable(typeof(ScriptClass))]
[JsonSerializable(typeof(ScriptEnum))]
[JsonSerializable(typeof(ScriptEvent))]
[JsonSerializable(typeof(ScriptProperty))]
[JsonSerializable(typeof(ScriptObsoletionInfo))]
[JsonSerializable(typeof(ScriptMethod))]
[JsonSerializable(typeof(ScriptParameter))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(ScriptClass[]))]
[JsonSerializable(typeof(ScriptEnum[]))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(ScriptProperty[]))]
[JsonSerializable(typeof(ScriptMethod[]))]
[JsonSerializable(typeof(ScriptEvent[]))]
[JsonSerializable(typeof(ScriptParameter[]))]
internal partial class APIRefGenerationContext : JsonSerializerContext { }
