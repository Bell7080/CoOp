# Networking decision record

## 결정

권장 최소 2인, 방장 포함 최대 6인의 신뢰 기반 협동 게임이다. **방을 만든 플레이어 PC가 NGO 서버와 로컬 플레이어를 함께 실행하는 리슨 서버**다. 전용 게임 서버는 만들지 않았다.

- 게임 상태: Netcode for GameObjects 2.14.0, GameObject/NetworkBehaviour 방식.
- 개발 접속: Multiplayer Services 2.5.0의 Sessions + Relay.
- 출시 접속 예정: Steam 로비·초대와 NGO에 맞는 Steam 전송 계층. **현재 미구현**.
- 이유: 소수 인원 협동·GameObject 워크플로이며 롤백/서버 리와인드가 필수라는 요구는 없다.
- 방장 종료: 현재 방 종료. 자동 호스트 이전·자동 재접속은 미구현이다.

## 설치된 버전과 소스 근거

`ProjectSettings/ProjectVersion.txt`:

```text
m_EditorVersion: 6000.3.29f1
```

`Packages/manifest.json`과 `Packages/packages-lock.json`:

```text
"com.unity.netcode.gameobjects": "2.14.0"
"com.unity.services.multiplayer": "2.5.0"
"com.unity.multiplayer.playmode": "2.0.2"
"com.unity.multiplayer.tools": "2.2.12"
"com.unity.pipeline": "1.0.1"
com.unity.transport version: "2.7.4"
```

설치 소스는 각 PC의 `Library/PackageCache/<package>@<hash>/`에 있다. 패키지 캐시 자체는 Git에 올리지 않는다.

- NGO `package.json`: `"version": "2.14.0"`, `"unity": "6000.0"`.
- NGO `Runtime/Core/NetworkManager.cs`: `public Action<ConnectionApprovalRequest, ConnectionApprovalResponse> ConnectionApprovalCallback`, `public bool Approved`, `public bool CreatePlayerObject`, `public Vector3? Position`.
- NGO `Runtime/Components/NetworkTransform.cs`: `public AuthorityModes AuthorityMode`, enum `Server`, `Owner`.
- NGO `Documentation~/basics/networkvariable.md`: `NetworkVariable.Value`를 사용하며 새 플레이어 스폰 시 현재 값과 이후 변경을 동기화한다.
- MPS `Runtime/Multiplayer/Session/Options/SessionOptions.cs`: `public int MaxPlayers { get; set; }`는 **방장 포함** 상한이다.
- MPS `Runtime/Multiplayer/Modules/Network/Handlers/NetworkHandler/NetworkManagerSession.cs`: `started = NetworkManager.StartHost();`, `started = NetworkManager.StartClient();`. 세션 서비스가 시작을 소유하므로 게임 코드에서 중복 호출하지 않는다.
- MPS `Runtime/Multiplayer/Session/SessionHandler.cs`: `public Task LeaveAsync();`, `public Task DeleteAsync();`.
- Multiplayer Tools `NetworkSimulator/Runtime/NetworkSimulatorPreset.cs`: `PacketDelayMs`, `PacketJitterMs`, `PacketLossPercent`. 기존 UnityTransport.DebugSimulator는 폐기되어 효과가 없으므로 사용하지 않는다.

공식 세션 문서: [create](https://docs.unity.com/en-us/mps-sdk/create-session), [join](https://docs.unity.com/en-us/mps-sdk/join-session), [networking](https://docs.unity.com/en-us/mps-sdk/session-networking).

## 구현 경계

| 구성 요소 | 역할 |
|---|---|
| `IRoomGateway` | 방 생성·참가·퇴장 경계. 현재 구현은 `RelayRoomGateway`. |
| `RelayRoomGateway` | 익명 플레이어 초기화, 코드 참가, Relay 구성, 방장 Delete/참가자 Leave. |
| `RoomController` | 작업 중복 방지, 접속 승인, 서버 슬롯/스폰 위치 지정, 연결 종료 UI 상태. |
| `ConnectionAvatar` | 서버만 쓰는 `ConfirmedSignals` 상태와 소유자만 호출하는 신호 RPC. |
| `FirstPersonController` | (0.2.0, `SandboxAvatarMotor` 대체) CharacterController 1인칭 이동·마우스 시점·Shift 달리기·점프·중력. 소유자 즉시 이동 + Owner 권한 NetworkTransform(위치·Y 회전). 소유자 쓰기 `Pitch` NetworkVariable로 다른 플레이어에게 위아래 시선 표시. 커서 잠금/Esc 해제, 소유자에게만 메인 카메라 부착. 서버 RPC로 받은 넉백·부활 순간이동 적용. |
| `PlayerCombat` | 소유자 좌/우클릭 → `PunchRpc(손, 조준 방향)` 요청. **서버**가 손별 쿨다운(클라 0.4초, 서버 허용 0.3초), 몸 방향 기준 조준 제한(89°), 머리 위치에서 반경 0.3m·거리 2.2m SphereCast, 자기 자신·사망자 제외를 검증한 뒤 피해 적용. 펀치 연출은 `NotOwner` RPC, 적중 표시는 공격자 `Owner` RPC. |
| `PlayerHealth` | 서버만 쓰는 `Health`(100)·`IsDead`. 서버가 피해(10~15), 피해자 소유자에게 넉백 RPC, 0이면 사망 → 3초 뒤 가장 먼 스폰 슬롯으로 소유자 대상 순간이동 RPC → 체력 100 복구. 바닥 아래(-20m)로 떨어지면 서버가 사망 처리. |
| `PlayerAvatarVisual` | 캡슐 몸·머리·양 주먹 표시, 펀치 잽 애니메이션, 피격 빨간 깜빡임, 사망 시 숨김. 소유자는 몸/머리를 그림자만 남기고 주먹만 본다. 게임 상태를 쓰지 않는다. |
| `CombatHud` | 실행 시 생성하는 십자선·적중 표시·내 체력·피격 화면 붉은 효과·조작 안내·전체 체력 목록. |
| `GameText` | 한국어 기본 문구 표. [LOCALIZATION.md](LOCALIZATION.md). |
| `DevelopmentRoomPanel` | 방 코드 UI, 상태·인원·체력·게임 버전 표시. 커서가 잠긴 접속 중에는 숨고 Esc로 다시 표시. |

Steam 구현 시에는 `IRoomGateway` 구현만 만드는 것으로 끝나지 않는다. 사용할 NGO 전송 계층과 Steam SDK 래퍼의 버전·API를 검증하고, SteamID ↔ NGO client ID를 매핑하고, 초대·로비 수명·네트워크 시작을 연결해야 한다. 게임 상태용 NetworkBehaviour는 가능한 한 유지한다. Steam과 Relay의 시작 경로를 동시에 실행하지 않는다.

## 권한과 상태

- `RoomLimits.MaxPlayers = 6`, `RecommendedPlayers = 2`, `TickRate = 30`, `ProtocolVersion = 2`(0.2.0에서 플레이어 프리팹·RPC 변경으로 올림). `RoomController`가 실행 시 이 값을 NetworkConfig에 적용한다.
- `SessionOptions.MaxPlayers`와 서버 승인 슬롯 모두 6인 상한을 적용한다.
- 서버가 빈 슬롯을 선택하고 스폰 위치를 정한다. 클라이언트가 슬롯을 임의 지정하지 않는다.
- 신호는 요청 RPC → 서버 속도 제한 → 서버 `NetworkVariable` 변경 → 모든 참가자 수신이다.
- 펀치 대전: 클라이언트는 "이 손으로 이 방향을 때리겠다"만 요청한다. 쿨다운·조준 제한·적중 판정·피해·사망·부활 위치는 방장이 결정한다. 넉백/부활 순간이동은 이동 권한이 소유자에게 있으므로 방장이 해당 소유자에게 RPC로 지시한다. 방장 쪽 원격 플레이어 위치는 보간된 값이라 지연이 크면 화면과 판정이 어긋날 수 있다(지연 보상/되감기 없음).
- 샌드박스 이동은 Owner 권한이다. 방장 게임 규칙 검증/이동 치트 방지를 완성한 구조는 아니다. 실제 아이템·문·적·피해·보상은 클라이언트 요청을 방장이 검증하도록 추가한다.
- 접속 승인은 `Application.version`, NGO 프로토콜, 프리팹 일치를 확인한다. 모든 소스 차이를 해시로 비교하는 것은 아니므로 팀은 같은 커밋으로 테스트한다.
- 음성은 [VOICE.md](VOICE.md) 설계에 따라 별도 전송/청취 정책으로 추가한다.

## 검증된 범위 — 0.1.0

동일 PC의 별도 MPPM 프로세스가 실제 Relay를 경유했다. 두 집/두 물리 PC 간 검증은 아직 하지 않았다.

```text
host: true, connectedClientIds: [0,1], objects: 2
client: connected: true, clientId: 1, transport: UnityTransport
client ConfirmedSignals: 1 == host's replicated client ConfirmedSignals: 1
client leave -> host clients: 1, objects: 1
rejoin: id 2, existing host signal 1 received
host leave -> client connected: false
approval: [true,true,true,true,true,true,false]
mismatchedBuildRejected: true
```

최종 소스·버전 `0.1.0`로 다시 실행했다.

```text
client moves to x=0, y=1.05, z=2 -> host sees the same position
host moves to x=-0.9999996 -> client sees the same x
client simulator available: true
packet delay 100ms, jitter 20ms, packet loss 2%
client remains connected; server/client ConfirmedSignals both 2
```

최대 인원은 승인 콜백에 대한 6개 승인/7번째 거절 테스트다. 실제 6인 동시 플레이·대역폭·호스트 CPU 예산은 아직 측정하지 않았다. 30Hz 설정은 씬의 실제 NetworkConfig를 읽어 검사했다. 초당 전송량/관련 오브젝트 개수는 콘텐츠 구현 후 계측해야 하며 임의의 성능 보장을 붙이지 않는다.

현재 조합에는 일부 문서의 `netcode.gameobjects` / `simulator` CLI 그룹이 없어 동일 상태를 설치된 공개 NGO/Multiplayer Tools API의 `script eval`로 확인했다. 에디터 복제본은 별도 프로세스이며, 프로젝트 수정은 메인 에디터에서만 했다. 상세한 실패 시도와 조정도 [개발 로그](Devlog/2026-11-24-v0.1.0.md)에 기록했다.

## 검증된 범위 — 0.2.0 펀치 대전

동일 PC의 메인 에디터(방장)와 별도 MPPM 프로세스(참가자)가 Relay를 경유했다. 두 물리 PC 간 검증은 아직 없다.

```text
both: ver 0.2.0, protocol 2; client spawn slot 1 (0, 1.04, 0)
client walk/run 4.02 / 6.91 m per s, jump +1.09 m -> host sees same final pos, remote y 1.04..2.04
host walk/run/jump -> client sees same final pos, remote y 1.04..2.03; client pitch -30 -> host pitchNV -30
client -> host punches: 100,85,70,60,45,30,15,2,0 -> dead -> ~3 s -> hp 100 at farthest slot (-3, 1.04, 3)
host -> client punches: 100,90,79,67 (same on both screens)
client leave -> camera restored; rejoin id 2 receives host hp 85; host leave -> client disconnected notice
```

펀치·이동 입력은 같은 코드 경로를 호출하는 자동화(`PlayerCombat.TryPunch`, `FirstPersonController.AutomationMove/RequestJump`)로 넣었다. 실제 마우스 좌/우클릭·Esc 커서 전환은 사람이 두 화면에서 직접 확인해야 한다. 상세: [0.2.0 개발 로그](Devlog/2026-11-24-v0.2.0.md).

## 다음 실제 두 PC 검사

같은 커밋/빌드로 서로 번갈아 방장을 맡는다. 생성→코드 참가→양방향 이동·점프→서로 펀치(좌/우클릭)·사망·부활→신호→퇴장/재참가→방장 종료를 확인한다. 이후 6인, 지연·손실, 서로 다른 버전 거절, 공유기/회사 네트워크, 장시간 실행을 검사한다. 마이크는 실제 장치로 별도 검사한다.

## 0.7.0 파손 상태

- `Appliance.Broken`은 서버만 쓰는 NetworkVariable이다. 충돌·긁힘으로 가치가 0이 되면 방장이 운반자를 놓고 플러그/소켓 관계를 해제한 뒤 파손 상태를 설정한다. 파손 물건은 상호작용·충돌·정산 대상에서 제외된다.
- 기본 가치가 0인 멀티탭은 생성 즉시 파손되지 않는다. 숨김·보관·이미 정산된 물건에는 손상이 적용되지 않는다.
- 가치 숫자, 감소 팝업, 원본 메시의 GPU 분할 파편과 소리는 복제된 값에서 생성하는 로컬 표현이다. 파편은 게임 물리나 다른 플레이어에게 피해를 주지 않는다. 뒤늦게 접속한 플레이어에게 과거 파손 효과를 반복하지 않는다.
- 프로토콜은 5, 게임 버전은 0.7.0이다. 이전 버전과 혼용하지 않는다.

## 0.8.0 몬스터 인지와 음향

- `Recognizing`과 `Chasing`은 호스트만 기록한다. 최초 발견 후 0.6초 인지 유예 동안 정지하고 공격하지 않는다. 이때 시야가 0.3초 이상 끊기면 인지를 취소한다. 클라이언트는 눈 색과 음향 등 표현만 실행한다.
- 프로토콜 6 / 게임 버전 0.8.0. 같은 버전의 메인 에디터와 별도 MPPM Player 2 프로세스로 Relay 참가 및 인지·추격 상태 복제를 확인했다.
- 효과음은 원본 ElevenLabs 클립을 각 클라이언트에서 재생한다. 문·플러그·손전등·체력은 복제된 상태 변화를, 괴물은 기존 서버 승인 이벤트를 사용한다. 발소리와 배경음은 로컬 표현이며 음성 PCM 전송을 추가하지 않았다.
- 테스트 복제 에디터가 0.7.0 설정으로 남아 있으면 접속 승인이 거절된다. 호스트와 테스트 시나리오를 정상 종료하고 재시작해 같은 bundleVersion을 받게 한다. 승인 검사를 우회하지 않는다.
