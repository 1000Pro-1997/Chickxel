using System.Globalization;
using System.Resources;
using System.Text;
using Chickxel.Core.Localization;

namespace Chickxel.App.Localization;

/// <summary>고를 수 있는 화면 언어. <see cref="Csv"/> = 언어 폴더에 그 언어의 CSV가 있음(내장 언어면 그 위에 덮어씀).</summary>
public sealed record UiLanguageInfo(string Code, string Name, bool BuiltIn, bool Csv)
{
    /// <summary>언어 목록에 보일 이름.</summary>
    public string Label => BuiltIn
        ? Csv ? UiStrings.FormatLanguage_WithCsv(Name) : Name
        : $"{Name} ({Code})";
}

/// <summary>
/// 화면 언어. 내장 언어 = 한국어(기본 리소스)·영어(*.en.resx). 언어 폴더(<see cref="Folder"/>)의 <c>&lt;언어 코드&gt;.csv</c>는
/// 같은 코드의 내장 언어 위에 덮어쓰고(지우면 내장 문구로 돌아감), 내장에 없는 코드면 새 언어가 된다(빈 칸은 영어).
/// 앱이 뜨기 전에 한 번 정한다(정적 필드에 담긴 문구도 있으므로 바꾸면 다시 시작).
/// </summary>
public static class UiLanguage
{
    public const string Korean = "ko";
    public const string English = "en";

    private static readonly (string Code, string Name)[] BuiltIns = [(Korean, "한국어"), (English, "English")];

    /// <summary>번역할 문구 표(CSV 키의 앞부분)와 그 리소스.</summary>
    private static readonly (string Table, ResourceManager Resources)[] Tables =
    [
        (nameof(UiStrings), UiStrings.ResourceManager),
        (nameof(MainStrings), MainStrings.ResourceManager),
        (nameof(StartStrings), StartStrings.ResourceManager),
        (nameof(CoreStrings), CoreStrings.ResourceManager),
        (nameof(DomainStrings), DomainStrings.ResourceManager),
        (nameof(ScriptApiStrings), ScriptApiStrings.ResourceManager),
        (nameof(ScriptGuideStrings), ScriptGuideStrings.ResourceManager),
    ];

    /// <summary>CSV로 더한 언어를 두는 폴더(<c>&lt;설정 폴더&gt;/Chickxel/Languages</c>).</summary>
    public static string Folder => Path.Combine(Platform.AppFolder, "Languages");

    /// <summary>지금 쓰는 언어 코드(<see cref="Apply"/>로 정함).</summary>
    public static string Current { get; private set; } = English;

    public static bool IsBuiltIn(string code) => BuiltIns.Any(b => Same(b.Code, code));

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string CsvPath(string code) => Path.Combine(Folder, code + ".csv");

    /// <summary>내장 언어와 언어 폴더의 CSV 언어(코드 순).</summary>
    public static IReadOnlyList<UiLanguageInfo> Available()
    {
        var custom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(Folder))
        {
            foreach (var path in Directory.EnumerateFiles(Folder, "*.csv"))
            {
                var code = Path.GetFileNameWithoutExtension(path);
                if (!TranslationCsv.IsValidCode(code) || ReadColumn(code) is not { } column) continue;
                custom[code] = column.Name;
            }
        }
        var list = BuiltIns.Select(b => new UiLanguageInfo(b.Code, b.Name, true, custom.Remove(b.Code))).ToList();
        list.AddRange(custom.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase).Select(c => new UiLanguageInfo(c.Key, c.Value, false, true)));
        return list;
    }

    /// <summary>
    /// 컴퓨터 언어(<paramref name="system"/>, 예: "pt-BR" → "pt-BR"·"pt" 순)에 맞는 언어 코드. 맞는 것이 없으면 영어.
    /// </summary>
    public static string Detect(CultureInfo system, IReadOnlyList<UiLanguageInfo> available)
    {
        for (var c = system; !string.IsNullOrEmpty(c.Name); c = c.Parent)
        {
            if (available.FirstOrDefault(l => Same(l.Code, c.Name)) is { } match) return match.Code;
        }
        return English;
    }

    /// <summary>
    /// <paramref name="code"/>를 화면 문구 언어로 정한다. CSV가 있으면 그 번역을 먼저 쓰고, 빈 칸은 같은 내장 언어(없으면 영어) 문구.
    /// 내장에 없는 코드인데 CSV를 읽지 못하면 영어. 숫자·날짜 형식(CurrentCulture)은 바꾸지 않는다.
    /// </summary>
    public static void Apply(string code)
    {
        bool builtIn = IsBuiltIn(code);
        var overlay = TranslationCsv.IsValidCode(code) ? LoadOverlay(code) : null;
        if (!builtIn && overlay is null) code = English;
        Current = builtIn ? BuiltIns.First(b => Same(b.Code, code)).Code : code;
        var english = CultureInfo.GetCultureInfo(English);
        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(Current);
        }
        catch (CultureNotFoundException)
        {
            culture = english;
        }
        Translations.Use(overlay, builtIn ? culture : english);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>언어 폴더의 CSV 번역(표, 키) → 문구. 없거나 읽지 못하면 null.</summary>
    private static Dictionary<(string Table, string Key), string>? LoadOverlay(string code)
    {
        if (ReadColumn(code) is not { } column) return null;
        var overlay = new Dictionary<(string Table, string Key), string>();
        foreach (var (key, value) in column.Values)
        {
            if (Split(key) is { } id) overlay[id] = value;
        }
        return overlay;
    }

    private static (string Table, string Key)? Split(string key)
    {
        int dot = key.IndexOf('.');
        return dot > 0 && dot < key.Length - 1 ? (key[..dot], key[(dot + 1)..]) : null;
    }

    /// <summary>언어 폴더의 <c>&lt;코드&gt;.csv</c>에서 그 코드의 열(없으면 첫 언어 열). 없거나 읽지 못하면 null.</summary>
    private static TranslationColumn? ReadColumn(string code)
    {
        var path = CsvPath(code);
        if (!File.Exists(path)) return null;
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8);
            var sheet = TranslationCsv.Read(reader);
            return sheet.Columns.FirstOrDefault(c => Same(c.Code, code)) ?? sheet.Columns.FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>모든 문구 키(표 순서, 표 안에서는 키 순).</summary>
    private static List<string> AllKeys()
    {
        var keys = new List<string>();
        foreach (var (table, resources) in Tables)
        {
            var set = resources.GetResourceSet(CultureInfo.InvariantCulture, true, true)!;
            keys.AddRange(set.Cast<System.Collections.DictionaryEntry>()
                .Select(e => (string)e.Key)
                .Order(StringComparer.Ordinal)
                .Select(k => table + "." + k));
        }
        return keys;
    }

    /// <summary>내장 언어의 리소스 문구(CSV를 덮기 전). 한국어 = 기본 리소스, 그 밖 = 그 언어 위성 리소스에 있는 것만.</summary>
    private static Dictionary<string, string> BuiltInValues(string code)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var culture = Same(code, Korean) ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(code);
        foreach (var (table, resources) in Tables)
        {
            if (resources.GetResourceSet(culture, true, false) is not { } set) continue;
            foreach (System.Collections.DictionaryEntry e in set)
            {
                if (e.Value is string s) values[table + "." + (string)e.Key] = s;
            }
        }
        return values;
    }

    /// <summary>
    /// 번역 CSV로 내보낸다: 모든 문구 키 × (내장 언어(CSV를 덮은 결과) + CSV로 더한 언어).
    /// 새 언어는 이 파일에 언어 코드 열을 더해 번역한 뒤 <see cref="ImportCsv"/>로 넣는다.
    /// </summary>
    public static void ExportCsv(string path)
    {
        var columns = new List<TranslationColumn>();
        foreach (var lang in Available())
        {
            var values = lang.BuiltIn ? BuiltInValues(lang.Code) : new Dictionary<string, string>(StringComparer.Ordinal);
            if (lang.Csv && ReadColumn(lang.Code) is { } column)
            {
                foreach (var (key, value) in column.Values) values[key] = value;
            }
            columns.Add(new TranslationColumn(lang.Code, lang.Name, values));
        }
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        TranslationCsv.Write(writer, AllKeys(), columns);
    }

    /// <summary>CSV로 넣은 언어 하나: 코드·이름·담은 문구 수(내장 언어는 내장 문구와 다른 것만).</summary>
    public sealed record ImportedLanguage(string Code, string Name, int Count);

    /// <summary><see cref="ImportCsv"/> 결과. <see cref="InvalidCodes"/> = 언어 코드로 쓸 수 없어 건너뛴 열, <see cref="UnknownKeys"/> = 앱에 없는 키 수.</summary>
    public sealed record ImportResult(IReadOnlyList<ImportedLanguage> Languages, IReadOnlyList<string> InvalidCodes, int UnknownKeys);

    /// <summary>
    /// 번역 CSV의 언어 열마다 언어 폴더에 <c>&lt;코드&gt;.csv</c>를 쓴다(같은 코드가 있으면 덮어씀).
    /// 내장 언어 열은 내장 문구와 다른 칸만 담고(다른 것이 없으면 그 언어의 CSV를 지움), 앱에 없는 키는 버린다.
    /// </summary>
    /// <exception cref="InvalidDataException">번역 CSV 형식이 아님.</exception>
    public static ImportResult ImportCsv(string path)
    {
        TranslationSheet sheet;
        using (var reader = new StreamReader(path, Encoding.UTF8)) sheet = TranslationCsv.Read(reader);
        var known = AllKeys();
        var knownSet = known.ToHashSet(StringComparer.Ordinal);
        int unknown = sheet.Keys.Count(k => !knownSet.Contains(k));
        var imported = new List<ImportedLanguage>();
        var invalid = new List<string>();
        Directory.CreateDirectory(Folder);
        foreach (var column in sheet.Columns)
        {
            if (!TranslationCsv.IsValidCode(column.Code))
            {
                invalid.Add(column.Code);
                continue;
            }
            var builtIn = BuiltIns.FirstOrDefault(b => Same(b.Code, column.Code));
            var code = builtIn.Code ?? column.Code;
            var baseValues = builtIn.Code is null ? null : BuiltInValues(code);
            var values = column.Values
                .Where(v => knownSet.Contains(v.Key) && (baseValues is null || !baseValues.TryGetValue(v.Key, out var b) || b != v.Value))
                .ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
            var name = builtIn.Code is null ? column.Name : builtIn.Name;
            var target = CsvPath(code);
            // 이름만 대소문자가 다른 같은 언어 파일도 덮어쓴다(macOS·Linux는 다른 파일로 봄).
            foreach (var old in Directory.EnumerateFiles(Folder, "*.csv").Where(p => Same(Path.GetFileNameWithoutExtension(p), code)))
                File.Delete(old);
            if (values.Count > 0 || builtIn.Code is null)
            {
                using var writer = new StreamWriter(target, false, new UTF8Encoding(true));
                TranslationCsv.Write(writer, known.Where(values.ContainsKey).ToList(), [new TranslationColumn(code, name, values)]);
            }
            imported.Add(new ImportedLanguage(code, name, values.Count));
        }
        return new ImportResult(imported, invalid, unknown);
    }
}
