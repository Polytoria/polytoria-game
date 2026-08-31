using System;

namespace Polytoria.Utils;

public static class BitmapUtils
{
	//Gets assuming preshifted
	public static bool GetRaw(uint value, uint valueOfPos)
	{
		return (value & valueOfPos) != 0;
	}


	public static bool Get(uint value, int index)
	{
		if (index < 1 || index > 32)
		{
			throw new InvalidOperationException("index is out of bounds");
		}

		return (value & 1u << (index - 1)) != 0;
	}

	//Sets assuming preshifted
	public static uint SetRaw(uint value, uint valueOfPos, bool setTo)
	{
		if (setTo)
		{
			value |= valueOfPos;
		}
		else
		{
			value &= ~valueOfPos;
		}

		return value;
	}

	public static uint Set(uint value, int index, bool setTo)
	{
		if (index < 1 || index > 32)
		{
			throw new InvalidOperationException("index is out of bounds");
		}

		if (setTo)
		{
			value |= 1u << (index - 1);
		}
		else
		{
			value &= ~(1u << (index - 1));
		}

		return value;
	}
}
