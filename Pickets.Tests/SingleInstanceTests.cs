namespace Pickets.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public void RecoveryTimeout_DoesNotClaimOwnership()
    {
        var name = "Pickets.Tests." + Guid.NewGuid().ToString("N");
        using var owner = new SingleInstance(name);
        bool? acquired = null;
        var thread = new Thread(() =>
        {
            using var recovery = new SingleInstance(name);
            acquired = recovery.WaitForOwnerExit(TimeSpan.FromMilliseconds(50));
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.False(acquired);
    }

    [Fact]
    public void RecoveryHandoff_KeepsMutexUntilRecoveryDisposes()
    {
        var name = "Pickets.Tests." + Guid.NewGuid().ToString("N");
        using var ownerReady = new ManualResetEventSlim();
        using var releaseOwner = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            using var owner = new SingleInstance(name);
            ownerReady.Set();
            releaseOwner.Wait(TimeSpan.FromSeconds(5));
        });
        thread.Start();
        Assert.True(ownerReady.Wait(TimeSpan.FromSeconds(5)));
        using (var recovery = new SingleInstance(name))
        {
            Assert.False(recovery.IsFirstInstance);
            releaseOwner.Set();
            Assert.True(recovery.WaitForOwnerExit(TimeSpan.FromSeconds(5)));
            using var otherLaunch = new SingleInstance(name);
            Assert.False(otherLaunch.IsFirstInstance);
        }
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        using var nextLaunch = new SingleInstance(name);
        Assert.True(nextLaunch.IsFirstInstance);
    }
}
