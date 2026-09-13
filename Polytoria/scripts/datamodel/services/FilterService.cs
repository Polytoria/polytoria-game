// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Polytoria.Datamodel.Services;

[Static("Filter"), ExplorerExclude]
[SaveIgnore]
public sealed partial class FilterService : Instance
{
	private static List<string> _profanityList = [];

	private static bool IsRegexEntry(string entry)
	{
		return Regex.IsMatch(entry, @"[\\(\[\^$|+?{]");
	}

	private static bool Matches(string input, string pattern)
	{
		try
		{
			return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase);
		}
		catch (ArgumentException)
		{
			return false;
		}
	}

	public override void Init()
	{
		base.Init();
		LoadFilter();
	}

	private static async void LoadFilter()
	{
		try
		{
			if (OS.HasFeature("offline"))
			{
				_profanityList = ["swear"];
				return;
			}
			string rawdata = await PolyAPI.GetProfanityList();
			_profanityList = [.. rawdata.Split(["\n"], StringSplitOptions.RemoveEmptyEntries)];
		}
		catch (Exception err)
		{
			PT.PrintErr("Failed to get profanity list: ", err);
		}
	}

	[ScriptMethod]
	public static string Filter(string input)
	{
		if (_profanityList.Count == 0)
		{
			LoadFilter();
			return new string('*', input.Length);
		}

		string matchInput = Regex.Replace(input, @"[\u200B\uFEFF]", "");
		matchInput = Regex.Replace(matchInput, @"\s+", " ").Trim();

		foreach (string filter in _profanityList)
		{
			string f = filter.Trim();
			if (!f.Contains(' ') && !f.Contains(@"\s")) continue;
			if (IsRegexEntry(f))
			{
				if (Matches(matchInput, f))
					return new string('*', input.Length);
			}
			else if (f.Contains('*'))
			{
				string regex = Regex.Replace(f, @"(?<!\.)\*", ".*").Replace(" ", @"\s+");
				if (Matches(matchInput, regex))
					return new string('*', input.Length);
			}
			else
			{
				string regex = @"\b" + Regex.Escape(f).Replace(@"\ ", @"\s+") + @"\b";
				if (Matches(matchInput, regex))
					return new string('*', input.Length);
			}
		}

		string[] words = input.Split([" "], StringSplitOptions.RemoveEmptyEntries);
		List<string> filteredWords = [];
		foreach (string word in words)
		{
			string matchWord = Regex.Replace(word.Trim(), @"[\u200B\uFEFF]", "");
			bool found = false;
			foreach (string filter in _profanityList)
			{
				string f = filter.Trim();
				if (IsRegexEntry(f))
				{
					if (Matches(matchWord, "^" + f))
					{
						filteredWords.Add(new string('*', word.Length));
						found = true;
						break;
					}
				}
				else if (f.Contains('*'))
				{
					string regex = "^" + Regex.Replace(f, @"(?<!\.)\*", ".*");
					if (Matches(matchWord, regex))
					{
						filteredWords.Add(new string('*', word.Length));
						found = true;
						break;
					}
				}
				else
				{
					if (matchWord.Equals(f, StringComparison.OrdinalIgnoreCase))
					{
						filteredWords.Add(new string('*', word.Length));
						found = true;
						break;
					}
				}
			}

			if (!found)
			{
				filteredWords.Add(word);
			}
		}

		return string.Join(" ", filteredWords.ToArray());
	}
}
