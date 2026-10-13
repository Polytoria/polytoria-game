// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace Polytoria.SourceGen;

[Generator]
public sealed class ScriptInterfaceGenerator : IIncrementalGenerator
{
	private const string ScriptPropertyAttributeFullName = "Polytoria.Attributes.ScriptPropertyAttribute";
	private const string ScriptMethodAttributeFullName = "Polytoria.Attributes.ScriptMethodAttribute";

	private static readonly DiagnosticDescriptor _unsupportedMemberRule = new(
#pragma warning disable RS2008 // Enable analyzer release tracking
		id: "PTG0002",
#pragma warning restore RS2008 // Enable analyzer release tracking
		title: "Unsupported scripting member",
		messageFormat: "'{0}' cannot be exposed to scripting by ScriptInterfaceGenerator ({1})",
		category: "Polytoria.SourceGen",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var properties = context.SyntaxProvider
			.ForAttributeWithMetadataName(
				ScriptPropertyAttributeFullName,
				predicate: static (node, _) => node is PropertyDeclarationSyntax,
				transform: static (ctx, _) => GetPropertyEntry((IPropertySymbol)ctx.TargetSymbol))
			.Collect();

		var methods = context.SyntaxProvider
			.ForAttributeWithMetadataName(
				ScriptMethodAttributeFullName,
				predicate: static (node, _) => node is MethodDeclarationSyntax,
				transform: static (ctx, _) => GetMethodEntry((IMethodSymbol)ctx.TargetSymbol))
			.Collect();

		context.RegisterSourceOutput(properties.Combine(methods), static (spc, data) =>
		{
			var (propertyEntries, methodEntries) = data;

			foreach (var entry in propertyEntries)
			{
				if (entry.Diagnostic is { } d) spc.ReportDiagnostic(d);
			}
			foreach (var entry in methodEntries)
			{
				if (entry.Diagnostic is { } d) spc.ReportDiagnostic(d);
			}
			spc.AddSource("ScriptInterfaceInvokers.g.cs", SourceText.From(GenerateSource(
				[.. propertyEntries.Where(static e => e.Diagnostic is null)],
				[.. methodEntries.Where(static e => e.Diagnostic is null)]), Encoding.UTF8));
		});
	}

	private static PropertyEntry GetPropertyEntry(IPropertySymbol prop)
	{
		Location loc = prop.Locations.FirstOrDefault() ?? Location.None;

		if (prop.Parameters.Length > 0)
			return new PropertyEntry(Diagnostic.Create(_unsupportedMemberRule, loc, prop.Name, "indexers are not supported"));

		if (prop.GetMethod is null)
			return new PropertyEntry(Diagnostic.Create(_unsupportedMemberRule, loc, prop.Name, "property has no accessible getter"));

		if (prop.DeclaredAccessibility != Accessibility.Public)
			return new PropertyEntry(Diagnostic.Create(_unsupportedMemberRule, loc, prop.Name, "script members must be public"));

		string containingType = prop.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

		if (containingType.Contains("<"))
			return new PropertyEntry(Diagnostic.Create(_unsupportedMemberRule, loc, prop.Name, "generic containing types are not supported"));
		string propertyType = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		bool hasPublicSetter = prop.SetMethod is { DeclaredAccessibility: Accessibility.Public };

		return new PropertyEntry(containingType, propertyType, prop.Name, prop.IsStatic, prop.ContainingType.IsValueType, hasPublicSetter);
	}

	private static MethodEntry GetMethodEntry(IMethodSymbol method)
	{
		Location loc = method.Locations.FirstOrDefault() ?? Location.None;

		if (method.IsGenericMethod)
			return new MethodEntry(Diagnostic.Create(_unsupportedMemberRule, loc, method.Name, "generic methods are not supported"));

		foreach (IParameterSymbol p in method.Parameters)
		{
			if (p.RefKind != RefKind.None)
				return new MethodEntry(Diagnostic.Create(_unsupportedMemberRule, loc, method.Name, "ref/out/in parameters are not supported"));

			if (p.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer)
				return new MethodEntry(Diagnostic.Create(_unsupportedMemberRule, loc, method.Name, "pointer parameters are not supported"));
		}

		if (method.DeclaredAccessibility != Accessibility.Public)
			return new MethodEntry(Diagnostic.Create(_unsupportedMemberRule, loc, method.Name, "script members must be public"));

		string containingType = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

		if (containingType.Contains("<"))
			return new MethodEntry(Diagnostic.Create(_unsupportedMemberRule, loc, method.Name, "generic containing types are not supported"));

		ImmutableArray<string> parameterTypes = [.. method.Parameters.Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))];

		return new MethodEntry(containingType, method.Name, method.IsStatic, method.ContainingType.IsValueType, method.ReturnsVoid, parameterTypes);
	}

	private static string GenerateSource(ImmutableArray<PropertyEntry> properties, ImmutableArray<MethodEntry> methods)
	{
		var sb = new StringBuilder();
		using var stringWriter = new StringWriter(sb);
		using var writer = new IndentedTextWriter(stringWriter);

		writer.WriteLine("// <auto-generated/>");
		writer.WriteLine("#nullable enable");
		writer.WriteLine("using System;");
		writer.WriteLine();
		writer.WriteLine("namespace Polytoria.Scripting.Luau;");
		writer.WriteLine();
		writer.WriteLine("internal static partial class ScriptInterfaceInvokers");
		writer.WriteLine("{");
		writer.Indent++;
		writer.WriteLine("internal static void RegisterGenerated()");
		writer.WriteLine("{");
		writer.Indent++;

		foreach (PropertyEntry p in properties
			.OrderBy(static e => e.ContainingTypeFullName, StringComparer.Ordinal)
			.ThenBy(static e => e.PropertyName, StringComparer.Ordinal))
		{
			string getter = p.IsStatic
				? $"static _ => {p.ContainingTypeFullName}.{p.PropertyName}"
				: $"static t => (({p.ContainingTypeFullName})t).{p.PropertyName}";

			string setter = "null";
			if (p.HasPublicSetter)
			{
				setter = p.IsStatic
					? $"static (_, v) => {p.ContainingTypeFullName}.{p.PropertyName} = ({p.PropertyTypeFullName})v!"
					: p.IsValueType
						? $"static (t, v) => System.Runtime.CompilerServices.Unsafe.Unbox<{p.ContainingTypeFullName}>(t).{p.PropertyName} = ({p.PropertyTypeFullName})v!"
						: $"static (t, v) => (({p.ContainingTypeFullName})t).{p.PropertyName} = ({p.PropertyTypeFullName})v!";
			}

			writer.WriteLine($"RegisterProperty(typeof({p.ContainingTypeFullName}).GetProperty(\"{p.PropertyName}\", MemberFlags)!, {getter}, {setter});");
		}

		writer.WriteLine();

		foreach (MethodEntry m in methods
			.OrderBy(static e => e.ContainingTypeFullName, StringComparer.Ordinal)
			.ThenBy(static e => e.MethodName, StringComparer.Ordinal))
		{
			string typesArray = m.ParameterTypeFullNames.Length == 0
				? "Type.EmptyTypes"
				: "new Type[] { " + string.Join(", ", m.ParameterTypeFullNames.Select(static t => $"typeof({t})")) + " }";

			string args = string.Join(", ", m.ParameterTypeFullNames.Select(static (t, i) => $"({t})args[{i}]!"));

			string call = m.IsStatic
				? $"{m.ContainingTypeFullName}.{m.MethodName}({args})"
				: m.IsValueType
					? $"System.Runtime.CompilerServices.Unsafe.Unbox<{m.ContainingTypeFullName}>(t!).{m.MethodName}({args})"
					: $"(({m.ContainingTypeFullName})t!).{m.MethodName}({args})";

			string body = m.ReturnsVoid ? $"{{ {call}; return null; }}" : call;

			writer.WriteLine($"RegisterMethod(typeof({m.ContainingTypeFullName}).GetMethod(\"{m.MethodName}\", 0, MemberFlags, null, {typesArray}, null)!, static (t, args) => {body});");
		}

		writer.Indent--;
		writer.WriteLine("}");
		writer.Indent--;
		writer.WriteLine("}");

		return sb.ToString();
	}

	private readonly struct PropertyEntry
	{
		public readonly string ContainingTypeFullName;
		public readonly string PropertyTypeFullName;
		public readonly string PropertyName;
		public readonly bool IsStatic;
		public readonly bool IsValueType;
		public readonly bool HasPublicSetter;
		public readonly Diagnostic? Diagnostic;

		public PropertyEntry(string containingTypeFullName, string propertyTypeFullName, string propertyName, bool isStatic, bool isValueType, bool hasPublicSetter)
		{
			ContainingTypeFullName = containingTypeFullName;
			PropertyTypeFullName = propertyTypeFullName;
			PropertyName = propertyName;
			IsStatic = isStatic;
			IsValueType = isValueType;
			HasPublicSetter = hasPublicSetter;
			Diagnostic = null;
		}

		public PropertyEntry(Diagnostic diagnostic)
		{
			ContainingTypeFullName = "";
			PropertyTypeFullName = "";
			PropertyName = "";
			IsStatic = false;
			IsValueType = false;
			HasPublicSetter = false;
			Diagnostic = diagnostic;
		}
	}

	private readonly struct MethodEntry
	{
		public readonly string ContainingTypeFullName;
		public readonly string MethodName;
		public readonly bool IsStatic;
		public readonly bool IsValueType;
		public readonly bool ReturnsVoid;
		public readonly ImmutableArray<string> ParameterTypeFullNames;
		public readonly Diagnostic? Diagnostic;

		public MethodEntry(string containingTypeFullName, string methodName, bool isStatic, bool isValueType, bool returnsVoid, ImmutableArray<string> parameterTypeFullNames)
		{
			ContainingTypeFullName = containingTypeFullName;
			MethodName = methodName;
			IsStatic = isStatic;
			IsValueType = isValueType;
			ReturnsVoid = returnsVoid;
			ParameterTypeFullNames = parameterTypeFullNames;
			Diagnostic = null;
		}

		public MethodEntry(Diagnostic diagnostic)
		{
			ContainingTypeFullName = "";
			MethodName = "";
			IsStatic = false;
			IsValueType = false;
			ReturnsVoid = false;
			ParameterTypeFullNames = [];
			Diagnostic = diagnostic;
		}
	}
}
