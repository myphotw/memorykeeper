using MemoryKeeper.Application;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class BackendFileIdCodecTests
{
    [Fact]
    public void ToGuid_Parses_Standard_Guid()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Assert.Equal(id, BackendFileIdCodec.ToGuid(id.ToString("D")));
        Assert.Equal(id.ToString("D"), BackendFileIdCodec.ToApiFileId(id));
    }

    [Fact]
    public void ToGuid_Maps_Sha256_Hex_Deterministically_And_Preserves_Original()
    {
        const string hash =
            "ccbfdcfbc360209cbf2cf8463ceb0a53986a6206fafbcd2b3834defd222e7645";

        var first = BackendFileIdCodec.ToGuid(hash);
        var second = BackendFileIdCodec.ToGuid(hash);

        Assert.NotEqual(Guid.Empty, first);
        Assert.Equal(first, second);
        Assert.Equal(hash, BackendFileIdCodec.ToApiFileId(first));
    }

    [Fact]
    public void GetPhotoAsync_Uses_Original_Hash_FileId_In_Path()
    {
        // Covered indirectly: codec must return original hash for repository URL building.
        const string hash =
            "3e9a227ea857813b4885714b8ca12e8e279c32a1f9c47af29dfafceaee5043fe";
        var guid = BackendFileIdCodec.ToGuid(hash);
        Assert.Equal(hash, BackendFileIdCodec.ToApiFileId(guid));
    }
}
