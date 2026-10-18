using System.Runtime.InteropServices;
using Xunit;

namespace Miningcore.Tests.Util;

/// <summary>
/// A [Fact] that is skipped, with the reason, when the native library it exercises cannot be
/// loaded. The native hashing libraries are built by build-libs-linux.sh and are not part of a
/// plain dotnet build, so on a box without them 36 tests used to fail with DllNotFoundException
/// and hide any real failure in the noise.
/// </summary>
public sealed class NativeLibFactAttribute : FactAttribute
{
    public NativeLibFactAttribute(string library)
    {
        if(!NativeLibrary.TryLoad(library, typeof(Miningcore.Native.Multihash).Assembly, DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.ApplicationDirectory, out _))
            Skip = $"native library '{library}' is not available on this machine (run build-libs-linux.sh)";
    }
}
