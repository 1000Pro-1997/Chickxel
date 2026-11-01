using Chickxel.Core.Localization;

namespace Chickxel.Core.IO;

/// <summary>
/// 임시 파일에 다 쓴 뒤 대상 파일을 바꿔치기한다(쓰다 실패해도 기존 파일이 깨지지 않게).
/// Windows에서는 다른 프로그램(OneDrive·탐색기 미리보기·백신·이미지 뷰어·게임 엔진 등)이 대상 파일을 열어 두기만 해도
/// 이름 바꾸기 덮어쓰기가 "Access to the path is denied"로 실패하므로, 잠깐 다시 해 보고 그래도 안 되면 대상 파일 안에 그대로 덮어쓴다.
/// 읽기 전용 파일과, 덮어쓰기도 막힌(쓰기를 함께 쓰지 않는 프로그램이 연) 파일은 까닭을 적은 오류로 알린다.
/// </summary>
public static class FileReplace
{
    /// <summary>바꿔치기를 다시 해 보는 횟수와 그 사이 기다림(ms, 회차마다 늘림).</summary>
    private const int Retries = 4, RetryDelayMs = 40;

    /// <summary><paramref name="temp"/>를 <paramref name="path"/>로 옮긴다(있으면 덮어씀). 성공하면 임시 파일은 남지 않는다.</summary>
    /// <exception cref="UnauthorizedAccessException">읽기 전용 파일이거나 다른 프로그램이 막아 덮어쓰지 못함(까닭을 적은 글).</exception>
    public static void Commit(string temp, string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException && File.Exists(path) && File.Exists(temp))
            {
                if (IsReadOnly(path))
                    throw new UnauthorizedAccessException(DomainStrings.FormatFile_ReadOnly(name: Path.GetFileName(path)), ex);
                if (attempt < Retries)
                {
                    Thread.Sleep(RetryDelayMs * (attempt + 1));
                    continue;
                }
                OverwriteInPlace(temp, path, ex);
                return;
            }
        }
    }

    private static bool IsReadOnly(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 이름 바꾸기가 막혔을 때: 대상 파일을 열어 내용을 임시 파일 것으로 바꾼다(다른 프로그램이 쓰기를 함께 허용하고 연 경우는 됨).
    /// 이것도 막히면 대상 파일은 그대로 두고 "다른 프로그램이 쓰는 중" 오류를 낸다.
    /// </summary>
    private static void OverwriteInPlace(string temp, string path, Exception moveError)
    {
        try
        {
            using (var source = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var target = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                target.SetLength(0);
                source.CopyTo(target);
                target.Flush(flushToDisk: true);
            }
            File.Delete(temp);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new UnauthorizedAccessException(DomainStrings.FormatFile_InUse(name: Path.GetFileName(path)), moveError);
        }
    }
}
