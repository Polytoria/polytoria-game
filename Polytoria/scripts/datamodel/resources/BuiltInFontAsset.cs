// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Enums;
using Polytoria.Shared;
using System.Collections.Generic;

namespace Polytoria.Datamodel.Resources;

[Instantiable]
public partial class BuiltInFontAsset : FontAsset
{
	private BuiltInTextFontPresetEnum _fontPreset;
	private FontWeightEnum _fontWeight = FontWeightEnum.Regular;
	private FontStyleEnum _fontStyle = FontStyleEnum.Normal;

	[Editable, ScriptProperty]
	public BuiltInTextFontPresetEnum FontPreset
	{
		get => _fontPreset;
		set
		{
			_fontPreset = value;
			LoadResource();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public FontWeightEnum FontWeight
	{
		get => _fontWeight;
		set
		{
			_fontWeight = value;
			LoadResource();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public FontStyleEnum FontStyle
	{
		get => _fontStyle;
		set
		{
			_fontStyle = value;
			LoadResource();
			OnPropertyChanged();
		}
	}

	private readonly Dictionary<(BuiltInTextFontPresetEnum, FontStyleEnum), string> VariantFontMapping = new()
	{
		// SourceSans3
		{ (BuiltInTextFontPresetEnum.SourceSans, FontStyleEnum.Normal), "SourceSans3-VariableFont_wght.ttf" },
		{ (BuiltInTextFontPresetEnum.SourceSans, FontStyleEnum.Italic), "SourceSans3-Italic-VariableFont_wght.ttf" },
	
		// Montserrat
		{ (BuiltInTextFontPresetEnum.Montserrat, FontStyleEnum.Normal), "Montserrat-VariableFont_wght.ttf" },
		{ (BuiltInTextFontPresetEnum.Montserrat, FontStyleEnum.Italic), "Montserrat-Italic-VariableFont_wght.ttf" },
	
		// RobotoMono
		{ (BuiltInTextFontPresetEnum.RobotoMono, FontStyleEnum.Normal), "RobotoMono-VariableFont_wght.ttf" },
		{ (BuiltInTextFontPresetEnum.RobotoMono, FontStyleEnum.Italic), "RobotoMono-Italic-VariableFont_wght.ttf" },
	
		// Rubik
		{ (BuiltInTextFontPresetEnum.Rubik, FontStyleEnum.Normal), "Rubik-VariableFont_wght.ttf" },
		{ (BuiltInTextFontPresetEnum.Rubik, FontStyleEnum.Italic), "Rubik-Italic-VariableFont_wght.ttf" },
	
		// JetBrainsMono
		{ (BuiltInTextFontPresetEnum.JetBrainsMono, FontStyleEnum.Normal), "JetBrainsMono-VariableFont_wght.ttf" },
		{ (BuiltInTextFontPresetEnum.JetBrainsMono, FontStyleEnum.Italic), "JetBrainsMono-Italic-VariableFont_wght.ttf" },
	
		// Single-variant Variable fonts
		{ (BuiltInTextFontPresetEnum.Domine, FontStyleEnum.Normal), "Domine-VariableFont_wght.ttf" },
		{ (BuiltInTextFontPresetEnum.Fredoka, FontStyleEnum.Normal), "Fredoka-VariableFont_wdth,wght.ttf" },
		{ (BuiltInTextFontPresetEnum.Orbitron, FontStyleEnum.Normal), "Orbitron-VariableFont_wght.ttf" }
	};

	private readonly Dictionary<(BuiltInTextFontPresetEnum, FontWeightEnum, FontStyleEnum), string> StaticFontMapping = new()
	{
		// PressStart2P (only regular)
		{ (BuiltInTextFontPresetEnum.PressStart2P, FontWeightEnum.Regular, FontStyleEnum.Normal), "PressStart2P-Regular.ttf" },

		// Poppins - Static fonts (Normal)
		{ (BuiltInTextFontPresetEnum.Poppins, FontWeightEnum.Thin, FontStyleEnum.Normal), "Poppins/Poppins-Thin.ttf" },
		{ (BuiltInTextFontPresetEnum.Poppins, FontWeightEnum.ExtraLight, FontStyleEnum.Normal), "Poppins/Poppins-ExtraLight.ttf" },
		{ (BuiltInTextFontPresetEnum.Poppins, FontWeightEnum.Light, FontStyleEnum.Normal), "Poppins/Poppins-Light.ttf" },
		{ (BuiltInTextFontPresetEnum.Poppins, FontWeightEnum.Regular, FontStyleEnum.Normal), "Poppins/Poppins-Regular.ttf" },
		{ (BuiltInTextFontPresetEnum.Poppins, FontWeightEnum.Medium, FontStyleEnum.Normal), "Poppins/Poppins-Medium.ttf" },
		{ (BuiltInTextFontPresetEnum.Poppins, FontWeightEnum.SemiBold, FontStyleEnum.Normal), "Poppins/Poppins-SemiBold.ttf" },
		{ (BuiltInTextFontPresetEnum.Poppins, FontWeightEnum.Bold, FontStyleEnum.Normal), "Poppins/Poppins-Bold.ttf" },
		{ (BuiltInTextFontPresetEnum.Poppins, FontWeightEnum.ExtraBold, FontStyleEnum.Normal), "Poppins/Poppins-ExtraBold.ttf" },
		{ (BuiltInTextFontPresetEnum.Poppins, FontWeightEnum.Black, FontStyleEnum.Normal), "Poppins/Poppins-Black.ttf" },
		
		// Poppins - Static fonts (Italic)
		{ (BuiltInTextFontPresetEnum.Poppins, FontWeightEnum.Thin, FontStyleEnum.Italic), "Poppins/Poppins-ThinItalic.ttf" },
		{ (BuiltInTextFontPresetEnum.Poppins, FontWeightEnum.ExtraLight, FontStyleEnum.Italic), "Poppins/Poppins-ExtraLightItalic.ttf" },
		{ (BuiltInTextFontPresetEnum.Poppins, FontWeightEnum.Light, FontStyleEnum.Italic), "Poppins/Poppins-LightItalic.ttf" },
		{ (BuiltInTextFontPresetEnum.Poppins, FontWeightEnum.Regular, FontStyleEnum.Italic), "Poppins/Poppins-Italic.ttf" },
		{ (BuiltInTextFontPresetEnum.Poppins, FontWeightEnum.Medium, FontStyleEnum.Italic), "Poppins/Poppins-MediumItalic.ttf" },
		{ (BuiltInTextFontPresetEnum.Poppins, FontWeightEnum.SemiBold, FontStyleEnum.Italic), "Poppins/Poppins-SemiBoldItalic.ttf" },
		{ (BuiltInTextFontPresetEnum.Poppins, FontWeightEnum.Bold, FontStyleEnum.Italic), "Poppins/Poppins-BoldItalic.ttf" },
		{ (BuiltInTextFontPresetEnum.Poppins, FontWeightEnum.ExtraBold, FontStyleEnum.Italic), "Poppins/Poppins-ExtraBoldItalic.ttf" },
		{ (BuiltInTextFontPresetEnum.Poppins, FontWeightEnum.Black, FontStyleEnum.Italic), "Poppins/Poppins-BlackItalic.ttf" },
		
		// ComicNeue - Static fonts (Normal)
		{ (BuiltInTextFontPresetEnum.ComicNeue, FontWeightEnum.Light, FontStyleEnum.Normal), "ComicNeue/ComicNeue-Light.ttf" },
		{ (BuiltInTextFontPresetEnum.ComicNeue, FontWeightEnum.Regular, FontStyleEnum.Normal), "ComicNeue/ComicNeue-Regular.ttf" },
		{ (BuiltInTextFontPresetEnum.ComicNeue, FontWeightEnum.Bold, FontStyleEnum.Normal), "ComicNeue/ComicNeue-Bold.ttf" },
		
		// ComicNeue - Static fonts (Italic)
		{ (BuiltInTextFontPresetEnum.ComicNeue, FontWeightEnum.Light, FontStyleEnum.Italic), "ComicNeue/ComicNeue-LightItalic.ttf" },
		{ (BuiltInTextFontPresetEnum.ComicNeue, FontWeightEnum.Regular, FontStyleEnum.Italic), "ComicNeue/ComicNeue-Italic.ttf" },
		{ (BuiltInTextFontPresetEnum.ComicNeue, FontWeightEnum.Bold, FontStyleEnum.Italic), "ComicNeue/ComicNeue-BoldItalic.ttf" },
		// Papyrus (only regular)
		{ (BuiltInTextFontPresetEnum.Papyrus, FontWeightEnum.Regular, FontStyleEnum.Normal), "Papyrus.ttf" },
		
		// Comic Sans MS (only regular)
		{ (BuiltInTextFontPresetEnum.ComicSansMS, FontWeightEnum.Regular, FontStyleEnum.Normal), "Comic Sans MS.ttf" },
	};

	private readonly Dictionary<BuiltInTextFontPresetEnum, string> DefaultFontFiles = new()
	{
		{ BuiltInTextFontPresetEnum.SourceSans, "SourceSans3-VariableFont_wght.ttf" },
		{ BuiltInTextFontPresetEnum.PressStart2P, "PressStart2P-Regular.ttf" },
		{ BuiltInTextFontPresetEnum.Montserrat, "Montserrat-VariableFont_wght.ttf" },
		{ BuiltInTextFontPresetEnum.RobotoMono, "RobotoMono-VariableFont_wght.ttf" },
		{ BuiltInTextFontPresetEnum.Rubik, "Rubik-VariableFont_wght.ttf" },
		{ BuiltInTextFontPresetEnum.Poppins, "Poppins/Poppins-Regular.ttf" },
		{ BuiltInTextFontPresetEnum.Domine, "Domine-VariableFont_wght.ttf" },
		{ BuiltInTextFontPresetEnum.Fredoka, "Fredoka-VariableFont_wdth,wght.ttf" },
		{ BuiltInTextFontPresetEnum.ComicNeue, "ComicNeue/ComicNeue-Regular.ttf" },
		{ BuiltInTextFontPresetEnum.Orbitron, "Orbitron-VariableFont_wght.ttf" },
		{ BuiltInTextFontPresetEnum.Papyrus, "Papyrus.ttf" },
		{ BuiltInTextFontPresetEnum.ComicSansMS, "Comic Sans MS.ttf" },
		{ BuiltInTextFontPresetEnum.JetBrainsMono, "JetBrainsMono-VariableFont_wght.ttf" },
	};

	public static void RegisterAsset()
	{
		RegisterType<BuiltInFontAsset>();
	}

	public override void LoadResource()
	{
		// Try Specific Weight/Style mapping
		if (!StaticFontMapping.TryGetValue((_fontPreset, _fontWeight, _fontStyle), out string? fontFile))
		{
			// Try Variable Fonts
			if (!VariantFontMapping.TryGetValue((_fontPreset, _fontStyle), out fontFile))
			{
				// Fallback to Normal Style if Italic wasn't found
				if (!VariantFontMapping.TryGetValue((_fontPreset, FontStyleEnum.Normal), out fontFile))
				{
					fontFile = DefaultFontFiles[_fontPreset];
				}
			}
		}

		InvokeResourceLoaded(LoadFont(fontFile));
	}

	private FontVariation LoadFont(string fontFile)
	{
		var font = GD.Load<Font>(Globals.BuiltInFontLocation.PathJoin(fontFile));
		FontVariation fv = new() { BaseFont = font };

		SetFontVariation(fv, _fontWeight);
		return fv;
	}

	private static void SetFontVariation(FontVariation fontVar, FontWeightEnum weight)
	{
		fontVar.SetVariationOpentype(new() { ["weight"] = GetWeightValue(weight) });
	}

	private static float GetWeightValue(FontWeightEnum weight)
	{
		return weight switch
		{
			FontWeightEnum.Thin => 100,
			FontWeightEnum.ExtraLight => 200,
			FontWeightEnum.Light => 300,
			FontWeightEnum.Regular => 400,
			FontWeightEnum.Medium => 500,
			FontWeightEnum.SemiBold => 600,
			FontWeightEnum.Bold => 700,
			FontWeightEnum.ExtraBold => 800,
			FontWeightEnum.Black => 900,
			_ => 400
		};
	}

	[ScriptEnum("FontPreset")]
	public enum BuiltInTextFontPresetEnum
	{
		SourceSans, PressStart2P, Montserrat, RobotoMono, Rubik, Poppins,
		Domine, Fredoka, ComicNeue, Orbitron, Papyrus, ComicSansMS, JetBrainsMono
	}
}
