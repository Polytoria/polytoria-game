// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Utils.DTOs;
using System;
using System.Buffers.Binary;

namespace Polytoria.Client.Networking;

internal static class TransformPacket
{
	public const int SnapshotHeaderSize = 9;
	public const int SnapshotEntrySize = 32;
	public const int CompactHeaderSize = 22;
	public const int CompactEntryBaseSize = 13;
	public const int MaxSnapshotPacketSize = 1500;
	public const int MaxUploadEntries = 1024;
	public const float PositionStep = 1f / 64;
	public const float SpinStep = 0.4f;
	public const int LerpFlag = 1;
	public const int PeriodShift = 1;
	public const int SpinFlag = 8;
	public const int RestFlag = 16;
	public const int PositionModeShift = 5;
	public const int VelocityFlag = 128;
	private const float CompactRange = short.MaxValue * PositionStep;
	private const float MaxNetVelocity = 60000;

	public static void WriteEntry(Span<byte> span, ushort index, byte[] transform, double time, double baseTime,
		byte flags, Vector3 velocity, byte writeSeq)
	{
		BinaryPrimitives.WriteUInt16LittleEndian(span, index);
		transform.CopyTo(span[2..]);
		BinaryPrimitives.WriteInt16LittleEndian(span[22..], TimeOffset(time, baseTime));
		span[24] = flags;
		WriteHalfVector3(span[25..], velocity);
		span[31] = writeSeq;
	}

	public static UploadEntry ReadEntry(ReadOnlySpan<byte> entry, double baseTime)
	{
		ushort index = BinaryPrimitives.ReadUInt16LittleEndian(entry);
		TransformPayloadDto transform = TransformPayloadDto.FromArray(entry.Slice(2, 20).ToArray());
		double time = baseTime + BinaryPrimitives.ReadInt16LittleEndian(entry[22..]) / 1000.0;
		return new(index, transform, time, entry[24], ReadHalfVector3(entry[25..]), entry[31]);
	}

	public static int CompactEntrySize(byte[] transform, Vector3 velocity, Vector3 spin, Vector3 origin)
	{
		Vector3 position = TransformPayloadDto.FromArray(transform).Position;
		int size = CompactEntryBaseSize + (CompactPositionMode(position, origin) == 0 ? 12 : 6);
		if (velocity != Vector3.Zero) size += 6;
		if (spin != Vector3.Zero) size += 3;
		return size;
	}

	public static int WriteCompactEntry(Span<byte> span, ushort index, byte[] transform, double time, double baseTime,
		byte flags, Vector3 velocity, Vector3 spin, Vector3 origin)
	{
		Vector3 position = TransformPayloadDto.FromArray(transform).Position;
		int mode = CompactPositionMode(position, origin);
		bool hasVelocity = velocity != Vector3.Zero;
		bool hasSpin = spin != Vector3.Zero;
		flags |= (byte)((mode << PositionModeShift) | (hasVelocity ? VelocityFlag : 0) | (hasSpin ? SpinFlag : 0));

		BinaryPrimitives.WriteUInt16LittleEndian(span, index);
		span[2] = flags;
		BinaryPrimitives.WriteInt16LittleEndian(span[3..], TimeOffset(time, baseTime));
		transform.AsSpan(12, 8).CopyTo(span[5..]);
		int length = CompactEntryBaseSize;
		if (mode == 0)
		{
			transform.AsSpan(0, 12).CopyTo(span[length..]);
			length += 12;
		}
		else
		{
			Vector3 offset = mode == 1 ? position - origin : position;
			BinaryPrimitives.WriteInt16LittleEndian(span[length..], (short)Math.Round(offset.X / PositionStep));
			BinaryPrimitives.WriteInt16LittleEndian(span[(length + 2)..], (short)Math.Round(offset.Y / PositionStep));
			BinaryPrimitives.WriteInt16LittleEndian(span[(length + 4)..], (short)Math.Round(offset.Z / PositionStep));
			length += 6;
		}
		if (hasVelocity)
		{
			WriteHalfVector3(span[length..], velocity);
			length += 6;
		}
		if (hasSpin)
		{
			span[length] = (byte)(sbyte)Mathf.Clamp(Math.Round(spin.X / SpinStep), -127, 127);
			span[length + 1] = (byte)(sbyte)Mathf.Clamp(Math.Round(spin.Y / SpinStep), -127, 127);
			span[length + 2] = (byte)(sbyte)Mathf.Clamp(Math.Round(spin.Z / SpinStep), -127, 127);
			length += 3;
		}
		return length;
	}

	private static int CompactPositionMode(Vector3 position, Vector3 origin)
	{
		if (FitsCompact(position - origin)) return 1;
		if (FitsCompact(position)) return 2;
		return 0;
	}

	private static bool FitsCompact(Vector3 offset)
	{
		return Mathf.Abs(offset.X) < CompactRange && Mathf.Abs(offset.Y) < CompactRange && Mathf.Abs(offset.Z) < CompactRange;
	}

	private static short TimeOffset(double time, double baseTime)
	{
		return (short)Mathf.Clamp(Mathf.Round((time - baseTime) * 1000), short.MinValue, short.MaxValue);
	}

	public static void WriteVector3(Span<byte> span, Vector3 value)
	{
		BinaryPrimitives.WriteSingleLittleEndian(span, value.X);
		BinaryPrimitives.WriteSingleLittleEndian(span[4..], value.Y);
		BinaryPrimitives.WriteSingleLittleEndian(span[8..], value.Z);
	}

	public static Vector3 ReadVector3(ReadOnlySpan<byte> span)
	{
		float x = BinaryPrimitives.ReadSingleLittleEndian(span);
		float y = BinaryPrimitives.ReadSingleLittleEndian(span[4..]);
		float z = BinaryPrimitives.ReadSingleLittleEndian(span[8..]);
		return new(x, y, z);
	}

	public static void WriteHalfVector3(Span<byte> span, Vector3 value)
	{
		BinaryPrimitives.WriteHalfLittleEndian(span, (Half)Mathf.Clamp(value.X, -MaxNetVelocity, MaxNetVelocity));
		BinaryPrimitives.WriteHalfLittleEndian(span[2..], (Half)Mathf.Clamp(value.Y, -MaxNetVelocity, MaxNetVelocity));
		BinaryPrimitives.WriteHalfLittleEndian(span[4..], (Half)Mathf.Clamp(value.Z, -MaxNetVelocity, MaxNetVelocity));
	}

	public static Vector3 ReadHalfVector3(ReadOnlySpan<byte> span)
	{
		float x = (float)BinaryPrimitives.ReadHalfLittleEndian(span);
		float y = (float)BinaryPrimitives.ReadHalfLittleEndian(span[2..]);
		float z = (float)BinaryPrimitives.ReadHalfLittleEndian(span[4..]);
		return new(x, y, z);
	}

	public readonly record struct UploadEntry(ushort Index, TransformPayloadDto Transform, double Time, byte Flags, Vector3 Velocity, byte WriteSeq);
}
