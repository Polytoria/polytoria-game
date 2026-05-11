namespace MarkdownBBCodeConversion.Tests
{
	public class ConversionsTests
	{
		[Fact]
		public void MarkdownToBBCode_TextInCodeBlocksDoNotChange()
		{
			string oldText = @"Here is some text with a code block:
```
**This should not be bold**
~~This should not be strikethrough~~
```
**But this should be bold**
`__This is also code only__`";
			string expected = @"Here is some text with a code block:
[code]**This should not be bold**
~~This should not be strikethrough~~[/code]
[b]But this should be bold[/b]
[code]__This is also code only__[/code]";

			Assert.Equal(expected, Conversions.MarkdownToBBCode(oldText));
		}
	}
}
