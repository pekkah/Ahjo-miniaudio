using System.Reflection;

using Xunit;

namespace Ahjo.Miniaudio.Tests;

public class MiniaudioLibraryTests
{
    [Fact]
    public void VersionMatchesPinnedRelease()
    {
        var pinned = typeof(MiniaudioLibraryTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "MiniaudioVersion").Value;

        Assert.Equal(Version.Parse(pinned!), MiniaudioLibrary.Version);
    }
}
