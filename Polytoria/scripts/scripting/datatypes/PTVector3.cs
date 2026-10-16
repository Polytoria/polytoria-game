// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;
using Polytoria.Utils;
using System;

namespace Polytoria.Scripting.Datatypes;

public class PTVector3 : IScriptGDObject
{
	internal Vector3 vector;

	[ScriptProperty] public float X { get => vector.X; set => vector.X = value; }
	[ScriptProperty] public float Y { get => vector.Y; set => vector.Y = value; }
	[ScriptProperty] public float Z { get => vector.Z; set => vector.Z = value; }

	// Constants
	[ScriptProperty] public static PTVector3 Forward { get; private set; } = new() { X = 0, Y = 0, Z = -1 };
	[ScriptProperty] public static PTVector3 Back { get; private set; } = new() { X = 0, Y = 0, Z = 1 };
	[ScriptProperty] public static PTVector3 Down { get; private set; } = new() { X = 0, Y = -1, Z = 0 };
	[ScriptProperty] public static PTVector3 Left { get; private set; } = new() { X = -1, Y = 0, Z = 0 };
	[ScriptProperty] public static PTVector3 One { get; private set; } = new() { X = 1, Y = 1, Z = 1 };
	[ScriptProperty] public static PTVector3 Zero { get; private set; } = new() { X = 0, Y = 0, Z = 0 };
	[ScriptProperty] public static PTVector3 Right { get; private set; } = new() { X = 1, Y = 0, Z = 0 };
	[ScriptProperty] public static PTVector3 Up { get; private set; } = new() { X = 0, Y = 1, Z = 0 };



	[ScriptProperty] public float Magnitude => vector.Length();
	[ScriptProperty] public PTVector3 Normalized => FromGDClass(vector.Normalized());
	[ScriptProperty] public float SqrMagnitude => vector.LengthSquared();

	public static PTVector3 FromGDClass(Vector3 vec)
	{
		return new PTVector3()
		{
			vector = vec
		};
	}

	public object ToGDClass()
	{
		return vector;
	}

	[ScriptMethod]
	public static PTVector3 New() // Returns a vector3 object with values (0, 0 ,0)
	{
		return new()
		{
			X = 0,
			Y = 0,
			Z = 0
		};
	}

	[ScriptMethod]
	public static PTVector3 New(float d) // Returns a vector3 object with values (d, d ,d)
	{
		return new()
		{
			X = d,
			Y = d,
			Z = d
		};
	}

	[ScriptMethod]
	public static PTVector3 New(float x, float y) // Returns a vector3 object with values (x, y ,0)
	{
		return new()
		{
			X = x,
			Y = y,
			Z = 0
		};
	}

	[ScriptMethod]
	public static PTVector3 New(float x, float y, float z) // Returns a vector3 object with values (x, y ,z)
	{
		return new()
		{
			X = x,
			Y = y,
			Z = z
		};
	}

	[ScriptMethod]
	public static PTVector3 New(PTVector2 v) // Returns a vector3 object with values (v.X, v.Y, 0)
	{
		return new()
		{
			X = v.X,
			Y = v.Y,
			Z = 0
		};
	}

	[ScriptMethod]
	public static PTVector3 New(PTVector2 v, float d) // Returns a vector3 object with values (v.X, v.Y, d)
	{
		return new()
		{
			X = v.X,
			Y = v.Y,
			Z = d
		};
	}

	[ScriptMethod]
	public static PTVector3 New(float d, PTVector2 v) // Returns a vector3 object with values (v.X, v.Y, d)
	{
		return new()
		{
			X = v.X,
			Y = v.Y,
			Z = d
		};
	}

	[ScriptMethod]
	public static PTVector3 New(bool a) // Returns a vector3 object based on boolean values (false = 0, true = 1)
	{
		// Conditional to convert boolean to a float
		float data = 0;
		if (a)
		{
			data = 1;
		}

		return new()
		{
			X = data,
			Y = data,
			Z = data
		};
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Add)]
	public static PTVector3 Add(PTVector3 a, PTVector3 b) // Adds two vectors together and returns
	{
		return FromGDClass(a.vector + b.vector);
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Sub)]
	public static PTVector3 SubVectorVector(PTVector3 a, PTVector3 b) // Subtracts two vectors and returns
	=> FromGDClass(a.vector - b.vector);

	[ScriptMetamethod(ScriptObjectMetamethod.Sub)]
	public static PTVector3 SubVectorQuaternion(PTVector3 a, PTQuaternion q) // Subtracts two quaternions and returns it as as a vector
		=> FromGDClass(a.vector - new Vector3(q.X, q.Y, q.Z));

	[ScriptMetamethod(ScriptObjectMetamethod.Mul)]
	public static PTVector3 MulVectorVector(PTVector3 a, PTVector3 b) // Multiplies two vectors and returns
	=> FromGDClass(a.vector * b.vector);

	[ScriptMetamethod(ScriptObjectMetamethod.Mul)]
	public static PTVector3 MulVectorScalar(PTVector3 a, double scalar) // multiplies a vector by a double
		=> FromGDClass(a.vector * (float)scalar);

	[ScriptMetamethod(ScriptObjectMetamethod.Mul)]
	public static PTVector3 MulScalarVector(double scalar, PTVector3 b) // multiplies a vector by a double but flipped
		=> FromGDClass(b.vector * (float)scalar);

	[ScriptMetamethod(ScriptObjectMetamethod.Mul)]
	public static PTVector3 MulVectorQuaternion(PTVector3 a, PTQuaternion q) // multiplies a vector by a quaternion which gets normalized
		=> FromGDClass(a.vector * q.quat.Normalized());

	[ScriptMetamethod(ScriptObjectMetamethod.Div)]
	public static PTVector3 Div(PTVector3 a, double b) // divides a vector by a double
	{
		return FromGDClass(a.vector / (float)b);
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Mod)] 
	public static PTVector3 Mod(PTVector3 a, PTVector3 b) // returns the modulo between two vectors
	{
		return FromGDClass(new Vector3(
			a.vector.X % b.vector.X,
			a.vector.Y % b.vector.Y,
			a.vector.Z % b.vector.Z
		));
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Unm)]
	public static PTVector3 Unm(PTVector3 a) // returns a negated vector
	{
		return FromGDClass(-a.vector);
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Pow)]
	public static PTVector3 Pow(PTVector3 a, PTVector3 b) // returns a vector to the power of another vector
	{
		return FromGDClass(new Vector3(
			(float)Math.Pow(a.vector.X, b.vector.X),
			(float)Math.Pow(a.vector.Y, b.vector.Y),
			(float)Math.Pow(a.vector.Z, b.vector.Z)
		));
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Pow)]
	public static PTVector3 Pow(PTVector3 a, float d) // returns a vector to the power of a float
	{
		return FromGDClass(new Vector3(
			(float)Math.Pow(a.vector.X, d),
			(float)Math.Pow(a.vector.Y, d),
			(float)Math.Pow(a.vector.Z, d)
		));
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Eq)] // returns if two vectors are equal
	public static bool Eq(PTVector3 a, PTVector3 b)
	{
		return a.vector == b.vector;
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Lt)]
	public static bool Lt(PTVector3 a, PTVector3 b) // returns if vector a is less than vector b
	{
		return a.vector.LengthSquared() < b.vector.LengthSquared();
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Le)]
	public static bool Le(PTVector3 a, PTVector3 b) // returns if vector a is less or equal to vector b
	{
		return a.vector.LengthSquared() <= b.vector.LengthSquared();
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Len)]
	public static double Len(PTVector3 a) // returns the length of a vector
	{
		return a.vector.Length();
	}

	[ScriptMetamethod(ScriptObjectMetamethod.ToString)]
	public static string ToString(PTVector3? v) // Converts a vector3 to a string to be printed out
	{
		if (v == null) return "<Vector3>";
		return $"<Vector3:({v.vector.X}, {v.vector.Y}, {v.vector.Z})>";
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static float Angle(PTVector3 from, PTVector3 to) => from.vector.AngleTo(to.vector);
	//[ScriptMethod] public static Vector3 ClampMagnitude(Vector3 vector, float maxLength) => vector.Clamp(vector, maxLength);
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static PTVector3 Cross(PTVector3 lhs, PTVector3 rhs) => FromGDClass(lhs.vector.Cross(rhs.vector));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static float Distance(PTVector3 a, PTVector3 b) => a.vector.DistanceTo(b.vector);
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static float Dot(PTVector3 lhs, PTVector3 rhs) => lhs.vector.Dot(rhs.vector);
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static PTVector3 Lerp(PTVector3 a, PTVector3 b, float t) => FromGDClass(a.vector.Lerp(b.vector, t));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static PTVector3 Max(PTVector3 lhs, PTVector3 rhs) => FromGDClass(lhs.vector.Max(rhs.vector));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static PTVector3 Min(PTVector3 lhs, PTVector3 rhs) => FromGDClass(lhs.vector.Min(rhs.vector));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static PTVector3 MoveTowards(PTVector3 current, PTVector3 target, float maxDistanceDelta) => FromGDClass(current.vector.MoveToward(target.vector, maxDistanceDelta));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static PTVector3 Normalize(PTVector3 value) => FromGDClass(value.vector.Normalized());
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static PTVector3 Project(PTVector3 vector, PTVector3 onNormal) => FromGDClass(vector.vector.Project(onNormal.vector));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static PTVector3 ProjectOnPlane(PTVector3 vector, PTVector3 planeNormal) => FromGDClass(vector.vector.Slide(planeNormal.vector.Normalized()));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static PTVector3 Reflect(PTVector3 inDirection, PTVector3 inNormal) => FromGDClass(inDirection.vector.Reflect(inNormal.vector));
	//[ScriptMethod] public static Vector3 RotateTowards(Vector3 current, Vector3 target, float maxRadiansDelta, float maxMagnitudeDelta) => current.RotateTowards(current, target, maxRadiansDelta, maxMagnitudeDelta);
	//public static Vector3 Scale(Vector3 a, Vector3 b) => a.Scale(b);
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static float SignedAngle(PTVector3 from, PTVector3 to, PTVector3 axis) => from.vector.SignedAngleTo(to.vector, axis.vector);

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static PTVector3 Slerp(PTVector3 a, PTVector3 b, float t)
	{
		Vector3 normalizedA = a.vector.Normalized();
		Vector3 normalizedB = b.vector.Normalized();
		return FromGDClass(normalizedA.Slerp(normalizedB, t));
	}
	//public static Vector3 SlerpUnclamped(Vector3 a, Vector3 b, float t) => a.SlerpUnclamped(b, t);
	//public static Vector3 SmoothDamp(Vector3 current, Vector3 target, ref Vector3 currentVelocity, float smoothTime, float maxSpeed, float deltaTime) => current.SmoothDamp(target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static PTVector3 Floor(PTVector3 val) => FromGDClass(val.vector.Floor());
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static PTVector3 Ceil(PTVector3 val) => FromGDClass(val.vector.Ceil());
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static PTVector3 Round(PTVector3 val) => FromGDClass(val.vector.Round());
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static PTVector3 Abs(PTVector3 val) => FromGDClass(val.vector.Abs());
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static PTVector3 Sign(PTVector3 val) => FromGDClass(val.vector.Sign());
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static PTVector3 Rotated(PTVector3 val, PTVector3 axis, float angle) => FromGDClass(val.vector.Rotated(axis.vector, angle));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static PTVector3 LimitLength(PTVector3 val, float length) => FromGDClass(val.vector.LimitLength(length));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static PTVector3 Clamp(PTVector3 val, PTVector3 min, PTVector3 max) => FromGDClass(val.vector.Clamp(min.vector, max.vector));
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static PTVector3 RadToDeg(PTVector3 val) => FromGDClass(val.vector.RadToDeg());
	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)] public static PTVector3 DegToRad(PTVector3 val) => FromGDClass(val.vector.DegToRad());
}
