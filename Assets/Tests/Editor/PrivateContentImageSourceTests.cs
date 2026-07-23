using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using NUnit.Framework;

public class PrivateContentImageSourceTests
{
    private string root;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "universal-gacha-image-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Test]
    public async Task LoadAsync_ReturnsBytesAndVerifiedHash()
    {
        byte[] bytes = { 1, 2, 3, 4, 5 };
        string relativePath = "en/sample/images/card.jpg";
        string fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllBytes(fullPath, bytes);
        string hash;
        using (SHA256 sha256 = SHA256.Create())
            hash = BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();

        ContentImageLoadResult result = await new PrivateContentImageSource(root)
            .LoadAsync(relativePath, hash);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Data, Is.EqualTo(bytes));
        Assert.That(result.Sha256, Is.EqualTo(hash));
    }

    [Test]
    public async Task LoadAsync_RejectsPathTraversal()
    {
        ContentImageLoadResult result = await new PrivateContentImageSource(root)
            .LoadAsync("../outside.jpg");

        Assert.That(result.Status, Is.EqualTo(ContentImageLoadStatus.InvalidPath));
    }

    [Test]
    public async Task LoadAsync_ReportsMissingFile()
    {
        ContentImageLoadResult result = await new PrivateContentImageSource(root)
            .LoadAsync("en/sample/images/missing.jpg");

        Assert.That(result.Status, Is.EqualTo(ContentImageLoadStatus.NotFound));
    }

    [Test]
    public async Task LoadAsync_ReportsIntegrityMismatch()
    {
        string relativePath = "en/sample/images/card.jpg";
        string fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllBytes(fullPath, new byte[] { 9, 8, 7 });

        ContentImageLoadResult result = await new PrivateContentImageSource(root)
            .LoadAsync(relativePath, new string('0', 64));

        Assert.That(result.Status, Is.EqualTo(ContentImageLoadStatus.IntegrityMismatch));
        Assert.That(result.Data, Is.Null);
    }
}
