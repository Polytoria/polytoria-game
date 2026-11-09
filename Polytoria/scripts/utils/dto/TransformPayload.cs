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
	public byte[] Data { get; set; } = null!;

	public Vector3 Position
	{
		get => new Vector3(
			BinaryPrimitives.ReadSingleLittleEndian(Data.AsSpan(0, 4)),
			BinaryPrimitives.ReadSingleLittleEndian(Data.AsSpan(4, 4)),
			BinaryPrimitives.ReadSingleLittleEndian(Data.AsSpan(8, 4))
		);
		set
		{
			BinaryPrimitives.WriteSingleLittleEndian(Data.AsSpan(0, 4), value.X);
			BinaryPrimitives.WriteSingleLittleEndian(Data.AsSpan(4, 4), value.Y);
			BinaryPrimitives.WriteSingleLittleEndian(Data.AsSpan(8, 4), value.Z);
		}
	}

	public ulong RawRotation
	{
		get => BinaryPrimitives.ReadUInt64LittleEndian(Data.AsSpan(12, 8));
		set => BinaryPrimitives.WriteUInt64LittleEndian(Data.AsSpan(12, 8), value);
	}

	public Quaternion Rotation
	{
		get => UnitQuaternionDto.FromCompressed(RawRotation);
		set => RawRotation = UnitQuaternionDto.ToCompressed(value);
	}

	[MemoryPackConstructor, JsonConstructor]
	public TransformPayloadDto() { }
	public TransformPayloadDto(byte[] bytes) { Data = bytes; }
	public TransformPayloadDto(Vector3 position, ulong rotation) { Data = ToArray(position, rotation); }
	public TransformPayloadDto(Vector3 position, Quaternion rotation) { Data = ToArray(position, rotation); }

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

	public static byte[] ToArray(Vector3 Position, ulong Rotation) => CreateData(Position, Rotation);
	public static byte[] ToArray(Vector3 Position, Quaternion Rotation) => ToArray(Position, UnitQuaternionDto.ToCompressed(Rotation));
	public static byte[] ToArray(Transform3D t) => ToArray(t.Origin, t.Basis.GetRotationQuaternion());
	public static byte[] ToArray(TransformPayloadDto t) => [.. t.Data];

	public static TransformPayloadDto FromArray(byte[] f) => new(f);
	public static TransformPayloadDto FromGDTransform(Transform3D t) => new(t.Origin, t.Basis.GetRotationQuaternion());

	private static byte[] CreateData(Vector3 position, ulong rotation)
	{
		byte[] data = new byte[20];
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(0, 4), position.X);
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4, 4), position.Y);
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(8, 4), position.Z);
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(12, 8), rotation);
		return data;
	}
}
