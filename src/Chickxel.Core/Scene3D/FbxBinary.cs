using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Scene3D;

/// <summary>FBX 바이너리 노드 하나: 이름, 속성들, 아래 노드들.</summary>
internal sealed class FbxNode
{
    public required string Name { get; init; }
    public required FbxProperty[] Properties { get; init; }
    public List<FbxNode> Children { get; } = [];

    /// <summary><paramref name="index"/>번째 속성(없으면 <see cref="FbxProperty.Type"/>이 '\0'인 빈 속성).</summary>
    public FbxProperty this[int index] => (uint)index < (uint)Properties.Length ? Properties[index] : default;

    /// <summary>이름이 <paramref name="name"/>인 첫 아래 노드(없으면 null).</summary>
    public FbxNode? Child(string name)
    {
        foreach (var c in Children)
            if (c.Name == name) return c;
        return null;
    }
}

/// <summary>
/// FBX 속성 하나: 수(Y C I L F D), 글자(S), 날 바이트(R), 배열(f d l i b — 압축된 배열은 꺼낼 때 푼다).
/// 배열·날 바이트는 파일 바이트를 가리키기만 하고 복사하지 않는다.
/// </summary>
internal readonly struct FbxProperty
{
    private readonly byte[]? _file;
    private readonly int _offset, _length, _count;
    private readonly bool _zlib;
    private readonly long _integer;
    private readonly double _number;

    /// <summary>형식 글자('\0' = 없는 속성).</summary>
    public char Type { get; }
    /// <summary>S 속성의 글자(UTF-8로 푼 것), 아니면 null.</summary>
    public string? Text { get; }

    public FbxProperty(char type, long integer)
    {
        Type = type;
        _integer = integer;
        _number = integer;
    }

    public FbxProperty(char type, double number)
    {
        Type = type;
        _number = number;
    }

    public FbxProperty(string text)
    {
        Type = 'S';
        Text = text;
    }

    /// <summary>R 속성(<paramref name="count"/> = 0) 또는 배열(<paramref name="count"/> = 원소 수).</summary>
    public FbxProperty(char type, byte[] file, int offset, int length, int count, bool zlib)
    {
        Type = type;
        _file = file;
        _offset = offset;
        _length = length;
        _count = count;
        _zlib = zlib;
    }

    public bool IsNumber => Type is 'Y' or 'C' or 'I' or 'L' or 'F' or 'D';
    public bool IsArray => Type is 'f' or 'd' or 'l' or 'i' or 'b';

    /// <summary>수 속성의 값(수가 아니면 0).</summary>
    public double Number => IsNumber ? _number : 0;

    /// <summary>정수 속성의 값(L·I·Y·C 그대로, F·D는 버림, 수가 아니면 0).</summary>
    public long Integer => Type switch
    {
        'Y' or 'C' or 'I' or 'L' => _integer,
        'F' or 'D' => double.IsFinite(_number) ? (long)Math.Clamp(_number, long.MinValue, long.MaxValue) : 0,
        _ => 0,
    };

    /// <summary>배열 원소 수(배열이 아니면 0).</summary>
    public int Count => IsArray ? _count : 0;

    /// <summary>R 속성의 바이트(R이 아니면 빈 것).</summary>
    public ReadOnlySpan<byte> Raw => Type == 'R' && _file != null ? _file.AsSpan(_offset, _length) : default;

    private int ElementSize => Type switch { 'd' or 'l' => 8, 'f' or 'i' => 4, _ => 1 };

    /// <summary>배열의 풀린 바이트(원소 수 × 원소 크기). 길이가 안 맞거나 풀 수 없으면 <see cref="InvalidDataException"/>.</summary>
    private ReadOnlySpan<byte> Payload()
    {
        long need = (long)_count * ElementSize;
        if (!_zlib)
        {
            if (_length != need) throw FbxBinary.Malformed(_offset);
            return _file.AsSpan(_offset, _length);
        }
        // deflate는 한 바이트를 1032바이트 넘게 늘리지 못한다 — 그보다 큰 원소 수는 잘못된 파일(엉뚱하게 큰 할당을 막음).
        if (need > Array.MaxLength || need > (long)_length * 1032 + 1024 || _length < 2) throw FbxBinary.Malformed(_offset);
        var buffer = GC.AllocateUninitializedArray<byte>((int)need);
        int read = 0;
        try
        {
            // zlib 머리 2바이트를 건너뛰고 deflate로 푼다(끝의 adler32 검사합은 보지 않음).
            using var source = new MemoryStream(_file!, _offset + 2, _length - 2, writable: false);
            using var inflate = new DeflateStream(source, CompressionMode.Decompress);
            while (read < buffer.Length)
            {
                int k = inflate.Read(buffer, read, buffer.Length - read);
                if (k <= 0) break;
                read += k;
            }
        }
        catch (InvalidDataException ex) { throw new InvalidDataException(FbxBinary.Malformed(_offset).Message, ex); }
        if (read < buffer.Length) throw FbxBinary.Malformed(_offset);
        return buffer;
    }

    /// <summary>배열을 double로(수 속성 하나면 원소 하나, 그 밖은 빈 배열).</summary>
    public double[] ToDoubles()
    {
        if (!IsArray) return IsNumber ? [_number] : [];
        var bytes = Payload();
        var result = new double[_count];
        switch (Type)
        {
            case 'd': MemoryMarshal.Cast<byte, double>(bytes).CopyTo(result); break;
            case 'f': { var s = MemoryMarshal.Cast<byte, float>(bytes); for (int i = 0; i < s.Length; i++) result[i] = s[i]; break; }
            case 'l': { var s = MemoryMarshal.Cast<byte, long>(bytes); for (int i = 0; i < s.Length; i++) result[i] = s[i]; break; }
            case 'i': { var s = MemoryMarshal.Cast<byte, int>(bytes); for (int i = 0; i < s.Length; i++) result[i] = s[i]; break; }
            default: for (int i = 0; i < bytes.Length; i++) result[i] = bytes[i]; break;
        }
        return result;
    }

    /// <summary>배열을 float로.</summary>
    public float[] ToFloats()
    {
        if (!IsArray) return IsNumber ? [(float)_number] : [];
        if (Type != 'f')
        {
            var d = ToDoubles();
            var r = new float[d.Length];
            for (int i = 0; i < d.Length; i++) r[i] = (float)d[i];
            return r;
        }
        var result = new float[_count];
        MemoryMarshal.Cast<byte, float>(Payload()).CopyTo(result);
        return result;
    }

    /// <summary>배열을 int로(긴 정수·실수는 int 범위로 자름).</summary>
    public int[] ToInts()
    {
        if (!IsArray) return IsNumber ? [(int)Math.Clamp(Integer, int.MinValue, int.MaxValue)] : [];
        var bytes = Payload();
        var result = new int[_count];
        switch (Type)
        {
            case 'i': MemoryMarshal.Cast<byte, int>(bytes).CopyTo(result); break;
            case 'l': { var s = MemoryMarshal.Cast<byte, long>(bytes); for (int i = 0; i < s.Length; i++) result[i] = (int)Math.Clamp(s[i], int.MinValue, int.MaxValue); break; }
            case 'b': for (int i = 0; i < bytes.Length; i++) result[i] = bytes[i]; break;
            default:
            {
                var d = ToDoubles();
                for (int i = 0; i < d.Length; i++) result[i] = double.IsFinite(d[i]) ? (int)Math.Clamp(d[i], int.MinValue, int.MaxValue) : 0;
                break;
            }
        }
        return result;
    }

    /// <summary>배열을 long으로(애니메이션 키 시각).</summary>
    public long[] ToLongs()
    {
        if (!IsArray) return IsNumber ? [Integer] : [];
        if (Type == 'l')
        {
            var result = new long[_count];
            MemoryMarshal.Cast<byte, long>(Payload()).CopyTo(result);
            return result;
        }
        if (Type is 'i' or 'b')
        {
            var ints = ToInts();
            var r = new long[ints.Length];
            for (int i = 0; i < ints.Length; i++) r[i] = ints[i];
            return r;
        }
        var d = ToDoubles();
        var l = new long[d.Length];
        for (int i = 0; i < d.Length; i++) l[i] = double.IsFinite(d[i]) ? (long)Math.Clamp(d[i], long.MinValue, long.MaxValue) : 0;
        return l;
    }
}

/// <summary>
/// FBX 바이너리(7.x) 읽기: 머리글 "Kaydara FBX Binary  \0\x1a\0" + 판(uint32), 그 뒤 노드 기록들(7.5부터 위치·길이가 64비트).
/// 위치·길이를 모두 검사해 잘리거나 엉터리인 파일은 <see cref="InvalidDataException"/>으로 바꾼다. ASCII FBX와 7.0보다 옛 판은 받지 않는다.
/// </summary>
internal static class FbxBinary
{
    public const int MinVersion = 7000;
    private const int HeaderLength = 27;
    private const int MaxDepth = 64;

    private static ReadOnlySpan<byte> Magic => "Kaydara FBX Binary  \0"u8;

    /// <summary>맨 위 노드들. <paramref name="version"/> = 파일 판(예: 7400).</summary>
    public static List<FbxNode> Parse(byte[] data, out int version)
    {
        if (data.Length < HeaderLength || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException(LooksAscii(data) ? CoreStrings.Fbx_AsciiNotSupported : CoreStrings.Fbx_InvalidFile);
        version = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(23)), int.MaxValue);
        if (version < MinVersion)
            throw new InvalidDataException(CoreStrings.FormatFbx_UnsupportedVersion(version: $"{version / 1000}.{version / 100 % 10}"));

        var parser = new Parser(data, version >= 7500);
        var top = new List<FbxNode>();
        long pos = HeaderLength;
        // 맨 위 목록은 빈 기록으로 끝나고 뒤에 꼬리말이 붙는다. 빈 기록 없이 파일이 노드 경계에서 끝나도 받는다.
        while (pos + parser.RecordHeader <= data.Length)
        {
            var node = parser.Node(ref pos, data.Length, 0);
            if (node == null) break;
            top.Add(node);
        }
        return top;
    }

    internal static InvalidDataException Malformed(long offset) => new(CoreStrings.FormatFbx_Malformed(where: $"0x{offset:X}"));

    /// <summary>글자 FBX처럼 보이는지(첫 줄이 ';' 주석이거나 앞부분에 FBXHeaderExtension이 있음).</summary>
    private static bool LooksAscii(byte[] data)
    {
        var head = data.AsSpan(0, Math.Min(data.Length, 1024));
        int i = head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF ? 3 : 0;
        while (i < head.Length && head[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') i++;
        return (i < head.Length && head[i] == (byte)';') || head.IndexOf("FBXHeaderExtension"u8) >= 0;
    }

    private sealed class Parser(byte[] data, bool wide)
    {
        /// <summary>노드 기록 머리 길이: 끝 위치·속성 수·속성 목록 길이(7.5부터 8바이트씩, 전에는 4바이트씩) + 이름 길이 1바이트.</summary>
        public int RecordHeader => wide ? 25 : 13;

        /// <summary><paramref name="pos"/>의 노드 하나(빈 기록이면 null). <paramref name="limit"/> = 이 목록이 끝나는 위치.</summary>
        public FbxNode? Node(ref long pos, long limit, int depth)
        {
            int header = RecordHeader;
            if (pos < 0 || pos + header > limit) throw Malformed(pos);
            var s = data.AsSpan((int)pos, header);
            ulong end, count, listLength;
            if (wide)
            {
                end = BinaryPrimitives.ReadUInt64LittleEndian(s);
                count = BinaryPrimitives.ReadUInt64LittleEndian(s[8..]);
                listLength = BinaryPrimitives.ReadUInt64LittleEndian(s[16..]);
            }
            else
            {
                end = BinaryPrimitives.ReadUInt32LittleEndian(s);
                count = BinaryPrimitives.ReadUInt32LittleEndian(s[4..]);
                listLength = BinaryPrimitives.ReadUInt32LittleEndian(s[8..]);
            }
            int nameLength = s[header - 1];
            if (end == 0 && count == 0 && listLength == 0 && nameLength == 0)
            {
                pos += header;
                return null;
            }

            long nameStart = pos + header;
            long propStart = nameStart + nameLength;
            // 속성 하나는 적어도 1바이트 — 속성 수가 목록 길이보다 크면 엉터리(큰 배열 할당을 막음).
            if (propStart > limit || end > (ulong)limit || listLength > (ulong)(limit - propStart) || count > listLength)
                throw Malformed(pos);
            long propEnd = propStart + (long)listLength;
            if ((ulong)propEnd > end) throw Malformed(pos);
            long nodeEnd = (long)end;

            var name = Encoding.UTF8.GetString(data, (int)nameStart, nameLength);
            var props = new FbxProperty[(int)count];
            long p = propStart;
            for (int i = 0; i < props.Length; i++) props[i] = Property(ref p, propEnd);
            if (p != propEnd) throw Malformed(p);

            var node = new FbxNode { Name = name, Properties = props };
            pos = propEnd;
            if (pos < nodeEnd)
            {
                if (depth >= MaxDepth) throw Malformed(pos);
                while (pos < nodeEnd)
                {
                    var child = Node(ref pos, nodeEnd, depth + 1);
                    if (child == null) break;
                    node.Children.Add(child);
                }
            }
            pos = nodeEnd;
            return node;
        }

        private FbxProperty Property(ref long p, long limit)
        {
            if (p >= limit) throw Malformed(p);
            char type = (char)data[p];
            long at = p + 1;
            switch (type)
            {
                case 'Y': Need(at, 2, limit); p = at + 2; return new FbxProperty(type, BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan((int)at)));
                case 'C': Need(at, 1, limit); p = at + 1; return new FbxProperty(type, data[at] != 0 ? 1 : 0);
                case 'I': Need(at, 4, limit); p = at + 4; return new FbxProperty(type, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan((int)at)));
                case 'L': Need(at, 8, limit); p = at + 8; return new FbxProperty(type, BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan((int)at)));
                case 'F': Need(at, 4, limit); p = at + 4; return new FbxProperty(type, (double)BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan((int)at)));
                case 'D': Need(at, 8, limit); p = at + 8; return new FbxProperty(type, BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan((int)at)));
                case 'f' or 'd' or 'l' or 'i' or 'b':
                {
                    Need(at, 12, limit);
                    uint count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)at));
                    uint encoding = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)at + 4));
                    uint length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)at + 8));
                    long start = at + 12;
                    Need(start, length, limit);
                    if (encoding > 1 || count > int.MaxValue) throw Malformed(at);
                    p = start + length;
                    return new FbxProperty(type, data, (int)start, (int)length, (int)count, encoding == 1);
                }
                case 'S' or 'R':
                {
                    Need(at, 4, limit);
                    uint length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)at));
                    long start = at + 4;
                    Need(start, length, limit);
                    p = start + length;
                    return type == 'S'
                        ? new FbxProperty(Encoding.UTF8.GetString(data, (int)start, (int)length))
                        : new FbxProperty(type, data, (int)start, (int)length, 0, false);
                }
                default:
                    throw Malformed(p);
            }
        }

        private static void Need(long at, long length, long limit)
        {
            if (length > limit - at) throw Malformed(at);
        }
    }
}
