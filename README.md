# CoOpHorror / CoOp

**현재 게임 버전: 0.8.0** · Unity **6.3 LTS `6000.3.29f1`** · **Universal 3D / URP** · Windows PC

친구와 함께 만드는 2~6인 협동 공포 게임의 초기 개발 프로젝트다. 권장 최소 인원은 2명, 접속 상한은 방장 포함 6명이다. 게임 이름은 **ElecMole**(전기 두더지)이다. 지금 들어 있는 것은 **인터넷 연결·동기화 샌드박스**와, 발전기→공용 전기→가전 충전→송신구 정산→암전→우주선 탈출을 해 볼 수 있는 **ElecMole 코어 루프 프로토타입**이다. 기획 요약은 [GAME_DESIGN.md](Docs/GAME_DESIGN.md)에 있다.

## 언어 / 현지화

**게임 기본 언어는 한국어(ko-KR)**다. 영어(en), 중국어 간체(zh-Hans), 일본어(ja)는 추후 추가 예정이다. 플레이어에게 보이는 문구는 코드에 직접 쓰지 않고 `GameText` 키 표를 거친다. 규칙과 추가 절차는 [LOCALIZATION.md](Docs/LOCALIZATION.md)를 따른다.

## 먼저 열 것

Unity Hub에서 이 저장소를 복제한 폴더 자체를 **Add → Add project from disk**로 추가하고, 정확히 `6000.3.29f1`로 연다. `Assets/_Game/Scenes/ElecMolePrototype.unity`(코어 루프 프로토타입) 또는 `NetworkSandbox.unity`(연결 샌드박스)를 열고 Play를 누른다. Steam용 별도 프로젝트 템플릿은 필요 없다.

원래 PC의 경로는 `D:\Unity Games\CoOpHorror`다. 다른 개발자는 원하는 경로에 별도 복제한다. Unity 에디터/캐시의 드라이브 이동은 수행하지 않았다.

## 친구 PC 최초 준비

Git for Windows(Git LFS 포함), Unity Hub와 동일한 에디터를 준비한다. PowerShell에서:

```powershell
git clone https://github.com/Bell7080/CoOp.git
cd CoOp
git lfs pull
.\tools\setup-dev.ps1
```

에디터도 CLI로 설치하려면 `setup-dev.ps1 -InstallEditor`를 사용한다. 각 개발자는 자신의 Unity 라이선스/에디터 약관을 사용한다. 이 스크립트가 동의를 대신하지 않는다. 로컬 PowerShell 실행 정책이 막는 경우 스크립트 내용을 검토한 뒤 해당 프로세스에만 허용하거나 Unity Hub에서 직접 프로젝트를 연다.

설정 스크립트는 공식 Unity CLI와 설치된 Codex·Claude Code의 Unity 플러그인을 준비한다. AI 도구 자체의 설치/로그인은 별도다. 플러그인 설치 뒤 Codex/Claude를 재시작한다. `unity`가 현재 터미널 PATH에 없어도 프로젝트의 `tools/unity.ps1`은 기본 설치 경로를 찾아 실행한다.

## 서로 다른 PC에서 방장/참가 테스트

1. 두 개발자가 **동일한 커밋·버전**을 받고 Unity에서 같은 씬(`ElecMolePrototype` 또는 `NetworkSandbox`)을 실행한다. 또는 같은 Windows 개발 빌드를 각각 실행한다.
2. 친구가 **인터넷 방 만들기**를 누른다. **친구 PC가 게임 서버와 플레이어를 겸한다.**
3. 친구가 **코드 복사**로 복사한 코드를 전달한다.
4. 다른 사람이 코드 입력 후 **참가**를 누른다. 공유기 포트 포워딩 없이 Relay를 경유한다.
5. 접속하면 1인칭 시점이 되고 마우스가 잠긴다. **WASD 이동 · Shift 달리기(스태미나) · Space 점프/스킬체크 · 마우스 시점 · 좌클릭 상호작용(누르기·길게, 꾹: 염력) · 우클릭 꾹: 든 물건 돌리기 · Q 놓기 · 숫자 1 인벤토리 · E 무전 · F 손전등**. 펀치 공격은 v0.3.0에서 없앴다.
   - `ElecMolePrototype`: 우주선 안에서 시작한다. 발전기를 돌려 공용 전기를 모으고 우주선 문·CCTV·가전·송신구에 전력을 넣는다. 충전한 가전은 염력으로 들거나 플러그를 뽑아 전선을 끌고 가서 송신구에 꽂는다. 우주선 모니터를 좌클릭하면 CCTV 관람 모드(지도·카메라 조명). 송신구 소켓을 다 채우면 암전이 오고, 우주선으로 돌아와 출발 버튼을 누르면 탈출한다. 죽으면 시체가 남고 본인은 유령으로 관전한다. 동료가 시체를 연타하거나 우주선 안으로 옮기면 살아난다. 자세한 순서는 [v0.4.0 개발 로그](Docs/Devlog/2026-11-25-v0.4.0.md)의 테스트 방법을 본다.
   - `NetworkSandbox`: 이동·시점 동기화 확인용. 죽으면 시체 연타로 부활하고, 전원이 죽으면 3초 뒤 모두 부활한다. 두 화면의 오른쪽 위 체력 목록이 같은지 확인한다.
   - **Esc**: 커서를 풀고 방 패널을 다시 표시한다. 게임 화면(패널 밖)을 클릭하면 조작으로 돌아간다.
   - **G** 또는 **동기화 신호 보내기** 버튼: 서버 기록 신호. 두 화면의 신호 값이 같아지는지 확인한다.
6. **방 나가기**로 나간다. 참가자가 나가면 그 플레이어가 제거되고, 방장이 나가면 방과 참가자 연결이 종료된다.

방 생성/참가 중에는 버튼이 잠긴다. 참가 실패 이유는 화면에 표시된다. 코드와 같은 빌드를 공유한다. 코어 루프 프로토타입의 맵·캐릭터·괴물·소리는 모두 임시다. 이동은 각자 PC가 결정하고(치트 방지 아님), 전기·충전·체력·피해·부활·스테이지 진행은 방장 PC가 판정한다.

개발용 Unity Cloud 프로젝트는 이미 **ET0707 / CoOpHorror**에 연결돼 있다. 친구가 별도 Cloud 프로젝트를 새로 만들어 링크를 바꾸지 않는다. 서비스 관리 권한과 게임 플레이용 익명 로그인은 별개다.

Relay는 연결을 중계하고 게임 시뮬레이션은 방장 PC가 실행한다. UGS 무료 구간과 초과 사용 요금은 [공식 가격표](https://unity.com/products/gaming-services/pricing)를 따른다. 이 구성에서 유료 플랜 전환·결제·상점 출시는 수행하지 않았다.

## 자동 E2E 테스트

에디터를 열어 둔 상태(플레이 모드 꺼짐)에서 PowerShell로 실행한다.

```powershell
.\tools\e2e-elecmole.ps1
```

MPPM `DeveloperHostClient` 시나리오로 메인 에디터(방장)와 Player 2 복제 에디터(참가자, 별도 프로세스)를 띄우고 Relay로 연결한 뒤, 두 플레이어를 실제 입력 경로로 자동 조작해 발전기부터 탈출까지 42단계를 검증한다(약 4분). 결과와 화면 캡처는 `Logs/E2E/<시각>/report.md`에 남는다. 자동 조작 코드는 `Assets/_Game/Scripts/Testing/ElecMoleE2E.cs`(에디터·개발 빌드 전용)다.

## Unity CLI / AI

```powershell
.\tools\unity.ps1 status --format json
.\tools\unity.ps1 command console status --format json
.\tools\unity.ps1 command script recompile
.\tools\unity.ps1 command script recompile_status
.\tools\unity.ps1 command editor.playmode start
.\tools\unity.ps1 command editor.playmode stop
```

Codex·Claude에서 **이 저장소 폴더를 작업 폴더로 열고** 요청한다. 예: “AGENTS.md와 NETWORKING.md를 읽고, 기존 NGO 구조를 유지해 문 상호작용을 구현해줘. 호스트와 클라이언트 모두 검증하고 버전·개발 로그도 갱신해줘.”

공통 지침은 `AGENTS.md`, Claude 진입 지침은 `CLAUDE.md`다. `DeveloperHostClient` Play Mode Scenario는 메인 에디터와 별도 가상 플레이어 1명을 실행하는 로컬 테스트 설정이다. 메모리가 부족하면 두 PC에서 직접 테스트한다. 메인 에디터에서 시나리오를 편집하고, 복제 플레이어에서는 프로젝트 파일을 바꾸지 않는다.

## Windows 실행 파일 만들기

Play 모드를 종료하고 Unity 메뉴 **CoOp → Build → Windows development player**를 실행한다.

출력: `Builds/WindowsDev/CoOpHorror.exe`. 친구에게 보낼 때는 `.exe`만 보내지 말고 **WindowsDev 폴더 전체**를 묶어서 보낸다. 빌드 폴더는 Git에서 제외된다. Windows용 Mono 개발 빌드이며 Steam 출시 빌드/IL2CPP 설정은 후속 작업이다.

## 두 명 + AI 협업

- 각자 별도 복제본·기능 브랜치를 쓴다. 같은 폴더를 네트워크 드라이브로 공유하지 않는다.
- 한 AI가 네트워크/게임 규칙을, 다른 AI가 별도 UI/맵/에셋을 맡도록 파일 소유 범위를 정한다.
- 같은 `.unity`, `.prefab`, ProjectSettings 파일을 동시에 바꾸지 않는다. 기능은 프리팹으로 나눈다.
- `.meta`를 에셋과 함께 커밋한다. `Library/`, `Temp/`, `Builds/`, `Logs/`, 사용자별 설정·자격 증명은 올리지 않는다.
- Git LFS를 사용한다. 친구도 LFS를 설치하고 `git lfs pull`로 실제 에셋을 받는다.
- 변경 후 컴파일·콘솔을 확인하고 별도 프로세스로 네트워크를 검증한다. main에 합치기 전 상대방이 변경 내역을 읽는다.

## 버전과 기록

게임 화면의 버전, `VERSION`, Unity의 `bundleVersion`이 같은 번호를 사용한다. **정식 출시 전에는 0.x.y**, **1.0.0은 정식 출시 전용**이다.

- [전체 변경 이력](CHANGELOG.md)
- [개발 로그 모음](Docs/Devlog)
- [업데이트 명령·버전 정책](Docs/VERSIONING.md)
- Unity 메뉴 **CoOp → Version → Open changelog**

## Steam과 음성 후속 작업

[NETWORKING.md](Docs/NETWORKING.md)에 현재 구조와 검증 범위를, [VOICE.md](Docs/VOICE.md)에 근접·무전 설계를 기록했다.

Steam 출시에 필요한 것은 별도 Steamworks AppID, Steam 로비/초대와 NGO에 맞는 Steam 전송 계층, 실제 플랫폼 테스트다. 아직 Steam SDK·음성 SDK를 설치하거나 Steam 등록·업로드를 수행하지 않았다. Steam 로비만으로 NGO 동기화가 자동 제공되는 것은 아니다.

AI로 만든 게임 내 아트·음성·텍스트와 사용 권한의 출처도 작업 로그에 남긴다. Steam 제출 시 [콘텐츠 설문](https://partner.steamgames.com/doc/gettingstarted/contentsurvey)의 당시 기준에 따라 신고 내용을 확인한다.
