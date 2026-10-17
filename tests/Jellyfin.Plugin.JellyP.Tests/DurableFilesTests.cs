using Jellyfin.Plugin.JellyP.Infrastructure;
using Xunit;

namespace Jellyfin.Plugin.JellyP.Tests;

public sealed class DurableFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jellyp-durable-" + Guid.NewGuid().ToString("N"));

    public DurableFilesTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void CreateWriteReplaceAndDeleteFlushRealDirectories()
    {
        // Exercise native open/fsync on the executing architecture, not copied numeric flags.
        var directory = Path.Combine(_root, "migration", "journal");
        DurableFiles.CreateDirectory(directory);
        var file = Path.Combine(directory, "record.json");
        DurableFiles.Write(file, [1, 2, 3]);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(file));
        DurableFiles.Write(file, [4, 5]);
        Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(file));
        Assert.False(File.Exists(file + ".tmp"));
        DurableFiles.FlushParentDirectory(file);
        DurableFiles.Delete(file);
        Assert.False(File.Exists(file));
        DurableFiles.Delete(file); // Repeated retirement is harmless.
    }

    [Fact]
    public void LinuxDirectoryFlushRejectsSymbolicLinkParent()
    {
        if (!OperatingSystem.IsLinux()) return;
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        var link = Path.Combine(_root, "link");
        Directory.CreateSymbolicLink(link, target);
        Assert.Throws<IOException>(() => DurableFiles.FlushParentDirectory(Path.Combine(link, "record.json")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }

    [Fact]
    public void MissingCanonicalDestinationIsCheckedWithoutCreatingIt()
    {
        var destination = Path.Combine(_root, "new", "library", "item", "folder.webp");
        DurableFiles.EnsureSafeParent(destination);
        Assert.False(Directory.Exists(Path.GetDirectoryName(destination)));
        var blocker = Path.Combine(_root, "not-a-directory");
        File.WriteAllText(blocker, "original");
        Assert.Throws<IOException>(() => DurableFiles.EnsureSafeParent(Path.Combine(blocker, "nested", "folder.webp")));
        Assert.Equal("original", File.ReadAllText(blocker));
    }

    [Fact]
    public void MissingDestinationBelowSymlinkIsRejected()
    {
        if (!OperatingSystem.IsLinux()) return;
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        var link = Path.Combine(_root, "link");
        Directory.CreateSymbolicLink(link, target);
        Assert.Throws<IOException>(() => DurableFiles.EnsureSafeParent(Path.Combine(link, "missing", "folder.webp")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
