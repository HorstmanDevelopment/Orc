using Orc.Core.Repos;
using Xunit;

namespace Orc.Tests;

public class RepoRegistryTests
{
    private static RepoRegistry Build(TempWorkspace ws) => new(ws.Layout);

    [Fact]
    public async Task Add_then_All_returns_one()
    {
        using var ws = new TempWorkspace();
        var reg = Build(ws);
        await reg.AddAsync("https://example.com/org/foo.git", "main", CancellationToken.None);
        var all = reg.All();
        Assert.Single(all);
        Assert.Equal("foo", all[0].Name);
        Assert.Equal("main", all[0].BaseBranch);
        Assert.EndsWith(Path.Combine("repos", "foo"), all[0].LocalPath);
    }

    [Fact]
    public async Task Add_dedupes_url()
    {
        using var ws = new TempWorkspace();
        var reg = Build(ws);
        await reg.AddAsync("https://example.com/org/foo.git", "main", CancellationToken.None);
        await reg.AddAsync("https://example.com/org/foo.git", "develop", CancellationToken.None);
        Assert.Single(reg.All());
    }

    [Fact]
    public async Task TryResolve_all_returns_all()
    {
        using var ws = new TempWorkspace();
        var reg = Build(ws);
        await reg.AddAsync("https://example.com/a/x.git", "main", CancellationToken.None);
        await reg.AddAsync("https://example.com/a/y.git", "main", CancellationToken.None);
        Assert.True(reg.TryResolve("all", out var repos, out _));
        Assert.Equal(2, repos.Count);
    }

    [Fact]
    public async Task TryResolve_csv_returns_subset()
    {
        using var ws = new TempWorkspace();
        var reg = Build(ws);
        await reg.AddAsync("https://example.com/a/x.git", "main", CancellationToken.None);
        await reg.AddAsync("https://example.com/a/y.git", "main", CancellationToken.None);
        Assert.True(reg.TryResolve("x", out var repos, out _));
        Assert.Single(repos);
        Assert.Equal("x", repos[0].Name);
    }

    [Fact]
    public async Task TryResolve_unknown_fails_with_error()
    {
        using var ws = new TempWorkspace();
        var reg = Build(ws);
        await reg.AddAsync("https://example.com/a/x.git", "main", CancellationToken.None);
        Assert.False(reg.TryResolve("x,nope", out _, out var err));
        Assert.NotNull(err);
        Assert.Contains("nope", err);
    }

    [Fact]
    public void All_empty_when_no_source()
    {
        using var ws = new TempWorkspace();
        var reg = Build(ws);
        Assert.Empty(reg.All());
    }

    [Fact]
    public async Task Reads_json_with_comments()
    {
        using var ws = new TempWorkspace();
        var path = ws.Layout.ReposJsonPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """
            // top-level comment
            [
              { "url": "https://example.com/a/x.git", "branch": "main" }, // inline
              { "url": "https://example.com/a/y.git", "branch": "develop" }
            ]
            """);
        var reg = Build(ws);
        var all = reg.All();
        Assert.Equal(2, all.Count);
    }
}
