# Chickxel 온라인 에셋 카탈로그 원본 (예전 정적 목록)

1.14.61까지의 앱이 에셋 관리자의 **둘러보기** 탭에서 읽는 정적 목록의 원본입니다. 여기의 패키지를 검사해 홈페이지(`docs/`)의 `docs/catalog/`에 정적 index와 파일을 만들고, 그 버전들은 `https://1000pro-1997.github.io/Chickxel/catalog/index.json`을 HTTPS로 읽습니다.

그다음 버전의 앱은 Chickxel 커뮤니티 서비스(`https://chickxel.com/api/catalog/index.json`, 같은 모양의 목록)를 읽고, 사용자는 앱의 **온라인에 올리기…** 로 Chickxel 계정을 써서 패키지를 바로 올립니다(검토 없이 게시, `review`는 `community`, 공식 패키지는 `official`). 이 폴더는 예전 버전을 위해 남겨 둡니다.

## 구조

```text
catalog/
  <패키지 ID>/
    <패키지 ID>-<버전>.chickxel-asset   제출한 패키지 하나(바이트 그대로 게시)
    review.json                       검토 정보
```

`review.json`:

```json
{
  "schemaVersion": 1,
  "summary": "목록에 보일 짧은 설명(200자 이하)",
  "tags": ["gradient", "starter"],
  "review": "reviewed",
  "published": "2026-10-28",
  "updated": "2026-10-28",
  "preview": "previews/pack.png"
}
```

- `review`: `official`(작성자 ID `chickxel`의 패키지만) 또는 `reviewed`(검토를 마친 기여 패키지).
- `tags`: 소문자·숫자·하이픈, 8개까지.
- `preview`(선택): 패키지 안 미리보기 PNG. 없으면 첫 에셋의 미리보기를 씁니다.

## 패키지 나누기

- 에셋 하나에 패키지 하나가 기본입니다. 그래야 둘러보기에서 에셋마다 이름으로 검색되고, 미리보기가 보이고, 따로 설치·삭제·업데이트됩니다.
- 레시피는 앱의 기본 효과를 `type`으로 부르고 그라데이션·스크립트 원문은 값으로 복사하므로, 다른 패키지에 기대는 에셋은 없습니다. 공통 기능이 필요하면 앱의 기본 효과나 스크립트 기본 함수로 넣습니다(라이브러리 패키지를 두지 않음).
- 같이 써야 맞는 테마 세트(한 팔레트에 맞춘 그라데이션 묶음, 한 게임 스타일의 피격·사망·회복 효과 묶음)만 한 패키지에 묶습니다.
- 공식 패키지 ID는 `chickxel.gradients.<이름>`, `chickxel.effects.<이름>`입니다.

## 검사 규칙

- 패키지는 앱 설치와 같은 엄격 검사를 통과해야 합니다(경로, 크기, 해시, 모든 에셋 schema, 미리보기).
- 폴더 이름 = 패키지 ID, 패키지 ID의 첫 조각 = 작성자 ID. 작성자 ID `chickxel`은 공식 패키지(`official`)만 씁니다.
- 개인 내보내기 패키지(작성자 `personal`, 라이선스 `LicenseRef-Personal`)는 받지 않습니다. 패키지 안에 `LICENSE.txt`나 `LICENSE.md`가 있어야 합니다.
- 목록 미리보기가 있어야 합니다(`review.json`의 `preview`나 미리보기가 있는 에셋).
- 다른 제출과 이름이나 패키지 파일이 겹치면 안 됩니다.
- 이미 게시한 버전의 내용은 바꿀 수 없고 버전을 내릴 수 없습니다. 고친 패키지는 버전을 올려 파일을 바꿉니다(목록에는 최신 버전 하나만 남습니다).

이 규칙은 Core의 `AssetPublishCheck`에 한 벌만 있고, `tools/CatalogBuilder`(CI)와 앱의 **온라인에 올리기** 창이 같이 씁니다(앱은 올리기 전에 검사하고, 작성자 ID·패키지 ID 첫 조각 = 계정 핸들 같은 소유 규칙은 서버가 다시 봅니다).

## 제출하기 (예전 버전의 기여자)

1.14.61까지의 앱은 **온라인에 올리기…** 에서 `<ID>-<버전>.chickxel-asset`과 `<ID>-<버전>-submission.zip`(이 폴더 구조 그대로 `<ID>/` 아래 패키지와 `review.json`)을 저장하고 [에셋 제출 양식](https://github.com/1000Pro-1997/Chickxel/issues/new?template=asset-submission.yml)을 엽니다. 기여자는 양식의 첨부 칸에 submission zip을 끌어다 놓고 제출합니다. 새 버전의 앱은 이 양식을 쓰지 않고 chickxel.com에 바로 올립니다.

## 제출 처리 (관리자)

1. 이슈의 zip을 받아 저장소의 `catalog/`에 풉니다(`catalog/<ID>/<ID>-<버전>.chickxel-asset`, `catalog/<ID>/review.json`). 새 버전이면 그 폴더의 예전 패키지 파일을 지우고, `review.json`의 `published`는 처음 게시한 날짜로 둡니다(예전 앱은 받은 목록에 있으면 그 날짜를 씁니다).
2. 에셋과 미리보기를 직접 보고 검토합니다. `review`는 `reviewed`(예전 앱이 쓰는 값) 그대로 둡니다.
3. `dotnet run --project tools/CatalogBuilder`로 `docs/catalog/`를 다시 만들고 `catalog/`와 함께 커밋합니다. 규칙에 걸리면 까닭이 한 줄씩 나옵니다 — 이슈에 그대로 답하면 됩니다.
4. 양식이 붙이는 `asset-submission` 라벨은 저장소에 만들어 두어야 붙습니다.

## 만들기와 확인

```
dotnet run --project tools/CatalogBuilder            # docs/catalog/ 다시 만들기
dotnet run --project tools/CatalogBuilder -- --check # 검사만, docs/catalog/가 최신이 아니면 실패(CI)
```

`docs/catalog/`의 `index.json`(다음 쪽은 `index-2.json` …, 쪽마다 50개), `packages/`, `previews/`는 손으로 고치지 않습니다.

공식 효과 패키지(`chickxel.effects.<효과 ID>`, 앱에 든 `src/Chickxel.Core/Scripting/Official/<ID>.js` 원문)는 CatalogBuilder가 지금 버전 파일이 없을 때 만듭니다. 원문을 고치면 `OfficialEffectPackages`의 버전을 올립니다(올리지 않으면 `--check`가 실패). CatalogBuilder는 지금 원문과 이 폴더·`docs/catalog/packages/`의 공식 효과 패키지(모든 버전) 원문의 해시를 `src/Chickxel.Core/Scripting/Official/hashes.txt`에 더하고, `--check`는 빠진 해시가 있으면 실패합니다. 앱은 이 목록의 원문을 묻지 않고 실행하므로, 예전 공식 원문을 담은 프로젝트·레시피도 원문이 바뀐 뒤에 그대로 열립니다. 목록의 줄은 지우지 않습니다.
