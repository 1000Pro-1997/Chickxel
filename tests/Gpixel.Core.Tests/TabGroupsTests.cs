using Gpixel.Core.Edit;

namespace Gpixel.Core.Tests;

/// <summary>편집기 탭 묶음(순서·고른 탭·나눔·다시 열기 모양).</summary>
public class TabGroupsTests
{
    private sealed class Tab(string? path)
    {
        public string? Path { get; } = path;
        public override string ToString() => Path ?? "(new)";
    }

    private static (TabGroups<Tab> Groups, Tab A, Tab B, Tab C) ThreeTabs()
    {
        var g = new TabGroups<Tab>();
        Tab a = new("a"), b = new("b"), c = new("c");
        g.Add(a, 0);
        g.Add(b, 0);
        g.Add(c, 0);
        return (g, a, b, c);
    }

    [Fact]
    public void Add_InsertsAfterActiveTabAndSelectsIt()
    {
        var (g, a, b, c) = ThreeTabs();
        Assert.Equal([a, b, c], g.Tabs(0));
        g.Activate(a);
        var d = new Tab("d");
        g.Add(d, 0);
        Assert.Equal([a, d, b, c], g.Tabs(0));
        Assert.Same(d, g.FocusedActive);
        // 이미 열린 탭을 다시 넣으면 고르기만 한다.
        g.Add(c, 0);
        Assert.Equal(4, g.Tabs(0).Count);
        Assert.Same(c, g.FocusedActive);
    }

    [Fact]
    public void Remove_SelectsRightNeighbourThenLeft()
    {
        var (g, a, b, c) = ThreeTabs();
        g.Activate(b);
        g.Remove(b);
        Assert.Same(c, g.FocusedActive);
        g.Remove(c);
        Assert.Same(a, g.FocusedActive);
        g.Remove(a);
        Assert.Null(g.FocusedActive);
        Assert.True(g.IsEmpty);
        Assert.Equal(-1, g.ActiveIndex(0));
    }

    [Fact]
    public void Remove_InactiveTabKeepsSelection()
    {
        var (g, a, b, c) = ThreeTabs();
        g.Activate(c);
        g.Remove(a);
        Assert.Same(c, g.FocusedActive);
        Assert.Equal(1, g.ActiveIndex(0));
        g.Remove(b);
        Assert.Same(c, g.FocusedActive);
    }

    [Fact]
    public void Move_ReordersAndActiveFollows()
    {
        var (g, a, b, c) = ThreeTabs();
        g.Activate(a);
        g.Move(0, 0, 2);
        Assert.Equal([b, c, a], g.Tabs(0));
        Assert.Same(a, g.FocusedActive);
        Assert.Equal(2, g.ActiveIndex(0));
        g.Move(0, 2, -5);
        Assert.Equal([a, b, c], g.Tabs(0));
    }

    [Fact]
    public void Cycle_WrapsAround()
    {
        var (g, a, b, c) = ThreeTabs();
        Assert.Same(a, g.Cycle(1));
        Assert.Same(c, g.Cycle(-1));
        Assert.Same(b, g.Cycle(-1));
        Assert.Null(new TabGroups<Tab>().Cycle(1));
    }

    [Fact]
    public void Split_NewGroupFocusesAndClosingLastTabUnsplits()
    {
        var (g, a, b, _) = ThreeTabs();
        var d = new Tab("d");
        Assert.Equal(1, g.OtherGroup(0));
        g.Add(d, g.OtherGroup(0));
        Assert.True(g.IsSplit);
        Assert.Equal(1, g.Focused);
        Assert.Same(d, g.FocusedActive);
        Assert.Equal(0, g.OtherGroup(1));

        g.Focus(0);
        g.Activate(b);
        Assert.Equal(0, g.Focused);
        Assert.Same(d, g.Active(1));

        Assert.True(g.Remove(d));
        Assert.False(g.IsSplit);
        Assert.Equal(0, g.Focused);
        Assert.Same(b, g.FocusedActive);
        Assert.Contains(a, g.All);
    }

    [Fact]
    public void Remove_LeftGroupEmptyMovesRightGroupToFront()
    {
        var g = new TabGroups<Tab>();
        Tab a = new("a"), b = new("b"), c = new("c");
        g.Add(a, 0);
        g.Add(b, 1);
        g.Add(c, 1);
        Assert.True(g.Remove(a));
        Assert.Equal(1, g.GroupCount);
        Assert.Equal([b, c], g.Tabs(0));
        Assert.Same(c, g.FocusedActive);
    }

    [Fact]
    public void MoveToGroup_SplitsAndUnsplitsWhenSourceEmpties()
    {
        var (g, a, b, c) = ThreeTabs();
        // 하나뿐인 묶음의 마지막 탭은 새 묶음으로 옮길 수 없다.
        var lone = new TabGroups<Tab>();
        lone.Add(a, 0);
        Assert.False(lone.MoveToGroup(a, 1));

        Assert.True(g.MoveToGroup(c, 1));
        Assert.True(g.IsSplit);
        Assert.Equal([a, b], g.Tabs(0));
        Assert.Equal([c], g.Tabs(1));
        Assert.Same(c, g.FocusedActive);
        Assert.False(g.MoveToGroup(c, 1));

        Assert.True(g.MoveToGroup(c, 0));
        Assert.False(g.IsSplit);
        Assert.Equal([a, b, c], g.Tabs(0));
        Assert.Same(c, g.FocusedActive);

        // 왼쪽 묶음의 마지막 탭을 오른쪽으로 옮기면 오른쪽 묶음이 하나 남는다.
        var h = new TabGroups<Tab>();
        h.Add(a, 0);
        h.Add(b, 1);
        Assert.True(h.MoveToGroup(a, 1));
        Assert.False(h.IsSplit);
        Assert.Equal([b, a], h.Tabs(0));
        Assert.Same(a, h.FocusedActive);
    }

    [Fact]
    public void ToSession_SkipsUntitledTabsAndKeepsSelection()
    {
        var g = new TabGroups<Tab>();
        Tab a = new("a"), untitled = new(null), b = new("b"), c = new("c");
        g.Add(a, 0);
        g.Add(untitled, 0);
        g.Add(b, 1);
        g.Add(c, 1);
        g.Activate(b);
        g.Activate(untitled);
        var s = g.ToSession(t => t.Path)!;
        Assert.Equal(2, s.Groups.Count);
        Assert.Equal(["a"], s.Groups[0]);
        Assert.Equal(["b", "c"], s.Groups[1]);
        Assert.Equal([0, 0], s.Active);
        Assert.Equal(0, s.Focused);

        var only = new TabGroups<Tab>();
        only.Add(untitled, 0);
        Assert.Null(only.ToSession(t => t.Path));

        // 새 문서만 있는 왼쪽 묶음은 빠지고 포커스는 남은 묶음 번호로.
        var right = new TabGroups<Tab>();
        right.Add(untitled, 0);
        right.Add(b, 1);
        var rs = right.ToSession(t => t.Path)!;
        Assert.Equal(["b"], Assert.Single(rs.Groups));
        Assert.Equal(0, rs.Focused);
    }

    [Fact]
    public void Session_NormalizedClampsAndDropsEmpty()
    {
        var s = new TabSession
        {
            Groups = [[], ["x", "", "X", "y"], ["z"], ["w"]],
            Active = [3, 9],
            Focused = 7,
        }.Normalized();
        Assert.Equal(2, s.Groups.Count);
        Assert.Equal(["x", "y"], s.Groups[0]);
        Assert.Equal(["z"], s.Groups[1]);
        Assert.Equal([1, 0], s.Active);
        Assert.Equal(0, s.Focused);

        var focused = new TabSession { Groups = [["a"], ["b"]], Active = [0, 0], Focused = 1 }.Normalized();
        Assert.Equal(1, focused.Focused);
        Assert.Empty(new TabSession().Normalized().Groups);
    }
}
