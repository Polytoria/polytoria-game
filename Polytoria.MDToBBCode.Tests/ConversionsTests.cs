namespace Polytoria.MDToBBCode.Tests
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

		[Fact]
		public void MarkdownToBBCode_BoldItalicsStrikethrough()
		{
			string oldText = @"This is **bold** text, this is *italic* text, and this is ~~strikethrough~~.";
			string expected = @"This is [b]bold[/b] text, this is [i]italic[/i] text, and this is [s]strikethrough[/s].";
			Assert.Equal(expected, Conversions.MarkdownToBBCode(oldText));
		}

		[Fact]
		public void MarkdownToBBCode_HorizontalRule()
		{
			string oldText = "This is some text above the horizontal rule.\n---\nThis is some text below the horizontal rule.";
			string expected = $"This is some text above the horizontal rule.\n[hr width={Conversions.HORIZONTAL_RULE_WIDTH_PERCENT}%]\nThis is some text below the horizontal rule.";
			Assert.Equal(expected, Conversions.MarkdownToBBCode(oldText));
		}

		[Fact]
		public void MarkdownToBBCode_BothBoldAndItalics()
		{
			string oldText = @"This is ***bold and italic*** text.";
			string expected = @"This is [b][i]bold and italic[/i][/b] text.";
			Assert.Equal(expected, Conversions.MarkdownToBBCode(oldText));
		}

		[Fact]
		public void MarkdownToBBCode_Lists()
		{
			string oldText = @"Here is a list:
* Item 1
* Item 2
* Item 3


Here is an ordered list:
1. Item 1
2. Item 2
3. Item 3";
			string expected = @"Here is a list:
[ul]
Item 1
Item 2
Item 3
[/ul]


Here is an ordered list:
[ol]
Item 1
Item 2
Item 3
[/ol]";
			Assert.Equal(expected, Conversions.MarkdownToBBCode(oldText));
		}

		[Fact]
		public void MarkdownToBBCode_Complex()
		{
			string oldText = @"```luau
function(dt: number): ()
```
----------
Do stuff to the part:
1. Creates rainbow color animation
2. Oscillates between large and small size


**Parameters**

- `dt` delta time";
			string expected = @"[code]function(dt: number): ()[/code]
[hr width=100%]
Do stuff to the part:
[ol]
Creates rainbow color animation
Oscillates between large and small size
[/ol]


[b]Parameters[/b]

[ul]
[code]dt[/code] delta time
[/ul]";
			Assert.Equal(expected, Conversions.MarkdownToBBCode(oldText));
		}
	}
}
