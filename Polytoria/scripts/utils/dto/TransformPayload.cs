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

	[MemoryPackIgnore, JsonIgnore]
	public bool UInt64 = false;

	[MemoryPackIgnore, JsonIgnore]
	public Vector3 Position
	{
		get => new(
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

	[MemoryPackIgnore, JsonIgnore]
	public Quaternion Rotation
	{
		get
		{
			if (UInt64)
			{
				return UnitQuaternionUInt64Dto.FromCompressed(BitConverter.ToUInt64(Data, 12));
			}
			return UnitQuaternionDto.FromCompressed(BitConverter.ToUInt32(Data, 12));
		}
		set
		{
			if (UInt64)
			{
				Array.Copy(BitConverter.GetBytes(UnitQuaternionUInt64Dto.ToCompressed(value)), 0, Data, 12, 8);
			}
			else
			{
				Array.Copy(BitConverter.GetBytes(UnitQuaternionDto.ToCompressed(value)), 0, Data, 12, 4);
			}
		}
	}

	[MemoryPackConstructor, JsonConstructor]
	public TransformPayloadDto() { }
	public TransformPayloadDto(byte[] bytes, bool uint64)
	{
		Data = bytes;
		UInt64 = uint64;
	}
	public TransformPayloadDto(byte[] bytes) : this(bytes, bytes.Length == 20) { }

	public bool IsEqualApprox(Vector3 pos, Quaternion rot) => Position.IsEqualApprox(pos) && Rotation.IsEqualApprox(rot);
	public bool IsEqualApprox(Transform3D t) => Position.IsEqualApprox(t.Origin) && Rotation.IsEqualApprox(t.Basis.GetRotationQuaternion());
	public bool IsEqualApprox(TransformPayloadDto other) => IsEqualApprox(other.Position, other.Rotation);

	// String helpers because memory pack don't like nested objects
	public static TransformPayloadDto FromString(string str)
	{
		var parts = str.Split('|');
		return new(ToArray(Vector3Dto.FromString(parts[0]), UnitQuaternionDto.FromString(parts[1])), false);
	}

	public static TransformPayloadDto FromStringUInt64(string str)
	{
		var parts = str.Split('|');
		return new(ToArrayUInt64(Vector3Dto.FromString(parts[0]), UnitQuaternionUInt64Dto.FromString(parts[1])), true);
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
	public static TransformPayloadDto FromTransform(Transform3D t) => new(ToArray(t), false);

	public static byte[] ToArrayUInt64(Vector3 Position, ulong Rotation) => [
		..BitConverter.GetBytes(Position.X),
		..BitConverter.GetBytes(Position.Y),
		..BitConverter.GetBytes(Position.Z),
		..BitConverter.GetBytes(Rotation)
	];
	public static byte[] ToArrayUInt64(Vector3 Position, Quaternion Rotation) => ToArrayUInt64(Position, UnitQuaternionUInt64Dto.ToCompressed(Rotation));
	public static byte[] ToArrayUInt64(Transform3D t) => ToArrayUInt64(t.Origin, t.Basis.GetRotationQuaternion());
	public static TransformPayloadDto FromTransformUInt64(Transform3D t) => new(ToArrayUInt64(t), true);
}
