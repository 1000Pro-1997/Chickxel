# GuideShots — 사용 가이드 스크린샷

실제 Gpixel 화면을 화면 없이(Avalonia Headless + Skia) 띄워 사용 가이드(홈페이지·F1)용 PNG를 찍습니다. 샷마다 임시 설정 폴더(`GPIXEL_DATA_DIR`)를 새로 만들어 사용자 설정과 실행 중인 앱을 건드리지 않습니다.

## 실행

```
dotnet run --project tools/GuideShots -- [쪽 id 앞부분 …]
dotnet run --project tools/GuideShots -- --lang en [쪽 id 앞부분 …]
dotnet run --project tools/GuideShots -- --list
```

- 인수가 없으면 모든 샷을 찍습니다. `screen-`처럼 앞부분만 주면 그 샷들만 찍습니다.
- 결과: 한국어는 `docs/assets/guide/<이름>.png`, `--lang en`은 `docs/assets/guide/en/<이름>.png`.
- UI를 바꾼 뒤 다시 돌리면 같은 상태로 다시 찍힙니다.

## 구조

- `Kit/ShotKit.cs`: 메인 창 띄우기(`Main`), 창·대화상자 찍기(`Save`, `Dialog`), 영역 자르기(`SaveControl`, `SaveArea`), 강조 테두리(`marks`), 키·마우스 입력, 메뉴 명령 실행(`Command`), 비공개 멤버 접근(`Get`/`Set`/`Call`). 열린 메뉴·우클릭 메뉴는 그림에 합쳐지고, 창에는 제목 줄을 그려 넣습니다.
- `Shots/<장>*.cs`: `[Shot("<쪽 id>")]` 메서드 하나가 그림 한 장 이상을 찍습니다. 여러 장이면 `<쪽 id>-2`처럼 이름을 붙입니다.
- `Sample/wolf.png`: 예시 그림(늑대귀 모험가). 열면 49×98 픽셀 그림으로 가져와집니다.
- 그림 목록과 움짤 요청은 저장소 루트의 `가이드-이미지.md`에 있습니다.
