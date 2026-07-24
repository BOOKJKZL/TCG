using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Infrastructure.Content;
using NUnit.Framework;

public class FileSystemContentPackageTransferTests
{
    private sealed class CaptureProgress : IProgress<long>
    {
        public readonly List<long> Values = new List<long>();
        public void Report(long value) => Values.Add(value);
    }

    private sealed class MemoryByteSource : IContentPackageByteSource
    {
        private readonly byte[] data;

        public MemoryByteSource(byte[] data)
        {
            this.data = data;
        }

        public bool InterruptFirstRequest { get; set; }
        public int OpenCalls { get; private set; }
        public readonly List<long> Offsets = new List<long>();

        public Task<Stream> OpenReadAsync(
            ContentPackageDescriptor package,
            long offset,
            CancellationToken cancellationToken)
        {
            OpenCalls++;
            Offsets.Add(offset);
            var remaining = new byte[data.Length - (int)offset];
            Buffer.BlockCopy(data, (int)offset, remaining, 0, remaining.Length);
            if (InterruptFirstRequest && OpenCalls == 1)
                return Task.FromResult<Stream>(new InterruptingStream(remaining, 30));
            return Task.FromResult<Stream>(new MemoryStream(remaining, false));
        }
    }

    private sealed class RejectingByteSource : IContentPackageByteSource
    {
        public int OpenCalls { get; private set; }

        public Task<Stream> OpenReadAsync(
            ContentPackageDescriptor package,
            long offset,
            CancellationToken cancellationToken)
        {
            OpenCalls++;
            throw new InvalidOperationException("source must not be opened");
        }
    }

    private sealed class InterruptingStream : Stream
    {
        private readonly MemoryStream inner;
        private readonly int firstReadBytes;
        private bool firstRead = true;

        public InterruptingStream(byte[] data, int firstReadBytes)
        {
            inner = new MemoryStream(data, false);
            this.firstReadBytes = firstReadBytes;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!firstRead)
                throw new IOException("connection dropped");
            firstRead = false;
            return inner.Read(buffer, offset, Math.Min(count, firstReadBytes));
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(Read(buffer, offset, count));
            }
            catch (Exception exception)
            {
                return Task.FromException<int>(exception);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private string temporaryRoot;
    private string sourceRoot;
    private string downloadRoot;

    [SetUp]
    public void SetUp()
    {
        temporaryRoot = Path.Combine(Path.GetTempPath(), "gacha-transfer-" + Guid.NewGuid().ToString("N"));
        sourceRoot = Path.Combine(temporaryRoot, "source");
        downloadRoot = Path.Combine(temporaryRoot, "downloads");
        Directory.CreateDirectory(sourceRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(temporaryRoot))
            Directory.Delete(temporaryRoot, true);
    }

    [Test]
    public async Task LocalFileSource_FreshDownloadPublishesCompleteArchive()
    {
        byte[] bytes = Data(100);
        File.WriteAllBytes(Path.Combine(sourceRoot, "en.base1.zip"), bytes);
        var transfer = new FileSystemContentPackageTransfer(
            downloadRoot,
            new LocalFileContentPackageByteSource(sourceRoot));
        var task = new ContentPackageDownloadTask(Package(100), transfer);

        ContentDownloadSnapshot result = await task.StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentDownloadState.Completed));
        Assert.That(File.ReadAllBytes(result.ArchivePath), Is.EqualTo(bytes));
        Assert.That(File.Exists(Path.Combine(downloadRoot, "en.base1.part")), Is.False);
        Assert.That(result.ArchivePath, Is.EqualTo(Path.Combine(downloadRoot, "en.base1.zip")));
    }

    [Test]
    public async Task LocalFileSource_ExistingPartialResumesWithoutRewritingPrefix()
    {
        byte[] bytes = Data(100);
        File.WriteAllBytes(Path.Combine(sourceRoot, "en.base1.zip"), bytes);
        Directory.CreateDirectory(downloadRoot);
        File.WriteAllBytes(Path.Combine(downloadRoot, "en.base1.part"), Slice(bytes, 0, 40));
        var transfer = new FileSystemContentPackageTransfer(
            downloadRoot,
            new LocalFileContentPackageByteSource(sourceRoot));

        ContentDownloadSnapshot result = await new ContentPackageDownloadTask(Package(100), transfer).StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentDownloadState.Completed));
        Assert.That(File.ReadAllBytes(result.ArchivePath), Is.EqualTo(bytes));
    }

    [Test]
    public async Task InterruptedSource_RetryContinuesFromPersistedFileOffset()
    {
        byte[] bytes = Data(100);
        var source = new MemoryByteSource(bytes) { InterruptFirstRequest = true };
        var transfer = new FileSystemContentPackageTransfer(downloadRoot, source);
        var task = new ContentPackageDownloadTask(Package(100), transfer);
        int failures = 0;
        task.FailureReported += _ => failures++;

        ContentDownloadSnapshot failed = await task.StartAsync();

        Assert.That(failed.State, Is.EqualTo(ContentDownloadState.Failed));
        Assert.That(failed.DownloadedBytes, Is.EqualTo(30));
        Assert.That(new FileInfo(Path.Combine(downloadRoot, "en.base1.part")).Length, Is.EqualTo(30));

        ContentDownloadSnapshot completed = await task.RetryAsync();

        Assert.That(completed.State, Is.EqualTo(ContentDownloadState.Completed));
        Assert.That(File.ReadAllBytes(completed.ArchivePath), Is.EqualTo(bytes));
        Assert.That(source.Offsets, Is.EqualTo(new[] { 0L, 30L }));
        Assert.That(failures, Is.EqualTo(1));
    }

    [Test]
    public async Task CompletePartialFile_IsPublishedWithoutOpeningSource()
    {
        byte[] bytes = Data(100);
        Directory.CreateDirectory(downloadRoot);
        File.WriteAllBytes(Path.Combine(downloadRoot, "en.base1.part"), bytes);
        var source = new RejectingByteSource();
        var transfer = new FileSystemContentPackageTransfer(downloadRoot, source);

        ContentDownloadSnapshot result = await new ContentPackageDownloadTask(Package(100), transfer).StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentDownloadState.Completed));
        Assert.That(source.OpenCalls, Is.Zero);
        Assert.That(File.ReadAllBytes(result.ArchivePath), Is.EqualTo(bytes));
    }

    [Test]
    public void Download_OffsetDifferentFromPartialLengthIsRejected()
    {
        Directory.CreateDirectory(downloadRoot);
        File.WriteAllBytes(Path.Combine(downloadRoot, "en.base1.part"), Data(10));
        var transfer = new FileSystemContentPackageTransfer(downloadRoot, new RejectingByteSource());

        Assert.ThrowsAsync<IOException>(async () => await transfer.DownloadAsync(
            Package(100),
            5,
            null,
            CancellationToken.None));
        Assert.That(new FileInfo(Path.Combine(downloadRoot, "en.base1.part")).Length, Is.EqualTo(10));
    }

    [Test]
    public async Task Download_SourceLargerThanDeclaredFailsWithoutPublishingArchive()
    {
        var transfer = new FileSystemContentPackageTransfer(downloadRoot, new MemoryByteSource(Data(101)));
        var task = new ContentPackageDownloadTask(Package(100), transfer);

        ContentDownloadSnapshot result = await task.StartAsync();

        Assert.That(result.State, Is.EqualTo(ContentDownloadState.Failed));
        Assert.That(result.ErrorMessage, Does.Contain("more bytes than declared"));
        Assert.That(transfer.GetArchivePath(Package(100)), Is.Null);
    }

    [Test]
    public void DeletePartial_RemovesPartialAndPublishedArchive()
    {
        Directory.CreateDirectory(downloadRoot);
        File.WriteAllBytes(Path.Combine(downloadRoot, "en.base1.part"), Data(5));
        File.WriteAllBytes(Path.Combine(downloadRoot, "en.base1.zip"), Data(100));
        var transfer = new FileSystemContentPackageTransfer(downloadRoot, new RejectingByteSource());

        transfer.DeletePartial(Package(100));

        Assert.That(Directory.Exists(downloadRoot), Is.False);
        Assert.That(transfer.GetDownloadedBytes(Package(100)), Is.Zero);
    }

    private static ContentPackageDescriptor Package(long downloadBytes)
    {
        return new ContentPackageDescriptor(
            "en.base1",
            "en/base1",
            1,
            "1.0.0",
            downloadBytes,
            200,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    }

    private static byte[] Data(int count)
    {
        var bytes = new byte[count];
        for (int index = 0; index < count; index++)
            bytes[index] = (byte)(index % 251);
        return bytes;
    }

    private static byte[] Slice(byte[] source, int offset, int count)
    {
        var result = new byte[count];
        Buffer.BlockCopy(source, offset, result, 0, count);
        return result;
    }
}
