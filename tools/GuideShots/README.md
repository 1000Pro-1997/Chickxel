# GuideShots — 사용 가이드 스크린샷

실제 Gpixel 화면을 화면 없이(Avalonia Headless + Skia) 띄워 사용 가이드(홈페이지·F1)용 PNG를 찍습니다. 샷마다 임시 설정 폴더(`GPIXEL_DATA_DIR`)를 새로 만들어 사용자 설정과 실행 중인 앱을 건드리지 않습니다.

## 실행

```
dotnet run --project tools/GuideShots -- [쪽 id 앞부분 …]
dotnet run --project tools/GuideShots -- --list
```

- 인수가 없으면 모든 샷을 찍습니다. `screen-`처럼 앞부분만 주면 그 샷들만 찍습니다. 샷은 하나씩 따로 된 프로세스(`--only <id>`)에서 찍어 앞 샷이 남긴 창·타이머·폴더 감시가 다음 샷에 끼어들지 않습니다(4분이 넘으면 멈춘 것으로 보고 실패 처리).
- macOS에서 돌려도 Windows 모습(단축키 `Ctrl`)으로 찍습니다. 가이드 글이 `Ctrl` 기준이기 때문입니다.
- 그림에 보이는 예시 파일 폴더는 Windows `C:\GpixelGuide\MyGame`, 그 밖에는 `/tmp/GpixelGuide/MyGame`입니다. 샷마다 지우고 다시 만들므로 여러 프로세스를 같이 돌릴 때는 `GUIDESHOTS_ROOT`로 프로세스마다 다른 폴더를 주고, 빌드도 `dotnet build tools/GuideShots --artifacts-path <폴더>`로 나눕니다(저장소 루트에서 실행).
- UI를 바꾼 뒤 다시 돌리면 같은 상태로 다시 찍힙니다. 그다음 `python tools/build-help.py`로 F1 가이드용 WebP를 갱신합니다.

## 표시 규칙

- 글이 설명하는 곳은 `marks`로 **빨간 테두리 + 빨간 동그라미 번호**를 붙입니다. 번호는 `marks`에 적은 순서이고, 한 쪽(h3)의 그림이 여러 장이면 `firstNumber`로 이어서 붙입니다(쪽 안에서 번호가 겹치지 않게).
- 가이드 HTML에서는 `<figure class="shot">` 안의 `<figcaption>`에 ①②… 설명을 적고, 본문에서도 그 항목 앞에 같은 번호를 붙입니다.
- 우클릭 메뉴·펼친 목록·설정 팝업은 열린 채로 찍습니다(팝업은 창 그림에 합쳐짐).

## 구조

- `Kit/ShotKit.cs`: 메인 창 띄우기(`Main`), 창·대화상자 찍기(`Save`, `Dialog`), 영역 자르기(`SaveControl`, `SaveArea`), 빨간 번호 테두리(`marks` — 컨트롤이나 창 좌표 사각형, `Mark`), 키·마우스 입력, 메뉴 명령 실행(`Command`), 비공개 멤버 접근(`Get`/`Set`/`Call`). 열린 메뉴·우클릭 메뉴는 그림에 합쳐지고, 창에는 제목 줄을 그려 넣습니다.
- `Shots/<장>*.cs`: `[Shot("<쪽 id>")]` 메서드 하나가 그림 한 장 이상을 찍습니다. 여러 장이면 `<쪽 id>-2`처럼 이름을 붙입니다.
- `Sample/wolf.png`: 예시 그림(늑대귀 모험가). 열면 49×98 픽셀 그림으로 가져와집니다.
- 그림 목록과 움짤 요청은 저장소 루트의 `가이드-이미지.md`에 있습니다.
