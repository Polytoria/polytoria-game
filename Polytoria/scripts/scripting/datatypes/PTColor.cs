// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Scripting.Datatypes;

public class PTColor : IScriptGDObject
{
	Color color;

	[ScriptProperty] public float R { get => color.R; set => color.R = value; }
	[ScriptProperty] public float G { get => color.G; set => color.G = value; }
	[ScriptProperty] public float B { get => color.B; set => color.B = value; }
	[ScriptProperty] public float A { get => color.A; set => color.A = value; }

	// Each constant returns a new instance rather than a shared one. R, G, B and A are
	// writable, so handing out one shared instance would let a script permanently change
	// what the constant means for everything else in the session.
	[ScriptProperty] public static PTColor Black => New(0, 0, 0);
	[ScriptProperty] public static PTColor Blue => New(0, 0, 1);
	[ScriptProperty] public static PTColor Clear => New(0, 0, 0, 0);
	[ScriptProperty] public static PTColor Cyan => New(0, 1, 1);
	[ScriptProperty] public static PTColor Gray => New(0.5f, 0.5f, 0.5f);
	[ScriptProperty] public static PTColor Green => New(0, 1, 0);
	[ScriptProperty] public static PTColor Magenta => New(1, 0, 1);
	[ScriptProperty] public static PTColor Red => New(1, 0, 0);
	[ScriptProperty] public static PTColor White => New(1, 1, 1);
	[ScriptProperty] public static PTColor Yellow => New(1, 1, 0);

	public static PTColor FromGDClass(Color clr)
	{
		return new PTColor()
		{
			color = clr
		};
	}

	public object ToGDClass()
	{
		return color;
	}

	[ScriptMethod]
	public static PTColor New()
	{
		return new()
		{
			R = 0,
			G = 0,
			B = 0,
			A = 1
		};
	}

	[ScriptMethod]
	public static PTColor New(float d)
	{
		return new()
		{
			R = d,
			G = d,
			B = d,
			A = 1
		};
	}

	[ScriptMethod]
	public static PTColor New(float r, float g, float b)
	{
		return new()
		{
			R = r,
			G = g,
			B = b,
			A = 1
		};
	}

	[ScriptMethod]
	public static PTColor New(float r, float g, float b, float a)
	{
		return new()
		{
			R = r,
			G = g,
			B = b,
			A = a
		};
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Add)]
	public static PTColor Add(PTColor a, PTColor b)
	{
		return FromGDClass(a.color + b.color);
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Sub)]
	public static PTColor Sub(PTColor a, PTColor b)
	{
		return FromGDClass(a.color - b.color);
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Mul)]
	public static object Mul(PTColor a, object b)
	{
		if (b is double d)
			return FromGDClass(a.color * new Color((float)d, (float)d, (float)d));
		return FromGDClass(a.color);
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Eq)]
	public static bool Eq(PTColor a, PTColor b)
	{
		return a.color == b.color;
	}

	[ScriptMetamethod(ScriptObjectMetamethod.ToString)]
	public static string ToString(PTColor? v)
	{
		if (v == null) return "<Color>";
		return $"<Color:({v.color.R}, {v.color.G}, {v.color.B}, {v.color.A})>";
	}

	[ScriptMethod]
	public static PTColor Random()
	{
		return New(GD.Randf(), GD.Randf(), GD.Randf());
	}

	[ScriptMethod]
	public static PTColor FromRGB(float r, float g, float b, float a = 1)
	{
		return FromGDClass(new Color(r / 255, g / 255, b / 255, a));
	}

	[ScriptMethod]
	public static PTColor FromHex(string hex)
	{
		return FromGDClass(Color.FromString(hex, new(1, 1, 1)));
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static string ToHex(PTColor c)
	{
		return c.color.ToHtml();
	}

	[ScriptMethod]
	public static PTColor FromHSV(float h, float s, float v, float a = 1)
	{
		return FromGDClass(Color.FromHsv(h, s, v, a));
	}

	[ScriptMethod(ConvertParamsToGD = false, SemiStatic = true)]
	public static PTColor Lerp(PTColor a, PTColor b, float t)
	{
		return FromGDClass(a.color.Lerp(b.color, t));
	}
}
