using System.Reflection.Emit;
using System.Text.RegularExpressions;

namespace MarkdownBBCodeConversion;

public static partial class Conversions
{
	public const int HORIZONTAL_RULE_WIDTH_PERCENT = 100;

	public static string MarkdownToBBCode(string mdText)
	{
		string result = mdText;

		return SeparateCodeRegex().Replace(mdText, m =>
		{
			// If it's a Fenced Code Block (captured by the first group)
			if (m.Groups[1].Success)
			{
				// Convert the wrapper to BBCode [code]
				return CodeBlockMDRegex().Replace(m.Value, "[code]$1[/code]");
			}

			// If it's Inline Code (captured by the second group)
			if (m.Groups[2].Success)
			{
				// Also convert the wrapper to BBCode [code]
				return InlineCodeMDRegex().Replace(m.Value, "[code]$1[/code]");
			}

			// Otherwise, it's regular text: perform your standard conversions here
			string text = m.Value;
			text = BoldMDRegex().Replace(text, "[b]$1[/b]");
			text = ItalicsMDRegex().Replace(text, "[i]$1[/i]");
			text = StrikethroughMDRegex().Replace(text, "[s]$1[/s]");
			text = HorizontalRuleMDRegex().Replace(text, $"[hr width={HORIZONTAL_RULE_WIDTH_PERCENT}%]");

			return text;
		});
	}

	public static string BBCodeToMarkdown(string bbcodeText)
	{
		throw new NotImplementedException();
	}

	[GeneratedRegex(@"(?s)(```[^\r\n]*\r?\n.*?\r?\n```)|(`.*?`)|(?<other>.+?)")]
	private static partial Regex SeparateCodeRegex();

	[GeneratedRegex(@"```[^\r\n]*\r?\n(.*?)\r?\n```", RegexOptions.Singleline)]
	private static partial Regex CodeBlockMDRegex();

	[GeneratedRegex(@"\*\*(.*?)\*\*")]
	private static partial Regex BoldMDRegex();
	[GeneratedRegex(@"\*(.*?)\*")]
	private static partial Regex ItalicsMDRegex();
	[GeneratedRegex(@"`(.*?)`")]
	private static partial Regex InlineCodeMDRegex();
	[GeneratedRegex(@"^(\-\-\-|\*\*\*|___)$", RegexOptions.Multiline)]
	private static partial Regex HorizontalRuleMDRegex();
	[GeneratedRegex(@"~~(.*?)~~")]
	private static partial Regex StrikethroughMDRegex();
}
