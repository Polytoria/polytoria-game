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
public sealed class DatamodelMetaGenerator : IIncrementalGenerator
{
	private const string EditableAttributeFullName = "Polytoria.Attributes.EditableAttribute";
	private const string SaveIncludeAttributeFullName = "Polytoria.Attributes.SaveIncludeAttribute";
	private const string CloneIncludeAttributeFullName = "Polytoria.Attributes.CloneIncludeAttribute";
	private const string CloneIgnoreAttributeFullName = "Polytoria.Attributes.CloneIgnoreAttribute";
	private const string SaveIgnoreAttributeFullName = "Polytoria.Attributes.SaveIgnoreAttribute";
	private const string ObsoleteAttributeFullName = "Polytoria.Attributes.ObsoleteAttribute";
	private const string NetworkedObjectFullName = "Polytoria.Datamodel.NetworkedObject";
	private const string FileLinkAssetFullName = "Polytoria.Datamodel.Resources.FileLinkAsset";

	private static readonly DiagnosticDescriptor _unsupportedPropRule = new(
#pragma warning disable RS2008 // Enable analyzer release tracking
		id: "PTG0006",
#pragma warning restore RS2008 // Enable analyzer release tracking
		title: "Unsupported datamodel property",
		messageFormat: "'{0}' cannot be handled by DatamodelMetaGenerator ({1})",
		category: "Polytoria.SourceGen",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var editableProps = CollectProps(context, EditableAttributeFullName);
		var saveIncludeProps = CollectProps(context, SaveIncludeAttributeFullName);
		var cloneIncludeProps = CollectProps(context, CloneIncludeAttributeFullName);

		context.RegisterSourceOutput(editableProps.Combine(saveIncludeProps).Combine(cloneIncludeProps), static (spc, data) =>
		{
			var ((editableEntries, saveIncludeEntries), cloneIncludeEntries) = data;

			var merged = MergeEntries(editableEntries, saveIncludeEntries, cloneIncludeEntries);

			var validTypes = merged
				.GroupBy(static e => e.DeclaringTypeFullName, StringComparer.Ordinal)
				.Where(static g => !g.Any(static e => e.Diagnostic is not null))
				.ToImmutableArray();

			spc.AddSource("DatamodelMetaRegistry.g.cs", SourceText.From(GenerateSource(validTypes), Encoding.UTF8));

			foreach (var e in merged)
			{
				if (e.Diagnostic is { } d) spc.ReportDiagnostic(d);
			}
		});
	}

	private static IncrementalValueProvider<ImmutableArray<MetaPropEntry?>> CollectProps(
		IncrementalGeneratorInitializationContext context, string attributeFullName)
	{
		return context.SyntaxProvider
			.ForAttributeWithMetadataName(
				attributeFullName,
				predicate: static (node, _) => node is PropertyDeclarationSyntax,
				transform: static (ctx, _) => GetPropEntry((IPropertySymbol)ctx.TargetSymbol))
			.Collect();
	}

	private static ImmutableArray<MetaPropEntry> MergeEntries(
		ImmutableArray<MetaPropEntry?> editableEntries,
		ImmutableArray<MetaPropEntry?> saveIncludeEntries,
		ImmutableArray<MetaPropEntry?> cloneIncludeEntries)
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var merged = ImmutableArray.CreateBuilder<MetaPropEntry>();

		foreach (var entry in editableEntries.Concat(saveIncludeEntries).Concat(cloneIncludeEntries))
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

	private static MetaPropEntry? GetPropEntry(IPropertySymbol prop)
	{
		if (prop.Parameters.Length > 0 || prop.IsStatic) return null;

		if (prop.DeclaredAccessibility != Accessibility.Public) return null;

		INamedTypeSymbol containingType = prop.ContainingType;

		Location loc = prop.Locations.FirstOrDefault() ?? Location.None;

		if (containingType.IsGenericType)
		{
			return new MetaPropEntry(Diagnostic.Create(_unsupportedPropRule, loc, prop.Name,
				"generic containing types are not supported"));
		}

		if (!DerivesFromType(containingType, NetworkedObjectFullName))
		{
			return new MetaPropEntry(Diagnostic.Create(_unsupportedPropRule, loc, prop.Name,
				"containing type must derive from NetworkedObject"));
		}

		bool isEditable = false, hasSaveInclude = false, hasCloneInclude = false,
			hasCloneIgnore = false, hasSaveIgnore = false, isObsolete = false;

		foreach (AttributeData attr in prop.GetAttributes())
		{
			string? name = attr.AttributeClass?.ToDisplayString();

			if (name == EditableAttributeFullName) isEditable = true;
			else if (name == SaveIncludeAttributeFullName) hasSaveInclude = true;
			else if (name == CloneIncludeAttributeFullName) hasCloneInclude = true;
			else if (name == CloneIgnoreAttributeFullName) hasCloneIgnore = true;
			else if (name == SaveIgnoreAttributeFullName) hasSaveIgnore = true;
			else if (name == ObsoleteAttributeFullName) isObsolete = true;
		}

		string declaringType = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		string propType = StripNullableAnnotation(prop.Type);

		if (prop.GetMethod is not null && !IsAccessorAccessible(prop.GetMethod))
		{
			return new MetaPropEntry(Diagnostic.Create(_unsupportedPropRule, loc, prop.Name,
				"meta properties need an assembly visible getter"));
		}

		if (prop.SetMethod is not null && !IsAccessorAccessible(prop.SetMethod))
		{
			return new MetaPropEntry(Diagnostic.Create(_unsupportedPropRule, loc, prop.Name,
				"meta properties need an assembly visible setter"));
		}

		return new MetaPropEntry(declaringType, prop.Name, propType,
			isEditable, hasSaveInclude, hasCloneInclude, hasCloneIgnore, hasSaveIgnore, isObsolete,
			DerivesFromType(prop.Type, FileLinkAssetFullName),
			DerivesFromType(prop.Type, NetworkedObjectFullName));
	}

	private static bool DerivesFromType(ITypeSymbol type, string fullName)
	{
		if (type is INamedTypeSymbol { IsValueType: false, NullableAnnotation: NullableAnnotation.Annotated })
			type = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

		for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
		{
			if (current.ToDisplayString() == fullName)
				return true;
		}

		return false;
	}

	private static bool IsAccessorAccessible(IMethodSymbol? accessor)
	{
		return accessor is not null && accessor.DeclaredAccessibility is
			Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;
	}

	private static string StripNullableAnnotation(ITypeSymbol type)
	{
		if (type is INamedTypeSymbol { IsValueType: false, NullableAnnotation: NullableAnnotation.Annotated })
			type = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

		return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
	}

	private static string GenerateSource(ImmutableArray<IGrouping<string, MetaPropEntry>> typeGroups)
	{
		var sb = new StringBuilder();
		using var stringWriter = new StringWriter(sb);
		using var writer = new IndentedTextWriter(stringWriter);

		writer.WriteLine("// <auto-generated/>");
		writer.WriteLine("#nullable enable");
		writer.WriteLine();
		writer.WriteLine("using System;");
		writer.WriteLine("using System.Reflection;");
		writer.WriteLine();
		writer.WriteLine("namespace Polytoria.Formats;");
		writer.WriteLine();
		writer.WriteLine("internal static partial class DatamodelMetaRegistry");
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
				WritePropEntry(writer, entry);

			writer.Indent--;
			writer.WriteLine("]);");
		}

		writer.Indent--;
		writer.WriteLine("}");
		writer.Indent--;
		writer.WriteLine("}");

		return sb.ToString();
	}

	private static void WritePropEntry(IndentedTextWriter writer, MetaPropEntry entry)
	{
		writer.WriteLine("new()");
		writer.WriteLine("{");
		writer.Indent++;
		writer.WriteLine($"Name = \"{entry.PropertyName}\",");
		writer.WriteLine($"Property = typeof({entry.DeclaringTypeFullName}).GetProperty(\"{entry.PropertyName}\", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!,");
		writer.WriteLine($"PropertyType = typeof({entry.PropertyTypeFullName}),");
		writer.WriteLine($"IsEditable = {entry.IsEditable.ToString().ToLowerInvariant()},");
		writer.WriteLine($"HasSaveInclude = {entry.HasSaveInclude.ToString().ToLowerInvariant()},");
		writer.WriteLine($"HasCloneInclude = {entry.HasCloneInclude.ToString().ToLowerInvariant()},");
		writer.WriteLine($"HasCloneIgnore = {entry.HasCloneIgnore.ToString().ToLowerInvariant()},");
		writer.WriteLine($"HasSaveIgnore = {entry.HasSaveIgnore.ToString().ToLowerInvariant()},");
		writer.WriteLine($"IsObsolete = {entry.IsObsolete.ToString().ToLowerInvariant()},");
		writer.WriteLine($"IsFileLink = {entry.IsFileLink.ToString().ToLowerInvariant()},");
		writer.WriteLine($"IsObjectRef = {entry.IsObjectRef.ToString().ToLowerInvariant()},");
		writer.WriteLine($"GetValue = {WriteGetter(entry)},");
		writer.WriteLine($"SetValue = {WriteSetter(entry)},");
		writer.Indent--;
		writer.WriteLine("},");
	}

	private static string WriteGetter(MetaPropEntry entry)
	{
		return $"static o => (({entry.DeclaringTypeFullName})o).{entry.PropertyName}";
	}

	private static string WriteSetter(MetaPropEntry entry)
	{
		return $"static (o, v) => (({entry.DeclaringTypeFullName})o).{entry.PropertyName} = ({entry.PropertyTypeFullName})v!";
	}

	private readonly struct MetaPropEntry
	{
		public readonly string DeclaringTypeFullName;
		public readonly string PropertyName;
		public readonly string PropertyTypeFullName;
		public readonly bool IsEditable;
		public readonly bool HasSaveInclude;
		public readonly bool HasCloneInclude;
		public readonly bool HasCloneIgnore;
		public readonly bool HasSaveIgnore;
		public readonly bool IsObsolete;
		public readonly bool IsFileLink;
		public readonly bool IsObjectRef;
		public readonly Diagnostic? Diagnostic;

		public MetaPropEntry(string declaringTypeFullName, string propertyName, string propertyTypeFullName,
			bool isEditable, bool hasSaveInclude, bool hasCloneInclude, bool hasCloneIgnore, bool hasSaveIgnore,
			bool isObsolete, bool isFileLink, bool isObjectRef)
		{
			DeclaringTypeFullName = declaringTypeFullName;
			PropertyName = propertyName;
			PropertyTypeFullName = propertyTypeFullName;
			IsEditable = isEditable;
			HasSaveInclude = hasSaveInclude;
			HasCloneInclude = hasCloneInclude;
			HasCloneIgnore = hasCloneIgnore;
			HasSaveIgnore = hasSaveIgnore;
			IsObsolete = isObsolete;
			IsFileLink = isFileLink;
			IsObjectRef = isObjectRef;
			Diagnostic = null;
		}

		public MetaPropEntry(Diagnostic diagnostic)
		{
			DeclaringTypeFullName = "";
			PropertyName = "";
			PropertyTypeFullName = "";
			IsEditable = false;
			HasSaveInclude = false;
			HasCloneInclude = false;
			HasCloneIgnore = false;
			HasSaveIgnore = false;
			IsObsolete = false;
			IsFileLink = false;
			IsObjectRef = false;
			Diagnostic = diagnostic;
		}
	}
}
