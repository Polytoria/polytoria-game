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
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Polytoria.DocsGen.APIReferenceGenerator;

namespace Polytoria.DocsGen;

public class APIReferenceGenerator
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

			if (type.IsAssignableTo(typeof(Instance)))
			{
				instanceClasses.Add(ProcessClassName(type));
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

				if (property.PropertyType == typeof(PTSignal) ||
					(property.PropertyType.IsGenericType &&
					 property.PropertyType.GetGenericTypeDefinition().Name.StartsWith(nameof(PTSignal))))
				{
					List<ScriptParameter> paramsDef = [];

					Type propertyType = property.PropertyType;
					if (propertyType.IsGenericType)
					{
						Type[] genericArgs = propertyType.GetGenericArguments();
						for (int i = 0; i < genericArgs.Length; i++)
						{
							string tn = ProcessTypeName(genericArgs[i]) ?? "nil";
							paramsDef.Add(new(null, tn));
						}
					}

					eventsDef.Add(new(property.Name, [.. paramsDef]));
				}
				else
				{
					if (property.PropertyType.IsEnum && !ScriptService.EnumMap.ContainsValue(property.PropertyType))
					{
						missingEnums.Add(property.PropertyType);
					}

					propertiesDef.Add(new(
						property.Name,
						ProcessTypeName(property.PropertyType),
						isEditable || isScriptProperty,
						isScriptProperty && property.GetSetMethod(false) == null,
						property.IsDefined(typeof(Attributes.ObsoleteAttribute)),
						property.GetAccessors(true)[0].IsStatic
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
					paramsDef.Add(new(
						isVarArg ? "..." : item.Name,
						ProcessTypeName(isVarArg ? item.ParameterType.GetElementType() : item.ParameterType),
						item.HasDefaultValue,
						item.HasDefaultValue ? item.DefaultValue?.ToString() : null
					));
				}

				Attributes.ObsoleteAttribute? obsoleteAttribute = method.GetCustomAttribute<Attributes.ObsoleteAttribute>();
				methodsDef.Add(new(
					metaMethodAttribute != null ? GetMetamethodIndexer(metaMethodAttribute.Metamethod) : methodAttribute?.MethodName ?? method.Name,
					ProcessTypeName(returnType),
					[.. paramsDef],
					asyncFunc,
					obsoleteAttribute != null,
					method.IsStatic,
					method.IsStatic && (methodAttribute?.SemiStatic ?? false),
					obsoleteAttribute != null ? new(obsoleteAttribute.Reason, obsoleteAttribute.UseInstead) : null
				));
			}

			// __index & __newindex for Instance
			if (type == typeof(Instance))
			{
				methodsDef.Add(new(
					"__index",
					"any",
					[
						new("self", nameof(Instance)),
					]
				));
				methodsDef.Add(new(
					"__newindex",
					null,
					[
						new("self", nameof(Instance)),
						new("val", "any"),
					]
				));
			}
			string name = ProcessClassName(type);
			bool isInstantiable = type.IsDefined(typeof(InstantiableAttribute), false);
			if (isInstantiable)
			{
				methodsDef.Add(new(
					"New",
					name,
					[
						new("parent", nameof(NetworkedObject), true)
					],
					isStatic: true
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
		if (type == null) return null;
		if (Nullable.GetUnderlyingType(type) is Type underlying)
			type = underlying;

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

		if (type == typeof(PTCallback) || type == typeof(PTFunction))
		{
			return "() -> ()";
		}

		if (type == typeof(void) ||
			type == typeof(Task) ||
			type == typeof(Nullable) ||
			type == typeof(ValueType))
		{
			return null;
		}

		// --- Proxies --- //

		if (type == typeof(Aabb))
		{
			return "Bounds";
		}

		// --------------- //

		if (type.IsAssignableTo(typeof(IScriptGDObject)))
		{
			return ProcessClassName(type);
		}

		if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
		{
			return ProcessTypeName(type.GetGenericArguments()[0]);
		}

		if (type.IsArray)
		{
			return $"{{ {ProcessTypeName(type.GetElementType())} }}";
		}

		if (type.IsAssignableTo(typeof(IDictionary)))
		{
			Type[] args = type.GetGenericArguments();
			string? indexType = ProcessTypeName(args[0]);
			string? valueType = ProcessTypeName(args[1]);
			if (indexType == null || valueType == null) return "{  }";
			return $"{{ [{indexType}]: {valueType} }}";
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

	public readonly struct ScriptParameter(string? name, string? type = null, bool isOptional = false, string? defaultValue = null)
	{
		public readonly string? Name = name;
		public readonly string? Type = type;
		public readonly bool IsOptional = isOptional;
		public readonly string? DefaultValue = defaultValue;
		[JsonIgnore]
		public readonly bool IsVarArg => Name == "...";

		public readonly override string ToString()
		{
			string argType = Type != null ? $"{Type}{(IsOptional ? "?" : "")}": "nil";
			return Name != null ? $"{Name}: {argType}" : argType;
		}
	}

	public readonly struct ScriptObsoletionInfo(string? reason = null, string? use = null)
	{
		public readonly string? Reason = reason;
		public readonly string? UseInstead = use;

		public readonly override string ToString()
		{
			List<string> parameters = [];
			if (Reason != null)
			{
				parameters.Add($"reason = \"{Reason}\"");
			}
			if (UseInstead != null)
			{
				parameters.Add($"use = \"{UseInstead}\"");
			}
			if (parameters.Count > 0)
			{
				return $"@[deprecated {{ {string.Join(", ", parameters)} }}]";
			}
			return "@deprecated";
		}
	}

	public readonly struct ScriptMethod(string name, string? returnType, ScriptParameter[] parameters, bool isAsync = false, bool isObsolete = false, bool isStatic = false, bool isSemiStatic = false, ScriptObsoletionInfo? obsoletionInfo = null)
	{
		public readonly string Name = name;
		public readonly string? ReturnType = returnType;
		public readonly ScriptParameter[] Parameters = parameters;
		public readonly bool IsAsync = isAsync;
		public readonly bool IsObsolete = isObsolete;
		public readonly bool IsStatic = isStatic;
		public readonly bool IsSemiStatic = isSemiStatic;
		public readonly ScriptObsoletionInfo? ObsoletionInfo = obsoletionInfo;
		[JsonIgnore]
		public readonly bool IsMetamethod => Name.StartsWith("__");
	}

	public readonly struct ScriptProperty(string name, string? type, bool isAccessibleByScripts, bool isReadOnly, bool isObsolete, bool isStatic)
	{
		public readonly string Name = name;
		public readonly string? Type = type;
		public readonly bool IsAccessibleByScripts = isAccessibleByScripts;
		public readonly bool IsReadOnly = isReadOnly;
		public readonly bool IsObsolete = isObsolete;
		public readonly bool IsStatic = isStatic;

		public readonly override string ToString()
		{
			return $"{Name}: {Type ?? "nil"}";
		}
	}

	public readonly struct ScriptEvent(string name, ScriptParameter[] parameters)
	{
		public readonly string Name = name;
		public readonly ScriptParameter[] Parameters = parameters;
	}

	public readonly struct ScriptEnum(string name, string internalName, string[] options)
	{
		public readonly string Name = name;
		public readonly string InternalName = internalName;
		public readonly string[] Options = options;
	}

	public readonly struct ScriptClass(string name, string? baseType, ScriptProperty[] properties, ScriptMethod[] methods, ScriptEvent[] events, bool isStatic, bool isAbstract, bool isInstantiable, string? staticAlias)
	{
		public readonly string Name = name;
		public readonly string? BaseType = baseType;
		public readonly ScriptProperty[] Properties = properties;
		public readonly ScriptMethod[] Methods = methods;
		public readonly ScriptEvent[] Events = events;
		public readonly bool IsStatic = isStatic;
		public readonly bool IsAbstract = isAbstract;
		public readonly bool IsInstantiable = isInstantiable;
		public readonly string? StaticAlias = staticAlias;
	}

	public readonly struct APIReferenceRoot(string version, ScriptClass[] classes, ScriptEnum[] enums, string[] instanceClasses)
	{
		public readonly string Version = version;
		public readonly ScriptClass[] Classes = classes;
		public readonly ScriptEnum[] Enums = enums;
		public readonly string[] InstanceClasses = instanceClasses;
	}
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
