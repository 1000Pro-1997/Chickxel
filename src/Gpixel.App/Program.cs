namespace Gpixel.App;

/// <summary>명령줄 인수: <c>--open-folder &lt;폴더&gt;</c>, <c>--open &lt;파일&gt;</c>(.gpixel/.png). 경로만 주면 --open과 같다.</summary>
public sealed record StartupArgs(string? OpenFolder, string? OpenFile)
{
    public static StartupArgs Parse(string[] args)
    {
        string? folder = null, file = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--open-folder" when i + 1 < args.Length:
                    folder = args[++i];
                    break;
                case "--open" when i + 1 < args.Length:
                    file = args[++i];
                    break;
                default:
                    if (!args[i].StartsWith("--", StringComparison.Ordinal)) file ??= args[i];
                    break;
            }
        }
        return new StartupArgs(folder, file);
    }
}

static class Program
{
    /// <summary>앱 진입점.</summary>
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        // 목록 머리글·스크롤 막대·콤보 상자 등 공용 컨트롤도 어두운 테마로 그린다.
        Application.SetColorMode(SystemColorMode.Dark);
        Application.Run(new MainForm(StartupArgs.Parse(args)));
    }
}
