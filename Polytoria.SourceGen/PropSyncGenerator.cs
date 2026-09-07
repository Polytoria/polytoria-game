// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace Polytoria.SourceGen;

[Generator]
public sealed class PropSyncGenerator : IIncrementalGenerator
{
	private const string SyncVarAttributeFullName = "Polytoria.Attributes.SyncVarAttribute";
	private const string EditableAttributeFullName = "Polytoria.Attributes.EditableAttribute";
	private const string NoSyncAttributeFullName = "Polytoria.Attributes.NoSyncAttribute";
	private const string NetworkedObjectFullName = "Polytoria.Datamodel.NetworkedObject";
	private const string MemoryPackableAttributeFullName = "MemoryPackableAttribute";
	private const string ObjectRefTypeFullName = "global::Polytoria.Datamodel.Services.NetworkService.NetPropNetworkedObjectRef";
	private const string DtoNamespace = "Polytoria.Utils.DTOs";

	private static readonly DiagnosticDescriptor _unsupportedSyncPropRule = new(
#pragma warning disable RS2008 // Enable analyzer release tracking
		id: "PTG0003",
#pragma warning restore RS2008 // Enable analyzer release tracking
		title: "Unsupported sync property",
		messageFormat: "'{0}' cannot be handled by PropSyncGenerator ({1})",
		category: "Polytoria.SourceGen",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor _conflictingDtoRule = new(
#pragma warning disable RS2008 // Enable analyzer release tracking
		id: "PTG0007",
#pragma warning restore RS2008 // Enable analyzer release tracking
		title: "Conflicting DTO conversion",
		messageFormat: "Source type '{0}' is matched by multiple DTOs ('{1}' and '{2}'); the sync encoding would be ambiguous",
		category: "Polytoria.SourceGen",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var syncVarProps = context.SyntaxProvider
			.ForAttributeWithMetadataName(
				SyncVarAttributeFullName,
				predicate: static (node, _) => node is PropertyDeclarationSyntax,
				transform: static (ctx, _) => GetPropEntry((IPropertySymbol)ctx.TargetSymbol, isSyncVar: true, ctx.Attributes[0]))
			.Collect();

		var editableProps = context.SyntaxProvider
			.ForAttributeWithMetadataName(
				EditableAttributeFullName,
				predicate: static (node, _) => node is PropertyDeclarationSyntax,
				transform: static (ctx, _) => GetPropEntry((IPropertySymbol)ctx.TargetSymbol, isSyncVar: false, attributeData: null))
			.Collect();

		var dtoMatch = context.CompilationProvider.Select(static (compilation, _) => BuildDtoMatch(compilation));

		context.RegisterSourceOutput(syncVarProps.Combine(editableProps).Combine(dtoMatch), static (spc, data) =>
		{
			var ((syncVarEntries, editableEntries), dtoMatchResult) = data;
			var (dtoMatchLookup, dtoConflicts) = dtoMatchResult;

			foreach (var (sourceType, first, second) in dtoConflicts)
				spc.ReportDiagnostic(Diagnostic.Create(_conflictingDtoRule, Location.None, sourceType, first, second));

			var merged = MergeEntries(syncVarEntries, editableEntries);

			var validTypes = merged
				.GroupBy(static e => e.DeclaringTypeFullName, StringComparer.Ordinal)
				.Where(static g => !g.Any(static e => e.Diagnostic is not null))
				.ToImmutableArray();

			spc.AddSource("PropSyncRegistry.g.cs", SourceText.From(GenerateSource(validTypes, dtoMatchLookup), Encoding.UTF8));

			foreach (var e in merged)
			{
				if (e.Diagnostic is { } d) spc.ReportDiagnostic(d);
			}
		});
	}

	private static ImmutableArray<PropEntry> MergeEntries(ImmutableArray<PropEntry?> syncVarEntries, ImmutableArray<PropEntry?> editableEntries)
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var merged = ImmutableArray.CreateBuilder<PropEntry>();

		foreach (var entry in syncVarEntries.Concat(editableEntries))
		{
			if (entry is not { } e) continue;

			if (e.Diagnostic is not null)
			{
				merged.Add(e);
				continue;
			}

			if (!seen.Add(e.DeclaringTypeFullName + "|" + e.PropertyName)) continue;

			merged.Add(e);
		}

		return merged.ToImmutable();
	}

	private static PropEntry? GetPropEntry(IPropertySymbol prop, bool isSyncVar, AttributeData? attributeData)
	{
		if (prop.Parameters.Length > 0 || prop.IsStatic) return null;

		if (prop.DeclaredAccessibility != Accessibility.Public) return null;

		INamedTypeSymbol containingType = prop.ContainingType;

		Location loc = prop.Locations.FirstOrDefault() ?? Location.None;

		if (containingType.IsGenericType)
		{
			return new PropEntry(Diagnostic.Create(_unsupportedSyncPropRule, loc, prop.Name,
				"generic containing types are not supported"));
		}

		if (!DerivesFromNetworkedObject(containingType)) return null;

		if (prop.GetAttributes().Any(static a => a.AttributeClass?.ToDisplayString() == NoSyncAttributeFullName)) return null;

		if (prop.GetMethod is null || prop.GetMethod.DeclaredAccessibility != Accessibility.Public)
			return new PropEntry(Diagnostic.Create(_unsupportedSyncPropRule, loc, prop.Name, "sync properties need a public getter"));

		if (prop.SetMethod is null ||
			prop.SetMethod.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
			return new PropEntry(Diagnostic.Create(_unsupportedSyncPropRule, loc, prop.Name, "sync properties need an assembly visible setter"));

		bool allowAuthorWrite = false, serverOnly = false, unreliable = false;
		foreach (var namedArg in attributeData?.NamedArguments ?? [])
		{
			if (namedArg.Value.Value is not bool flag) continue;

			if (namedArg.Key == "AllowAuthorWrite") allowAuthorWrite = flag;
			else if (namedArg.Key == "ServerOnly") serverOnly = flag;
			else if (namedArg.Key == "Unreliable") unreliable = flag;
		}

		string declaringType = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		string propType = StripNullableAnnotation(prop.Type);
		PropTypeFacts facts = ClassifyPropTypeFacts(prop.Type);

		return new PropEntry(declaringType, prop.Name, propType, facts,
			isSyncVar, allowAuthorWrite, serverOnly, unreliable);
	}

	private static bool DerivesFromNetworkedObject(INamedTypeSymbol type)
	{
		if (type.ToDisplayString() == NetworkedObjectFullName)
			return true;

		for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
		{
			if (baseType.ToDisplayString() == NetworkedObjectFullName)
				return true;
		}

		return false;
	}

	private static string StripNullableAnnotation(ITypeSymbol type)
	{
		if (type is INamedTypeSymbol { IsValueType: false, NullableAnnotation: NullableAnnotation.Annotated })
			type = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

		return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
	}

	private static PropTypeFacts ClassifyPropTypeFacts(ITypeSymbol type)
	{
		bool isObjectRef = false;
		for (var baseType = type as INamedTypeSymbol; baseType is not null; baseType = baseType.BaseType)
		{
			if (baseType.ToDisplayString() == NetworkedObjectFullName)
			{
				isObjectRef = true;
				break;
			}
		}

		bool isEnum = type.TypeKind == TypeKind.Enum;
		bool specialTypeEligible = type.SpecialType is not (SpecialType.None or SpecialType.System_Object);
		bool isArray = type is IArrayTypeSymbol;
		bool arrayElementTypedSafe = type is IArrayTypeSymbol array && IsTypedSafeElement(array.ElementType);
		bool valueTypeMemoryPackable = type.IsValueType && HasMemoryPackableAttribute(type);

		return new PropTypeFacts(isObjectRef, isEnum, specialTypeEligible, isArray, arrayElementTypedSafe, valueTypeMemoryPackable);
	}

	private static PropKind ResolveKind(in PropEntry entry, IReadOnlyDictionary<string, (string Dto, string ConvertMethod)> dtoMatch)
	{
		if (entry.Facts.IsObjectRef) return PropKind.ObjectRef;
		if (entry.Facts.IsEnum) return PropKind.EnumType;
		if (dtoMatch.ContainsKey(entry.PropertyTypeFullName)) return PropKind.Dto;
		if (entry.Facts.SpecialTypeEligible) return PropKind.Typed;
		if (entry.Facts.IsArray) return entry.Facts.ArrayElementTypedSafe ? PropKind.Typed : PropKind.Fallback;
		if (entry.Facts.ValueTypeMemoryPackable) return PropKind.Typed;
		return PropKind.Fallback;
	}

	private static bool IsTypedSafeElement(ITypeSymbol element)
	{
		if (element.SpecialType is not (SpecialType.None or SpecialType.System_Object))
			return true;

		return element.IsValueType && HasMemoryPackableAttribute(element);
	}

	private static bool HasMemoryPackableAttribute(ITypeSymbol type) =>
		type.GetAttributes().Any(static a => a.AttributeClass?.Name == MemoryPackableAttributeFullName);

	private static (ImmutableDictionary<string, (string Dto, string ConvertMethod)> Map, ImmutableArray<(string SourceType, string First, string Second)> Conflicts) BuildDtoMatch(Compilation compilation)
	{
		var builder = ImmutableDictionary.CreateBuilder<string, (string Dto, string ConvertMethod)>(StringComparer.Ordinal);
		var conflicts = ImmutableArray.CreateBuilder<(string SourceType, string First, string Second)>();

		if (FindNamespace(compilation.GlobalNamespace, DtoNamespace) is not { } dtoNamespace)
			return (builder.ToImmutable(), conflicts.ToImmutable());

		foreach (INamedTypeSymbol type in dtoNamespace.GetTypeMembers())
		{
			if (type.DeclaredAccessibility != Accessibility.Public) continue;

			foreach (IMethodSymbol ctor in type.InstanceConstructors)
			{
				if (ctor.DeclaredAccessibility != Accessibility.Public || ctor.Parameters.Length != 1) continue;

				ITypeSymbol sourceType = ctor.Parameters[0].Type;

				IMethodSymbol? convertMethod = type.GetMembers()
					.OfType<IMethodSymbol>()
					.FirstOrDefault(m => m.MethodKind == MethodKind.Ordinary && !m.IsStatic &&
						m.DeclaredAccessibility == Accessibility.Public && m.Parameters.Length == 0 &&
						SymbolEqualityComparer.Default.Equals(m.ReturnType, sourceType));

				if (convertMethod is null) continue;

				string sourceTypeKey = sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
				string dtoFullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

				if (builder.TryGetValue(sourceTypeKey, out var existing) && existing.Dto != dtoFullName)
				{
					conflicts.Add((sourceTypeKey, existing.Dto, dtoFullName));
					break;
				}

				builder[sourceTypeKey] = (dtoFullName, convertMethod.Name);
				break;
			}
		}

		return (builder.ToImmutable(), conflicts.ToImmutable());
	}

	private static INamespaceSymbol? FindNamespace(INamespaceSymbol root, string fullName)
	{
		INamespaceSymbol? current = root;
		foreach (string part in fullName.Split('.'))
		{
			if (current is null) return null;
			current = current.GetNamespaceMembers().FirstOrDefault(n => n.Name == part);
		}

		return current;
	}

	private static string GenerateSource(ImmutableArray<IGrouping<string, PropEntry>> typeGroups,
		IReadOnlyDictionary<string, (string Dto, string ConvertMethod)> dtoMatch)
	{
		var sb = new StringBuilder();
		using var stringWriter = new StringWriter(sb);
		using var writer = new IndentedTextWriter(stringWriter);

		writer.WriteLine("// <auto-generated/>");
		writer.WriteLine("#nullable enable");
		writer.WriteLine();
		writer.WriteLine("using System;");
		writer.WriteLine("using Polytoria.Utils;");
		writer.WriteLine();
		writer.WriteLine("namespace Polytoria.Networking.Synchronizers;");
		writer.WriteLine();
		writer.WriteLine("internal static partial class PropSyncRegistry");
		writer.WriteLine("{");
		writer.Indent++;
		writer.WriteLine("static partial void RegisterGenerated()");
		writer.WriteLine("{");
		writer.Indent++;

		foreach (var group in typeGroups)
		{
			writer.WriteLine($"Register(typeof({group.Key}),");
			writer.WriteLine("[");
			writer.Indent++;

			foreach (var entry in group.OrderBy(static e => e.PropertyName, StringComparer.Ordinal))
				WritePropEntry(writer, entry, dtoMatch);

			writer.Indent--;
			writer.WriteLine("]);");
		}

		writer.Indent--;
		writer.WriteLine("}");
		writer.Indent--;
		writer.WriteLine("}");

		return sb.ToString();
	}

	private static void WritePropEntry(IndentedTextWriter writer, PropEntry entry,
		IReadOnlyDictionary<string, (string Dto, string ConvertMethod)> dtoMatch)
	{
		PropKind kind = ResolveKind(entry, dtoMatch);

		(string serialize, string deserialize) = kind switch
		{
			PropKind.ObjectRef => (
				$"static v => v is null ? [] : SerializeUtils.Serialize(({ObjectRefTypeFullName})v)",
				$"static raw => raw.Length == 0 ? null : SerializeUtils.Deserialize<{ObjectRefTypeFullName}>(raw)"),
			PropKind.EnumType => (
				$"static v => v is null ? [] : SerializeUtils.Serialize(Convert.ToInt32(({entry.PropertyTypeFullName})v))",
				$"static raw => DeserializeEnum<{entry.PropertyTypeFullName}>(raw)"),
			PropKind.Dto => (
				$"static v => v is null ? [] : SerializeUtils.Serialize(new {dtoMatch[entry.PropertyTypeFullName].Dto}(({entry.PropertyTypeFullName})v))",
				$"static raw => raw.Length == 0 ? null : SerializeUtils.Deserialize<{dtoMatch[entry.PropertyTypeFullName].Dto}>(raw) is {{ }} dto ? (object?)dto.{dtoMatch[entry.PropertyTypeFullName].ConvertMethod}() : null"),
			PropKind.Typed => (
				$"static v => v is null ? [] : SerializeUtils.Serialize(({entry.PropertyTypeFullName})v)",
				$"static raw => raw.Length == 0 ? null : SerializeUtils.Deserialize<{entry.PropertyTypeFullName}>(raw)"),
			_ => (
				$"static v => v is null ? [] : SerializeUtils.Serialize(v.GetType(), v)",
				$"static raw => raw.Length == 0 ? null : SerializeUtils.Deserialize(typeof({entry.PropertyTypeFullName}), raw)"),
		};

		writer.WriteLine("new()");
		writer.WriteLine("{");
		writer.Indent++;
		writer.WriteLine($"Name = \"{entry.PropertyName}\",");
		writer.WriteLine($"HasSyncVar = {entry.HasSyncVar.ToString().ToLowerInvariant()},");
		writer.WriteLine($"AllowAuthorWrite = {entry.AllowAuthorWrite.ToString().ToLowerInvariant()},");
		writer.WriteLine($"ServerOnly = {entry.ServerOnly.ToString().ToLowerInvariant()},");
		writer.WriteLine($"Unreliable = {entry.Unreliable.ToString().ToLowerInvariant()},");
		writer.WriteLine($"IsObjectRef = {(kind == PropKind.ObjectRef).ToString().ToLowerInvariant()},");
		writer.WriteLine($"GetValue = static o => (({entry.DeclaringTypeFullName})o).{entry.PropertyName},");
		writer.WriteLine($"SetValue = static (o, v) => (({entry.DeclaringTypeFullName})o).{entry.PropertyName} = ({entry.PropertyTypeFullName})v!,");
		writer.WriteLine($"Serialize = {serialize},");
		writer.WriteLine($"Deserialize = {deserialize},");
		writer.Indent--;
		writer.WriteLine("},");
	}

	private enum PropKind
	{
		ObjectRef,
		EnumType,
		Dto,
		Typed,
		Fallback
	}

	private readonly struct PropTypeFacts
	{
		public readonly bool IsObjectRef;
		public readonly bool IsEnum;
		public readonly bool SpecialTypeEligible;
		public readonly bool IsArray;
		public readonly bool ArrayElementTypedSafe;
		public readonly bool ValueTypeMemoryPackable;

		public PropTypeFacts(bool isObjectRef, bool isEnum, bool specialTypeEligible, bool isArray,
			bool arrayElementTypedSafe, bool valueTypeMemoryPackable)
		{
			IsObjectRef = isObjectRef;
			IsEnum = isEnum;
			SpecialTypeEligible = specialTypeEligible;
			IsArray = isArray;
			ArrayElementTypedSafe = arrayElementTypedSafe;
			ValueTypeMemoryPackable = valueTypeMemoryPackable;
		}
	}

	private readonly struct PropEntry
	{
		public readonly string DeclaringTypeFullName;
		public readonly string PropertyName;
		public readonly string PropertyTypeFullName;
		public readonly PropTypeFacts Facts;
		public readonly bool HasSyncVar;
		public readonly bool AllowAuthorWrite;
		public readonly bool ServerOnly;
		public readonly bool Unreliable;
		public readonly Diagnostic? Diagnostic;

		public PropEntry(string declaringTypeFullName, string propertyName, string propertyTypeFullName, PropTypeFacts facts,
			bool hasSyncVar, bool allowAuthorWrite, bool serverOnly, bool unreliable)
		{
			DeclaringTypeFullName = declaringTypeFullName;
			PropertyName = propertyName;
			PropertyTypeFullName = propertyTypeFullName;
			Facts = facts;
			HasSyncVar = hasSyncVar;
			AllowAuthorWrite = allowAuthorWrite;
			ServerOnly = serverOnly;
			Unreliable = unreliable;
			Diagnostic = null;
		}

		public PropEntry(Diagnostic diagnostic)
		{
			DeclaringTypeFullName = "";
			PropertyName = "";
			PropertyTypeFullName = "";
			Facts = default;
			HasSyncVar = false;
			AllowAuthorWrite = false;
			ServerOnly = false;
			Unreliable = false;
			Diagnostic = diagnostic;
		}
	}
}
