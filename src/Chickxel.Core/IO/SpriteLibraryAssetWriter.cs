using System.Globalization;
using System.Text;

namespace Chickxel.Core.IO;

/// <summary>
/// Unity Sprite Library Source Asset(<c>.spriteLib</c>, com.unity.2d.animation 패키지) 쓰기.
/// <para>
/// 형식은 Unity YAML 직렬화 파일 하나: <c>MonoBehaviour</c>(<c>m_Script</c> = <c>SpriteLibrarySourceAsset</c> 스크립트, guid <see cref="SourceAssetScriptGuid"/>)의
/// <c>m_Library</c>에 범주(<c>SpriteLibCategoryOverride</c>: <c>m_Name</c>·<c>m_Hash</c>·<c>m_CategoryList</c>·<c>m_OverrideEntries</c>·<c>m_FromMain</c>·<c>m_EntryOverrideCount</c>)와
/// 라벨(<c>SpriteCategoryEntryOverride</c>: <c>m_Name</c>·<c>m_Hash</c>·<c>m_Sprite</c>·<c>m_FromMain</c>·<c>m_SpriteOverride</c>)을 적는다.
/// Unity 가져오기(<c>SpriteLibrarySourceAssetImporter</c>)는 범주마다 <c>m_OverrideEntries</c>의 <c>m_SpriteOverride</c>를 읽어 Sprite Library Asset을 만든다.
/// 스프라이트는 <c>{fileID: 스프라이트 internalID, guid: 텍스처 .meta guid, type: 3}</c>로 가리킨다.
/// 메타는 ScriptedImporter(<c>SpriteLibrarySourceAssetImporter</c>, 스크립트 guid <see cref="ImporterScriptGuid"/>).
/// </para>
/// <para>
/// 출처(2D Animation 패키지 소스, 미러 github.com/needle-mirror/com.unity.2d.animation): Runtime/SpriteLib/SpriteLibrarySourceAsset.cs(.meta — 7.x·10.x·17.x에서 같은 guid),
/// Runtime/SpriteLib/SpriteLibraryData.cs, Runtime/SpriteLib/SpriteLibraryAsset.cs, Runtime/SpriteLib/SpriteLibraryUtility.cs(이름 해시 = Animator.StringToHash의 아래 30비트),
/// Editor/SpriteLib/SpriteLibrarySourceAsset/SpriteLibrarySourceAssetImporter.cs(.meta), 예제 Samples~/AnimationSamples/4 SpriteSwap/Sprite Library/Part Swap.spriteLib.
/// </para>
/// </summary>
public static class SpriteLibraryAssetWriter
{
    /// <summary><c>UnityEngine.U2D.Animation.SpriteLibrarySourceAsset</c> 스크립트 guid(패키지의 SpriteLibrarySourceAsset.cs.meta).</summary>
    public const string SourceAssetScriptGuid = "a5e6fedc2472449cead18ef23b5cb30d";

    /// <summary><c>UnityEditor.U2D.Animation.SpriteLibrarySourceAssetImporter</c> 스크립트 guid(패키지의 SpriteLibrarySourceAssetImporter.cs.meta).</summary>
    public const string ImporterScriptGuid = "db2778f6d440c47ddacff25997d7c062";

    /// <summary>스프라이트 참조: 텍스처 안 스프라이트의 internalID(= Unity fileID)와 텍스처 <c>.meta</c> guid.</summary>
    public sealed record SpriteRef(long FileId, string Guid);

    /// <summary>라벨 하나. 스프라이트가 null이면 빈 항목(Sprite Resolver가 아무것도 그리지 않음).</summary>
    public sealed record Label(string Name, SpriteRef? Sprite);

    /// <summary>범주 하나와 그 라벨들.</summary>
    public sealed record Category(string Name, IReadOnlyList<Label> Labels);

    /// <summary>
    /// Sprite Library 이름 해시: <c>Animator.StringToHash</c>(UTF-8 CRC-32)의 아래 30비트(<c>SpriteLibraryUtility.GetStringHash</c>).
    /// 가져오기는 이름으로 해시를 다시 계산하므로 참고값이지만 Unity가 쓰는 값과 같게 적는다.
    /// </summary>
    public static int NameHash(string name) => (int)(Crc32(Encoding.UTF8.GetBytes(name)) & 0x3FFFFFFF);

    /// <summary><c>.spriteLib</c> YAML 텍스트. 같은 내용이면 같은 텍스트(<c>m_ModificationHash</c>도 내용에서 계산).</summary>
    public static string Generate(IReadOnlyList<Category> categories)
    {
        var lib = new StringBuilder();
        void L(string s) => lib.Append(s).Append('\n');
        if (categories.Count == 0) L("  m_Library: []");
        else
        {
            L("  m_Library:");
            foreach (var c in categories)
            {
                L($"  - m_Name: {MiniYaml.QuoteIfNeeded(c.Name)}");
                L($"    m_Hash: {NameHash(c.Name).ToString(CultureInfo.InvariantCulture)}");
                L("    m_CategoryList: []");
                if (c.Labels.Count == 0) L("    m_OverrideEntries: []");
                else
                {
                    L("    m_OverrideEntries:");
                    foreach (var label in c.Labels)
                    {
                        var sprite = Reference(label.Sprite);
                        L($"    - m_Name: {MiniYaml.QuoteIfNeeded(label.Name)}");
                        L($"      m_Hash: {NameHash(label.Name).ToString(CultureInfo.InvariantCulture)}");
                        L($"      m_Sprite: {sprite}");
                        L("      m_FromMain: 0");
                        L($"      m_SpriteOverride: {sprite}");
                    }
                }
                L("    m_FromMain: 0");
                L($"    m_EntryOverrideCount: {c.Labels.Count.ToString(CultureInfo.InvariantCulture)}");
            }
        }
        var body = lib.ToString();

        var sb = new StringBuilder();
        sb.Append("%YAML 1.1\n");
        sb.Append("%TAG !u! tag:unity3d.com,2011:\n");
        sb.Append("--- !u!114 &1\n");
        sb.Append("MonoBehaviour:\n");
        sb.Append("  m_ObjectHideFlags: 36\n");
        sb.Append("  m_CorrespondingSourceObject: {fileID: 0}\n");
        sb.Append("  m_PrefabInstance: {fileID: 0}\n");
        sb.Append("  m_PrefabAsset: {fileID: 0}\n");
        sb.Append("  m_GameObject: {fileID: 0}\n");
        sb.Append("  m_Enabled: 1\n");
        sb.Append("  m_EditorHideFlags: 0\n");
        sb.Append($"  m_Script: {{fileID: 11500000, guid: {SourceAssetScriptGuid}, type: 3}}\n");
        sb.Append("  m_Name: \n");
        sb.Append("  m_EditorClassIdentifier: \n");
        sb.Append(body);
        sb.Append("  m_PrimaryLibraryGUID: \n");
        sb.Append($"  m_ModificationHash: {ModificationHash(body).ToString(CultureInfo.InvariantCulture)}\n");
        sb.Append("  m_Version: 1\n");
        return sb.ToString();
    }

    /// <summary>ScriptedImporter 메타 텍스트(<see cref="ImporterScriptGuid"/>).</summary>
    public static string GenerateMeta(string guid) =>
        "fileFormatVersion: 2\n" +
        $"guid: {guid}\n" +
        "ScriptedImporter:\n" +
        "  internalIDToNameTable: []\n" +
        "  externalObjects: {}\n" +
        "  serializedVersion: 2\n" +
        "  userData: \n" +
        "  assetBundleName: \n" +
        "  assetBundleVariant: \n" +
        $"  script: {{fileID: 11500000, guid: {ImporterScriptGuid}, type: 3}}\n";

    /// <summary>
    /// <c>.spriteLib</c>를 쓴다(내용이 같으면 그대로 두어 Unity가 다시 가져오지 않게). <c>.meta</c>는 없을 때만 새 guid로 만들고,
    /// 있으면 그대로 둔다(guid·Unity에서 바꾼 가져오기 설정 유지).
    /// </summary>
    public static void Write(string path, IReadOnlyList<Category> categories)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var text = Generate(categories);
        if (!File.Exists(path) || File.ReadAllText(path) != text) WriteAtomic(path, text);
        var metaPath = UnityMeta.GetMetaPath(path);
        if (!File.Exists(metaPath) || UnityMeta.TryRead(metaPath)?.Guid is null)
            WriteAtomic(metaPath, GenerateMeta(UnityMeta.NewGuid()));
    }

    private static void WriteAtomic(string path, string text)
    {
        var temp = path + ".chickxel-tmp";
        try
        {
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static string Reference(SpriteRef? sprite)
        => sprite is null || sprite.Guid.Length == 0
            ? "{fileID: 0}"
            : $"{{fileID: {sprite.FileId.ToString(CultureInfo.InvariantCulture)}, guid: {sprite.Guid}, type: 3}}";

    /// <summary>내용에서 계산한 0 이상 64비트 값(FNV-1a): 내용이 바뀌면 바뀌고 같으면 같다.</summary>
    private static long ModificationHash(string text)
    {
        ulong h = 14695981039346656037UL;
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            h ^= b;
            h *= 1099511628211UL;
        }
        return (long)(h & long.MaxValue);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
