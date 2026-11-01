using Chickxel.Core.Assets;

namespace CatalogBuilder;

/// <summary>
/// <c>catalog/</c>(제출 패키지 + review.json)를 검사해 <c>docs/catalog/</c>(쪽 index JSON, 패키지, 미리보기)를 만든다. 먼저 공식 효과 패키지
/// (<see cref="OfficialEffectCatalog"/>)의 지금 버전이 <c>catalog/</c>에 없으면 만들어 두고, 공식 원문 해시 목록(<c>src/Chickxel.Core/Scripting/Official/hashes.txt</c>)에
/// 지금 원문과 카탈로그 공식 패키지의 해시를 더한다.
/// <c>--check</c>는 쓰지 않고, 검사 실패나 커밋한 결과가 최신이 아니면(해시 목록에 빠진 해시가 있어도) 1로 끝난다(CI).
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        bool check = args.Contains("--check");
        if (args.Any(arg => arg != "--check"))
        {
            Console.Error.WriteLine("usage: dotnet run --project tools/CatalogBuilder -- [--check]");
            return 2;
        }
        string root = RepoRoot();
        string source = Path.Combine(root, "catalog");
        string output = Path.Combine(root, "docs", "catalog");

        var (problems, writes) = OfficialEffectCatalog.Plan(source);
        foreach (var problem in problems) Console.Error.WriteLine("catalog/" + problem);
        if (problems.Count > 0)
        {
            Console.Error.WriteLine($"{problems.Count} official effect package(s) out of date.");
            return 1;
        }

        // 공식 원문 해시 목록: 예전 버전 패키지를 지우기 전에 그 해시까지 모은다
        string shippedPath = Path.Combine(root, "src", "Chickxel.Core", "Scripting", "Official", Chickxel.Core.Scripting.OfficialEffects.ShippedFileName);
        var (listed, missing) = OfficialEffectCatalog.PlanShipped(shippedPath, [source, Path.Combine(output, "packages")]);
        if (missing.Count > 0)
        {
            if (check)
            {
                foreach (var (hash, id, from) in missing) Console.Error.WriteLine($"not listed: {id} {hash} ({from})");
                Console.Error.WriteLine($"{Path.GetRelativePath(root, shippedPath)} is missing {missing.Count} official script hash(es). Run: dotnet run --project tools/CatalogBuilder");
                return 1;
            }
            foreach (var (hash, id, _) in missing) listed[hash] = id;
            File.WriteAllText(shippedPath, Chickxel.Core.Scripting.OfficialEffects.FormatShipped(listed), new System.Text.UTF8Encoding(false));
            Console.WriteLine($"updated: {Path.GetRelativePath(root, shippedPath)} (+{missing.Count})");
        }

        if (writes.Count > 0)
        {
            if (check)
            {
                foreach (var path in writes.Keys) Console.Error.WriteLine("stale: catalog/" + path);
                Console.Error.WriteLine("catalog/ is missing official effect packages. Run: dotnet run --project tools/CatalogBuilder");
                return 1;
            }
            OfficialEffectCatalog.Apply(source, writes);
            foreach (var path in writes.Keys) Console.WriteLine("updated: catalog/" + path);
        }

        var build = RemoteCatalogBuilder.Build(source, output);
        if (!build.Ok)
        {
            foreach (var issue in build.Issues) Console.Error.WriteLine("catalog/" + issue.Message);
            Console.Error.WriteLine($"{build.Issues.Count} problem(s) in catalog/.");
            return 1;
        }
        var stale = RemoteCatalogBuilder.Stale(build, output);
        if (check)
        {
            if (stale.Count == 0)
            {
                Console.WriteLine($"docs/catalog is up to date ({build.Entries.Count} package(s)).");
                return 0;
            }
            foreach (var path in stale) Console.Error.WriteLine("stale: docs/catalog/" + path);
            Console.Error.WriteLine("docs/catalog is out of date. Run: dotnet run --project tools/CatalogBuilder");
            return 1;
        }
        RemoteCatalogBuilder.Write(build, output);
        foreach (var path in stale) Console.WriteLine("updated: docs/catalog/" + path);
        Console.WriteLine($"docs/catalog: {build.Entries.Count} package(s), {stale.Count} file(s) changed.");
        return 0;
    }

    /// <summary>저장소 루트: 지금 폴더, 없으면 실행 파일 위쪽에서 Chickxel.sln을 찾는다.</summary>
    private static string RepoRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Chickxel.sln"))) return dir.FullName;
        throw new InvalidOperationException("Chickxel.sln not found above " + Environment.CurrentDirectory);
    }
}
