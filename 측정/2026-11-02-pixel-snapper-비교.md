# Sprite Fusion Pixel Snapper와 AI 그림 픽셀로 정리 비교

경쟁 도구 [Sprite Fusion Pixel Snapper](https://github.com/Hugo-Dz/spritefusion-pixel-snapper)(Rust, MIT, 별 약 3.4k, 웹·CLI·유료 데스크톱 $7.99)가 같은 일(AI가 만든 픽셀 느낌 그림을 격자에 맞춤)을 한다. 알고리즘을 읽고, 저장소의 AI 그림 표본 9장에 두 도구를 기본 설정으로 돌려 칸 크기·결과·알파·속도를 비교했다.

## 환경

- 날짜: 2026-11-02, Windows 11 x64(10.0.26200), Intel Core i5-13400F, .NET SDK 10.0.300 Release, rustc 1.97.1 `--release`.
- Pixel Snapper: 커밋 `02f9315`(2026-10-20), `spritefusion-pixel-snapper <입력> <출력> 16`(기본 16색).
- Chickxel: `Pixelator.Run(image, new PixelateSettings())`(기본 프리셋), `dotnet run` 파일 기반 프로그램(Core 프로젝트 참조). 측정 코드는 저장소에 넣지 않았다(임시 폴더에서 돌림).
- 입력: `tests/fixtures/ai-*.png` 9장.
- 시간: Pixel Snapper는 프로세스 실행 포함 4번, Chickxel은 같은 프로세스에서 4번 돌려 첫 번(JIT)을 빼고 3번. 표에는 범위.

## Pixel Snapper 알고리즘(`src/lib.rs` 약 1,460줄)

1. 원본 전체를 RGB k-평균(k-means++ 시작, 씨앗 42 고정, 15번 반복, RGB 유클리드)으로 k색 양자화. 알파는 그대로 둔다.
2. 양자화한 그림의 밝기(0.299R+0.587G+0.114B, 투명 = 0)에 [-1,0,1] 기울기를 줘 열·행마다 합한 투영 곡선 두 개.
3. 곡선에서 최댓값 20% 넘는 봉우리, 4px 미만 간격은 버림 → 봉우리 간격의 중앙값 = 칸 크기. 가로·세로 비가 1.8 넘으면 작은 쪽, 아니면 평균(정사각형 칸만).
4. 걷기: 칸 크기만큼 나아가 ±35% 안에서 가장 센 곡선 자리를 자름 선으로(약하면 그냥 칸 크기 자리). 두 축 칸 크기가 어긋나면 고른 격자로 다시 맞춤.
5. 칸마다 양자화한 RGBA의 최빈값 → 결과 픽셀. 팔레트를 주면 마지막에 RGB 가장 가까운 색.

배경 지우기·프레임 나누기·외곽선 처리·정리·후보·점수는 없다. 결과는 원본 캔버스 전체를 칸 수만큼 줄인 한 장.

## 결과

| 그림(원본) | Snapper 칸 | Snapper 결과 | Chickxel 칸 | Chickxel 결과 | Snapper 시간 | Chickxel 시간 |
|---|---|---|---|---|---|---|
| ai-walk-sample 2172×724, 4프레임 | 9.0 | 256×86 한 장 | 15.95 | 29×38 ×4 | 268~381ms | 247~290ms |
| ai-elf-walk-sheet 2172×724, 6프레임 | 6.0 | 438×142 한 장 | 3.46 | 123×183 ×6 | 287~293ms | 245~396ms |
| ai-mage-chibi-sheet 2172×724, 6프레임 | 5.5 | 450×141 한 장 | 6.81 | 59×78 ×6 | 333~406ms | 376~490ms |
| ai-mage 1254×1254 | 14.0 | 90×91 | 14.00 | 65×87 | 284~291ms | 273~355ms |
| ai-spear-boy-sticker 1254×1254 | 7.0 | 197×197 | 24.75 | 42×42 | 285~357ms | 263~353ms |
| ai-sword-boy 1254×1254 | 21.0 | 60×62 | 20.76 | 45×46 | 201~278ms | 202~219ms |
| ai-hoodie-girl 627×627 | 6.0 | 115×125 | 3.38 | 84×195 | 58~81ms | 40~50ms |
| ai-girl-bg 162×166 | 6.0 | 27×27 | 6.21 | 23×26 | 27~29ms | 8~10ms |
| ai-water-machine-bg 127×128 | 4.0 | 33×32 | 3.82 | 29×28 | 23~26ms | 9~14ms |

Snapper 결과의 알파(16색으로 돌림):

| 그림 | 서로 다른 RGB | 서로 다른 RGBA | 알파 1~15 | 알파 16~239 | 알파 240~254 | 알파 255 |
|---|---|---|---|---|---|---|
| ai-spear-boy-sticker | 16 | 206 | 1,203 | 250 | 18,034 | 33 |
| ai-elf-walk-sheet | 16 | 725 | 6,642 | 1,438 | 19,017 | 27 |
| ai-walk-sample | 16 | 64 | 278 | 7 | 8,242 | 6 |

## 나란히 본 것(원본 · Snapper · Chickxel)

- 칸 크기가 같은 그림(ai-mage, ai-sword-boy, ai-girl-bg, ai-water-machine-bg)은 비슷하다. ai-sword-boy는 Chickxel(6색)이 칼날 회색을 갈색으로 바꿨고 Snapper(16색)는 회색을 지켰다. ai-girl-bg는 Snapper가 베이지 배경을 그대로 남겼고 Chickxel은 지웠다.
- ai-walk-sample·ai-spear-boy-sticker: Snapper는 안쪽 세부 크기(약 7~9px)를, Chickxel 기본(크기를 줄여서)은 외곽선 두께(약 16~25px)를 칸으로 골라 결과가 훨씬 작고 거칠다. ai-walk-sample에서 Chickxel은 빨간 목도리를 잃었고, ai-spear-boy-sticker는 얼굴 모양이 무너졌다. Snapper는 스티커 흰 테두리까지 남겼다.
- ai-hoodie-girl: Snapper가 칸을 6px(실제 약 3.4px)로 잡아 계단처럼 두 칸씩 겹친 자리가 보인다. Chickxel 쪽이 맞다.

## 결론과 남은 문제

- 속도는 같은 급이다(이 표본에서 둘 다 큰 그림 200~400ms). Snapper가 속도로 앞서지 않는다.
- Snapper의 약점(코드와 결과로 확인): 알파를 양자화하지 않아 불투명해 보이는 픽셀이 거의 다 알파 240~254이고 옅은 알파 1~15 티가 남는다 — "16색"이라도 RGBA는 64~725가지. 밝기만 보므로 밝기가 같은 다른 색 경계를 못 보고, 투명은 밝기 0으로 쳐서 투명 바탕에 닿은 검은(에 가까운) 외곽선 경계는 아주 약하게만 잡힌다. 자동으로는 4px 미만 칸을 찾지 못하고(봉우리 간격 4px 미만은 버림) 가로·세로 칸 크기를 하나로 묶는다. 배경·프레임·외곽선 처리가 없다.
- Chickxel이 배울 것: 기본 프리셋이 외곽선 두께를 칸으로 골라 Snapper보다 세부·작은 색(목도리, 칼날)을 잃는 표본이 있다(ai-walk-sample, ai-spear-boy-sticker, ai-sword-boy). 이 표본들에서 기본 칸 크기·색 수 선택을 다시 볼 만하다. Snapper의 일괄 처리(폴더 → 폴더)·CLI 같은 가벼운 입구는 Chickxel에 없다(명령줄은 `--open`·`--open-folder`뿐).
