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
	public byte[] Data { get; set; } = null!;

	public Vector3 Position
	{
		get => new Vector3(
			BitConverter.ToSingle(Data, 0),
			BitConverter.ToSingle(Data, 4),
			BitConverter.ToSingle(Data, 8)
		);
		set
		{
			byte[] newData = [
				..BitConverter.GetBytes(value.X),
				..BitConverter.GetBytes(value.Y),
				..BitConverter.GetBytes(value.Z)
			];
			Array.Copy(newData, Data, 12);
		}
	}

	public uint RawRotation
	{
		get => BitConverter.ToUInt32(Data, 12);
		set
		{
			byte[] rot = BitConverter.GetBytes(value);
			Array.Copy(rot, 0, Data, 12, 4);
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

	// UnitQuaternionDto is suitable for most nethwork replication, however may be problematic at larger scales.
	// UnitQuaternionDto has a ~0.137 degree step and uses 4 bytes,
	// UnitQuaternionUInt64Dto has a ~0.003 497 degree step and uses 8 bytes.
	// This is the the unimplemented TransformPayload logic for higher precision network replication of rotations.
	[MemoryPackIgnore]
	[JsonIgnore]
	public ulong RawRotationUInt64
	{
		get => BitConverter.ToUInt64(Data, 12);
		set
		{
			byte[] rot = BitConverter.GetBytes(value);
			Array.Copy(rot, 0, Data, 12, 8);
		}
	}

	[MemoryPackIgnore]
	[JsonIgnore]
	public Quaternion RotationUInt64
	{
		get => UnitQuaternionUInt64Dto.FromCompressed(RawRotationUInt64);
		set
		{
			RawRotationUInt64 = UnitQuaternionUInt64Dto.ToCompressed(value);
		}
	}

	[MemoryPackConstructor, JsonConstructor]
	public TransformPayloadDto() { }
	public TransformPayloadDto(byte[] bytes)
	{
		Data = bytes;
	}
	public TransformPayloadDto(Vector3 position, Quaternion rotation)
	{
		Data = ToArray(position, rotation);
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

	public static byte[] ToArray(Vector3 Position, uint Rotation) => [
		..BitConverter.GetBytes(Position.X),
		..BitConverter.GetBytes(Position.Y),
		..BitConverter.GetBytes(Position.Z),
		..BitConverter.GetBytes(Rotation)
	];
	public static byte[] ToArray(Vector3 Position, Quaternion Rotation) => ToArray(Position, UnitQuaternionDto.ToCompressed(Rotation));
	public static byte[] ToArray(Transform3D t) => ToArray(t.Origin, t.Basis.GetRotationQuaternion());
	public static byte[] ToArray(TransformPayloadDto t) => ToArray(t.Position, t.RawRotation);

	public static TransformPayloadDto FromArray(byte[] f) => new(f);
	public static TransformPayloadDto FromGDTransform(Transform3D t) => new(t.Origin, t.Basis.GetRotationQuaternion());
}
