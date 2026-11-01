using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using Chickxel.Core.Assets;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>효과 패키지 미리보기: 고른 병아리에 입힌 그림(움직이면 APNG, 같은 칸은 합침)과 같은 크기의 원본, 병아리 선택의 저장.</summary>
public class PackagePreviewTests
{
    /// <summary>PNG 덩어리 이름과 내용(서명 뒤부터 IEND 앞까지).</summary>
    private static List<(string Type, byte[] Data)> Chunks(byte[] png)
    {
        var chunks = new List<(string, byte[])>();
        for (int at = 8; at < png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            string type = Encoding.ASCII.GetString(png, at + 4, 4);
            chunks.Add((type, png.AsSpan(at + 8, length).ToArray()));
            at += 12 + length;
        }
        return chunks;
    }

    private static PixelImage Decode(byte[] png) => PngIO.Load(new MemoryStream(png));

    [Fact]
    public void StillEffect_IsOnePlainPng_AndTheOriginalIsTheChickAtTheSameSpot()
    {
        var recipe = new EffectRecipe { Name = "Outline", PreviewChick = "knight", Effects = [new OutlineEffect { Thickness = 1 }] };
        var preview = AssetPreview.Package(recipe)!;

        // 열 칸이 모두 같아 한 장으로 합쳐진다: APNG 덩어리 없이 보통 PNG
        Assert.DoesNotContain(Chunks(preview.Png), chunk => chunk.Type is "acTL" or "fcTL" or "fdAT");
        var image = Decode(preview.Png);
        var original = Decode(preview.OriginalPng!);
        Assert.Equal((image.Width, image.Height), (original.Width, original.Height));

        // 원본 = 바깥 외곽선 여백(1px)만큼 안쪽에 놓은 기사 병아리를 같은 배율로 키운 것
        var chick = PreviewChicks.Load("knight");
        int scale = AssetPreview.PngMaxSize / (chick.Width + 2);
        Assert.Equal((chick.Width + 2) * scale, original.Width);
        Assert.Equal(chick[chick.Width / 2, chick.Height / 2], original[(1 + chick.Width / 2) * scale, (1 + chick.Height / 2) * scale]);
        Assert.Equal(default, original[0, 0]);
    }

    [Fact]
    public void MovingEffect_IsAnApng_WhoseFramesLastOneSecondTogether()
    {
        var recipe = new EffectRecipe { Name = "Bob", Effects = [new BobEffect { Height = 2, Cycles = 1 }] };
        var preview = AssetPreview.Package(recipe)!;
        var chunks = Chunks(preview.Png);

        int frames = BinaryPrimitives.ReadInt32BigEndian(Assert.Single(chunks, chunk => chunk.Type == "acTL").Data);
        var controls = chunks.Where(chunk => chunk.Type == "fcTL").Select(chunk => chunk.Data).ToList();
        Assert.InRange(frames, 2, AssetPreview.PackageFrames);
        Assert.Equal(frames, controls.Count);
        Assert.Equal(frames - 1, chunks.Count(chunk => chunk.Type == "fdAT"));
        // 지연 = 분자/1000초, 합친 칸은 그만큼 길다: 모두 더하면 10칸 × 100ms
        Assert.All(controls, control => Assert.Equal(1000, BinaryPrimitives.ReadUInt16BigEndian(control.AsSpan(22))));
        Assert.Equal(AssetPreview.PackageFrames * AssetPreview.PackageFrameMs, controls.Sum(control => BinaryPrimitives.ReadUInt16BigEndian(control.AsSpan(20))));
        // 순번은 fcTL·fdAT를 통틀어 0부터 빠짐없이
        var sequence = chunks.Where(chunk => chunk.Type is "fcTL" or "fdAT").Select(chunk => BinaryPrimitives.ReadInt32BigEndian(chunk.Data)).ToArray();
        Assert.Equal(Enumerable.Range(0, sequence.Length), sequence);

        // APNG를 모르는 읽기는 첫 칸을 본다
        Assert.Equal((Decode(preview.OriginalPng!).Width, Decode(preview.OriginalPng!).Height), (Decode(preview.Png).Width, Decode(preview.Png).Height));
    }

    /// <summary>실행을 허락한 스크립트 에셋(원문 끝 주석으로 시험마다 다른 해시).</summary>
    private static ScriptAsset TrustedScript(string source, string test)
    {
        var program = ScriptEffectProgram.Parse(source + "\n// " + test + "\n").Program!;
        ScriptLibrary.Add(program.Source);
        ScriptLibrary.SetTrust(program.ContentHash, true);
        return ScriptAsset.From("", program);
    }

    [Fact]
    public void ScriptPreview_RunsTheDeclaredPreviewFrames()
    {
        // 칸마다 빨강이 달라 합쳐지는 칸이 없다: 선언한 20칸 × 100ms
        var script = TrustedScript("""
            effect({ name: "Fade", animated: true, previewFrames: 20, render(img, ctx) {
              const d = img.data; const v = Math.round(ctx.progress * 255);
              for (let i = 0; i < d.length; i += 4) if (d[i + 3] > 0) d[i] = v;
            } });
            """, nameof(ScriptPreview_RunsTheDeclaredPreviewFrames));
        Assert.Equal(20, script.PreviewFrames);
        var chunks = Chunks(AssetPreview.Package(script)!.Png);
        Assert.Equal(20, BinaryPrimitives.ReadInt32BigEndian(Assert.Single(chunks, chunk => chunk.Type == "acTL").Data));
        Assert.Equal(20 * AssetPreview.PackageFrameMs, chunks.Where(chunk => chunk.Type == "fcTL").Sum(chunk => BinaryPrimitives.ReadUInt16BigEndian(chunk.Data.AsSpan(20))));
    }

    [Fact]
    public void ScriptPreview_ThinsFramesToStayUnderThePreviewSizeLimit()
    {
        // 넓은 여백 전체를 칸마다 다른 잡음으로 채운다(잘 줄지 않음): 20칸이면 한도를 넘으므로 두 칸씩 합쳐 한도 안에, 한 바퀴 시간은 그대로
        var script = TrustedScript("""
            effect({ name: "Noise", animated: true, previewFrames: 20, padding: [60, 60, 60, 60], render(img, ctx) {
              const d = img.data; const seed = Math.round(ctx.progress * 1000) * 4;
              for (let y = 0; y < img.height; y++)
                for (let x = 0; x < img.width; x++) {
                  const i = (y * img.width + x) * 4;
                  d[i] = Math.floor(ctx.hash(x, y, seed) * 256);
                  d[i + 1] = Math.floor(ctx.hash(x, y, seed + 1) * 256);
                  d[i + 2] = Math.floor(ctx.hash(x, y, seed + 2) * 256);
                  d[i + 3] = 255;
                }
            } });
            """, nameof(ScriptPreview_ThinsFramesToStayUnderThePreviewSizeLimit));
        var png = AssetPreview.Package(script)!.Png;
        Assert.InRange(png.Length, 1, AssetPackageLimits.Default.MaxPreviewBytes);
        var chunks = Chunks(png);
        Assert.InRange(BinaryPrimitives.ReadInt32BigEndian(Assert.Single(chunks, chunk => chunk.Type == "acTL").Data), 2, 19);
        Assert.Equal(20 * AssetPreview.PackageFrameMs, chunks.Where(chunk => chunk.Type == "fcTL").Sum(chunk => BinaryPrimitives.ReadUInt16BigEndian(chunk.Data.AsSpan(20))));
        var issues = new List<AssetIssue>();
        AssetPackageValidator.CheckPreview("previews/noise.png", png, AssetPackageLimits.Default, issues);
        Assert.Empty(issues);
    }

    [Fact]
    public void Gradient_HasNoOriginal()
        => Assert.Null(AssetPreview.Package(new GradientAsset("", AssetSamples.Gradient()))!.OriginalPng);

    [Fact]
    public void RecipeChick_IsWrittenOnlyWhenChosen_AndReadBack()
    {
        var plain = new EffectRecipe { Id = "personal.a", Name = "Outline", Effects = [new OutlineEffect()] };
        var plainJson = EffectRecipeFormat.Write(plain);
        // 기본 병아리면 키를 쓰지 않아 예전 앱이 읽는 낮은 형식 그대로
        Assert.Null(plainJson["previewChick"]);
        Assert.Equal(1, (int)plainJson["schemaVersion"]!);

        var json = EffectRecipeFormat.Write(plain with { PreviewChick = "wizard" });
        Assert.Equal((EffectRecipeFormat.SchemaVersion, "wizard"), ((int)json["schemaVersion"]!, (string)json["previewChick"]!));
        var context = new AssetReadContext("recipe.json", strict: true);
        Assert.Equal("wizard", EffectRecipeFormat.Read(JsonNode.Parse(json.ToJsonString()), context)!.PreviewChick);
        Assert.Empty(context.Issues);

        // 다음 버전의 병아리(모르는 ID)도 읽고 기본 병아리로 그린다; 모양이 틀린 ID는 거부
        json["previewChick"] = "future-chick";
        Assert.Equal("future-chick", EffectRecipeFormat.Read(JsonNode.Parse(json.ToJsonString()), new AssetReadContext("recipe.json", strict: true))!.PreviewChick);
        Assert.Equal(PreviewChicks.Load(PreviewChicks.Default).Pixels, PreviewChicks.Load("future-chick").Pixels);
        json["previewChick"] = "Not A Chick";
        var bad = new AssetReadContext("recipe.json", strict: true);
        Assert.Null(EffectRecipeFormat.Read(JsonNode.Parse(json.ToJsonString()), bad));
        Assert.Contains(bad.Issues, issue => issue.Detail == "previewChick");
    }

    [Fact]
    public void ScriptDeclaration_PreviewChick_IsOptionalAndChecked()
    {
        const string body = "render(img, ctx, s) {} })";
        Assert.Equal(PreviewChicks.Default, ScriptEffectProgram.Parse("effect({ name: \"A\", " + body).Program!.PreviewChick);
        var chosen = ScriptEffectProgram.Parse("effect({ name: \"A\", previewChick: \"ninja\", " + body).Program!;
        Assert.Equal("ninja", ScriptAsset.From("", chosen).PreviewChick);
        var wrong = ScriptEffectProgram.Parse("effect({ name: \"A\", previewChick: 3, " + body);
        Assert.Null(wrong.Program);
        Assert.NotEmpty(wrong.Errors);
    }

    [Fact]
    public void EveryListedChick_IsItsOwnEmbeddedPicture()
    {
        var images = PreviewChicks.Ids.Select(PreviewChicks.Load).ToList();
        for (int i = 0; i < images.Count; i++)
            for (int j = i + 1; j < images.Count; j++)
                Assert.False(images[i].ContentEquals(images[j]), $"{PreviewChicks.Ids[i]} = {PreviewChicks.Ids[j]}");
    }
}
