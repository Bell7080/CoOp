# 언어 / 현지화

## 결정

- **기본 언어: 한국어 (`ko-KR`)**. 게임의 모든 기본 문구는 한국어로 작성한다.
- **추가 예정 언어**: 영어(`en`), 중국어 간체(`zh-Hans`), 일본어(`ja`). 아직 번역 표는 만들지 않았다.
- 출시 전 번역 작업 시 Unity 공식 `com.unity.localization` 패키지(String Table)로 옮기는 것을 기본 계획으로 한다. 패키지는 아직 설치하지 않았다.

## 규칙

1. 플레이어가 보는 문구(UI, HUD, 상태 메시지, 오류 안내)를 코드나 씬에 **직접 문자열로 넣지 않는다**.
2. 모든 문구는 `Assets/_Game/Scripts/Localization/GameText.cs`의 **키**로 가져온다.
   - `GameText.Get("room.host")` → `인터넷 방 만들기`
   - `GameText.Format("hud.health", hp, max)` → `체력 87 / 100`
3. 새 문구는 `GameText`의 한국어 표에 `영역.이름` 형식 키로 추가한다. 예: `door.locked`, `hud.stamina`.
4. 문장을 이어 붙이지 말고 `{0}` 자리표시자를 쓴 한 문장으로 만든다. 언어마다 어순이 다르다.
5. 개발자용 로그(`Debug.Log`), 코드 주석, 패키지 API 오류 원문은 번역 대상이 아니다. 플레이어에게 보일 때는 `status.error` 같은 한국어 틀로 감싼다.
6. 글꼴: uGUI 기본 글꼴은 한글 글리프가 없을 수 있어 `GameText.UiFont`(OS 글꼴 Malgun Gothic → Microsoft YaHei → Yu Gothic → Arial 순서)를 사용한다. 출시 전에는 라이선스가 확인된 CJK 글꼴 에셋을 포함하고 TextMeshPro 폰트 폴백을 구성한다.

## 언어 추가 절차(예정)

1. `GameLanguage` 열거형에 이미 `English`, `ChineseSimplified`, `Japanese`가 있다. 해당 언어 표를 `GameText.Tables`에 추가하면 같은 키로 바뀐다. 빠진 키는 한국어로 대체된다.
2. 언어 수가 늘거나 번역가와 협업할 때는 `com.unity.localization`의 String Table로 옮기고 `GameText.Get`의 내부만 바꾼다. 호출 코드는 그대로 둔다.
3. 각 언어 글꼴(간체/일본어 한자 범위)과 줄 길이, 설정 화면의 언어 선택을 함께 검증한다.

## 현재 적용 범위 — 0.2.0

- 방 패널(`DevelopmentRoomPanel`), 방 상태·오류(`RoomController`, `RelayRoomGateway`), 전투 HUD(`CombatHud`)가 모두 `GameText`를 사용한다.
- 씬에 저장된 영어 라벨은 실행 시 `GameText`의 한국어 문구로 덮어쓴다.
- 언어 선택 UI는 아직 없다(항상 한국어).
