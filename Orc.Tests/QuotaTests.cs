using Microsoft.Extensions.Options;
using Orc.Core.Configuration;
using Orc.Core.Orchitect;
using Xunit;

namespace Orc.Tests;

public class QuotaTests
{
    private static Quota Build(TempWorkspace ws, int total = 3, int perRepo = 2)
    {
        var opts = Options.Create(new OrchitectOptions
        {
            MaxModificationsPerDay = total,
            MaxModificationsPerRepoPerDay = perRepo,
        });
        return new Quota(ws.Layout, opts);
    }

    [Fact]
    public void Fresh_can_modify()
    {
        using var ws = new TempWorkspace();
        var q = Build(ws);
        Assert.True(q.CanModify("foo"));
        Assert.Equal(0, q.Snapshot().ModificationsToday);
    }

    [Fact]
    public void Per_repo_cap_blocks()
    {
        using var ws = new TempWorkspace();
        var q = Build(ws, total: 10, perRepo: 2);
        q.IncrementModification("foo");
        q.IncrementModification("foo");
        Assert.False(q.CanModify("foo"));
        Assert.True(q.CanModify("bar"));
    }

    [Fact]
    public void Total_cap_blocks()
    {
        using var ws = new TempWorkspace();
        var q = Build(ws, total: 2, perRepo: 5);
        q.IncrementModification("a");
        q.IncrementModification("b");
        Assert.False(q.CanModify("a"));
        Assert.False(q.CanModify("c"));
    }

    [Fact]
    public void Snapshot_reports_counts()
    {
        using var ws = new TempWorkspace();
        var q = Build(ws);
        q.IncrementModification("foo");
        q.IncrementModification("bar");
        q.IncrementModification("foo");

        var snap = q.Snapshot();
        Assert.Equal(3, snap.ModificationsToday);
        Assert.Equal(2, snap.PerRepo["foo"]);
        Assert.Equal(1, snap.PerRepo["bar"]);
    }

    [Fact]
    public void State_persists_across_instances_same_day()
    {
        using var ws = new TempWorkspace();
        var q1 = Build(ws);
        q1.IncrementModification("foo");
        var q2 = Build(ws);
        Assert.Equal(1, q2.Snapshot().ModificationsToday);
        Assert.Equal(1, q2.Snapshot().PerRepo["foo"]);
    }
}
