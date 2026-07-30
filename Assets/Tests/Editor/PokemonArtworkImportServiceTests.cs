using NUnit.Framework;

public sealed class PokemonArtworkImportServiceTests
{
    [Test]
    public void PngValidation_RequiresFullSignature()
    {
        Assert.That(PokemonArtworkImportService.IsPng(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), Is.True);
        Assert.That(PokemonArtworkImportService.IsPng(new byte[] { 137, 80, 78, 71 }), Is.False);
        Assert.That(PokemonArtworkImportService.IsPng(new byte[] { 0, 80, 78, 71, 13, 10, 26, 10 }), Is.False);
    }

    [Test]
    public void PortableFormName_RemovesArchiveUnsafeColon()
    {
        Assert.That(PokemonArtworkImportService.PortableFormName("pokemon-form:10091"),
            Is.EqualTo("pokemon-form-10091"));
    }
}
