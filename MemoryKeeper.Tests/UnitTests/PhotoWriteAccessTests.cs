using MemoryKeeper.Application;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class PhotoWriteAccessTests
{
    [Theory]
    [InlineData("favorite")]
    [InlineData("memo")]
    [InlineData("place")]
    [InlineData("tag")]
    [InlineData("delete")]
    public async Task BackendOnly_Photo_Does_Not_Invoke_Local_Write(string operation)
    {
        var localWriteCalls = 0;

        var executed = await PhotoWriteAccess.TryExecuteLocalAsync(
            isBackendOnly: true,
            () =>
            {
                localWriteCalls++;
                return Task.CompletedTask;
            });

        Assert.False(executed);
        Assert.Equal(0, localWriteCalls);
        Assert.False(string.IsNullOrWhiteSpace(operation));
    }

    [Fact]
    public async Task Local_Photo_Keeps_Existing_Write_Path()
    {
        var localWriteCalls = 0;

        var executed = await PhotoWriteAccess.TryExecuteLocalAsync(
            isBackendOnly: false,
            () =>
            {
                localWriteCalls++;
                return Task.CompletedTask;
            });

        Assert.True(executed);
        Assert.Equal(1, localWriteCalls);
    }
}
