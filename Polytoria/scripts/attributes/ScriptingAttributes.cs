// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Scripting;
using System;

namespace Polytoria.Attributes;

/// <summary>
/// Mark the property as accessible by scripts
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ScriptPropertyAttribute : Attribute
{
	public ScriptPermissionFlags Permissions { get; set; } = ScriptPermissionFlags.None;
}

/// <summary>
/// Mark this property as accessible by legacy scripts
/// </summary>
/// <param name="methodName">Name to be overrided in script</param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ScriptLegacyPropertyAttribute(string propName) : Attribute
{
	public string PropertyName { get; set; } = propName;
}

/// <summary>
/// Interface for method based attributes
/// </summary>
public interface IScriptMethodAttribute
{
	string? MethodName { get; set; }
	/// <summary>
	/// Should the argument types be converted to Godot equivalent
	/// </summary>
	bool ConvertParamsToGD { get; set; }
}

/// <summary>
/// Mark this method as accessible by scripts
/// </summary>
/// <param name="methodName">Optional name to be overrided in script</param>
[AttributeUsage(AttributeTargets.Method)]
public class ScriptMethodAttribute(string? methodName = null) : Attribute, IScriptMethodAttribute
{
	public string? MethodName { get; set; } = methodName;
	/// <summary>
	/// Should the argument types be converted to Godot equivalent
	/// </summary>
	public bool ConvertParamsToGD { get; set; } = true;
	/// <summary>
	/// Should the parameter be get as a function
	/// </summary>
	public bool GetParamsAsFunction { get; set; } = false;
	public ScriptPermissionFlags Permissions { get; set; } = ScriptPermissionFlags.None;
	/// <summary>
	/// Should the static method also be name called as a regular method.
	/// This is achieved by implicitly passing self as the first argument.
	/// </summary>
	public bool SemiStatic { get; set; } = false;
}

[AttributeUsage(AttributeTargets.Parameter)]
public class ScriptingCallerAttribute : Attribute { }


/// <summary>
/// Mark this method as accessible by legacy scripts
/// </summary>
/// <param name="methodName">Name to be overrided in script</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ScriptLegacyMethodAttribute(string methodName) : Attribute, IScriptMethodAttribute
{
	public string? MethodName { get; set; } = methodName;
	/// <summary>
	/// Should the argument types be converted to Godot equivalent
	/// </summary>
	public bool ConvertParamsToGD { get; set; } = true;
}

/// <summary>
/// Mark this method as metamethod (operations such as + - tostring)
/// </summary>
/// <param name="metamethod">The target metamethod</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ScriptMetamethodAttribute(ScriptObjectMetamethod metamethod) : Attribute
{
	public ScriptObjectMetamethod Metamethod { get; } = metamethod;
	/// <summary>
	/// Should the argument types be converted to Godot equivalent
	/// </summary>
	public bool ConvertParamsToGD { get; set; } = false;
}
