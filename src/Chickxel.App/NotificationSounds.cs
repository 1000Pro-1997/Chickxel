using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 알림 소리: 파일 없이 코드로 만든 짧은 소리 몇 가지(사인파 음 — 차임·딩·팝·방울·두 음·소나·종). 처음 쓸 때 16비트 모노 WAV로 만들어 두고
/// Windows는 메모리에서 바로(winmm PlaySound), macOS는 임시 파일을 afplay로, Linux는 paplay·aplay로 비동기로 튼다. 못 틀면 조용히 넘어간다.
/// </summary>
internal static class NotificationSounds
{
    public const string None = "none", Default = "chime";

    /// <summary>고를 수 있는 소리 id(설정 순서).</summary>
    public static readonly string[] Ids = ["chime", "ding", "pop", "bloop", "twotone", "sonar", "bell"];

    private const int Rate = 44100;

    /// <summary>음 하나: 시작·길이(초), 시작·끝 주파수(Hz, 길이 동안 바뀜), 세기, 줄어드는 빠르기(1/초), 배음(주파수 배수, 세기).</summary>
    private readonly record struct Note(double Start, double Length, double From, double To, double Amp, double Decay, (double Mult, double Amp)[] Partials);

    private static readonly (double, double)[] Soft = [(1, 1), (2, 0.25), (3, 0.08)];
    private static readonly (double, double)[] Pure = [(1, 1)];

    private static Note[] Notes(string id) => id switch
    {
        "chime" => [new(0, 0.35, 880, 880, 0.6, 9, Soft), new(0.12, 0.5, 1318.5, 1318.5, 0.6, 7, Soft)],
        "ding" => [new(0, 0.7, 1568, 1568, 0.8, 6, [(1, 1), (2.76, 0.3), (5.4, 0.1)])],
        "pop" => [new(0, 0.08, 900, 280, 0.9, 30, Pure)],
        "bloop" => [new(0, 0.16, 320, 760, 0.8, 12, [(1, 1), (2, 0.15)])],
        "twotone" => [new(0, 0.14, 659.3, 659.3, 0.5, 4, [(1, 1), (3, 0.2)]), new(0.16, 0.2, 880, 880, 0.5, 4, [(1, 1), (3, 0.2)])],
        "sonar" => [new(0, 0.9, 1046.5, 1046.5, 0.8, 4.5, [(1, 1), (2, 0.1)]), new(0.32, 0.6, 1046.5, 1046.5, 0.25, 6, Pure)],
        "bell" => [new(0, 1.3, 523.25, 523.25, 0.7, 3, [(1, 1), (2, 0.5), (3, 0.3), (4.2, 0.15), (5.4, 0.08)])],
        _ => [],
    };

    /// <summary>설정 화면에 보일 이름.</summary>
    public static string Name(string id) => id switch
    {
        "chime" => UiStrings.Notify_Sound_Chime,
        "ding" => UiStrings.Notify_Sound_Ding,
        "pop" => UiStrings.Notify_Sound_Pop,
        "bloop" => UiStrings.Notify_Sound_Bloop,
        "twotone" => UiStrings.Notify_Sound_TwoTone,
        "sonar" => UiStrings.Notify_Sound_Sonar,
        "bell" => UiStrings.Notify_Sound_Bell,
        _ => UiStrings.Notify_Sound_None,
    };

    /// <summary>만들어 둔 WAV와(Windows) 그 고정 주소 — 비동기 재생 중에 옮겨지지 않게 앱이 끝날 때까지 고정한다.</summary>
    private static readonly Dictionary<string, (byte[] Wav, GCHandle Pin)> Cache = new(StringComparer.Ordinal);

    /// <summary>소리를 튼다(없음·모르는 id면 아무것도 안 함). UI 스레드.</summary>
    public static void Play(string? id)
    {
        if (id is null || Array.IndexOf(Ids, id) < 0) return;
        if (!Cache.TryGetValue(id, out var sound))
        {
            var wav = Wav(Render(Notes(id)));
            sound = (wav, GCHandle.Alloc(wav, GCHandleType.Pinned));
            Cache[id] = sound;
        }
        try
        {
            if (OperatingSystem.IsWindows())
                PlaySound(sound.Pin.AddrOfPinnedObject(), IntPtr.Zero, SndAsync | SndNoDefault | SndMemory);
            else
                PlayFile(id, sound.Wav);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            // 소리를 못 내도 알림 창은 뜬다.
        }
    }

    /// <summary>macOS·Linux: 임시 폴더의 WAV(처음 한 번 씀)를 시스템 재생기로.</summary>
    private static void PlayFile(string id, byte[] wav)
    {
        var dir = Path.Combine(Path.GetTempPath(), "chickxel-sounds");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, id + ".wav");
        if (!File.Exists(file) || new FileInfo(file).Length != wav.Length) File.WriteAllBytes(file, wav);
        string[] players = OperatingSystem.IsMacOS() ? ["/usr/bin/afplay"] : ["paplay", "aplay"];
        foreach (var player in players)
        {
            try
            {
                var start = new ProcessStartInfo(player) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add(file);
                Process.Start(start)?.Dispose();
                return;
            }
            catch (Win32Exception)
            {
                // 이 재생기가 없으면 다음 것.
            }
        }
    }

    /// <summary>음들을 섞은 소리(−1~1, 가장 큰 값 0.7로 맞춤). 음마다 4ms 들어오고 끝 10ms는 0으로 줄여 딸깍 소리가 나지 않게.</summary>
    private static float[] Render(Note[] notes)
    {
        double total = notes.Length == 0 ? 0 : notes.Max(n => n.Start + n.Length) + 0.02;
        var buffer = new float[(int)(total * Rate)];
        foreach (var note in notes)
        {
            int start = (int)(note.Start * Rate), count = (int)(note.Length * Rate);
            double phase = 0;
            for (int i = 0; i < count && start + i < buffer.Length; i++)
            {
                double t = (double)i / Rate;
                double f = note.From + (note.To - note.From) * (t / note.Length);
                phase += 2 * Math.PI * f / Rate;
                double env = Math.Min(1, t / 0.004) * Math.Exp(-t * note.Decay) * Math.Min(1, (note.Length - t) / 0.01);
                double v = 0;
                foreach (var (mult, amp) in note.Partials) v += amp * Math.Sin(phase * mult);
                buffer[start + i] += (float)(note.Amp * env * v);
            }
        }
        float peak = buffer.Length == 0 ? 0 : buffer.Max(Math.Abs);
        if (peak > 0)
            for (int i = 0; i < buffer.Length; i++) buffer[i] *= 0.7f / peak;
        return buffer;
    }

    /// <summary>16비트 모노 PCM WAV.</summary>
    private static byte[] Wav(float[] samples)
    {
        using var ms = new MemoryStream(44 + samples.Length * 2);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + samples.Length * 2);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(Rate);
        w.Write(Rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(samples.Length * 2);
        foreach (var s in samples) w.Write((short)Math.Clamp(s * short.MaxValue, short.MinValue, short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }

    private const uint SndAsync = 0x1, SndNoDefault = 0x2, SndMemory = 0x4;

    [DllImport("winmm.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);
}
