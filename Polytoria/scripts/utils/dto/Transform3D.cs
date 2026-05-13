// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using MemoryPack;
using System.Text.Json.Serialization;

namespace Polytoria.Utils.DTOs;

[MemoryPackable]
public partial class Transform3DDto
{
	public float[] Value { get; set; } = null!;

	[MemoryPackConstructor, JsonConstructor]
	public Transform3DDto() { }

	public Transform3DDto(Transform3D transform)
	{
		Value = ToFloatArray(transform);
	}

	public Transform3D ToTransform3D()
	{
		if (Value == null || Value.Length != 12)
		{
			throw new System.InvalidOperationException($"Invalid Transform3DDto value length: {Value?.Length ?? 0}");
		}

		return FromFloatArray(Value);
	}

	public static Transform3DDto FromString(string str)
	{
		var parts = str.Split('|');
		Basis basis = new(
			Vector3Dto.FromString(parts[0]),
			Vector3Dto.FromString(parts[1]),
			Vector3Dto.FromString(parts[2])
		);

		return new Transform3DDto(new Transform3D(basis, Vector3Dto.FromString(parts[3])));
	}

	public static string ToString(Transform3D transform)
	{
		return $"{Vector3Dto.ToString(transform.Basis.X)}|{Vector3Dto.ToString(transform.Basis.Y)}|{Vector3Dto.ToString(transform.Basis.Z)}|{Vector3Dto.ToString(transform.Origin)}";
	}

	public static float[] ToFloatArray(Transform3D transform) => [
		transform.Basis.X.X, transform.Basis.X.Y, transform.Basis.X.Z,
		transform.Basis.Y.X, transform.Basis.Y.Y, transform.Basis.Y.Z,
		transform.Basis.Z.X, transform.Basis.Z.Y, transform.Basis.Z.Z,
		transform.Origin.X,  transform.Origin.Y,  transform.Origin.Z
	];

	public static Transform3D FromFloatArray(float[] f) => new(
		new Basis(
			new Vector3(f[0], f[1], f[2]),
			new Vector3(f[3], f[4], f[5]),
			new Vector3(f[6], f[7], f[8])
		),
		new Vector3(f[9], f[10], f[11])
	);
}
