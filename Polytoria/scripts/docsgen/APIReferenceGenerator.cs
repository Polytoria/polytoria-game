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
using System.Collections.Immutable;
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
							? [.. propertyType.GetGenericArguments().Select(a => new ScriptParameter(ProcessScriptType(a).FirstOrDefault(ScriptType.Nil)))]
							: []
					));
				}
				else
				{
					if (propertyType.IsEnum && !ScriptService.EnumMap.ContainsValue(propertyType))
					{
						missingEnums.Add(propertyType);
					}

					ScriptType? scriptType = ProcessScriptType(propertyType).FirstOrDefault();
					if (scriptType == null) continue;

					propertiesDef.Add(new(
						property.Name,
						scriptType,
						isEditable || isScriptProperty,
						isScriptProperty && property.GetSetMethod(false) == null,
						property.GetGetMethod(true)?.IsStatic ?? false,
						property.GetCustomAttribute<Attributes.ObsoleteAttribute>()?.Info
					));
				}
			}

			foreach (MethodInfo method in methods)
			{
				if (method.IsDefined(typeof(HandlesLuaStateAttribute))) continue;

				ScriptMethodAttribute? methodAttribute = method.GetCustomAttribute<ScriptMethodAttribute>();
				ScriptMetamethodAttribute? metaMethodAttribute = method.GetCustomAttribute<ScriptMetamethodAttribute>();

				if (methodAttribute != null)
				{
					ScriptLegacyMethodAttribute? legacyMethodAttribute = method.GetCustomAttribute<ScriptLegacyMethodAttribute>();
					// ignore methods that have ScriptMethodAttribute but are only
					// meant for legacy scripts (e.g. Datastore.Get)
					if (legacyMethodAttribute != null && legacyMethodAttribute.MethodName == method.Name) continue;
				}
				else if (metaMethodAttribute == null) continue;

				Type returnType = method.ReturnType;

				List<ScriptParameter> paramsDef = [];

				foreach (ParameterInfo item in method.GetParameters())
				{
					if (item.ParameterType == typeof(Node)) continue;
					if (item.IsDefined(typeof(ScriptingCallerAttribute))) continue;

					bool isVarArg = item.IsDefined(typeof(ParamArrayAttribute));
					Type? paramType = isVarArg ? item.ParameterType.GetElementType() : item.ParameterType;
					if (paramType == null) continue;

					ScriptType? scriptType = ProcessScriptType(paramType, item.HasDefaultValue).FirstOrDefault();
					if (scriptType == null) continue;

					paramsDef.Add(new(
						isVarArg ? new ScriptTypeTuple(scriptType) : scriptType,
						item.Name,
						item.HasDefaultValue ? item.DefaultValue?.ToString() : null
					));
				}

				methodsDef.Add(new(
					metaMethodAttribute != null ? GetMetamethodIndexer(metaMethodAttribute.Metamethod) : methodAttribute?.MethodName ?? method.Name,
					[.. ProcessScriptType(returnType)],
					[.. paramsDef],
					returnType == typeof(Task) || returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>),
					method.IsStatic,
					method.IsStatic && (methodAttribute?.SemiStatic ?? false),
					method.GetCustomAttribute<Attributes.ObsoleteAttribute>()?.Info
				));
			}

			// __index & __newindex for Instance
			if (type == typeof(Instance))
			{
				methodsDef.Add(new(
					"__index",
					[
						ScriptType.Any,
					],
					[
						new(ScriptType.Any, "index"),
					]
				));
				methodsDef.Add(new(
					"__newindex",
					[],
					[
						new(ScriptType.Any, "index"),
						new(ScriptType.Any, "value"),
					]
				));
			}

			bool isInstantiable = type.IsDefined(typeof(InstantiableAttribute), false);
			if (isInstantiable)
			{
				methodsDef.Add(new(
					"New",
					[
						new ScriptType(name),
					],
					[
						new(new ScriptType(nameof(NetworkedObject), true), "parent")
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
			enums.Add(new(key, enumType.Name, [.. Enum.GetNames(enumType)]));
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
			if (!classMap.TryGetValue(type, out ScriptClass? v)) return;

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

	private static IEnumerable<ScriptType> ProcessScriptType(Type? type, bool optional = false)
	{
		if (type == null ||
			type == typeof(void) ||
			type == typeof(Task) ||
			type == typeof(ValueType))
		{
			yield break;
		}

		Type? underlying = Nullable.GetUnderlyingType(type);
		if (underlying != null)
		{
			type = underlying;
			optional = true;
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
			yield return new ScriptType("number", optional);
		}
		else if (type == typeof(string))
		{
			yield return new ScriptType("string", optional);
		}
		else if (type == typeof(bool))
		{
			yield return new ScriptType("boolean", optional);
		}
		else if (type == typeof(object))
		{
			yield return new ScriptType("any", optional);
		}
		else if (type == typeof(byte[]))
		{
			yield return new ScriptType("buffer", optional);
		}
		// TODO: make PTFunction and PTCallback generic so these aren't garbage types
		else if (type == typeof(PTCallback))
		{
			yield return new ScriptTypeFunction([new(ScriptTypeTuple.Any)], [], optional);
		}
		else if (type == typeof(PTFunction))
		{
			yield return new ScriptTypeFunction([new(ScriptTypeTuple.Any)], [ScriptTypeTuple.Any], optional);
		}
		// this behavior is caused by LuaMetatable
		else if (type == typeof(Task<object?[]>))
		{
			yield return ScriptTypeTuple.Any;
		}
		// --- Proxies --- //
		else if (type == typeof(Aabb))
		{
			yield return new ScriptType("Bounds", optional);
		}
		// --------------- //
		else if (type.IsAssignableTo(typeof(ITuple)))
		{
			foreach (ScriptType t in type.GetGenericArguments().SelectMany(arg => ProcessScriptType(arg)))
			{
				yield return t;
			}
		}
		else if (type.IsAssignableTo(typeof(IScriptGDObject)))
		{
			yield return new ScriptType(ProcessClassName(type), optional);
		}
		else if (type.IsAssignableTo(typeof(IDictionary)))
		{
			ScriptType keyType = ScriptType.Any;
			ScriptType valueType = ScriptType.Any;
			Type[] args = type.GetGenericArguments();
			if (args.Length >= 2)
			{
				keyType = ProcessScriptType(args[0]).FirstOrDefault(ScriptType.Nil);
				valueType = ProcessScriptType(args[1]).FirstOrDefault(ScriptType.Nil);
			}
			yield return new ScriptTypeDictionary(keyType, valueType, optional);
		}
		else if (type.IsGenericType)
		{
			Type genericType = type.GetGenericTypeDefinition();
			if (genericType == typeof(Task<>))
			{
				foreach (ScriptType t in ProcessScriptType(type.GetGenericArguments()[0]))
				{
					yield return t;
				}
			}
			else if (genericType == typeof(IEnumerable<>))
			{
				yield return new ScriptTypeFunction([new(ScriptType.Any)], [ProcessScriptType(type.GetGenericArguments()[0]).FirstOrDefault(ScriptType.Nil), ScriptType.Nil, ScriptType.Nil], optional);
			}
		}
		else if (type.IsArray)
		{
			yield return new ScriptTypeArray(ProcessScriptType(type.GetElementType()).FirstOrDefault(ScriptType.Nil), optional);
		}
		else if (type.IsEnum)
		{
			// Find the Enum's external name
			string name = ScriptService.EnumMap.FirstOrDefault(x => x.Value == type).Key;
			if (!string.IsNullOrEmpty(name))
				yield return new ScriptType(name, optional);
		}
		else
		{
			yield return new ScriptType(type.Name, optional);
		}
	}

	public record APIReferenceRoot(string Version, ImmutableArray<ScriptClass> Classes, ImmutableArray<ScriptEnum> Enums, ImmutableArray<string> InstanceClasses);
}

public record ScriptParameter(ScriptType Type, string? Name = null, string? DefaultValue = null)
{
	public string LuaifyForFuncType()
	{
		string luaType = Type.Luaify();
		if (Type is not ScriptTypeTuple && Name != null) return $"{Name}: {luaType}";

		return luaType;
	}

	public string LuaifyForFuncDef()
	{
		if (Type is ScriptTypeTuple t) return $"...: {t.ElementType.Luaify()}";

		return $"{Name ?? "_"}: {Type.Luaify()}";
	}
}

public record ScriptMethod(string Name, ImmutableArray<ScriptType> Returns, ImmutableArray<ScriptParameter> Parameters, bool IsAsync = false, bool IsStatic = false, bool IsSemiStatic = false, ObsoletionInfo? ObsoletionInfo = null)
{
	[JsonIgnore]
	public readonly bool IsMetamethod = Name.StartsWith("__");
}

public record ScriptProperty(string Name, ScriptType Type, bool IsAccessibleByScripts, bool IsReadOnly, bool IsStatic, ObsoletionInfo? ObsoletionInfo = null);

public record ScriptEvent(string Name, ImmutableArray<ScriptParameter> Parameters);

public record ScriptEnum(string Name, string InternalName, ImmutableArray<string> Options);

public record ScriptClass(string Name, string? BaseType, ImmutableArray<ScriptProperty> Properties, ImmutableArray<ScriptMethod> Methods, ImmutableArray<ScriptEvent> Events, bool IsStatic, bool IsAbstract, bool IsInstantiable, string? StaticAlias);

[JsonDerivedType(typeof(ScriptTypeTuple))]
[JsonDerivedType(typeof(ScriptTypeFunction))]
[JsonDerivedType(typeof(ScriptTypeArray))]
[JsonDerivedType(typeof(ScriptTypeDictionary))]
public record ScriptType(string Name, bool IsOptional = false)
{
	public static readonly ScriptType Nil = new("nil");
	public static readonly ScriptType Any = new("any");

	protected virtual string InternalLuaType => Name;

	public string Luaify() => IsOptional ? InternalLuaType + '?' : InternalLuaType;
}

public record ScriptTypeTuple(ScriptType ElementType) : ScriptType("Tuple", false)
{
	public new static readonly ScriptTypeTuple Any = new(ScriptType.Any);

	protected override string InternalLuaType => $"...{ElementType.Luaify()}";
}

public record ScriptTypeFunction(ImmutableArray<ScriptParameter> Parameters, ImmutableArray<ScriptType> Returns, bool IsOptional = false) : ScriptType("function", IsOptional)
{
	protected override string InternalLuaType => $"({string.Join(", ", Parameters.Select(p => p.LuaifyForFuncType()))}) -> ({string.Join(", ", Returns.Select(t => t.Luaify()))})";
}

public record ScriptTypeArray(ScriptType ElementType, bool IsOptional = false) : ScriptType("Array", IsOptional)
{
	protected override string InternalLuaType => $"{{ {ElementType.Luaify()} }}";
}

public record ScriptTypeDictionary(ScriptType KeyType, ScriptType ValueType, bool IsOptional = false) : ScriptType("Dictionary", IsOptional)
{
	protected override string InternalLuaType => $"{{ [{KeyType.Luaify()}]: {ValueType.Luaify()} }}";
}

[JsonSourceGenerationOptions(IncludeFields = true)]
[JsonSerializable(typeof(APIReferenceRoot))]
[JsonSerializable(typeof(ScriptClass))]
[JsonSerializable(typeof(ScriptEnum))]
[JsonSerializable(typeof(ScriptEvent))]
[JsonSerializable(typeof(ScriptProperty))]
[JsonSerializable(typeof(ObsoletionInfo))]
[JsonSerializable(typeof(ScriptMethod))]
[JsonSerializable(typeof(ScriptParameter))]
[JsonSerializable(typeof(ScriptType))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(ImmutableArray<ScriptClass>))]
[JsonSerializable(typeof(ImmutableArray<ScriptEnum>))]
[JsonSerializable(typeof(ImmutableArray<string>))]
[JsonSerializable(typeof(ImmutableArray<ScriptProperty>))]
[JsonSerializable(typeof(ImmutableArray<ScriptMethod>))]
[JsonSerializable(typeof(ImmutableArray<ScriptEvent>))]
[JsonSerializable(typeof(ImmutableArray<ScriptParameter>))]
[JsonSerializable(typeof(ImmutableArray<ScriptType>))]
internal partial class APIRefGenerationContext : JsonSerializerContext { }
