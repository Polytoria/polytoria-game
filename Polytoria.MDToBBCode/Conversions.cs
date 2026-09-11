using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Polytoria.MDToBBCode;

public static partial class Conversions
{
	public const int HORIZONTAL_RULE_WIDTH_PERCENT = 100;

	public static string MarkdownToBBCode(string mdText)
	{
		return SeparateFencedCodeRegex().Replace(mdText, EvaluateFencedCodeOrBlocks);
	}

	private static string EvaluateFencedCodeOrBlocks(Match match)
	{
		// Fenced code blocks
		if (match.Groups[1].Success)
		{
			return CodeBlockMDRegex().Replace(match.Value, "[code]$1[/code]");
		}

		// Block-level elements (lists, horizontal rules)
		string text = match.Value;
		text = HorizontalRuleMDRegex().Replace(text, $"[hr width={HORIZONTAL_RULE_WIDTH_PERCENT}%]");
		text = OrderedListBlockRegex().Replace(text, EvaluateOrderedList);
		text = UnorderedListBlockRegex().Replace(text, EvaluateUnorderedList);

		// Inline code and other formatting
		return SeparateInlineCodeRegex().Replace(text, EvaluateInlineCodeOrFormatting);
	}

	private static string EvaluateInlineCodeOrFormatting(Match match)
	{
		// Inline code
		if (match.Groups[1].Success)
		{
			return InlineCodeMDRegex().Replace(match.Value, "[code]$1[/code]");
		}

		// Other stuff
		return ApplyTextFormatting(match.Value);
	}

	private static string ApplyTextFormatting(string text)
	{
		text = BoldItalicsMDRegex().Replace(text, "[b][i]$1$2[/i][/b]");
		text = BoldMDRegex().Replace(text, "[b]$1$2[/b]");
		text = ItalicsMDRegex().Replace(text, "[i]$1$2[/i]");
		text = StrikethroughMDRegex().Replace(text, "[s]$1[/s]");

		return text;
	}

	private static string EvaluateOrderedList(Match match)
	{
		bool endsWithNewline = match.Value.EndsWith('\n');
		string content = OrderedListItemStripRegex().Replace(match.Value, "").TrimEnd('\r', '\n');
		return $"[ol]\n{content}\n[/ol]" + (endsWithNewline ? "\n" : "");
	}

	private static string EvaluateUnorderedList(Match match)
	{
		bool endsWithNewline = match.Value.EndsWith('\n');
		string content = UnorderedListItemStripRegex().Replace(match.Value, "").TrimEnd('\r', '\n');
		return $"[ul]\n{content}\n[/ul]" + (endsWithNewline ? "\n" : "");
	}

	// ---------------------------------------------------------------------
	// REGEX DEFINITIONS
	// ---------------------------------------------------------------------

	[ExcludeFromCodeCoverage]
	[GeneratedRegex(@"(?s)(```.*?```)|(?<other>(?:(?!```).)+)")]
	private static partial Regex SeparateFencedCodeRegex();

	[ExcludeFromCodeCoverage]
	[GeneratedRegex(@"(?s)(`.*?`)|(?<other>(?:(?!`).)+)")]
	private static partial Regex SeparateInlineCodeRegex();

	[ExcludeFromCodeCoverage]
	[GeneratedRegex(@"(?s)```[^\r\n]*\r?\n(.*?)\r?\n```")]
	private static partial Regex CodeBlockMDRegex();

	[ExcludeFromCodeCoverage]
	[GeneratedRegex(@"`(.*?)`")]
	private static partial Regex InlineCodeMDRegex();

	[ExcludeFromCodeCoverage]
	[GeneratedRegex(@"\*\*\*(.*?)\*\*\*|___(.*?)___", RegexOptions.Singleline)]
	private static partial Regex BoldItalicsMDRegex();

	[ExcludeFromCodeCoverage]
	[GeneratedRegex(@"\*\*(.*?)\*\*|__(.*?)__", RegexOptions.Singleline)]
	private static partial Regex BoldMDRegex();

	[ExcludeFromCodeCoverage]
	[GeneratedRegex(@"(?<!\*)\*(?!\*)(.*?)(?<!\*)\*(?!\*)|(?<!_)_(?!_)(.*?)(?<!_)_(?!_)", RegexOptions.Singleline)]
	private static partial Regex ItalicsMDRegex();

	[ExcludeFromCodeCoverage]
	[GeneratedRegex(@"^ {0,3}([\*\-_])( *(\1)){2,} *$", RegexOptions.Multiline)]
	private static partial Regex HorizontalRuleMDRegex();

	[ExcludeFromCodeCoverage]
	[GeneratedRegex(@"~~(.*?)~~", RegexOptions.Singleline)]
	private static partial Regex StrikethroughMDRegex();

	[ExcludeFromCodeCoverage]
	[GeneratedRegex(@"(?m)^((?:[ \t]*\d+\.[ \t].*(?:\r?\n|$))+)")]
	private static partial Regex OrderedListBlockRegex();

	[ExcludeFromCodeCoverage]
	[GeneratedRegex(@"(?m)^((?:[ \t]*[*+-][ \t].*(?:\r?\n|$))+)")]
	private static partial Regex UnorderedListBlockRegex();

	[ExcludeFromCodeCoverage]
	[GeneratedRegex(@"(?m)^[ \t]*\d+\.[ \t]*")]
	private static partial Regex OrderedListItemStripRegex();

	[ExcludeFromCodeCoverage]
	[GeneratedRegex(@"(?m)^[ \t]*[*+-][ \t]*")]
	private static partial Regex UnorderedListItemStripRegex();
}
