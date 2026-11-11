using Chickxel.Core.Assets;
using Chickxel.Core.IO;

namespace CatalogBuilder;

/// <summary>
/// 상점에 올리는 공식 테마 모양 펜 세트(그림은 같은 폴더의 <c>*ShapeSets.cs</c>): 실생활에서 흩뿌리거나 늘어놓은 것처럼 보이는 세트 — 크리스마스, 랜덤 박스·상자·쓰레기·거리,
/// 가로로 끄는 벽·울타리, 땅·물가, 생물·나무, 판타지 소품, 일상 소품. 목록 순서 = 올리는 순서(<see cref="LocalShapeSets"/>).
/// </summary>
internal static class ThemeShapeSets
{
    private static OfficialShapeSetPackage P(string id, string description, string nameKo, string summaryKo, Func<StampSet> create, params string[] tags)
        => new(id, "1.0.0", description, nameKo, summaryKo, create, [.. tags.Take(7), "official"]);

    public static IReadOnlyList<OfficialShapeSetPackage> Packages { get; } =
    [
        // 크리스마스
        P("christmas-presents", "Wrapped gift boxes in eight papers (stripes, dots, bands, plaid) with ribbons and bows, tall, flat, cube and small sizes plus stacked piles, for under a tree.",
            "크리스마스 선물 상자", "줄무늬·물방울·체크 포장지에 리본을 묶은 크고 작은 선물 상자와 쌓아 올린 상자 더미로, 트리 아래에 흩어 놓기 좋습니다.",
            ChristmasPartyShapeSets.Presents, "christmas", "gift", "present", "box", "holiday"),
        P("christmas-cookies-candy", "Gingerbread men, iced star, tree, bell and snowflake cookies, candy canes facing both ways, peppermint sticks and swirls, and foil-wrapped candies for festive tables.",
            "크리스마스 쿠키와 사탕", "생강 쿠키 사람, 아이싱한 별·트리·종·눈송이 쿠키, 양쪽 방향 지팡이 사탕, 박하사탕과 포장 사탕을 담았습니다.",
            ChristmasPartyShapeSets.Cookies, "christmas", "cookie", "candy", "gingerbread", "sweets"),
        P("snowmen", "Snowmen in four sizes with top hats or knit beanies, colored scarves, carrot noses, stick arms and lean variants facing both ways, plus small snow piles.",
            "눈사람", "실크해트나 털모자, 목도리, 당근 코, 나뭇가지 팔을 단 네 가지 크기의 눈사람과 작은 눈 더미입니다.",
            ChristmasPartyShapeSets.Snowmen, "christmas", "snowman", "winter", "snow"),
        P("holly-wreaths", "Pine wreaths with red or gold bows and berries in two sizes, holly sprigs facing both ways, mistletoe, red and white poinsettias and short pine garland pieces.",
            "호랑가시와 화환", "리본과 열매를 단 솔잎 화환, 호랑가시 가지, 겨우살이, 포인세티아, 짧은 솔잎 가랜드로 문과 벽, 식탁을 꾸밉니다.",
            ChristmasPartyShapeSets.Wreaths, "christmas", "wreath", "holly", "poinsettia", "decoration"),
        P("stockings-santa-hats", "Red, green and striped Christmas stockings with fur cuffs, some with a toy peeking out, drooping and pointed Santa hats, mitten pairs on a cord and knitted scarves.",
            "양말과 산타 모자", "털 테두리를 단 빨강·초록·줄무늬 양말(장난감이 빼꼼 나온 것 포함), 산타 모자, 끈 달린 벙어리장갑과 뜨개 목도리입니다.",
            ChristmasPartyShapeSets.Stockings, "christmas", "stocking", "santa-hat", "mittens", "winter"),

        // 랜덤 박스·상자·거리
        P("mystery-boxes", "Question-mark loot blocks in gold, purple, blue and red, glowing rare blocks, ?-wrapped mystery gifts and wooden treasure chests, closed or open with sparkling gold.",
            "랜덤 박스", "금·보라·파랑·빨강 물음표 블록, 빛나는 희귀 블록, 물음표 무늬 선물 상자, 닫히거나 열려 반짝이는 보물 상자를 흩뿌리는 랜덤 박스 세트입니다.",
            BoxesAndStreetShapeSets.MysteryBoxes, "loot", "mystery-box", "treasure", "game", "chest"),
        P("cardboard-boxes", "Taped moving boxes in several sizes, a fragile box, an open box, flattened cardboard, stacked pairs, wooden crates and a pallet, all in the same 3/4 top-front view.",
            "종이 상자·나무 상자", "테이프 붙인 이삿짐 상자, 깨짐 주의 상자, 열린 상자, 접은 골판지, 쌓은 상자, 나무 상자와 팔레트를 같은 3/4 시점으로 담았습니다.",
            BoxesAndStreetShapeSets.CardboardBoxes, "boxes", "cardboard", "crates", "warehouse", "moving"),
        P("litter-and-trash", "Crushed soda cans, plastic and glass bottles, paper balls, a banana peel, an apple core, chip bags, a plastic bag, newspaper, cigarette butts, bottle caps and coffee cups for littered streets.",
            "길거리 쓰레기", "찌그러진 캔, 페트병과 유리병, 구긴 종이, 바나나 껍질, 사과 속, 과자 봉지, 비닐봉지, 신문지, 꽁초, 병뚜껑, 커피 컵 등 길에 버려진 쓰레기입니다.",
            BoxesAndStreetShapeSets.Trash, "trash", "litter", "street", "garbage", "city"),
        P("traffic-cones-and-barriers", "Orange traffic cones in three sizes plus tipped-over ones, striped barricades, water-filled barriers, warning and detour signs, a road drum and a delineator post for road works.",
            "라바콘·바리케이드", "크기별 라바콘과 쓰러진 라바콘, 줄무늬 바리케이드, 물 채운 방호벽, 경고·우회 표지판, 드럼통, 시선 유도봉으로 공사 현장을 꾸밉니다.",
            BoxesAndStreetShapeSets.TrafficCones, "traffic", "road", "construction", "cones", "barriers", "city"),
        P("barrels-and-sacks", "Wooden barrels standing, lying and as a small keg, a rain barrel, grain and flour sacks full, open or slumped, clay pots and jars, and a water bucket for village and RPG scenes.",
            "통·자루", "서 있거나 누운 나무 통, 작은 술통, 빗물 통, 곡식·밀가루 자루, 항아리, 물 양동이 등 마을과 RPG 장면에 어울리는 소품입니다.",
            BoxesAndStreetShapeSets.BarrelsAndSacks, "barrels", "sacks", "village", "rpg", "props", "medieval"),

        // 가로로 끌어 잇는 벽·울타리
        P("gray-brick-wall", "Seamless gray brick wall segments with a flat stone cap, including darker, cracked, chipped, mossy, water-stained and missing-brick variants; drag sideways to build walls.",
            "회색 벽돌담", "납작한 갓돌을 얹은 회색 벽돌담 조각으로 금 간 벽돌, 이끼, 물때, 빠진 벽돌 변형이 있어 옆으로 끌면 이어진 담이 됩니다.",
            WallsAndFencesShapeSets.GrayBrickWall, "wall", "brick", "gray", "street", "building"),
        P("red-brick-fence", "Low red brick garden wall with a concrete cap and stone base, plus pillars, iron railing windows, pierced brickwork, burnt bricks and ivy; drag sideways to build.",
            "붉은 벽돌 담장", "콘크리트 갓돌과 돌 굽을 갖춘 낮은 붉은 벽돌 담장으로 기둥, 철 난간 창, 구멍 무늬, 탄 벽돌, 담쟁이 조각을 옆으로 이어 그립니다.",
            WallsAndFencesShapeSets.RedBrickFence, "fence", "brick", "garden", "wall", "red"),
        P("picket-fence", "White painted picket fence with pointed pickets, two rails and posts, plus weathered, missing, broken and flower-vine pickets; drag sideways for a garden fence.",
            "흰 말뚝 울타리", "뾰족한 흰 말뚝과 가로대, 기둥으로 된 울타리로 바랜 말뚝, 빠진 말뚝, 부러진 말뚝, 덩굴꽃 변형을 옆으로 이어 그립니다.",
            WallsAndFencesShapeSets.PicketFence, "fence", "picket", "white", "garden", "wood"),
        P("hedge", "Trimmed green hedge strip with a rounded leafy top and darker base, with flowers, berries, a sunlit patch and a trunk gap; drag sideways to line gardens and parks.",
            "산울타리", "윗선이 둥글게 다듬어진 초록 산울타리 띠로 꽃, 열매, 볕 든 자리, 줄기가 보이는 빈틈 변형을 옆으로 이어 그립니다.",
            WallsAndFencesShapeSets.Hedge, "hedge", "garden", "bush", "green", "nature"),
        P("bunting-flags", "Party bunting on a gently sagging string with triangular pennants in many colors, some striped, banded or dotted; drag sideways to hang a festive garland.",
            "만국기 줄", "살짝 늘어진 끈에 여러 색 삼각 깃발을 단 파티 장식 줄로 줄무늬, 띠, 물방울 무늬 깃발이 있어 옆으로 끌면 이어집니다.",
            WallsAndFencesShapeSets.BuntingFlags, "bunting", "party", "flags", "festival", "decoration"),

        // 땅·물가
        P("mushrooms", "Red and orange spotted toadstools, brown boletes, tan mushroom clusters, a tall slender mushroom and tiny white buttons for forest floors and fairy-tale scenes.",
            "버섯", "흰 점 빨간 광대버섯과 주황 변형, 그물버섯, 황갈색 버섯 무리, 가늘고 키 큰 버섯, 작은 양송이로 숲 바닥과 동화 풍경을 꾸밉니다.",
            NatureGroundShapeSets.Mushrooms, "mushroom", "forest", "nature", "fungi", "autumn"),
        P("forest-floor", "Acorns, a chestnut, standing and lying pine cones, twigs, fallen oak, beech, maple and ginkgo leaves and a small fern for woodland ground.",
            "숲 바닥", "도토리, 밤, 서 있거나 누운 솔방울, 잔가지, 참나무·너도밤나무·단풍·은행 낙엽, 작은 고사리로 숲속 땅을 채웁니다.",
            NatureGroundShapeSets.ForestFloor, "forest", "autumn", "leaves", "acorn", "pinecone", "nature"),
        P("lily-pads", "Top-down lily pads with notches facing different ways, pink and white water lilies, a bud, duckweed and soft ripple rings for ponds and lakes.",
            "수련 잎", "홈 방향이 다른 여러 크기의 수련 잎(위에서 본 모습), 분홍·흰 수련꽃과 봉오리, 개구리밥, 물결 고리로 연못과 호수를 꾸밉니다.",
            NatureGroundShapeSets.LilyPads, "pond", "water", "lily", "nature", "flower"),
        P("seashells-starfish", "Scallops, clams, a conch, a cowrie, mussels, orange, purple and red starfish, sand dollars and pebbles to scatter along beaches and sandy shores.",
            "조개와 불가사리", "가리비, 대합, 소라, 개오지, 홍합, 주황·보라·빨강 불가사리, 모래 달러, 조약돌을 해변과 모래사장에 흩뿌립니다.",
            NatureGroundShapeSets.Seashells, "beach", "seashell", "starfish", "sand", "ocean", "summer"),
        P("footprints-tracks", "Semi-transparent shoe, boot and bare-foot strides plus dog, cat, bird, rabbit and deer tracks that press into snow, sand or mud of any color.",
            "발자국", "신발·장화·맨발 걸음과 개·고양이·새·토끼·사슴 발자국을 반투명하게 찍어 눈, 모래, 진흙 어디에나 어울립니다.",
            NatureGroundShapeSets.Footprints, "footprints", "tracks", "snow", "sand", "animals", "trail"),
        P("puddles", "Semi-transparent blue-gray rain puddles in many sizes and shapes with sky glints, some with ripples or a floating leaf, plus small splash drops.",
            "물웅덩이", "크기와 모양이 다른 반투명 물웅덩이(하늘 반사, 물결 고리, 떠 있는 잎)와 작은 물방울로 비 온 뒤 길과 땅을 표현합니다.",
            NatureGroundShapeSets.Puddles, "puddle", "rain", "water", "weather", "ground"),

        // 생물·나무
        P("butterflies", "Butterflies in monarch, morpho blue, yellow, white and pink with open and folded wings, plus bees and ladybugs for gardens and meadows.",
            "나비와 곤충", "여러 색 나비(편 날개·접은 옆모습)와 꿀벌, 무당벌레로 꽃밭과 풀밭을 꾸밉니다.",
            NatureLifeShapeSets.Butterflies, "butterfly", "insect", "garden", "spring", "nature"),
        P("flying-birds", "Birds in flight for skies: distant dark silhouettes in several sizes and wing poses, seagulls with wings up, flat or down, and small sparrows.",
            "나는 새", "하늘에 흩뿌리는 날아가는 새: 먼 새 실루엣, 갈매기, 참새를 여러 날갯짓으로 담았습니다.",
            NatureLifeShapeSets.Birds, "bird", "sky", "seagull", "flying", "nature"),
        P("fish", "Side-view fish for underwater scenes: clownfish, blue tang, goldfish with bubbles, sardine schools, pufferfish and angelfish, facing both ways.",
            "물고기", "흰동가리, 블루탱, 금붕어, 정어리 떼, 복어, 엔젤피시를 양쪽 방향으로 담은 바닷속 물고기입니다.",
            NatureLifeShapeSets.Fish, "fish", "underwater", "ocean", "aquarium", "sea"),
        P("coral-and-seaweed", "Sea-floor props standing on a base line: branching, brain and fan coral, tube sponges, tall and short kelp, seaweed clumps and sea anemones.",
            "산호와 해초", "바닥에 서 있는 가지산호, 뇌산호, 부채산호, 관해면, 다시마와 해초, 말미잘로 바닷속 바닥을 꾸밉니다.",
            NatureLifeShapeSets.CoralAndSeaweed, "coral", "seaweed", "underwater", "ocean", "reef"),
        P("cacti-desert-plants", "Desert scenery: saguaros with zero to three arms, barrel and flowering cacti, prickly pears, agave and aloe, tumbleweeds and desert rocks.",
            "선인장과 사막 식물", "사와로, 통선인장, 꽃 핀 작은 선인장, 부채선인장, 용설란·알로에, 회전초, 바위로 사막을 꾸밉니다.",
            NatureLifeShapeSets.Cacti, "cactus", "desert", "plant", "western", "nature"),
        P("pine-trees", "Side-view conifers 12 to 40 px tall in dark, blue and yellow greens with layered tiers and visible trunks, some snow-dusted, for forest backgrounds.",
            "침엽수", "높이 12~40px의 층층 침엽수를 세 가지 초록과 눈 덮인 것으로 담아 숲 배경을 만듭니다.",
            NatureLifeShapeSets.PineTrees, "tree", "pine", "forest", "winter", "nature"),
        P("round-trees-bushes", "Side-view deciduous trees with round leafy crowns in several sizes and greens, an autumn tree and a blossom tree, plus round bushes and shrubs.",
            "둥근 나무와 덤불", "크기와 초록이 다른 둥근 활엽수, 단풍나무, 꽃나무, 그리고 줄기 없는 덤불로 공원과 숲을 꾸밉니다.",
            NatureLifeShapeSets.RoundTrees, "tree", "bush", "forest", "park", "autumn"),

        // 판타지·계절 소품
        P("bones-and-skulls", "Skulls seen from the front and at three-quarters, single and crossed bones, a bone pile with a skull, a rib cage, a horned animal skull and broken bones for dungeon floors.",
            "뼈와 해골", "앞·옆으로 돌린 해골, 여러 방향의 뼈, 엇갈린 뼈, 해골 얹은 뼈 무더기, 갈비뼈, 뿔 달린 짐승 해골로 던전 바닥을 꾸밉니다.",
            FantasyPropsShapeSets.Bones, "bones", "skull", "dungeon", "halloween", "fantasy"),
        P("potions-and-bottles", "Corked glass potions (round flasks, vials, square bottles, cone flasks) in red, blue, green, purple and gold, glowing magic potions and broken flasks with puddles.",
            "물약과 병", "코르크 마개를 단 둥근 플라스크·시약병·네모병·원뿔 플라스크에 다섯 색 물약, 빛나는 마법 물약, 깨져 쏟아진 병까지 연금술 탁자에 어울립니다.",
            FantasyPropsShapeSets.Potions, "potion", "bottle", "alchemy", "magic", "fantasy"),
        P("crystals", "Faceted cave crystal clusters in amethyst, blue, green, pink and clear, growing from rocks or the ground, with single crystals, loose shards and glowing variants.",
            "수정", "바위나 땅에서 자란 자수정·청수정·녹수정·장미 수정·맑은 수정 무리와 낱개 수정, 떨어진 조각, 빛나는 수정으로 동굴을 꾸밉니다.",
            FantasyPropsShapeSets.Crystals, "crystal", "gem", "cave", "magic", "fantasy"),
        P("tombstones", "Graveyard headstones on dirt mounds (round, pointed, flat, stone crosses), some cracked, tilted or mossy, plus small marker stones and dead grass tufts.",
            "비석", "흙 둔덕에 선 둥근·뾰족한·네모 비석과 돌 십자가, 금 가고 기울고 이끼 낀 비석, 작은 표석, 마른 풀 포기로 묘지를 꾸밉니다.",
            FantasyPropsShapeSets.Tombstones, "tombstone", "graveyard", "halloween", "cemetery", "spooky"),
        P("pumpkins", "Ribbed orange pumpkins in round, tall, flat and small shapes, green and white pumpkins, glowing carved jack-o'-lanterns, gourds and a pumpkin stack for harvest or Halloween.",
            "호박", "둥글고 길쭉하고 납작한 주황 호박, 초록·흰 호박, 빛나는 잭오랜턴, 박, 쌓은 호박으로 추수철과 핼러윈을 꾸밉니다.",
            FantasyPropsShapeSets.Pumpkins, "pumpkin", "halloween", "harvest", "autumn", "jack-o-lantern"),
        P("books-and-papers", "Closed books lying flat or standing in rows, stacks of 2-4 books, open books, rolled and open scrolls, loose paper sheets, a sealed envelope and quill pens for desks and libraries.",
            "책과 종이", "누운 책과 세운 책 묶음, 두세 권씩 쌓은 책, 펼친 책, 두루마리, 흩어진 종이, 봉인한 편지 봉투, 깃펜으로 서재와 책상을 꾸밉니다.",
            FantasyPropsShapeSets.BooksAndPapers, "books", "library", "scroll", "paper", "desk", "study"),

        // 일상 소품
        P("fruit", "Hand-drawn fruit: red, green and yellow apples, pears, bananas, cherry pairs, an orange, a lemon, a plum and a strawberry, for fruit stands, picnics and kitchens.",
            "과일", "사과(빨강·초록·노랑), 배, 바나나, 체리, 오렌지, 레몬, 자두, 딸기를 과일 가판대나 소풍, 부엌 장면에 흩어 놓을 수 있습니다.",
            EverydayShapeSets.Fruit, "fruit", "food", "apple", "market", "kitchen"),
        P("candies-and-sweets", "Twist-wrapped candies, swirl lollipops, sugared gumdrops, macarons, jelly beans, sprinkle donuts and a half-unwrapped chocolate bar for parties and sweet shops.",
            "사탕·과자", "포장 사탕, 막대사탕, 젤리, 마카롱, 젤리빈, 도넛, 판 초콜릿으로 파티나 과자 가게를 꾸밀 수 있습니다.",
            EverydayShapeSets.Candies, "candy", "sweets", "dessert", "party", "food"),
        P("party-balloons", "Shiny party balloons in many colors, sizes and tilts with curly ribbons, plus heart balloons, bunches of three and star foil balloons.",
            "풍선", "여러 색·크기·기울기의 반짝이는 고무풍선과 꼬불 리본, 하트 풍선, 세 개 다발, 별 은박 풍선입니다.",
            EverydayShapeSets.Balloons, "balloons", "party", "birthday", "celebration", "decoration"),
        P("confetti-and-streamers", "Tiny confetti squares, strips, dots and stars in bright colors at many angles, curly streamer bits, and dense handfuls for festive scattering.",
            "색종이 조각", "여러 각도의 밝은 색 네모·띠·동그라미·별 조각과 꼬불 리본, 조각을 한 줌 흩은 모양으로 축제 분위기를 냅니다.",
            EverydayShapeSets.Confetti, "confetti", "party", "celebration", "festive", "streamers"),
        P("paint-splatters", "Glossy paint splats with satellite droplets in red, blue, yellow, green, purple, white, black and more, plus dripping paint and brush smears.",
            "물감 자국", "여러 색의 윤기 나는 물감 튄 자국과 방울, 흘러내린 자국, 붓으로 칠한 자국으로 작업실이나 낙서 장면을 꾸밉니다.",
            EverydayShapeSets.PaintSplatters, "paint", "splatter", "art", "drips", "messy"),
        P("logs-and-stumps", "Cut oak and birch logs with end-grain rings, a stacked log pile, tree stumps, a stump with an axe, split firewood and a mossy fallen branch.",
            "통나무·그루터기", "나이테가 보이는 참나무·자작나무 통나무, 쌓은 통나무, 그루터기, 도끼 꽂힌 그루터기, 쪼갠 장작, 이끼 낀 나뭇가지입니다.",
            EverydayShapeSets.Firewood, "wood", "logs", "forest", "camping", "firewood"),
    ];
}
