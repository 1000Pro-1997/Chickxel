using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using Chickxel.Core.Text;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;

namespace Chickxel.Core.Scripting;

/// <summary>Renders ctx.text with the same font layout and rasterization as the built-in text effect.</summary>
internal static class ScriptText
{
    public static PixelImage Render(Engine engine, JsValue text, JsValue options, uint maxArrayLength)
    {
        JavaScriptException BadArgument() => new(engine.Intrinsics.TypeError, Chickxel.Core.Localization.DomainStrings.Script_InvalidTextOptions);
        if (text is not JsString value || value.AsString().Length > TextEffect.MaxLength)
            throw BadArgument();
        if (!options.IsUndefined() && (options is not ObjectInstance || options.IsCallable()))
            throw BadArgument();
        var obj = options as ObjectInstance;
        JsValue Get(string name) => obj?.Get(name) ?? JsValue.Undefined;
        string String(string name, string fallback, int maxLength)
        {
            var v = Get(name);
            if (v.IsUndefined()) return fallback;
            if (v is not JsString s || s.AsString().Length > maxLength) throw BadArgument();
            return s.AsString();
        }
        int Int(string name, int fallback, int min, int max)
        {
            var v = Get(name);
            if (v.IsUndefined()) return fallback;
            if (v is not JsNumber n || !double.IsFinite(n.AsNumber())) throw BadArgument();
            return (int)Math.Clamp(Math.Truncate(n.AsNumber()), min, max);
        }
        bool Bool(string name, bool fallback)
        {
            var v = Get(name);
            if (v.IsUndefined()) return fallback;
            if (!v.IsBoolean()) throw BadArgument();
            return v.AsBoolean();
        }
        Color32 Color()
        {
            var v = Get("color");
            if (v.IsUndefined()) return Color32.Black;
            if (v is JsString s && Color32.TryParseHex(s.AsString(), out var hex)) return hex;
            if (v is ObjectInstance color && !v.IsCallable())
            {
                byte Channel(string name, byte fallback)
                {
                    var channel = color.Get(name);
                    if (channel.IsUndefined()) return fallback;
                    if (channel is not JsNumber number || !double.IsFinite(number.AsNumber())) throw BadArgument();
                    return ScriptHostApi.ToByte(number.AsNumber());
                }
                return new Color32(Channel("r", 0), Channel("g", 0), Channel("b", 0), Channel("a", 255));
            }
            throw BadArgument();
        }
        string alignName = String("align", "left", 6);
        var align = alignName switch
        {
            "left" => TextAlign.Left,
            "center" => TextAlign.Center,
            "right" => TextAlign.Right,
            _ => throw BadArgument(),
        };
        var effect = new TextEffect
        {
            Text = value.AsString(),
            FontName = String("font", FontLibrary.DefaultName, TextEffect.MaxFontNameLength),
            Size = Int("size", 0, 0, TextEffect.MaxSize),
            Color = Color(),
            Bold = Bool("bold", false),
            Italic = Bool("italic", false),
            Smooth = Bool("smooth", false),
            Align = align,
            LetterSpacing = Int("letterSpacing", 0, TextEffect.MinLetterSpacing, TextEffect.MaxLetterSpacing),
            LineSpacing = Int("lineSpacing", 0, TextEffect.MinLineSpacing, TextEffect.MaxLineSpacing),
            BoxWidth = Int("width", 0, 0, TextEffect.MaxBoxSize),
        };
        // Retain the text box (including leading whitespace) and any glyphs extending outside it.
        var layout = TextLayout.Measure(effect);
        Rectangle bounds = layout.Ink.IsEmpty ? layout.Frame : Rectangle.Union(layout.Frame, layout.Ink);
        int width = Math.Max(1, bounds.Width), height = Math.Max(1, bounds.Height);
        if (width > PixelImage.MaxSize || height > PixelImage.MaxSize || (long)width * height * 4 > maxArrayLength)
            throw new JavaScriptException(engine.Intrinsics.TypeError,
                Chickxel.Core.Localization.DomainStrings.FormatScript_InvalidTextSize(max: PixelImage.MaxSize, pixels: maxArrayLength / 4));
        var image = new PixelImage(width, height);
        TextRender.Apply(image, effect with { X = -bounds.X, Y = -bounds.Y }, image.Bounds);
        return image;
    }
}
