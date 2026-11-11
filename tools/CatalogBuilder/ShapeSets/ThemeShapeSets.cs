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
        // 1.0.1: 물음표 블록만(선물 상자·보물 상자는 따로 — 무작위로 흩뿌려도 비슷한 것끼리)
        P("mystery-boxes", "Question-mark loot blocks in gold, purple, blue, red, green and silver, two sizes, glowing rare blocks and a used empty block, for game-style levels.",
            "랜덤 박스 (물음표 블록)", "금·보라·파랑·빨강·초록·은색 물음표 블록을 두 가지 크기로 담았고, 빛나는 희귀 블록과 다 쓴 빈 블록도 들어 있습니다.",
            BoxesAndStreetShapeSets.MysteryBoxes, "mystery-box", "loot", "game", "question-block", "retro") with { Version = "1.0.1" },
        P("mystery-gifts", "Gift boxes wrapped in question-mark paper with ribbons and bows in purple, teal, red, gold, blue, pink and green, in several sizes, for surprise and reward scenes.",
            "랜덤 박스 (선물 상자)", "물음표 무늬 포장지에 리본과 나비 매듭을 두른 선물 상자를 일곱 가지 색과 여러 크기로 담았습니다.",
            BoxesAndStreetShapeSets.MysteryGifts, "gift", "mystery-box", "present", "reward", "game"),
        P("treasure-chests", "Wooden treasure chests with gold or iron bands: large, small, dark wood, padlocked, open with gold or gems and sparkles, and overflowing with spilling coins.",
            "보물 상자", "금띠·쇠띠를 두른 나무 보물 상자로, 작은 상자, 자물쇠 채운 상자, 금화나 보석이 반짝이는 열린 상자, 금화가 넘쳐 쏟아지는 상자까지 들어 있습니다.",
            BoxesAndStreetShapeSets.TreasureChests, "treasure", "chest", "loot", "gold", "rpg", "game"),
        // 1.0.1: 살짝 위에서 본 금화로 다시 그림(옆모습 금화는 벽돌처럼 보였음)
        P("gold-coin-stacks", "Gold coins seen slightly from above: single and loose coins, short and tall stacks, clusters of two to four stacks with coins at the base, coin heaps, and a coin standing on edge.",
            "금화 더미", "살짝 위에서 본 금화로, 낱개 금화와 흩어진 금화, 낮고 높은 금화 탑, 높이가 다른 탑 묶음, 금화 무더기와 세워 둔 금화를 담았습니다.",
            BoxesAndStreetShapeSets.CoinStacks, "gold", "coins", "money", "treasure", "stack", "rpg") with { Version = "1.0.1" },
        // 1.0.1: 불룩한 자루와 위에서 본 금화로 다시 그림
        P("coin-sacks", "Bulging tied money sacks in burlap, leather and red velvet, an open sack heaped with gold coins, a sack with a coin stack beside it, and a tipped sack spilling coins.",
            "돈 자루", "끈으로 묶은 삼베·가죽·빨간 벨벳 돈 자루와 금화가 소복한 열린 자루, 옆에 금화 탑을 둔 자루, 쓰러져 금화를 쏟는 자루를 담았습니다.",
            BoxesAndStreetShapeSets.CoinSacks, "money", "coins", "sack", "gold", "rpg", "treasure") with { Version = "1.0.1" },
        // 1.0.1: 옆모습 종이 상자만(45도 상자와 나무 상자는 따로 — 한 세트 안에서 시점·재질이 섞이지 않게)
        P("cardboard-boxes", "Taped cardboard boxes drawn straight from the side: small to large and tall boxes, a handle-hole side panel, a fragile box, an open box and stacks.",
            "종이 상자 (옆모습)", "옆에서 똑바로 본 테이프 상자: 작은·큰·세로 상자, 손잡이 구멍 난 옆판, 깨짐 주의 상자, 열린 상자, 쌓은 상자입니다.",
            BoxesAndStreetShapeSets.CardboardBoxesSide, "boxes", "cardboard", "side-view", "warehouse", "moving") with { Version = "1.0.1" },
        P("cardboard-boxes-isometric", "Taped cardboard boxes at a 45-degree isometric angle showing the top and two sides: several sizes, a fragile box, an open box and stacks.",
            "종이 상자 (45도)", "윗면과 두 옆면이 보이게 45도로 돌린 테이프 상자: 여러 크기, 깨짐 주의 상자, 열린 상자, 쌓은 상자입니다.",
            BoxesAndStreetShapeSets.CardboardBoxesIsometric, "boxes", "cardboard", "isometric", "warehouse", "moving"),
        P("wooden-crates", "Wooden crates drawn straight from the side with frame boards and a diagonal brace, in light and dark wood, stacked, plus a pallet and a crate on a pallet.",
            "나무 상자 (옆모습)", "옆에서 똑바로 본 테두리 널과 버팀목을 댄 밝은·어두운 나무 상자, 쌓은 상자, 팔레트와 팔레트에 올린 상자입니다.",
            BoxesAndStreetShapeSets.WoodenCratesSide, "crates", "wood", "side-view", "warehouse", "pallet"),
        P("wooden-crates-isometric", "Wooden crates at a 45-degree isometric angle with plank tops, frame boards and braces, in light and dark wood, stacked, plus a pallet and a crate on a pallet.",
            "나무 상자 (45도)", "윗면과 두 옆면이 보이게 45도로 돌린 밝은·어두운 나무 상자, 쌓은 상자, 팔레트와 팔레트에 올린 상자입니다.",
            BoxesAndStreetShapeSets.WoodenCratesIsometric, "crates", "wood", "isometric", "warehouse", "pallet"),
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
        // 1.0.1: 큰 나비만(작은 나비·옆모습·벌·무당벌레는 곤충 세트로 — 무작위로 흩뿌려도 비슷한 것끼리)
        P("butterflies", "Big butterflies with open wings in monarch orange, morpho blue, yellow, white and pink for gardens and meadows.",
            "나비", "날개를 편 큰 나비(주황·파랑·노랑·흰색·분홍)로 꽃밭과 풀밭을 꾸밉니다.",
            NatureLifeShapeSets.Butterflies, "butterfly", "insect", "garden", "spring", "nature") with { Version = "1.0.1" },
        P("insects", "Small garden bugs: little butterflies with open or folded wings, honeybees and ladybugs, facing both ways.",
            "곤충", "작은 나비(편 날개·접은 옆모습), 꿀벌, 무당벌레를 좌우로 뒤집은 것까지 담은 꽃밭 곤충입니다.",
            NatureLifeShapeSets.Insects, "insect", "bee", "ladybug", "garden", "nature"),
        P("flying-birds", "Birds in flight for skies: distant dark silhouettes in several sizes and wing poses, seagulls with wings up, flat or down, and small sparrows.",
            "나는 새", "하늘에 흩뿌리는 날아가는 새: 먼 새 실루엣, 갈매기, 참새를 여러 날갯짓으로 담았습니다.",
            NatureLifeShapeSets.Birds, "bird", "sky", "seagull", "flying", "nature"),
        // 1.0.1: 한 마리씩만(떼는 물고기 떼 세트로 — 무작위로 흩뿌려도 비슷한 것끼리), 종류를 늘림
        P("fish", "Single side-view fish for underwater scenes: clownfish, tangs, goldfish, pufferfish, angelfish, butterflyfish, koi, tetra, mackerel, grouper and a small shark.",
            "물고기", "흰동가리, 탱, 금붕어, 복어, 엔젤피시, 나비고기, 비단잉어, 네온테트라, 고등어, 능성어, 작은 상어를 한 마리씩 담았습니다.",
            NatureLifeShapeSets.Fish, "fish", "underwater", "ocean", "aquarium", "sea") with { Version = "1.0.1" },
        P("fish-schools", "Small schooling fish swimming together in groups of 2 to 7, all facing one way: silver sardines, anchovies, blue herring and yellow-striped fish.",
            "물고기 떼", "정어리, 멸치, 청어, 노란 줄무늬 물고기가 2~7마리씩 한 방향으로 함께 헤엄치는 물고기 떼입니다.",
            NatureLifeShapeSets.FishSchools, "fish", "school", "underwater", "ocean", "sea"),
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
        // 1.0.1: 깎인 면이 보이는 수정 무리로 다시 그림(낱개·떨어진 조각은 뺌)
        P("crystals", "Faceted crystal clusters in amethyst, sapphire, emerald, rose, clear quartz and citrine, fanning out from dark cave rock in small, medium and large sizes, some softly glowing.",
            "수정", "어두운 동굴 바위에서 부채꼴로 솟은 자수정·청수정·녹수정·장미 수정·맑은 수정·황수정 무리를 작은·중간·큰 크기로 담았고, 몇은 은은하게 빛납니다.",
            FantasyPropsShapeSets.Crystals, "crystal", "gem", "cave", "magic", "fantasy") with { Version = "1.0.1" },
        // 1.0.1: 두께 없는 정면 비석만(마른 풀 포기는 뺌)
        P("tombstones", "Flat front-view gravestones on dirt strips: rounded, pointed and square stones, stone and Celtic crosses, cracked, mossy and tilted stones, and small markers in three grays.",
            "비석", "흙 위에 선 둥근·뾰족한·네모 비석과 돌 십자가, 켈트 십자가, 금 가고 이끼 낀 비석, 기운 비석, 작은 표석을 세 가지 회색 돌로 담은 정면 묘지 세트입니다.",
            FantasyPropsShapeSets.Tombstones, "tombstone", "graveyard", "halloween", "cemetery", "spooky") with { Version = "1.0.1" },
        P("pumpkins", "Ribbed orange pumpkins in round, tall, flat and small shapes, green and white pumpkins, glowing carved jack-o'-lanterns, gourds and a pumpkin stack for harvest or Halloween.",
            "호박", "둥글고 길쭉하고 납작한 주황 호박, 초록·흰 호박, 빛나는 잭오랜턴, 박, 쌓은 호박으로 추수철과 핼러윈을 꾸밉니다.",
            FantasyPropsShapeSets.Pumpkins, "pumpkin", "halloween", "harvest", "autumn", "jack-o-lantern"),
        // 책상·서재(예전 "책과 종이"를 종류마다 한 세트로 나눔 — 무작위로 흩뿌려도 비슷한 것끼리)
        P("book-stacks", "Books lying flat seen from the side, single or stacked two to six high, with page-edge or spine faces in varied colors, thicknesses and offsets for desks and libraries.",
            "쌓은 책", "옆에서 본 누운 책 한 권부터 여섯 권 더미까지, 책장 쪽·책등 쪽과 여러 색·두께로 책상과 서재를 꾸밉니다.",
            FantasyPropsShapeSets.BookStacks, "books", "stack", "library", "desk", "study"),
        P("standing-books", "Upright books seen from the spine, from single volumes to rows of six in varied heights and colors, some rows with a book leaning at the end, for shelves.",
            "세운 책", "책등이 보이게 선 책 한 권부터 여섯 권 줄까지, 높이와 색이 다르고 끝에 기댄 책이 있는 줄도 있어 책장에 어울립니다.",
            FantasyPropsShapeSets.StandingBooks, "books", "bookshelf", "library", "shelf", "study"),
        P("open-books", "Open books in several cover colors and sizes with text pages, picture pages, red illuminated initials and a page being turned, for desks and reading scenes.",
            "펼친 책", "여러 표지 색과 크기의 펼친 책으로, 글줄·그림·붉은 머리글자 쪽과 넘어가는 책장이 있어 책상과 독서 장면에 어울립니다.",
            FantasyPropsShapeSets.OpenBooks, "books", "open-book", "reading", "library", "desk"),
        P("papers-and-letters", "Loose paper sheets slightly rotated (text, heading, parchment, ruled notebook), crumpled paper balls, a folded note and envelopes with wax seals or a stamp.",
            "종이와 편지", "살짝 돌아간 낱장 종이(글줄·제목·양피지·줄 공책), 구긴 종이 뭉치, 접은 쪽지, 밀랍 봉인이나 우표를 붙인 편지 봉투로 책상 위를 어지럽힙니다.",
            FantasyPropsShapeSets.Papers, "paper", "letter", "envelope", "desk", "office"),
        P("scrolls", "Parchment scrolls rolled and tied with red, blue or green ribbon or a wax seal, half-open scrolls and fully open vertical and horizontal scrolls in light and aged tones.",
            "두루마리", "끈으로 묶거나 밀랍으로 봉인한 두루마리, 반쯤 펼친 것, 세로·가로로 다 펼친 밝은·바랜 양피지 두루마리입니다.",
            FantasyPropsShapeSets.Scrolls, "scroll", "parchment", "fantasy", "magic", "library"),
        P("quill-pens", "Feather quill pens in white, hawk brown, raven black, red, blue and green at two angles and both directions, plus a quill standing in an inkwell.",
            "깃펜", "흰색·매·까마귀·빨강·파랑·초록 깃털 펜을 두 가지 기울기와 양 방향으로 담았고, 잉크병에 꽂은 깃펜도 있습니다.",
            FantasyPropsShapeSets.Quills, "quill", "feather", "pen", "desk", "writing"),

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
