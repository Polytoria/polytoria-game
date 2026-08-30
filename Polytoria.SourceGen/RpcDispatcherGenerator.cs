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
public sealed class RpcDispatcherGenerator : IIncrementalGenerator
{
	private const string NetRpcAttributeFullName = "Polytoria.Attributes.NetRpcAttribute";
	private const string NetworkedObjectFullName = "Polytoria.Datamodel.NetworkedObject";
	private const string RegistryTypeFullName = "global::Polytoria.Networking.RpcDispatchRegistry";
	private const string AuthorityModeEnumRef = "global::Polytoria.Networking.AuthorityMode";
	private const string TransferModeEnumRef = "global::Polytoria.Networking.TransferMode";
	private const string FallbackDeserializeFullName = "global::Polytoria.Networking.Synchronizers.NetworkPropSync.DeserializePropValue";
	private const string RpcArgDeserializeFullName = "global::Polytoria.Networking.Synchronizers.NetworkPropSync.DeserializeRpcArg";
	private const string HelperTypeName = "RpcGenerated";

	private static readonly DiagnosticDescriptor _duplicateRpcNameRule = new(
#pragma warning disable RS2008 // Enable analyzer release tracking
		id: "PTG0004",
#pragma warning restore RS2008 // Enable analyzer release tracking
		title: "Conflicting RPC method name",
		messageFormat: "RPC method name '{0}' is already used on '{1}', NetRpc methods cannot be overloaded",
		category: "Polytoria.SourceGen",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	private static readonly DiagnosticDescriptor _unsupportedRpcRule = new(
#pragma warning disable RS2008 // Enable analyzer release tracking
		id: "PTG0005",
#pragma warning restore RS2008 // Enable analyzer release tracking
		title: "Unsupported RPC method",
		messageFormat: "'{0}' cannot be handled by RpcDispatcherGenerator ({1})",
		category: "Polytoria.SourceGen",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var rpcMethods = context.SyntaxProvider
			.ForAttributeWithMetadataName(
				NetRpcAttributeFullName,
				predicate: static (node, _) => node is MethodDeclarationSyntax,
				transform: static (ctx, _) => GetRpcEntry((IMethodSymbol)ctx.TargetSymbol, ctx.Attributes[0]))
			.Collect();

		context.RegisterSourceOutput(rpcMethods, static (spc, entries) =>
		{
			ImmutableArray<RpcEntry> valid = ReportDiagnostics(spc, entries);

			spc.AddSource("RpcGenerated.g.cs", SourceText.From(GenerateTypeSource(valid), Encoding.UTF8));
			spc.AddSource("RpcDispatchRegistry.g.cs", SourceText.From(GenerateRegistrySource(valid), Encoding.UTF8));
		});
	}

	private static ImmutableArray<RpcEntry> ReportDiagnostics(SourceProductionContext spc, ImmutableArray<RpcEntry> entries)
	{
		foreach (RpcEntry entry in entries)
		{
			if (entry.Diagnostic is { } d) spc.ReportDiagnostic(d);
		}

		var seen = new HashSet<string>(StringComparer.Ordinal);
		var valid = ImmutableArray.CreateBuilder<RpcEntry>();

		foreach (RpcEntry entry in entries
			.Where(static e => e.Diagnostic is null)
			.OrderBy(static e => e.DeclaringTypeFullName, StringComparer.Ordinal)
			.ThenBy(static e => e.MethodName, StringComparer.Ordinal))
		{
			if (!seen.Add(entry.DeclaringTypeFullName + "|" + entry.MethodName))
			{
				spc.ReportDiagnostic(Diagnostic.Create(_duplicateRpcNameRule, entry.Location, entry.MethodName, entry.DeclaringTypeFullName));
				continue;
			}

			valid.Add(entry);
		}

		return valid.ToImmutable();
	}

	private static RpcEntry GetRpcEntry(IMethodSymbol method, AttributeData attribute)
	{
		Location location = method.Locations.FirstOrDefault() ?? Location.None;

		if (method.IsStatic)
			return new RpcEntry(location, Diagnostic.Create(_unsupportedRpcRule, location, method.Name, "static methods are not supported"));

		if (method.IsGenericMethod)
			return new RpcEntry(location, Diagnostic.Create(_unsupportedRpcRule, location, method.Name, "generic methods are not supported"));

		INamedTypeSymbol containingType = method.ContainingType;

		if (containingType.IsGenericType)
			return new RpcEntry(location, Diagnostic.Create(_unsupportedRpcRule, location, method.Name, "generic containing types are not supported"));

		if (!DerivesFromNetworkedObject(containingType))
			return new RpcEntry(location, Diagnostic.Create(_unsupportedRpcRule, location, method.Name, "containing type must derive from NetworkedObject"));

		if (containingType.GetMembers(HelperTypeName).Length > 0)
			return new RpcEntry(location, Diagnostic.Create(_unsupportedRpcRule, location, method.Name, $"containing type already declares a '{HelperTypeName}' member"));

		foreach (IParameterSymbol parameter in method.Parameters)
		{
			if (parameter.RefKind != RefKind.None)
				return new RpcEntry(location, Diagnostic.Create(_unsupportedRpcRule, location, method.Name, "ref/out/in parameters are not supported"));
		}

		string authorMode = AuthorityModeEnumRef + ".Any";
		if (attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Type is { } authorType)
			authorMode = AuthorityModeEnumRef + "." + GetEnumMemberName(authorType, attribute.ConstructorArguments[0]);

		string transferMode = TransferModeEnumRef + ".Reliable";
		int transferChannel = 0;
		bool callLocal = false;
		bool allowToServerOnly = true;

		foreach (var namedArg in attribute.NamedArguments)
		{
			switch (namedArg.Key)
			{
				case "TransferMode":
					if (namedArg.Value.Type is not null)
						transferMode = TransferModeEnumRef + "." + GetEnumMemberName(namedArg.Value.Type, namedArg.Value);
					break;
				case "TransferChannel":
					if (namedArg.Value.Value is int channel) transferChannel = channel;
					break;
				case "CallLocal":
					if (namedArg.Value.Value is bool call) callLocal = call;
					break;
				case "AllowToServerOnly":
					if (namedArg.Value.Value is bool allow) allowToServerOnly = allow;
					break;
			}
		}

		return new RpcEntry(
			containingType, method.Name,
			[.. method.Parameters.Select(static p => StripNullableAnnotation(p.Type))],
			[.. method.Parameters.Select(static p => GetArgKind(p.Type))],
			authorMode, transferMode, transferChannel, callLocal, allowToServerOnly, location);
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

	private static string GetEnumMemberName(ITypeSymbol enumType, TypedConstant constant)
	{
		foreach (IFieldSymbol field in enumType.GetMembers().OfType<IFieldSymbol>())
		{
			if (field.IsConst && Equals(field.ConstantValue, constant.Value))
				return field.Name;
		}

		return constant.Value?.ToString() ?? "0";
	}

	private static string StripNullableAnnotation(ITypeSymbol type)
	{
		if (type is INamedTypeSymbol { IsValueType: false, NullableAnnotation: NullableAnnotation.Annotated })
			type = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

		return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
	}

	private static ArgKind GetArgKind(ITypeSymbol type)
	{
		if (type.TypeKind == TypeKind.Enum || type.SpecialType == SpecialType.System_Object)
			return ArgKind.Fallback;

		if (type.IsValueType)
		{
			if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
				return GetArgKind(((INamedTypeSymbol)type).TypeArguments[0]) == ArgKind.Typed ? ArgKind.Typed : ArgKind.Fallback;

			return IsPrimitiveSpecial(type.SpecialType) ? ArgKind.Typed : ArgKind.Fallback;
		}

		if (type.SpecialType == SpecialType.System_String)
			return ArgKind.TypedNullable;

		if (type is IArrayTypeSymbol array && IsTypedSafeElement(array.ElementType))
			return ArgKind.TypedNullable;

		return ArgKind.Fallback;
	}

	private static bool IsPrimitiveSpecial(SpecialType specialType) => specialType switch
	{
		SpecialType.System_Boolean or
		SpecialType.System_Char or
		SpecialType.System_SByte or SpecialType.System_Byte or
		SpecialType.System_Int16 or SpecialType.System_UInt16 or
		SpecialType.System_Int32 or SpecialType.System_UInt32 or
		SpecialType.System_Int64 or SpecialType.System_UInt64 or
		SpecialType.System_Single or SpecialType.System_Double or
		SpecialType.System_Decimal or
		SpecialType.System_IntPtr or SpecialType.System_UIntPtr => true,
		_ => false
	};

	private static bool IsTypedSafeElement(ITypeSymbol element)
	{
		if (IsPrimitiveSpecial(element.SpecialType) || element.SpecialType == SpecialType.System_String)
			return true;

		return element.IsValueType && HasMemoryPackableAttribute(element);
	}

	private static bool HasMemoryPackableAttribute(ITypeSymbol type) =>
		type.GetAttributes().Any(static a => a.AttributeClass?.Name == "MemoryPackableAttribute");

	private static string GetTypeModifiers(INamedTypeSymbol type)
	{
		string accessibility = type.DeclaredAccessibility switch
		{
			Accessibility.Public => "public",
			Accessibility.Internal => "internal",
			Accessibility.Protected => "protected",
			Accessibility.ProtectedOrInternal => "protected internal",
			Accessibility.ProtectedAndInternal => "private protected",
			_ => "internal"
		};

		if (type.IsStatic) return $"{accessibility} static";
		if (type.IsSealed) return $"{accessibility} sealed";
		if (type.IsAbstract) return $"{accessibility} abstract";

		return accessibility;
	}

	private static string GetWireArgExpression(string typeRef, ArgKind kind, int index)
	{
		string arg = $"a[{index}]";

		return kind switch
		{
			ArgKind.Typed => $"{RpcArgDeserializeFullName}<{typeRef}>({arg})",
			ArgKind.TypedNullable => $"({arg}.Length == 0 ? null : {RpcArgDeserializeFullName}<{typeRef}>({arg}))!",
			_ => $"({typeRef}){FallbackDeserializeFullName}({arg}, typeof({typeRef}))!"
		};
	}

	private static string GenerateTypeSource(ImmutableArray<RpcEntry> entries)
	{
		var sb = new StringBuilder();
		using var stringWriter = new StringWriter(sb);
		using var writer = new IndentedTextWriter(stringWriter);

		writer.WriteLine("// <auto-generated/>");
		writer.WriteLine("#nullable enable");

		foreach (var namespaceGroup in entries
			.GroupBy(static e => e.NamespaceName, StringComparer.Ordinal)
			.OrderBy(static g => g.Key, StringComparer.Ordinal))
		{
			int savedIndent = writer.Indent;
			writer.Indent = 0;
			writer.WriteLine();
			writer.Indent = savedIndent;
			writer.WriteLine($"namespace {namespaceGroup.Key}");
			writer.WriteLine("{");
			writer.Indent++;

			foreach (var typeGroup in namespaceGroup
				.GroupBy(static e => e.DeclaringTypeFullName, StringComparer.Ordinal)
				.OrderBy(static g => g.Key, StringComparer.Ordinal))
			{
				RpcEntry head = typeGroup.First();

				savedIndent = writer.Indent;
				writer.Indent = 0;
				writer.WriteLine();
				writer.Indent = savedIndent;
				writer.WriteLine($"{head.TypeModifiers} partial class {head.TypeName}");
				writer.WriteLine("{");
				writer.Indent++;
				writer.WriteLine($"internal static class {HelperTypeName}");
				writer.WriteLine("{");
				writer.Indent++;
				writer.WriteLine("internal static void Register()");
				writer.WriteLine("{");
				writer.Indent++;
				writer.WriteLine($"{RegistryTypeFullName}.RegisterDeclared(typeof({head.DeclaringTypeFullName}),");
				writer.WriteLine("[");
				writer.Indent++;

				foreach (RpcEntry entry in typeGroup.OrderBy(static e => e.MethodName, StringComparer.Ordinal))
					WriteRpcEntry(writer, entry);

				writer.Indent--;
				writer.WriteLine("]);");
				writer.Indent--;
				writer.WriteLine("}");
				writer.Indent--;
				writer.WriteLine("}");
				writer.Indent--;
				writer.WriteLine("}");
			}

			writer.Indent--;
			writer.WriteLine("}");
		}

		return sb.ToString();
	}

	private static void WriteRpcEntry(IndentedTextWriter writer, RpcEntry entry)
	{
		string localArgs = string.Join(", ", entry.ParameterTypeFullNames.Select((t, i) => $"(({t})a![{i}]!)"));
		string wireArgs = string.Join(", ", entry.ParameterTypeFullNames.Select((t, i) => GetWireArgExpression(t, entry.ParameterKinds[i], i)));

		writer.WriteLine("new()");
		writer.WriteLine("{");
		writer.Indent++;
		writer.WriteLine($"Name = \"{entry.MethodName}\",");
		writer.WriteLine($"AuthorMode = {entry.AuthorMode},");
		writer.WriteLine($"TransferMode = {entry.TransferMode},");
		writer.WriteLine($"TransferChannel = {entry.TransferChannel},");
		writer.WriteLine($"CallLocal = {entry.CallLocal.ToString().ToLowerInvariant()},");
		writer.WriteLine($"AllowToServerOnly = {entry.AllowToServerOnly.ToString().ToLowerInvariant()},");
		writer.WriteLine($"InvokeLocal = static (o, a) => (({entry.DeclaringTypeFullName})o).{entry.MethodName}({localArgs}),");
		writer.WriteLine($"InvokeWire = static (o, a) => (({entry.DeclaringTypeFullName})o).{entry.MethodName}({wireArgs}),");
		writer.Indent--;
		writer.WriteLine("},");
	}

	private static string GenerateRegistrySource(ImmutableArray<RpcEntry> entries)
	{
		var sb = new StringBuilder();
		using var stringWriter = new StringWriter(sb);
		using var writer = new IndentedTextWriter(stringWriter);

		writer.WriteLine("// <auto-generated/>");
		writer.WriteLine("#nullable enable");
		writer.WriteLine();
		writer.WriteLine("namespace Polytoria.Networking;");
		writer.WriteLine();
		writer.WriteLine("internal static partial class RpcDispatchRegistry");
		writer.WriteLine("{");
		writer.Indent++;
		writer.WriteLine("static partial void RegisterGenerated()");
		writer.WriteLine("{");
		writer.Indent++;

		foreach (string typeName in entries
			.Select(static e => e.DeclaringTypeFullName)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(static n => n, StringComparer.Ordinal))
		{
			writer.WriteLine($"{typeName}.{HelperTypeName}.Register();");
		}

		writer.Indent--;
		writer.WriteLine("}");
		writer.Indent--;
		writer.WriteLine("}");

		return sb.ToString();
	}

	private enum ArgKind
	{
		Typed,
		TypedNullable,
		Fallback
	}

	private readonly struct RpcEntry
	{
		public readonly string DeclaringTypeFullName;
		public readonly string NamespaceName;
		public readonly string TypeModifiers;
		public readonly string TypeName;
		public readonly string MethodName;
		public readonly ImmutableArray<string> ParameterTypeFullNames;
		public readonly ImmutableArray<ArgKind> ParameterKinds;
		public readonly string AuthorMode;
		public readonly string TransferMode;
		public readonly int TransferChannel;
		public readonly bool CallLocal;
		public readonly bool AllowToServerOnly;
		public readonly Location Location;
		public readonly Diagnostic? Diagnostic;

		public RpcEntry(INamedTypeSymbol containingType, string methodName, ImmutableArray<string> parameterTypeFullNames,
			ImmutableArray<ArgKind> parameterKinds, string authorMode, string transferMode, int transferChannel,
			bool callLocal, bool allowToServerOnly, Location location)
		{
			DeclaringTypeFullName = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			NamespaceName = containingType.ContainingNamespace.ToDisplayString();
			TypeModifiers = GetTypeModifiers(containingType);
			TypeName = containingType.Name;
			MethodName = methodName;
			ParameterTypeFullNames = parameterTypeFullNames;
			ParameterKinds = parameterKinds;
			AuthorMode = authorMode;
			TransferMode = transferMode;
			TransferChannel = transferChannel;
			CallLocal = callLocal;
			AllowToServerOnly = allowToServerOnly;
			Location = location;
			Diagnostic = null;
		}

		public RpcEntry(Location location, Diagnostic diagnostic)
		{
			DeclaringTypeFullName = "";
			NamespaceName = "";
			TypeModifiers = "";
			TypeName = "";
			MethodName = "";
			ParameterTypeFullNames = [];
			ParameterKinds = [];
			AuthorMode = "";
			TransferMode = "";
			TransferChannel = 0;
			CallLocal = false;
			AllowToServerOnly = true;
			Location = location;
			Diagnostic = diagnostic;
		}
	}
}
