# Changelog

## [0.2.0] - 2026-11-24

1인칭 시점과 간단한 캐릭터로 멀티 테스트에서 서로 펀치로 싸울 수 있게 했다. 게임 기본 언어를 한국어로 정하고 문구 키 표를 도입했다.

### 변경

- 게임 기본 언어 한국어(ko-KR) 명시. 영어·중국어 간체·일본어 추가 예정. Docs/LOCALIZATION.md, README 언어 섹션, AGENTS.md 규칙 추가.
- GameText 키 기반 문구 표(언어 열거형, 한국어 기본값, OS 한글 글꼴). 방 패널·방 상태·오류·HUD 문구를 모두 한국어로 전환.
- SandboxAvatarMotor를 FirstPersonController로 교체: CharacterController, 마우스 시점(상하 ±85°), WASD 4m/s, Shift 7m/s, Space 점프·중력. Owner 권한 NetworkTransform 유지, 소유자 쓰기 Pitch NetworkVariable.
- 로컬 플레이어에게 메인 카메라를 머리 높이에 부착, 자기 몸/머리는 그림자만 표시. 커서 잠금, Esc로 해제 후 방 패널 사용, 게임 화면 클릭으로 복귀, 방 코드 입력 중 조작 무시. E 신호 유지.
- 간단한 캐릭터: 캡슐 몸·머리·양 주먹, 모든 화면에 보이는 펀치 잽 애니메이션과 피격 붉은 깜빡임.
- 호스트 권한 전투: PlayerHealth(서버 기록 체력 100), PlayerCombat(좌클릭 왼주먹/우클릭 오른주먹, 0.4초 쿨다운, 서버에서 쿨다운·조준 제한·2.2m SphereCast 검증, 10~15 피해, 피해자 소유자에게 넉백), 체력 0이면 3초 뒤 가장 먼 스폰 위치에서 부활, 낙하 시 사망 처리.
- HUD: 십자선, 적중 표시, 내 체력, 피격 화면 효과, 한국어 조작 안내, 전체 플레이어 체력 목록. 접속 중 커서가 잠기면 방 패널 숨김.
- RoomLimits.ProtocolVersion 1 → 2(실행 시 NetworkConfig에 적용). 플레이어 프리팹과 씬은 에디터 메뉴 CoOp/Setup/Upgrade to first-person fighting (v0.2)로 갱신.
- 도구 수정: update-version.ps1이 Windows PowerShell 5.1에서도 UTF-8로 읽도록 수정, MPPM 복제 플레이어에서 VERSION 파일이 없을 때 ProjectVersionSync 예외가 나지 않도록 수정.

### 검증

- 메인 에디터 재컴파일 오류 0개.
- 동일 PC 메인 에디터(방장) + MPPM 가상 플레이어(별도 프로세스)로 Relay 방 생성·코드 참가 성공, 양쪽 ProtocolVersion 2.
- 방장·참가자 양쪽 Application.version 0.2.0, PlayerSettings.bundleVersion 0.2.0. 이동·점프·시점 동기화, 양방향 펀치 피해, 사망→3초 뒤 부활, 퇴장/재참가/방장 종료 확인(상세 수치는 개발 로그).

### 남은 항목

- 두 대의 실제 PC(서로 다른 네트워크)에서는 아직 테스트하지 않았다.
- 실제 마우스 클릭으로 펀치하는 경로는 첫 테스트 중 방장 에디터에서 누군가 직접 조작한 입력으로만 관찰됐다(좌/우 구분은 확인 못 함). 나머지 펀치·이동·점프는 같은 코드 경로를 부르는 자동화 호출(PlayerCombat.TryPunch, FirstPersonController.AutomationMove/RequestJump)로 검증했다. 사람이 두 화면에서 직접 좌/우클릭·WASD·Esc/재클릭을 해 보는 확인이 필요하다.
- 지연 보상 없음: 지연이 크면 화면상 맞은 것 같아도 방장 판정에서 빗나갈 수 있다. 이동은 Owner 권한이라 치트 방지가 아니다.
- 영어·중국어·일본어 표, 언어 선택 UI, 출시용 CJK 글꼴 에셋은 아직 없다.
- Windows 개발 빌드는 이번 버전에서 다시 만들지 않았다.

[개발 로그](Docs/Devlog/2026-11-24-v0.2.0.md)

## [0.1.0] - 2026-11-24

2~6인 협동 공포 게임의 Unity·AI·협업·리슨 서버 개발 환경을 처음 구성했다.

### 변경

- Unity 6.3 LTS `6000.3.29f1`, Universal 3D(URP) 프로젝트 생성.
- GitHub `Bell7080/CoOp` 기존 이력에 연결. Unity 무시 규칙, Git LFS, Smart Merge 설정.
- Codex·Claude 공통 지침과 개발 PC 초기 설정·Unity CLI 실행 스크립트 추가.
- ET0707 조직의 CoOpHorror Cloud 프로젝트와 개발용 Multiplayer Services + Relay 연결.
- 방 코드 생성·참가·복사·퇴장 UI, 최대 6인 접속 승인, 빌드 버전 불일치 거절.
- 등록된 네트워크 플레이어 프리팹과 서버가 기록하는 신호 동기화 테스트 씬.
- WASD 이동 샌드박스와 Windows 개발 빌드 메뉴.
- Steam 리슨 서버 교체 지점과 근접·무전 음성 설계 문서.
- 0.x 버전 정책, VERSION 동기화, 변경 이력·개발 로그 자동 작성 명령.

### 검증

- Unity 에디터와 CLI 연결, 컴파일 오류 0개.
- 별도 MPPM 프로세스에서 Relay 방 생성·참가 성공: 방장 ID 0, 참가자 ID 1, 생성 객체 2개.
- 참가자 신호 값 1이 방장과 참가자 양쪽에서 일치.
- 참가자 퇴장 시 방장에게 플레이어/객체 1개만 남음.
- 재참가 ID 2가 방장의 기존 신호 값 1을 수신.
- 방장 퇴장 후 참가자 연결 종료 및 안내 표시.
- 접속 승인 프로브: 6명 승인, 7번째 거절, 잘못된 빌드 버전 거절.
- 최종 0.1.0에서 양방향 이동 동기화 및 신호 값 일치 확인.
- 100ms 지연·20ms 지터·2% 패킷 손실에서 연결과 동기화 유지.
- Windows x64 개발 빌드 성공. VERSION/Unity 게임 버전 모두 0.1.0.
- 업데이트 명령의 patch/minor 계산, README·CHANGELOG·개발 로그 동시 생성, 1.0.0 거절 검증.

### 남은 항목

- 친구 PC를 포함한 서로 다른 실제 네트워크 테스트.
- Steam AppID, 로비·초대·Steam 전송 계층 및 근접/무전 음성 구현.
- 실제 공포 게임 콘텐츠, 첫인칭 컨트롤러, 상호작용·적 AI·저장 시스템.
- 방장 이탈 시 자동 호스트 이전은 구현하지 않음.

[개발 로그](Docs/Devlog/2026-11-24-v0.1.0.md)
