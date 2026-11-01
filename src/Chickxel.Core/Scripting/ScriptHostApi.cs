using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;
using Jint;
using Jint.Native;
using Jint.Native.Array;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;

namespace Chickxel.Core.Scripting;

/// <summary>
/// 렌더 함수에 넘기는 host API(13.3). 그림은 호출마다 한 번에 넣고 한 번에 꺼낸다.
/// <para>
/// <c>img.data</c>는 RGBA 순서의 JS 배열(길이 = 너비 × 높이 × 4)이다. 7단계 첫 측정(256², Jint 4.17)에서 JS 배열이 typed array와
/// 픽셀마다 호스트 호출보다 빨랐다. 렌더가 끝나면 배열 값을 <c>Uint8ClampedArray</c> 규칙(NaN = 0, 0~255로 자르기, 가운데는 짝수로 반올림)으로
/// 그림에 되쓴다. 숫자가 아닌 값이나 길이 변경은 오류다.
/// </para>
/// </summary>
internal static class ScriptHostApi
{
    private const PropertyFlag Fixed = PropertyFlag.Enumerable; // 쓰기·지우기 불가, 열거 가능

    /// <summary>전역 도우미(<c>hsvToRgb</c>, <c>rgbToHsv</c>)를 넣고 그림 원형을 만든다. <c>lerp</c>는 JS로 잠금 단계에서 넣는다.</summary>
    public static ObjectInstance Install(ScriptSandbox sandbox)
    {
        var engine = sandbox.Engine;
        // Color32와 같은 계산(float)이라 기본 효과와 결과가 같다
        engine.SetValue("hsvToRgb", new ClrFunction(engine, "hsvToRgb", (_, args) =>
        {
            byte alpha = args.Length > 3 && !args[3].IsUndefined() ? ToByte(TypeConverter.ToNumber(args[3])) : (byte)255;
            var color = Color32.FromHsv((float)Number(args, 0), (float)Number(args, 1), (float)Number(args, 2), alpha);
            return ColorObject(engine, color, frozen: false);
        }, 3));
        engine.SetValue("rgbToHsv", new ClrFunction(engine, "rgbToHsv", (_, args) =>
        {
            var (h, s, v) = new Color32(ToByte(Number(args, 0)), ToByte(Number(args, 1)), ToByte(Number(args, 2))).ToHsv();
            var hsv = new JsObject(engine);
            hsv.CreateDataProperty("h", h);
            hsv.CreateDataProperty("s", s);
            hsv.CreateDataProperty("v", v);
            return hsv;
        }, 3));
        // OKLab(Pixelate.OkLab과 같은 float 계산 — 외곽선 명암 등 기본 효과와 같은 결과): rgbToOklab은 { l, a, b }, oklabToRgb는 sRGB 밖이면 채도만 줄인 색
        engine.SetValue("rgbToOklab", new ClrFunction(engine, "rgbToOklab", (_, args) =>
        {
            var lab = Fast.JsOps.RgbToOklab(Number(args, 0), Number(args, 1), Number(args, 2));
            var result = new JsObject(engine);
            result.CreateDataProperty("l", lab.L);
            result.CreateDataProperty("a", lab.A);
            result.CreateDataProperty("b", lab.B);
            return result;
        }, 3));
        engine.SetValue("oklabToRgb", new ClrFunction(engine, "oklabToRgb", (_, args)
            => ColorObject(engine, Fast.JsOps.OklabToRgb(Number(args, 0), Number(args, 1), Number(args, 2)), frozen: false), 3));

        var prototype = new JsObject(engine);
        prototype.DefineOwnProperty("blank", new PropertyDescriptor(new ClrFunction(engine, "blank", (_, args) =>
        {
            int w = TypeConverter.ToInt32(Arg(args, 0)), h = TypeConverter.ToInt32(Arg(args, 1));
            if (w < 1 || h < 1 || w > PixelImage.MaxSize || h > PixelImage.MaxSize || (long)w * h * 4 > sandbox.Limits.MaxArrayLength)
                throw new JavaScriptException(engine.Intrinsics.TypeError,
                    DomainStrings.FormatScript_InvalidBlankSize(max: PixelImage.MaxSize, pixels: sandbox.Limits.MaxArrayLength / 4));
            var zero = JsNumber.Create(0);
            var values = new JsValue[w * h * 4];
            Array.Fill(values, zero);
            return Image(sandbox, w, h, new JsArray(engine, values));
        }, 2), Fixed));
        prototype.DefineOwnProperty("blendOver", new PropertyDescriptor(new ClrFunction(engine, "blendOver", (self, args) =>
        {
            BlendOver(engine, ReadImage(engine, self), ReadImage(engine, Arg(args, 0)),
                TypeConverter.ToInt32(Arg(args, 1)), TypeConverter.ToInt32(Arg(args, 2)));
            return self;
        }, 3), Fixed));
        prototype.PreventExtensions();
        return prototype;
    }

    /// <summary><paramref name="image"/>의 픽셀을 담은 <c>img</c>와 그 <c>data</c> 배열.</summary>
    public static (ObjectInstance Image, ArrayInstance Data) CreateImage(ScriptSandbox sandbox, PixelImage image)
    {
        var data = ToArray(sandbox.Engine, image);
        return (Image(sandbox, image.Width, image.Height, data), data);
    }

    /// <summary>렌더가 끝난 <paramref name="data"/>를 <paramref name="image"/>에 되쓴다. 값이 잘못되면 그림은 그대로 두고 오류.</summary>
    public static void CopyBack(ArrayInstance data, PixelImage image)
    {
        var px = image.Pixels;
        uint length = (uint)px.Length * 4;
        if (Length(data) != length) throw BadData(length, Length(data));
        var result = new Color32[px.Length];
        for (int i = 0; i < result.Length; i++)
        {
            uint at = (uint)i * 4;
            result[i] = new Color32(DataByte(data, at, length), DataByte(data, at + 1, length), DataByte(data, at + 2, length), DataByte(data, at + 3, length));
        }
        result.CopyTo(px, 0);
    }

    /// <summary>렌더 함수의 세 번째 인자 <c>s</c>: 설정 키 → 값(고정된 객체).</summary>
    public static ObjectInstance CreateSettings(Engine engine, IReadOnlyList<ScriptSetting> schema, IReadOnlyDictionary<string, object> values)
    {
        var settings = new JsObject(engine);
        foreach (var setting in schema)
        {
            var value = values[setting.Key];
            JsValue js = value switch
            {
                int i => JsNumber.Create(i),
                double d => JsNumber.Create(d),
                bool b => b ? JsBoolean.True : JsBoolean.False,
                Color32 c => ColorObject(engine, c, frozen: true),
                IReadOnlyList<Color32> colors => FixedArray(engine, colors.Select(c => (JsValue)ColorObject(engine, c, frozen: true))),
                IReadOnlyList<ScriptColorPair> pairs => FixedArray(engine, pairs.Select(p =>
                {
                    var pair = new JsObject(engine);
                    pair.DefineOwnProperty("from", new PropertyDescriptor(ColorObject(engine, p.From, frozen: true), Fixed));
                    pair.DefineOwnProperty("to", new PropertyDescriptor(ColorObject(engine, p.To, frozen: true), Fixed));
                    pair.PreventExtensions();
                    return (JsValue)pair;
                })),
                _ => new JsString((string)value),
            };
            settings.DefineOwnProperty(setting.Key, new PropertyDescriptor(js, Fixed));
        }
        settings.PreventExtensions();
        return settings;
    }

    // 고정된 배열(항목을 바꾸거나 더하거나 지울 수 없음)
    private static ArrayInstance FixedArray(Engine engine, IEnumerable<JsValue> items)
    {
        var array = new JsArray(engine, [.. items]);
        for (uint i = 0, n = Length(array); i < n; i++)
            array.DefineOwnProperty(i, new PropertyDescriptor(array[i], Fixed));
        array.PreventExtensions();
        return array;
    }

    /// <summary>
    /// 렌더 함수의 두 번째 인자 <c>ctx</c>. <paramref name="settings"/>는 <see cref="CreateSettings"/>로 만든 <c>s</c>,
    /// <paramref name="image"/>는 <c>img</c> 크기(다른 레이어 그림을 같은 크기·같은 자리로 맞춘다), <paramref name="colors"/>는 색 설정 읽기,
    /// <paramref name="deferred"/>는 미루는 스크립트인지(<c>ctx.renderedFrameSource</c>를 줄지).
    /// </summary>
    public static ObjectInstance CreateContext(ScriptSandbox sandbox, IReadOnlyList<ScriptSetting> schema, ObjectInstance settings,
        Rectangle source, ScriptFrameContext frame, Size image, ScriptColors colors, bool deferred = false)
    {
        var engine = sandbox.Engine;
        var ctx = new JsObject(engine);
        var rect = new JsObject(engine);
        Define(rect, "x", source.X);
        Define(rect, "y", source.Y);
        Define(rect, "w", source.Width);
        Define(rect, "h", source.Height);
        rect.PreventExtensions();
        var (pivotX, pivotY) = LayerEffects.PivotIn(frame.Pivot, source);
        var pivot = new JsObject(engine);
        Define(pivot, "x", pivotX);
        Define(pivot, "y", pivotY);
        pivot.PreventExtensions();
        // 효과 원본 가운데(그림 좌표): 피벗이 없을 때의 피벗 자리와 같다
        var (centerX, centerY) = LayerEffects.PivotIn(null, source);
        var center = new JsObject(engine);
        Define(center, "x", centerX);
        Define(center, "y", centerY);
        center.PreventExtensions();
        ctx.DefineOwnProperty("source", new PropertyDescriptor(rect, Fixed));
        ctx.DefineOwnProperty("pivot", new PropertyDescriptor(pivot, Fixed));
        ctx.DefineOwnProperty("center", new PropertyDescriptor(center, Fixed));
        ctx.DefineOwnProperty("slice", new PropertyDescriptor(frame.Slice is { } border ? SliceObject(engine, border) : JsValue.Null, Fixed));
        Define(ctx, "t", frame.Time);
        Define(ctx, "phase", frame.Phase);
        Define(ctx, "progress", frame.Progress);
        Define(ctx, "frame", frame.FrameIndex);
        Define(ctx, "step", frame.FrameStep);
        Define(ctx, "span", frame.FrameSpan);

        // 다른 프레임 원본: 부를 때마다 새 사본이라 고쳐도 원본과 다음 호출에 닿지 않는다
        var frameSource = frame.FrameSource;
        Method(ctx, engine, "frameSource", 1, args =>
            frameSource?.Invoke(TypeConverter.ToInt32(Arg(args, 0))) is { } other ? CreateImage(sandbox, other).Image : JsValue.Null);

        // 다른 레이어: 레이어 고르기 설정(type: "layer")의 키로 그 설정에 고른 레이어를 읽는다. img와 같은 크기·같은 자리, 부를 때마다 새 사본.
        // 모르는 키·레이어 설정이 아닌 키·비운 값·없는 레이어·프로젝트 밖 그리기는 null(효과 이름은 빈 배열) — 오류를 내지 않는다
        var layers = frame.Layers;
        string? Chosen(JsValue key)
        {
            if (layers is null || key is not JsString) return null;
            string name = key.AsString();
            foreach (var setting in schema)
                if (setting.Type == ScriptSettingType.Layer && setting.Key == name)
                    return settings.Get(name) is JsString chosen && chosen.AsString() is { Length: > 0 } text ? text : null;
            return null;
        }
        JsValue Layer(JsValue[] args, bool withEffects)
            => Chosen(Arg(args, 0)) is { } name && layers!.Named(name, withEffects, image.Width, image.Height, source) is { } other
                ? CreateImage(sandbox, other).Image
                : JsValue.Null;
        Method(ctx, engine, "layer", 1, args => Layer(args, false));
        Method(ctx, engine, "layerWithEffects", 1, args => Layer(args, true));
        Method(ctx, engine, "layerEffects", 1, args => Strings(engine, Chosen(Arg(args, 0)) is { } name ? layers!.EffectsByName(name) : []));

        // 미루는 스크립트만: 다른 프레임에 이 레이어의 다른 효과를 입힌 그림 { image, offsetX, offsetY }(원본 대비 위치). 원본이 없거나 크기가 다르면 null
        var rendered = frame.RenderedFrameSource;
        Method(ctx, engine, "renderedFrameSource", 1, args =>
        {
            if (!deferred || frameSource is null) return JsValue.Null;
            int index = TypeConverter.ToInt32(Arg(args, 0));
            if (frameSource(index) is not { } raw || raw.Width != source.Width || raw.Height != source.Height) return JsValue.Null;
            var (picture, offsetX, offsetY) = rendered?.Invoke(index, raw) ?? (raw, 0, 0);
            var result = new JsObject(engine);
            result.DefineOwnProperty("image", new PropertyDescriptor(CreateImage(sandbox, picture).Image, Fixed));
            Define(result, "offsetX", offsetX);
            Define(result, "offsetY", offsetY);
            result.PreventExtensions();
            return result;
        });

        // 색 설정: 그라데이션이 없으면 s의 색 객체 그대로, 있으면 (x, y)에서 고른 새 색 객체(EffectGradientSampler — 기본 효과와 같은 계산)
        int Setting(JsValue key, ScriptSettingType type, Func<string, string> unknown)
        {
            var name = TypeConverter.ToString(key);
            int index = colors.IndexOf(name, type);
            return index >= 0 ? index : throw new JavaScriptException(engine.Intrinsics.TypeError, unknown(name));
        }
        int Item(int setting, JsValue value)
        {
            double number = TypeConverter.ToNumber(value);
            int item = number == Math.Floor(number) && number is >= int.MinValue and <= int.MaxValue ? (int)number : -1;
            return colors.Has(setting, item) ? item
                : throw new JavaScriptException(engine.Intrinsics.TypeError,
                    DomainStrings.FormatScript_InvalidColorIndex(key: schema[setting].Key, index: TypeConverter.ToString(value)));
        }
        JsValue Sample(int setting, int item, bool to, JsValue[] args, int at, Func<JsValue> solid)
        {
            int x = TypeConverter.ToInt32(Arg(args, at)), y = TypeConverter.ToInt32(Arg(args, at + 1));
            var boundsValue = Arg(args, at + 2);
            Rectangle? bounds = boundsValue.IsUndefined() ? null : ReadBounds(engine, boundsValue);
            if (!colors.HasGradient(setting, item, to)) return solid();
            return ColorObject(engine, bounds is { } b ? colors.Sample(setting, item, to, x, y, b) : colors.Sample(setting, item, to, x, y), frozen: true);
        }
        Method(ctx, engine, "color", 3, args =>
        {
            int setting = Setting(Arg(args, 0), ScriptSettingType.Color, key => DomainStrings.FormatScript_UnknownColorSetting(key: key));
            return Sample(setting, -1, false, args, 1, () => settings.Get(schema[setting].Key));
        });
        Method(ctx, engine, "colorAt", 4, args =>
        {
            int setting = Setting(Arg(args, 0), ScriptSettingType.ColorList, key => DomainStrings.FormatScript_UnknownColorListSetting(key: key));
            int item = Item(setting, Arg(args, 1));
            return Sample(setting, item, false, args, 2, () => ((ObjectInstance)settings.Get(schema[setting].Key)).Get(item.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        });
        Method(ctx, engine, "pairColor", 5, args =>
        {
            int setting = Setting(Arg(args, 0), ScriptSettingType.ColorPairs, key => DomainStrings.FormatScript_UnknownColorPairSetting(key: key));
            int item = Item(setting, Arg(args, 1));
            var part = Arg(args, 2);
            bool to = part is JsString && part.AsString() == "to";
            if (!to && !(part is JsString && part.AsString() == "from"))
                throw new JavaScriptException(engine.Intrinsics.TypeError, DomainStrings.FormatScript_InvalidPairPart(part: TypeConverter.ToString(part)));
            return Sample(setting, item, to, args, 3, () => ((ObjectInstance)((ObjectInstance)settings.Get(schema[setting].Key))
                .Get(item.ToString(System.Globalization.CultureInfo.InvariantCulture))).Get(to ? "to" : "from"));
        });
        // ctx.hasGradient("슬롯") 또는 목록 항목을 번호로: ctx.hasGradient("목록 키", 번호[, "from"|"to"]) — 정수 항목 번호가 아니면 false
        Method(ctx, engine, "hasGradient", 1, args =>
        {
            var slot = TypeConverter.ToString(Arg(args, 0));
            if (args.Length > 1 && !args[1].IsUndefined())
            {
                int list = colors.IndexOf(slot, ScriptSettingType.ColorList), pairs = colors.IndexOf(slot, ScriptSettingType.ColorPairs);
                if (list < 0 && pairs < 0)
                    throw new JavaScriptException(engine.Intrinsics.TypeError, DomainStrings.FormatScript_UnknownColorSlot(slot: slot));
                var part = Arg(args, 2);
                bool toPart = part is JsString && part.AsString() == "to";
                if (pairs >= 0 ? !toPart && !(part is JsString && part.AsString() == "from") : !part.IsUndefined())
                    throw new JavaScriptException(engine.Intrinsics.TypeError, DomainStrings.FormatScript_InvalidPairPart(part: TypeConverter.ToString(part)));
                return Fast.JsOps.HasGradientAt(colors, Math.Max(list, pairs), TypeConverter.ToNumber(args[1]), toPart) ? JsBoolean.True : JsBoolean.False;
            }
            if (!ScriptColorSlots.TryParse(schema, slot, out var setting, out int item, out bool to))
                throw new JavaScriptException(engine.Intrinsics.TypeError, DomainStrings.FormatScript_UnknownColorSlot(slot: slot));
            int index = colors.IndexOf(setting.Key, setting.Type);
            return colors.Has(index, item) && colors.HasGradient(index, item, to) ? JsBoolean.True : JsBoolean.False;
        });

        // 무작위: 위치·시드·용도(salt, 기본 0) 해시(프레임과 무관)와 프레임·시드 값. 계산 순서에 따라 바뀌는 생성기는 두지 않는다(12.4)
        Method(ctx, engine, "hash", 3, args => LayerEffects.Random(
            TypeConverter.ToInt32(Arg(args, 2)), TypeConverter.ToInt32(Arg(args, 0)), TypeConverter.ToInt32(Arg(args, 1)), TypeConverter.ToInt32(Arg(args, 3))));
        int frameIndex = frame.FrameIndex;
        Method(ctx, engine, "random", 1, args => LayerEffects.Random(TypeConverter.ToInt32(Arg(args, 0)), frameIndex, 0, 1));
        // 순서 디더 문턱값(기본 효과의 Bayer와 같음): 2×2는 0~3, 4×4는 0~15
        Method(ctx, engine, "bayer2", 2, args => Bayer.Threshold2(TypeConverter.ToInt32(Arg(args, 0)), TypeConverter.ToInt32(Arg(args, 1))));
        Method(ctx, engine, "bayer4", 2, args => Bayer.Threshold4(TypeConverter.ToInt32(Arg(args, 0)), TypeConverter.ToInt32(Arg(args, 1))));
        ctx.PreventExtensions();
        return ctx;
    }

    // 그라데이션 범위 { x, y, w, h }(정수로 — ToInt32)
    private static Rectangle ReadBounds(Engine engine, JsValue value)
    {
        if (value is not ObjectInstance obj || value.IsCallable())
            throw new JavaScriptException(engine.Intrinsics.TypeError, DomainStrings.Script_InvalidBounds);
        int x = TypeConverter.ToInt32(obj.Get("x")), y = TypeConverter.ToInt32(obj.Get("y"));
        int w = TypeConverter.ToInt32(obj.Get("w")), h = TypeConverter.ToInt32(obj.Get("h"));
        return new Rectangle(x, y, w, h);
    }

    private static ObjectInstance Image(ScriptSandbox sandbox, int width, int height, ArrayInstance data)
    {
        var image = new JsObject(sandbox.Engine);
        image.Prototype = sandbox.ImagePrototype;
        Define(image, "width", width);
        Define(image, "height", height);
        image.DefineOwnProperty("data", new PropertyDescriptor(data, Fixed));
        return image;
    }

    private static ArrayInstance ToArray(Engine engine, PixelImage image)
    {
        var px = image.Pixels;
        var values = new JsValue[px.Length * 4];
        for (int i = 0, j = 0; i < px.Length; i++, j += 4)
        {
            var c = px[i];
            values[j] = JsNumber.Create(c.R);
            values[j + 1] = JsNumber.Create(c.G);
            values[j + 2] = JsNumber.Create(c.B);
            values[j + 3] = JsNumber.Create(c.A);
        }
        return new JsArray(engine, values);
    }

    private readonly record struct ImageView(int Width, int Height, ArrayInstance Data);

    private static ImageView ReadImage(Engine engine, JsValue value)
    {
        if (value is ObjectInstance obj && obj.Get("data") is ArrayInstance data
            && obj.Get("width") is JsNumber w && obj.Get("height") is JsNumber h)
        {
            double width = w.AsNumber(), height = h.AsNumber();
            if (width >= 1 && height >= 1 && width == Math.Floor(width) && height == Math.Floor(height) && Length(data) == width * height * 4)
                return new ImageView((int)width, (int)height, data);
        }
        throw new JavaScriptException(engine.Intrinsics.TypeError, DomainStrings.Script_InvalidImageArgument);
    }

    // Color32.BlendOver로 src를 dst의 (dx, dy)에 겹친다(기본 효과의 알파 합성과 같은 결과)
    private static void BlendOver(Engine engine, ImageView dst, ImageView src, int dx, int dy)
    {
        int x0 = Math.Max(0, -dx), y0 = Math.Max(0, -dy);
        int x1 = (int)Math.Min(src.Width, (long)dst.Width - dx), y1 = (int)Math.Min(src.Height, (long)dst.Height - dy);
        for (int sy = y0; sy < y1; sy++)
            for (int sx = x0; sx < x1; sx++)
            {
                var top = ReadPixel(engine, src.Data, (uint)(sy * src.Width + sx) * 4);
                if (top.A == 0) continue;
                uint at = (uint)((sy + dy) * dst.Width + sx + dx) * 4;
                var blended = Color32.BlendOver(ReadPixel(engine, dst.Data, at), top);
                dst.Data[at] = JsNumber.Create(blended.R);
                dst.Data[at + 1] = JsNumber.Create(blended.G);
                dst.Data[at + 2] = JsNumber.Create(blended.B);
                dst.Data[at + 3] = JsNumber.Create(blended.A);
            }
    }

    private static Color32 ReadPixel(Engine engine, ArrayInstance data, uint at)
    {
        byte Channel(uint i) => data[i] is JsNumber n
            ? ToByte(n.AsNumber())
            : throw new JavaScriptException(engine.Intrinsics.TypeError, DomainStrings.FormatScript_InvalidImageData(length: Length(data), index: i));
        return new Color32(Channel(at), Channel(at + 1), Channel(at + 2), Channel(at + 3));
    }

    private static byte DataByte(ArrayInstance data, uint index, uint length)
        => data[index] is JsNumber n ? ToByte(n.AsNumber()) : throw BadData(length, index);

    private static ScriptEffectException BadData(uint length, uint index)
        => new(new(ScriptErrorKind.Runtime, DomainStrings.FormatScript_InvalidImageData(length: length, index: index), 0, 0));

    /// <summary>Uint8ClampedArray 규칙: NaN = 0, 0~255로 자르고 가운데 값은 짝수 쪽으로 반올림.</summary>
    internal static byte ToByte(double value)
        => double.IsNaN(value) ? (byte)0 : (byte)Math.Round(Math.Clamp(value, 0, 255), MidpointRounding.ToEven);

    private static ObjectInstance ColorObject(Engine engine, Color32 color, bool frozen)
    {
        var obj = new JsObject(engine);
        foreach (var (name, channel) in new[] { ("r", color.R), ("g", color.G), ("b", color.B), ("a", color.A) })
        {
            if (frozen) Define(obj, name, channel);
            else obj.CreateDataProperty(name, JsNumber.Create(channel));
        }
        if (frozen) obj.PreventExtensions();
        return obj;
    }

    // ctx.slice: 9-slice 테두리 { left, top, right, bottom }(고정된 객체)
    private static ObjectInstance SliceObject(Engine engine, SpriteBorder border)
    {
        var obj = new JsObject(engine);
        Define(obj, "left", border.Left);
        Define(obj, "top", border.Top);
        Define(obj, "right", border.Right);
        Define(obj, "bottom", border.Bottom);
        obj.PreventExtensions();
        return obj;
    }

    // 문자열 배열(부를 때마다 새 배열)
    private static ArrayInstance Strings(Engine engine, IReadOnlyList<string> values)
    {
        var items = new JsValue[values.Count];
        for (int i = 0; i < items.Length; i++) items[i] = new JsString(values[i]);
        return new JsArray(engine, items);
    }

    private static void Define(ObjectInstance obj, string name, double value)
        => obj.DefineOwnProperty(name, new PropertyDescriptor(JsNumber.Create(value), Fixed));

    private static void Method(ObjectInstance obj, Engine engine, string name, int length, Func<JsValue[], JsValue> body)
        => obj.DefineOwnProperty(name, new PropertyDescriptor(new ClrFunction(engine, name, (_, args) => body(args), length), Fixed));

    /// <summary>배열의 <c>length</c>(스크립트가 바꿀 수 있으므로 그때그때 읽는다).</summary>
    public static uint Length(ArrayInstance array) => (uint)TypeConverter.ToNumber(array.Get("length"));

    private static JsValue Arg(JsValue[] args, int index) => index < args.Length ? args[index] : JsValue.Undefined;

    private static double Number(JsValue[] args, int index) => TypeConverter.ToNumber(Arg(args, index));
}
