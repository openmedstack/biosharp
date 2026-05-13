namespace OpenMedStack.Preator.Tests;

using Xunit;

public sealed class E2ECommandOptionTests
{
    [Fact]
    public void CreateOptions_MapsDisableSoftclipFlag()
    {
        var parseResult = Program.CreateRootCommand().Parse([
            "e2e",
            "--fastq", "reads.fastq.gz",
            "--reference", "reference.fa.gz",
            "--database", "transcripts.db",
            "--disable-softclip-realign"
        ]);

        Assert.Empty(parseResult.Errors);

        var options = E2ECommand.CreateOptions(parseResult);

        Assert.False(options.EnableSoftClipRealignment);
    }
}
