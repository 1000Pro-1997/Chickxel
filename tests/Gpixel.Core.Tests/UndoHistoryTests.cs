using System.IO.Compression;
using System.Security.Cryptography;
using Gpixel.Core.Edit;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.Core.Tests;

public class UndoHistoryTests
{
    /// <summary>프로젝트 파일 내용(project.json + 셀 PNG) 지문 — 같으면 저장 결과가 같다.</summary>
    private static string Fingerprint(SpriteProject p)
    {
        using var ms = new MemoryStream();
        ProjectIO.Save(p, ms);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var e in zip.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            sha.AppendData(System.Text.Encoding.UTF8.GetBytes(e.FullName));
            using var s = e.Open();
            using var buf = new MemoryStream();
            s.CopyTo(buf);
            sha.AppendData(buf.ToArray());
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    private static void Draw(SpriteProject p, int layer, int frame, int x, int y, Color32 color)
    {
        var img = p.BeginCelStroke(layer, frame);
        img.Pixels[y * img.Width + x] = color;
        Assert.True(p.CommitCelStroke("연필"));
    }

    /// <summary>지금 상태에서 끝까지 실행취소한 상태들, 다시 끝까지 다시 실행한 상태들(라벨 포함). 끝나면 처음 위치로 돌아온다.</summary>
    private static List<(string State, string? Label)> Walk(SpriteProject p)
    {
        int undo = p.History.UndoCount, redo = p.History.RedoCount;
        var states = new List<(string, string?)> { (Fingerprint(p), p.History.UndoLabel) };
        while (p.History.CanUndo) { p.Undo(); states.Add((Fingerprint(p), p.History.UndoLabel)); }
        while (p.History.CanRedo) { p.Redo(); states.Add((Fingerprint(p), p.History.RedoLabel)); }
        for (int i = 0; i < redo; i++) p.Undo();
        Assert.Equal(undo, p.History.UndoCount);
        return states;
    }

    /// <summary>스트로크·레이어 추가·부위·스킨·효과·프레임·태그·레이어 삭제가 섞인 프로젝트(마지막 두 단계는 실행취소해 다시 실행 목록에 둠).</summary>
    private static SpriteProject BuildMixed()
    {
        var p = new SpriteProject(16, 16);
        Draw(p, 0, 0, 1, 1, Color32.Black);
        var top = p.AddLayer(name: "위");
        Draw(p, p.IndexOf(top), 0, 2, 2, new Color32(200, 10, 10));
        var group = p.AddGroup("몸");
        var head = p.AddPart(group, "머리", 8, 8);
        Draw(p, p.IndexOf(head), 0, 3, 3, new Color32(10, 200, 10));
        var alt = p.AddSkin(group, "갑옷");
        p.SetActiveSkin(group, alt);
        Draw(p, p.IndexOf(head), 0, 4, 4, new Color32(10, 10, 200));
        p.SetLayerEffects(0, [new OutlineEffect { Color = new Color32(255, 0, 255), Thickness = 2 }]);
        p.SaveOutfit("전투");
        p.AddFrame();
        Draw(p, 0, 1, 5, 5, Color32.White);
        p.AddTag("걷기", 0, 1);
        p.DeleteLayer(p.IndexOf(top));
        p.SetFrameDuration(1, 250);
        Draw(p, 0, 1, 6, 6, Color32.Black);
        p.Undo();
        p.Undo();
        return p;
    }

    [Fact]
    public void RoundTrip_ReproducesEveryUndoAndRedoState()
    {
        using var tmp = new TempDir();
        var file = tmp.File("a.gpixel");
        var dir = tmp.File("history");
        var p = BuildMixed();
        ProjectIO.Save(p, file);
        var written = UndoHistoryIO.Write(UndoHistoryIO.Capture(p, 1000)!, dir);
        Assert.Equal(p.History.UndoCount, written.UndoSteps);
        Assert.Equal(2, written.RedoSteps);
        var expected = Walk(p);

        var q = ProjectIO.Load(file);
        Assert.Equal(p.History.UndoCount + 2, UndoHistoryIO.Read(q, dir));
        Assert.Equal(p.History.UndoLabel, q.History.UndoLabel);
        Assert.Equal("프레임 길이", q.History.RedoLabel);
        Assert.Equal(2, q.History.RedoCount);
        Assert.Equal(expected, Walk(q));

        // 읽은 기록 위에 새로 편집해도 실행취소가 이어진다(새 편집이 다시 실행 목록을 지움).
        var before = Fingerprint(q);
        Draw(q, 0, 0, 7, 7, Color32.White);
        Assert.Equal(0, q.History.RedoCount);
        q.Undo();
        Assert.Equal(before, Fingerprint(q));
    }

    [Fact]
    public void Rewrite_AfterLoad_KeepsHistoryAndReusesBlobs()
    {
        using var tmp = new TempDir();
        var file = tmp.File("a.gpixel");
        var dir = tmp.File("history");
        var p = BuildMixed();
        ProjectIO.Save(p, file);
        UndoHistoryIO.Write(UndoHistoryIO.Capture(p, 1000)!, dir);
        var expected = Walk(p);

        // 다시 열고 그대로 다시 쓰기(조각은 이미 있음) → 또 열어도 같은 기록.
        var q = ProjectIO.Load(file);
        UndoHistoryIO.Read(q, dir);
        var blobsBefore = Directory.GetFiles(Path.Combine(dir, UndoHistoryIO.BlobFolder)).Select(Path.GetFileName).Order().ToList();
        UndoHistoryIO.Write(UndoHistoryIO.Capture(q, 1000)!, dir);
        var blobsAfter = Directory.GetFiles(Path.Combine(dir, UndoHistoryIO.BlobFolder)).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(blobsBefore, blobsAfter);
        var r = ProjectIO.Load(file);
        UndoHistoryIO.Read(r, dir);
        Assert.Equal(expected, Walk(r));
    }

    [Fact]
    public void ManyStructuralSteps_ShareCelBlobs()
    {
        using var tmp = new TempDir();
        var p = new SpriteProject(64, 64);
        var noise = p.AddLayer(name: "잡음");
        p.SetCel(p.IndexOf(noise), 0, TestUtil.RandomImage(64, 64, seed: 7));
        for (int i = 0; i < 200; i++) p.SetLayerProperties(p.IndexOf(noise), visible: i % 2 == 1);
        // 마지막에 레이어를 지워 그 그림이 지금 프로젝트(파일)에 없게 한다 → 기록에 그림 내용이 들어가야 한다.
        p.DeleteLayer(p.IndexOf(noise));
        var file = tmp.File("a.gpixel");
        ProjectIO.Save(p, file);
        var dir = tmp.File("history");
        var result = UndoHistoryIO.Write(UndoHistoryIO.Capture(p, 1000)!, dir);

        int imageBytes = 64 * 64 * 4;
        Assert.True(result.Bytes < imageBytes * 2, $"기록 크기 {result.Bytes}B — 그림({imageBytes}B)이 단계마다 복사된 것 같다");
        Assert.True(Directory.GetFiles(Path.Combine(dir, UndoHistoryIO.BlobFolder)).Length < 10);

        var q = ProjectIO.Load(file);
        Assert.Equal(p.History.UndoCount, UndoHistoryIO.Read(q, dir));
        q.Undo();
        Assert.Equal(TestUtil.RandomImage(64, 64, seed: 7).Pixels, q.GetCel(q.Layers.Count - 1, 0)!.Pixels);
    }

    [Fact]
    public void Load_TrimsOldestUndoStepsToCapacity()
    {
        using var tmp = new TempDir();
        var p = new SpriteProject(8, 8);
        for (int i = 0; i < 8; i++) Draw(p, 0, 0, i, 0, Color32.Black);
        var file = tmp.File("a.gpixel");
        ProjectIO.Save(p, file);
        var dir = tmp.File("history");
        UndoHistoryIO.Write(UndoHistoryIO.Capture(p, 1000)!, dir);
        var expected = Walk(p).Take(4).ToList(); // 지금 + 3번 실행취소

        var q = ProjectIO.Load(file);
        q.History.Capacity = 3;
        Assert.Equal(3, UndoHistoryIO.Read(q, dir));
        Assert.Equal(3, q.History.UndoCount);
        var states = new List<string> { Fingerprint(q) };
        while (q.Undo()) states.Add(Fingerprint(q));
        Assert.Equal(expected.Select(e => e.State), states);
    }

    [Fact]
    public void Capture_LimitsStepsPerFile()
    {
        var p = new SpriteProject(8, 8);
        for (int i = 0; i < 6; i++) Draw(p, 0, 0, i, 0, Color32.Black);
        for (int i = 0; i < 3; i++) p.Undo();
        var c = UndoHistoryIO.Capture(p, 2)!;
        Assert.Equal(2, c.UndoCount);
        Assert.Equal(2, c.RedoCount);
    }

    [Fact]
    public void Read_ProjectThatDoesNotMatch_ThrowsAndLeavesProjectAlone()
    {
        using var tmp = new TempDir();
        var p = BuildMixed();
        var dir = tmp.File("history");
        UndoHistoryIO.Write(UndoHistoryIO.Capture(p, 1000)!, dir);
        var other = new SpriteProject(16, 16);
        Draw(other, 0, 0, 0, 0, Color32.White);
        var before = Fingerprint(other);
        Assert.Throws<InvalidDataException>(() => UndoHistoryIO.Read(other, dir));
        Assert.Equal(before, Fingerprint(other));
        Assert.Equal(1, other.History.UndoCount);
    }

    [Fact]
    public void Store_DiscardsHistoryWhenFileChangedElsewhere_AndFollowsRename()
    {
        using var tmp = new TempDir();
        var store = new UndoHistoryStore(tmp.File("store"));
        var file = tmp.File("a.gpixel");
        var p = new SpriteProject(8, 8);
        Draw(p, 0, 0, 1, 1, Color32.Black);
        ProjectIO.Save(p, file);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.NotNull(store.Write(UndoHistoryIO.Capture(p, 1000)!, file, FileStamp.Of(file)!.Value, now));

        var renamed = tmp.File("b.gpixel");
        File.Move(file, renamed);
        Assert.True(store.Move(file, renamed));
        Assert.Null(store.Find(file));
        var q = ProjectIO.Load(renamed);
        Assert.Equal(new UndoHistoryLoadResult(UndoHistoryLoadStatus.Loaded, 1), store.Load(q, now));
        Assert.Equal("연필", q.History.UndoLabel);

        // 밖에서 바뀐 파일: 기록을 버린다.
        File.SetLastWriteTimeUtc(renamed, File.GetLastWriteTimeUtc(renamed).AddMinutes(1));
        var r = ProjectIO.Load(renamed);
        Assert.Equal(UndoHistoryLoadStatus.Stale, store.Load(r, now).Status);
        Assert.Equal(0, r.History.UndoCount);
        Assert.Null(store.Find(renamed));
    }

    [Fact]
    public void CleanupPolicy_PurgesMissingChangedOldAndLeastRecentlyUsedOverCap()
    {
        var now = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var stamp = new FileStamp(100, 10);
        const long mb = 1024 * 1024;
        UndoHistoryEntry E(string name, long bytes, int daysAgo, FileStamp? s = null)
            => new(name, "/p/" + name, s ?? stamp, 5, bytes, now.AddDays(-daysAgo));
        var missing = E("missing", mb, 0);
        var changed = E("changed", mb, 0, new FileStamp(99, 10));
        var old = E("old", mb, 31);
        var lru1 = E("lru1", 200 * mb, 20);
        var lru2 = E("lru2", 200 * mb, 10);
        var recent = E("recent", 200 * mb, 1);
        var entries = new[] { missing, changed, old, lru1, lru2, recent };
        FileStamp? StampOf(string path) => path.EndsWith("missing") ? null : stamp;

        var purged = UndoHistoryStore.SelectPurge(entries, new UndoHistoryPolicy(30, 500), now, StampOf);
        // 600MB > 500MB → 가장 오래 안 쓴 lru1(200MB)만 지우면 400MB.
        Assert.Equal(new[] { "changed", "lru1", "missing", "old" }, purged.Select(e => e.Key).Order());

        // 제한 없음(0): 없어지거나 바뀐 파일만.
        var unlimited = UndoHistoryStore.SelectPurge(entries, new UndoHistoryPolicy(0, 0), now, StampOf);
        Assert.Equal(new[] { "changed", "missing" }, unlimited.Select(e => e.Key).Order());
    }

    [Fact]
    public void StoreCleanup_RemovesEntriesOfDeletedFilesAndBrokenFolders()
    {
        using var tmp = new TempDir();
        var store = new UndoHistoryStore(tmp.File("store"));
        var now = DateTime.UtcNow;
        var keep = tmp.File("keep.gpixel");
        var gone = tmp.File("gone.gpixel");
        foreach (var f in new[] { keep, gone })
        {
            var p = new SpriteProject(4, 4);
            Draw(p, 0, 0, 0, 0, Color32.Black);
            ProjectIO.Save(p, f);
            store.Write(UndoHistoryIO.Capture(p, 1000)!, f, FileStamp.Of(f)!.Value, now);
        }
        Directory.CreateDirectory(Path.Combine(store.Root, "broken"));
        File.Delete(gone);

        Assert.Equal(2, store.Cleanup(UndoHistoryPolicy.Default, now));
        Assert.Equal([Path.GetFullPath(keep)], store.Entries().Select(e => e.FilePath));
        Assert.Equal(1, store.Usage().Files);
    }
}
