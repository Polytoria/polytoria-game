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
	private readonly bool UInt64 = false;

	public byte[] Data { get; set; } = null!;

	[MemoryPackIgnore, JsonIgnore]
	public Vector3 Position => new(
		BitConverter.ToSingle(Data, 0),
		BitConverter.ToSingle(Data, 4),
		BitConverter.ToSingle(Data, 8)
	);

	[MemoryPackIgnore, JsonIgnore]
	public Quaternion Rotation => UInt64 ? UnitQuaternionUInt64Dto.FromCompressed(BitConverter.ToUInt64(Data, 12)) : UnitQuaternionDto.FromCompressed(BitConverter.ToUInt32(Data, 12));

	[MemoryPackConstructor, JsonConstructor]
	public TransformPayloadDto() { }
	public TransformPayloadDto(byte[] bytes)
	{
		UInt64 = bytes.Length switch
		{
			// 3 floats + 1 uint (use UnitQuaternionDto)
			16 => false,
			// 3 floats + 1 ulong (use UnitQuaternionUInt64Dto)
			20 => true,
			// invalid
			_ => throw new ArgumentOutOfRangeException(nameof(bytes)),
		};
		Data = bytes;
	}

	public bool IsEqualApprox(Vector3 pos, Quaternion rot) => Position.IsEqualApprox(pos) && Rotation.IsEqualApprox(rot);
	public bool IsEqualApprox(Transform3D t) => Position.IsEqualApprox(t.Origin) && Rotation.IsEqualApprox(t.Basis.GetRotationQuaternion());
	public bool IsEqualApprox(TransformPayloadDto other) => IsEqualApprox(other.Position, other.Rotation);

	public static byte[] ToArray(Vector3 Position, uint Rotation) => [
		..BitConverter.GetBytes(Position.X),
		..BitConverter.GetBytes(Position.Y),
		..BitConverter.GetBytes(Position.Z),
		..BitConverter.GetBytes(Rotation)
	];
	public static byte[] ToArray(Vector3 Position, Quaternion Rotation) => ToArray(Position, UnitQuaternionDto.ToCompressed(Rotation));
	public static byte[] ToArray(Transform3D t) => ToArray(t.Origin, t.Basis.GetRotationQuaternion());
	public static TransformPayloadDto FromGDTransform(Transform3D t) => new(ToArray(t));

	public static byte[] ToArrayUInt64(Vector3 Position, ulong Rotation) => [
		..BitConverter.GetBytes(Position.X),
		..BitConverter.GetBytes(Position.Y),
		..BitConverter.GetBytes(Position.Z),
		..BitConverter.GetBytes(Rotation)
	];
	public static byte[] ToArrayUInt64(Vector3 Position, Quaternion Rotation) => ToArrayUInt64(Position, UnitQuaternionUInt64Dto.ToCompressed(Rotation));
	public static byte[] ToArrayUInt64(Transform3D t) => ToArrayUInt64(t.Origin, t.Basis.GetRotationQuaternion());
	public static TransformPayloadDto FromGDTransformUInt64(Transform3D t) => new(ToArrayUInt64(t));
}
