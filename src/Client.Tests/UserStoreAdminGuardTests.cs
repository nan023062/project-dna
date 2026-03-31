using Dna.Auth;
using Xunit;

namespace Client.Tests;

public sealed class UserStoreAdminGuardTests
{
    [Fact]
    public void UpdateRole_ShouldRejectDowngradingLastAdmin()
    {
        using var fixture = new UserStoreFixture();

        var admin = fixture.Store.GetByUsername("admin");
        Assert.NotNull(admin);

        var result = fixture.Store.UpdateRole(admin!.Id, ServerRoles.Editor);

        Assert.False(result.Success);
        Assert.Contains("至少需要保留一个管理员账号", result.Message);
    }

    [Fact]
    public void DeleteUser_ShouldRejectDeletingLastAdmin()
    {
        using var fixture = new UserStoreFixture();

        var admin = fixture.Store.GetByUsername("admin");
        Assert.NotNull(admin);

        var result = fixture.Store.DeleteUser(admin!.Id);

        Assert.False(result.Success);
        Assert.Contains("至少需要保留一个管理员账号", result.Message);
    }

    [Fact]
    public void ResetPassword_ShouldAllowAuthenticatingWithNewPassword()
    {
        using var fixture = new UserStoreFixture();
        var create = fixture.Store.Register("alice", "oldpass", ServerRoles.Editor);
        Assert.True(create.Success);

        var reset = fixture.Store.ResetPassword(create.User!.Id, "newpass");
        Assert.True(reset.Success);

        var auth = fixture.Store.Authenticate("alice", "newpass");
        Assert.True(auth.Success);
        Assert.Equal("alice", auth.User?.Username);
    }

    private sealed class UserStoreFixture : IDisposable
    {
        public UserStoreFixture()
        {
            DataDir = Path.Combine(Path.GetTempPath(), "dna-userstore-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDir);
            Store = new UserStore();
            Store.Initialize(DataDir);
        }

        public string DataDir { get; }

        public UserStore Store { get; }

        public void Dispose()
        {
            Store.Dispose();
            if (Directory.Exists(DataDir))
            {
                Directory.Delete(DataDir, recursive: true);
            }
        }
    }
}
