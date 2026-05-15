using System;

namespace Polytoria.Utils;

public static class BitmapUtils
{
	public static bool Get(uint value, int index)
	{
		if (index < 1 || index > 32)
		{
			throw new InvalidOperationException("index is out of bounds");
		}

		return (value & 1u << (index - 1)) != 0;
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
