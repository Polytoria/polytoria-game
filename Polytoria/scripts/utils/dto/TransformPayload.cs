// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using MemoryPack;
using System;
using System.Text.Json.Serialization;

namespace Polytoria.Utils.DTOs;

[MemoryPackable]
public partial class TransformPayloadDto
{
	public float[] Values { get; set; } = null!;

	public Vector3 Position
	{
		get => new Vector3(Values[0], Values[1], Values[2]);
		set
		{
			Values[0] = value.X; Values[1] = value.Y; Values[2] = value.Z;
		}
	}

	public uint RawRotation
	{
		get => BitConverter.ToUInt32(BitConverter.GetBytes(Values[3]), 0);
		set
		{
			Values[3] = BitConverter.ToSingle(BitConverter.GetBytes(value), 0);
		}
	}

	public Quaternion Rotation
	{
		get => UnitQuaternionDto.FromCompressed(RawRotation);
		set
		{
			RawRotation = UnitQuaternionDto.ToCompressed(value);
		}
	}

	[MemoryPackConstructor, JsonConstructor]
	public TransformPayloadDto() { }
	public TransformPayloadDto(float[] values)
	{
		Values = values;
	}
	public TransformPayloadDto(Vector3 position, Quaternion rotation)
	{
		Values = ToArray(position, rotation);
	}

	public bool IsEqualApprox(TransformPayloadDto other) => Position.IsEqualApprox(other.Position) && Rotation.IsEqualApprox(other.Rotation);

	// String helpers because memory pack don't like nested objects
	public static TransformPayloadDto FromString(string str)
	{
		var parts = str.Split('|');
		return new TransformPayloadDto(Vector3Dto.FromString(parts[0]), UnitQuaternionDto.FromString(parts[1]));
	}

	public static string ToString(Vector3 Position, Quaternion Rotation)
	{
		return $"{Vector3Dto.ToString(Position)}|{UnitQuaternionDto.ToString(Rotation)}";
	}

	public static float[] ToArray(Vector3 Position, uint Rotation) => [
		Position.X, Position.Y, Position.Z,
		BitConverter.ToSingle(BitConverter.GetBytes(Rotation), 0)
	];
	public static float[] ToArray(Vector3 Position, Quaternion Rotation) => ToArray(Position, UnitQuaternionDto.ToCompressed(Rotation));
	public static float[] ToArray(Transform3D t) => ToArray(t.Origin, t.Basis.GetRotationQuaternion());
	public static float[] ToArray(TransformPayloadDto t) => ToArray(t.Position, t.RawRotation);

	public static TransformPayloadDto FromArray(float[] f) => new(f);
	public static TransformPayloadDto FromGDTransform(Transform3D t) => new(t.Origin, t.Basis.GetRotationQuaternion());
}
