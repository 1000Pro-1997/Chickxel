using System.Text;

namespace Chickxel.Core.IO;

/// <summary>임시 파일에 쓰고 원자적으로 바꿔치기(같은 폴더 안 rename). 쓰는 도중 끊겨도 이전 파일은 그대로다.</summary>
public static class AtomicFile
{
    /// <summary>BOM 없는 UTF-8(<c>File.WriteAllText</c> 기본값과 같은 바이트).</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// <paramref name="data"/>를 <paramref name="path"/>에 쓴다(임시 파일에 쓴 뒤 바꿔치기).
    /// <paramref name="flushToDisk"/>가 false면 바꿔치기 전에 디스크까지 내려 쓰기를 기다리지 않는다(자주·크게 쓰는 기록용).
    /// </summary>
    public static void Write(string path, byte[] data, bool flushToDisk = true)
    {
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                if (flushToDisk) stream.Flush(flushToDisk: true);
            }
            FileReplace.Commit(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary><paramref name="text"/>를 BOM 없는 UTF-8로 <paramref name="path"/>에 쓴다(임시 파일에 쓴 뒤 바꿔치기).</summary>
    public static void WriteText(string path, string text) => Write(path, Utf8NoBom.GetBytes(text));
}
