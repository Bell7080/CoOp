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
| `SandboxAvatarMotor` | 소유자 즉시 이동, Owner 권한 NetworkTransform 동기화. 최종 게임 모터가 아니다. |
| `DevelopmentRoomPanel` | 방 코드 UI, 상태·인원·게임 버전 표시. |

Steam 구현 시에는 `IRoomGateway` 구현만 만드는 것으로 끝나지 않는다. 사용할 NGO 전송 계층과 Steam SDK 래퍼의 버전·API를 검증하고, SteamID ↔ NGO client ID를 매핑하고, 초대·로비 수명·네트워크 시작을 연결해야 한다. 게임 상태용 NetworkBehaviour는 가능한 한 유지한다. Steam과 Relay의 시작 경로를 동시에 실행하지 않는다.

## 권한과 상태

- `RoomLimits.MaxPlayers = 6`, `RecommendedPlayers = 2`, `TickRate = 30`, `ProtocolVersion = 1`.
- `SessionOptions.MaxPlayers`와 서버 승인 슬롯 모두 6인 상한을 적용한다.
- 서버가 빈 슬롯을 선택하고 스폰 위치를 정한다. 클라이언트가 슬롯을 임의 지정하지 않는다.
- 신호는 요청 RPC → 서버 속도 제한 → 서버 `NetworkVariable` 변경 → 모든 참가자 수신이다.
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

## 다음 실제 두 PC 검사

같은 커밋/빌드로 서로 번갈아 방장을 맡는다. 생성→코드 참가→양방향 이동→신호→퇴장/재참가→방장 종료를 확인한다. 이후 6인, 지연·손실, 서로 다른 버전 거절, 공유기/회사 네트워크, 장시간 실행을 검사한다. 마이크는 실제 장치로 별도 검사한다.
