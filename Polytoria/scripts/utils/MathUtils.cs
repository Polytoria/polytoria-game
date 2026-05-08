// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Utils;

/// <summary>
/// MathUtils, primarly used for flipping axis
/// As Polytoria uses left handed coordinates and Godot uses right hand. All Vector2, 3 and Quat must be flipped first before interacting with each other
/// </summary>
public static class MathUtils
{
	public static Vector3 FlipVector3(Vector3 vector3)
	{
		vector3.X = -vector3.X;
		return vector3;
	}

	public static Vector2 FlipVector2(Vector2 vector2)
	{
		vector2.Y = -vector2.Y;
		return vector2;
	}

	public static Quaternion FlipQuat(Quaternion quat)
	{
		return new(-quat.X, -quat.Y, quat.Z, quat.W);
	}

	public static Vector3 FlipEuler(Vector3 polyRot)
	{
		Vector3 godotEuler = new(
			polyRot.X,
			-polyRot.Y,
			-polyRot.Z
		);
		return godotEuler;
	}

	public static Vector3 Vector3RadToDeg(Vector3 v)
	{
		return new(
			Mathf.RadToDeg(v.X),
			Mathf.RadToDeg(v.Y),
			Mathf.RadToDeg(v.Z)
		);
	}

	public static Vector3 Vector3DegToRad(Vector3 v)
	{
		return new(
			Mathf.DegToRad(v.X),
			Mathf.DegToRad(v.Y),
			Mathf.DegToRad(v.Z)
		);
	}
}

public static class MathUtilsExtensions
{
	public static Vector2 Flip(this Vector2 v)
	{
		return MathUtils.FlipVector2(v);
	}

	public static Vector3 Flip(this Vector3 v)
	{
		return MathUtils.FlipVector3(v);
	}

	public static Vector3 FlipEuler(this Vector3 v)
	{
		return MathUtils.FlipEuler(v);
	}

	public static Vector3 DegToRad(this Vector3 v)
	{
		return MathUtils.Vector3DegToRad(v);
	}

	public static Vector3 RadToDeg(this Vector3 v)
	{
		return MathUtils.Vector3RadToDeg(v);
	}

	public static Quaternion Flip(this Quaternion q)
	{
		return MathUtils.FlipQuat(q);
	}

	public static Aabb Flip(this Aabb a)
	{
		return new Aabb(new Vector3(-a.End.X, a.Position.Y, a.Position.Z), a.Size);
	}
}
