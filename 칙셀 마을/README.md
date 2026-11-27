# 칙셀 마을

병아리 후원 마을의 **편집 원본**입니다. 웹 배포용 파일은 `docs/assets/village/`에 따로 생성합니다. 그 폴더의 PNG나 JSON을 직접 편집하지 않습니다.

## 리소스

| 파일 | 역할 |
|---|---|
| `village.chickxel` | Chickxel에서 여는 마을 맵. 16픽셀 자동 타일 지형, 집 바닥과 다리, 밭 자리, 서 있는 물건의 배치 레이어를 포함합니다. |
| `terrain.chickxel-tileset`, `terrain-variants.chickxel-tileset` | 물과 땅의 연결 모양, 변형을 포함한 타일셋. |
| `floor.chickxel-tileset`, `plots.chickxel-tileset`, `props.chickxel-tileset` | 바닥, 밭 자리, 물건 배치용 연결 타일셋. |
| `objects.chickxel` | 기존 합본 원본 보관용. 현재 내보내기는 이 파일을 읽지 않고 `objects/`의 개별 그림을 사용합니다. |
| `objects/*.chickxel` | 나무·버섯·꽃·바위·울타리 등 자동 타일로 잇지 않는 그림과 병아리·작물 등의 별도 원본. 프레임이 있는 파일은 프레임 순서를 유지합니다. |
| `sources.json` | 별도 그림의 프레임을 `props` 타일 ID와 연결합니다. 타일 ID를 바꾸거나 프레임을 추가하면 함께 수정합니다. |

## 편집과 반영

1. Chickxel에서 `village.chickxel`을 열어 지형은 자동 타일 도구로, 나무와 시설은 물건 배치 레이어에서 그리거나 옮깁니다. 맵과 연결 타일셋 파일은 이 폴더에서 함께 보관합니다.
2. 나무·버섯·꽃처럼 연결되지 않는 그림은 `objects/`의 해당 `.chickxel`을 편집합니다. 연결된 타일은 내보낼 때 이 원본으로 갱신됩니다. 배치는 `village.chickxel`에 있으므로 별도 그림의 캔버스 크기와 발 위치를 그대로 유지합니다.
3. 저장소 루트에서 `dotnet run --project tools/VillageExport`를 실행합니다. `docs/assets/village/ground.png`, `objects.png`, `scene.json`이 다시 생성됩니다. 다음으로 `python tools/build-docs.py`를 실행해 웹 페이지의 리소스 버전을 갱신합니다.

현재 웹은 맵 지형을 `ground.png` 한 장으로 미리 합성하고, 서 있는 물건은 `scene.json`의 좌표와 발 기준 Y 순서로 그립니다. **웹에서 지형을 타일 단위로 편집하거나 사용자가 구조물을 배치하는 기능은 아직 없습니다.** 이를 추가할 때는 타일 종류·셀 좌표를 담은 맵 데이터와 구조물의 별도 배치 데이터를 내보내야 합니다. 지형과 기본 나무는 마을 원본, 사용자 구조물은 별도 상태로 두어야 맵을 다시 그려 확장해도 사용자 배치가 사라지지 않습니다. 작물 성장 단계와 낮·밤, 계절, 날씨도 원본 타일을 덮어쓰지 않는 별도 상태로 다룹니다.

맵은 현재 56×36칸, 칸 크기 16픽셀입니다. `docs/village.js`의 세계·마을 경계와 주민 이동 경로는 아직 이 크기와 지형에 맞춰져 있으므로, 맵을 키우거나 길·집·밭을 옮길 때 함께 조정해야 합니다.
