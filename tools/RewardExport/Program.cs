using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chickxel.Core.Community;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

// 레벨 보상을 홈페이지(Chickxel-server)용으로 내보낸다:
//   dotnet run --project tools/RewardExport -- [출력 폴더]
// 출력 폴더(기본 ../Chickxel-server/public/static/rewards)에 catalog.json과 스프라이트 시트 PNG — 서버의 장착 검사와 홈페이지 그리기가 함께 읽는다.
// 손으로 고치지 않는다(Core의 LevelRewards·픽셀 그림이 원본). 홈페이지 크기: 작은 테두리 22·28·36px(2배), 큰 테두리·효과 56·72px(4배), 카드 높이 120px(3배).

string output = args.Length > 0 ? args[0] : Path.Combine("..", "Chickxel-server", "public", "static", "rewards");
Directory.CreateDirectory(output);

var ko = CultureInfo.GetCultureInfo("ko");
var en = CultureInfo.GetCultureInfo("en");
JsonObject Names(Func<CultureInfo, string> name) => new() { ["ko"] = name(ko), ["en"] = name(en) };
string Hex(Color32 c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
string Key(int n) => n.ToString(CultureInfo.InvariantCulture);

// ───────────── 목록 ─────────────

var slotNames = new JsonObject();
foreach (var slot in Enum.GetValues<RewardSlot>())
    slotNames[LevelRewards.SlotKey(slot)] = Names(c => LevelRewards.SlotNameIn(slot, c));

var rewards = new JsonArray();
foreach (var reward in LevelRewards.All)
{
    var item = new JsonObject
    {
        ["id"] = reward.Id,
        ["level"] = reward.Level,
        ["slot"] = LevelRewards.SlotKey(reward.Slot),
        ["name"] = Names(reward.NameIn),
    };
    if (reward.Slot == RewardSlot.Title)
    {
        item["color"] = Hex(LevelRewards.TitleColor(reward));
        if (LevelRewards.IsRainbowTitle(reward)) item["rainbow"] = true;
    }
    rewards.Add(item);
}

// ───────────── 스프라이트 시트 ─────────────

const int Columns = 12;
foreach (var old in Directory.EnumerateFiles(output, "*.png")) File.Delete(old);

// 그림의 모든 프레임(또는 겹 하나)을 한 장에 줄지어 넣고 Sprite JSON을 돌려준다.
JsonObject Sheet(IPixelArt art, string file, int scale, Action<int, Color32[]>? render = null)
{
    int frames = art.Frames, columns = Math.Min(Columns, frames), rows = (frames + columns - 1) / columns;
    var sheet = new PixelImage(art.Width * columns, art.Height * rows);
    var buffer = new Color32[art.Width * art.Height];
    for (int f = 0; f < frames; f++)
    {
        if (render is null) art.Render(f, buffer);
        else render(f, buffer);
        int ox = f % columns * art.Width, oy = f / columns * art.Height;
        for (int y = 0; y < art.Height; y++)
            Array.Copy(buffer, y * art.Width, sheet.Pixels, (oy + y) * sheet.Width + ox, art.Width);
    }
    ImageWriter.Save(sheet, Path.Combine(output, file), ExportImageFormat.Png);
    return new JsonObject
    {
        ["file"] = file, ["frameWidth"] = art.Width, ["frameHeight"] = art.Height, ["frames"] = frames, ["columns"] = columns, ["scale"] = scale,
        ["box"] = new JsonObject { ["x"] = art.Box.X, ["y"] = art.Box.Y, ["width"] = art.Box.Width, ["height"] = art.Box.Height },
    };
}

JsonObject BySize(int[] sizes, Func<int, JsonObject> sheet)
{
    var o = new JsonObject();
    foreach (int px in sizes) o[Key(px)] = sheet(px);
    return o;
}

var font = new JsonObject();
foreach (var (ch, bits) in LevelBadgeArt.Font) font[ch.ToString()] = bits;

var badges = new JsonObject();
foreach (var skin in BadgeSkin.All)
{
    var layouts = new JsonObject();
    for (int digits = 1; digits <= 4; digits++)
    {
        var art = new LevelBadgeArt((int)Math.Pow(10, digits - 1), skin.Id);
        layouts[Key(digits)] = new JsonObject
        {
            ["under"] = Sheet(art, $"{skin.Id}-{digits}-under.png", 2, (f, px) => art.RenderUnder(f, px)),
            ["over"] = Sheet(art, $"{skin.Id}-{digits}-over.png", 2, (f, px) => { Array.Clear(px); art.RenderOver(f, px); }),
        };
    }
    badges[skin.Id] = new JsonObject { ["text"] = Hex(skin.Text), ["shadow"] = Hex(skin.Shadow), ["layouts"] = layouts };
}

var frames = new JsonObject();
var effects = new JsonObject();
var cards = new JsonObject();
foreach (var reward in LevelRewards.All)
{
    switch (reward.Slot)
    {
        case RewardSlot.Frame:
            // 같은 테두리의 작은 모양(22·28·36px, 2배)과 큰 모양(56·72px, 4배)
            var sizes = BySize([22, 28, 36], px => Sheet(new AvatarFrameArt(reward.Id, px / 2, large: false), $"{reward.Id}-{px}.png", 2));
            foreach (int px in new[] { 56, 72 })
                sizes[Key(px)] = Sheet(new AvatarFrameArt(reward.Id, px / 4, large: true), $"{reward.Id}-{px}.png", 4);
            frames[reward.Id] = sizes;
            break;
        case RewardSlot.Effect:
            effects[reward.Id] = BySize([56, 72], px => Sheet(new AvatarEffectArt(reward.Id, px / 4), $"{reward.Id}-{px}.png", 4));
            break;
        case RewardSlot.Card:
            cards[reward.Id] = Sheet(new ProfileCardArt(reward.Id, 256, 40), $"{reward.Id}.png", 3);
            break;
    }
}

var nameStyles = new JsonObject();
foreach (var style in NameStyle.All)
    nameStyles[style.Id] = new JsonObject
    {
        ["effect"] = JsonNamingPolicy.CamelCase.ConvertName(style.Effect.ToString()),
        ["colors"] = new JsonArray(style.Colors.Select(c => (JsonNode)Hex(c)).ToArray()),
        ["periodMs"] = style.PeriodMs,
    };

var catalog = new JsonObject
{
    ["version"] = 2,
    ["maxLevel"] = LevelRewards.MaxLevel,
    ["frameMs"] = RewardArt.FrameMs,
    ["slots"] = new JsonArray(Enum.GetValues<RewardSlot>().Select(s => (JsonNode)LevelRewards.SlotKey(s)).ToArray()),
    ["slotNames"] = slotNames,
    ["rewards"] = rewards,
    ["font"] = new JsonObject { ["width"] = 3, ["height"] = 5, ["glyphs"] = font },
    ["badgeText"] = new JsonObject
    {
        ["x"] = LevelBadgeArt.TextX, ["y"] = LevelBadgeArt.TextY, ["glyphAdvance"] = LevelBadgeArt.GlyphAdvance, ["spaceAdvance"] = LevelBadgeArt.SpaceAdvance,
    },
    ["badges"] = badges,
    ["frames"] = frames,
    ["effects"] = effects,
    ["cards"] = cards,
    ["nameStyles"] = nameStyles,
};

File.WriteAllText(Path.Combine(output, "catalog.json"), catalog.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
Console.WriteLine($"{rewards.Count} rewards, {Directory.EnumerateFiles(output, "*.png").Count()} sheets → {Path.GetFullPath(output)}");
