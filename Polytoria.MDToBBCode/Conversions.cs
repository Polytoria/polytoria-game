using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Polytoria.MDToBBCode;

public static partial class Conversions
{
	public const int HORIZONTAL_RULE_WIDTH_PERCENT = 100;

	public static string MarkdownToBBCode(string mdText)
	{
		// We match: Fenced Code OR Inline Code OR "The rest"
		// The (?:(?!```|`).)+ part ensures "other" text is captured in large chunks.
		return SeparateCodeRegex().Replace(mdText, m =>
		{
			// Group 1: Fenced code block
			if (m.Groups[1].Success)
			{
				// Strip the ``` markers and wrap in [code]
				return CodeBlockMDRegex().Replace(m.Value, "[code]$1[/code]");
			}

			// Group 2: Inline code
			if (m.Groups[2].Success)
			{
				return InlineCodeMDRegex().Replace(m.Value, "[code]$1[/code]");
			}

			// Group 3: Regular text (the "other" group)
			string text = m.Value;
			text = BoldMDRegex().Replace(text, "[b]$1[/b]");
			text = ItalicsMDRegex().Replace(text, "[i]$1[/i]");
			text = StrikethroughMDRegex().Replace(text, "[s]$1[/s]");
			text = HorizontalRuleMDRegex().Replace(text, $"[hr width={HORIZONTAL_RULE_WIDTH_PERCENT}%]");

			return text;
		});
	}

	[ExcludeFromCodeCoverage]
	[GeneratedRegex(@"(?s)(```.*?```)|(`.*?`)|(?<other>(?:(?!```|`).)+)")]
	private static partial Regex SeparateCodeRegex();

	[ExcludeFromCodeCoverage]
	[GeneratedRegex(@"(?s)```[^\r\n]*\r?\n(.*?)\r?\n```")]
	private static partial Regex CodeBlockMDRegex();

	[ExcludeFromCodeCoverage]
	[GeneratedRegex(@"\*\*(.*?)\*\*")]
	private static partial Regex BoldMDRegex();

	[ExcludeFromCodeCoverage]
	[GeneratedRegex(@"(?<!\*)\*(?!\*)(.*?)(?<!\*)\*(?!\*)")]
	private static partial Regex ItalicsMDRegex();

	[ExcludeFromCodeCoverage]
	[GeneratedRegex(@"`(.*?)`")]
	private static partial Regex InlineCodeMDRegex();

	[ExcludeFromCodeCoverage]
	[GeneratedRegex(@"^(\-\-\-|\*\*\*|___)$", RegexOptions.Multiline)]
	private static partial Regex HorizontalRuleMDRegex();

	[ExcludeFromCodeCoverage]
	[GeneratedRegex(@"~~(.*?)~~")]
	private static partial Regex StrikethroughMDRegex();
}
