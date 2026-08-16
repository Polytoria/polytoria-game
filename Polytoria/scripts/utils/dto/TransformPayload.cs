// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using MemoryPack;
using System;
using System.Buffers.Binary;
using System.Text.Json.Serialization;

namespace Polytoria.Utils.DTOs;

[MemoryPackable]
public partial class TransformPayloadDto
{
	private readonly bool _uInt64 = false;

	public byte[] Data { get; set; } = null!;

	[MemoryPackIgnore, JsonIgnore]
	public Vector3 Position => new(
		BinaryPrimitives.ReadSingleLittleEndian(Data.AsSpan(0, 4)),
		BinaryPrimitives.ReadSingleLittleEndian(Data.AsSpan(4, 4)),
		BinaryPrimitives.ReadSingleLittleEndian(Data.AsSpan(8, 4))
	);

	[MemoryPackIgnore, JsonIgnore]
	public Quaternion Rotation => _uInt64
		? UnitQuaternionUInt64Dto.FromCompressed(BinaryPrimitives.ReadUInt64LittleEndian(Data.AsSpan(12, 8)))
		: UnitQuaternionDto.FromCompressed(BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(12, 4)));

	[MemoryPackConstructor, JsonConstructor]
	public TransformPayloadDto() { }
	public TransformPayloadDto(byte[] bytes)
	{
		switch (bytes.Length)
		{
			case 16: // 3 floats + 1 uint (use UnitQuaternionDto)
				_uInt64 = false;
				break;
			case 20: // 3 floats + 1 ulong (use UnitQuaternionUInt64Dto)
				_uInt64 = true;
				break;
			default: // invalid
				Data = new byte[16];
				return;
		}
		Data = bytes;
	}

	public bool IsEqualApprox(Vector3 pos, Quaternion rot) => Position.IsEqualApprox(pos) && Rotation.IsEqualApprox(rot);
	public bool IsEqualApprox(Transform3D t) => Position.IsEqualApprox(t.Origin) && Rotation.IsEqualApprox(t.Basis.GetRotationQuaternion());
	public bool IsEqualApprox(TransformPayloadDto other) => IsEqualApprox(other.Position, other.Rotation);

	public static byte[] ToArray(Vector3 Position, uint Rotation)
	{
		byte[] data = new byte[16];
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(0, 4), Position.X);
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4, 4), Position.Y);
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(8, 4), Position.Z);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12, 4), Rotation);
		return data;
	}
	public static byte[] ToArray(Vector3 Position, Quaternion Rotation) => ToArray(Position, UnitQuaternionDto.ToCompressed(Rotation));
	public static byte[] ToArray(Transform3D t) => ToArray(t.Origin, t.Basis.GetRotationQuaternion());
	public static TransformPayloadDto FromGDTransform(Transform3D t) => new(ToArray(t));

	public static byte[] ToArrayUInt64(Vector3 Position, ulong Rotation)
	{
		byte[] data = new byte[20];
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(0, 4), Position.X);
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4, 4), Position.Y);
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(8, 4), Position.Z);
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(12, 8), Rotation);
		return data;
	}
	public static byte[] ToArrayUInt64(Vector3 Position, Quaternion Rotation) => ToArrayUInt64(Position, UnitQuaternionUInt64Dto.ToCompressed(Rotation));
	public static byte[] ToArrayUInt64(Transform3D t) => ToArrayUInt64(t.Origin, t.Basis.GetRotationQuaternion());
	public static TransformPayloadDto FromGDTransformUInt64(Transform3D t) => new(ToArrayUInt64(t));
}
