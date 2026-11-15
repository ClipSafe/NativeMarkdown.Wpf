using System.Text;
using System.Windows.Controls;
using System.Windows.Documents;

namespace NativeMarkdown.Wpf.Tests;

public class EmojiTextTests
{
    [Fact]
    public void Renders_emoji_as_art_and_keeps_the_character()
    {
        StaThread.Run(() =>
        {
            var block = new TextBlock { FontSize = 16 };
            EmojiText.SetSource(block, "Ship it \U0001F680");

            Assert.Single(block.Inlines.OfType<InlineUIContainer>());
            Assert.Equal("Ship it \U0001F680", GetText(block));
        });
    }

    [Fact]
    public void Uses_native_text_when_there_is_no_emoji()
    {
        StaThread.Run(() =>
        {
            var block = new TextBlock();
            EmojiText.SetSource(block, "Plain title");

            // Native Text keeps CharacterEllipsis trimming exact, so it is preferred when it suffices.
            Assert.Equal("Plain title", block.Text);
            Assert.Empty(block.Inlines.OfType<InlineUIContainer>());
        });
    }

    [Fact]
    public void Can_be_disabled()
    {
        StaThread.Run(() =>
        {
            var block = new TextBlock();
            EmojiText.SetIsEnabled(block, false);
            EmojiText.SetSource(block, "Ship it \U0001F680");

            Assert.Equal("Ship it \U0001F680", block.Text);
            Assert.Empty(block.Inlines.OfType<InlineUIContainer>());
        });
    }

    private static string GetText(TextBlock block)
    {
        var builder = new StringBuilder();
        foreach (var inline in block.Inlines)
        {
            if (inline is Run run)
                builder.Append(run.Text);
        }

        return builder.ToString();
    }
}
