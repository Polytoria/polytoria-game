// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Globalization;
using System.Linq;
using System.IO;

namespace Polytoria.Utils;

public static class Node3DExtension
{
	public static Aabb CalculateBounds(this Node3D parent, bool excludeTopLevelTransform = true)
	{
		Aabb bounds = parent is VisualInstance3D vi ? vi.GetAabb() : new();

		foreach (Node child in parent.GetChildren())
		{
			if (child is not Node3D child3D)
			{
				continue;
			}

			Aabb childBounds = child3D.CalculateBounds(false);
			bounds = bounds.Size == Vector3.Zero ? childBounds : bounds.Merge(childBounds);
		}

		if (bounds.Size == Vector3.Zero)
		{
			bounds = new(new(-0.2f, -0.2f, -0.2f), new(0.4f, 0.4f, 0.4f));
		}

		if (!excludeTopLevelTransform)
		{
			bounds = parent.Transform * bounds;
		}

		return bounds;
	}
}

public static class Vector3Extension
{
	public static Vector3 Pow(this Vector3 vector, float exponent)
	{
		return new(Mathf.Pow(vector.X, exponent), Mathf.Pow(vector.Y, exponent), Mathf.Pow(vector.Z, exponent));
	}

	public static Vector3 Snap(this Vector3 value, float step)
	{
		return new Vector3(
			Mathf.Round(value.X / step) * step,
			Mathf.Round(value.Y / step) * step,
			Mathf.Round(value.Z / step) * step
		);
	}

	public static Vector3 SanitizeNaN(this Vector3 v, float fallback = 0f)
	{
		return new(
			float.IsNaN(v.X) ? fallback : v.X,
			float.IsNaN(v.Y) ? fallback : v.Y,
			float.IsNaN(v.Z) ? fallback : v.Z
		);
	}
}

public static class Transform3DExtension
{
	public static Vector3 Xform(this Transform3D xform, Vector3 vector)
	{
		return xform.Basis.Xform(vector - xform.Origin);
	}

	public static Vector3 XformInv(this Transform3D xform, Vector3 vector)
	{
		return xform.Basis.XformInv(vector - xform.Origin);
	}

	public static Transform3D LerpPR(this Transform3D from, Transform3D to, float t)
	{
		Vector3 scale = to.Basis.Scale;
		Vector3 position = from.Origin.Lerp(to.Origin, t);

		Quaternion fromRot = new Quaternion(from.Basis).Normalized();
		Quaternion toRot = new Quaternion(to.Basis).Normalized();

		// Ensure shortest path
		if (fromRot.Dot(toRot) < 0.0f)
			toRot = -toRot;

		Quaternion rot = fromRot.Slerp(toRot, t);
		Basis basis = new(rot);
		basis = basis.Scaled(scale);
		return new Transform3D(basis, position);
	}
}

public static class BasisExtension
{
	public static Vector3 GetColumn(this Basis basis, int index)
	{
		return index switch
		{
			0 => basis.Column0,
			1 => basis.Column1,
			2 => basis.Column2,
			_ => throw new IndexOutOfRangeException(),
		};
	}

	public static Vector3 Xform(this Basis basis, Vector3 vector)
	{
		return new(
		basis.Row0.Dot(vector),
		basis.Row1.Dot(vector),
		basis.Row2.Dot(vector)
	);
	}

	public static Vector3 XformInv(this Basis basis, Vector3 vector)
	{
		return new(
		(basis.Row0[0] * vector.X) + (basis.Row1[0] * vector.Y) + (basis.Row2[0] * vector.Z),
		(basis.Row0[1] * vector.X) + (basis.Row1[1] * vector.Y) + (basis.Row2[1] * vector.Z),
		(basis.Row0[2] * vector.X) + (basis.Row1[2] * vector.Y) + (basis.Row2[2] * vector.Z)
	);
	}
}

public static class AabbExtension
{
	public static void GetEdge(this Aabb aabb, int edge, out Vector3 from, out Vector3 to)
	{
		switch (edge)
		{
			case 0:
				from = new(aabb.Position.X + aabb.Size.X, aabb.Position.Y, aabb.Position.Z);
				to = new(aabb.Position.X, aabb.Position.Y, aabb.Position.Z);
				break;
			case 1:
				from = new(aabb.Position.X + aabb.Size.X, aabb.Position.Y, aabb.Position.Z + aabb.Size.Z);
				to = new(aabb.Position.X + aabb.Size.X, aabb.Position.Y, aabb.Position.Z);
				break;
			case 2:
				from = new(aabb.Position.X, aabb.Position.Y, aabb.Position.Z + aabb.Size.Z);
				to = new(aabb.Position.X + aabb.Size.X, aabb.Position.Y, aabb.Position.Z + aabb.Size.Z);
				break;
			case 3:
				from = new(aabb.Position.X, aabb.Position.Y, aabb.Position.Z);
				to = new(aabb.Position.X, aabb.Position.Y, aabb.Position.Z + aabb.Size.Z);
				break;
			case 4:
				from = new(aabb.Position.X, aabb.Position.Y + aabb.Size.Y, aabb.Position.Z);
				to = new(aabb.Position.X + aabb.Size.X, aabb.Position.Y + aabb.Size.Y, aabb.Position.Z);
				break;
			case 5:
				from = new(aabb.Position.X + aabb.Size.X, aabb.Position.Y + aabb.Size.Y, aabb.Position.Z);
				to = new(aabb.Position.X + aabb.Size.X, aabb.Position.Y + aabb.Size.Y, aabb.Position.Z + aabb.Size.Z);
				break;
			case 6:
				from = new(aabb.Position.X + aabb.Size.X, aabb.Position.Y + aabb.Size.Y, aabb.Position.Z + aabb.Size.Z);
				to = new(aabb.Position.X, aabb.Position.Y + aabb.Size.Y, aabb.Position.Z + aabb.Size.Z);
				break;
			case 7:
				from = new(aabb.Position.X, aabb.Position.Y + aabb.Size.Y, aabb.Position.Z + aabb.Size.Z);
				to = new(aabb.Position.X, aabb.Position.Y + aabb.Size.Y, aabb.Position.Z);
				break;
			case 8:
				from = new(aabb.Position.X, aabb.Position.Y, aabb.Position.Z + aabb.Size.Z);
				to = new(aabb.Position.X, aabb.Position.Y + aabb.Size.Y, aabb.Position.Z + aabb.Size.Z);
				break;
			case 9:
				from = new(aabb.Position.X, aabb.Position.Y, aabb.Position.Z);
				to = new(aabb.Position.X, aabb.Position.Y + aabb.Size.Y, aabb.Position.Z);
				break;
			case 10:
				from = new(aabb.Position.X + aabb.Size.X, aabb.Position.Y, aabb.Position.Z);
				to = new(aabb.Position.X + aabb.Size.X, aabb.Position.Y + aabb.Size.Y, aabb.Position.Z);
				break;
			case 11:
				from = new(aabb.Position.X + aabb.Size.X, aabb.Position.Y, aabb.Position.Z + aabb.Size.Z);
				to = new(aabb.Position.X + aabb.Size.X, aabb.Position.Y + aabb.Size.Y, aabb.Position.Z + aabb.Size.Z);
				break;
			default:
				throw new IndexOutOfRangeException();
		}
	}
}

public static class NodeExtension
{
	public static bool IsDescendantOf(this Node node, Node potentialParent)
	{
		if (node == null || potentialParent == null)
			return false;

		Node current = node.GetParent();
		while (current != null)
		{
			if (current == potentialParent)
				return true;

			current = current.GetParent();
		}

		return false;
	}
}


public static class StringExtension
{
	private static readonly char[] InvalidFileNameChars =
			[.. Path.GetInvalidFileNameChars(), '\\', '/', ':', '*', '?', '"', '<', '>', '|'];

	public static string SanitizePath(this string s)
	{
		string ns = s.Replace('\\', '/');

		// Remove ./
		if (ns == "./" || ns == ".\\")
		{
			ns = "";
		}

		if (ns == ".")
		{
			ns = "";
		}
		return ns;
	}

	public static string SanitizeFileName(this string s)
	{
		return string.Join("_", s.Split(InvalidFileNameChars));
	}

	public static string TrimExtension(this string s)
	{
		return s.GetFile().TrimSuffix('.' + s.GetExtension());
	}

	public static string RemoveSymbols(this string s)
	{
		if (string.IsNullOrEmpty(s))
			return s;

		return new string([.. s.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))]);
	}

	public static string EnforceName(this string s)
	{
		if (string.IsNullOrEmpty(s))
			return s;

		return new string([.. s.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == '_' || c == '-')]);
	}
}

public static class DoubleExtension
{
	// Source - https://stackoverflow.com/a/48000498
	// Posted by Ramin Bateni Parvar, modified by community. See post 'Timeline' for change history
	// Retrieved 2026-02-16, License - CC BY-SA 4.0

	public static string ToKMB(this double num)
	{
		if (num > 999999999 || num < -999999999)
		{
			return num.ToString("0,,,.###B", CultureInfo.InvariantCulture);
		}
		else
		{
			if (num > 999999 || num < -999999)
			{
				return num.ToString("0,,.##M", CultureInfo.InvariantCulture);
			}
			else
			{
				if (num > 999 || num < -999)
				{
					return num.ToString("0,.#K", CultureInfo.InvariantCulture);
				}
				else
				{
					return num.ToString(CultureInfo.InvariantCulture);
				}
			}
		}
	}
}
