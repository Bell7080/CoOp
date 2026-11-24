# 버전과 개발 로그 정책

정식 출시 전에는 `0.MINOR.PATCH`만 사용한다. **`1.0.0`은 정식 출시 전용**이다. 사용자가 정식 출시를 명시적으로 지시하기 전에는 주요 버전을 1로 바꾸지 않는다.

- 현재 시작 버전: **0.1.0**.
- PATCH: 버그 수정, 작은 기능 개선, 개발 도구 개선. 예: `0.1.0 → 0.1.1`.
- MINOR: 팀이 사용하는 기능 묶음 추가 또는 네트워크 호환성이 바뀌는 큰 개발 단계. 예: `0.1.7 → 0.2.0`.
- 파일 저장·컴파일마다 올리지 않는다. 사용자에게 전달하는 한 묶음의 업데이트마다 한 번 올린다.
- 전달 시 변경 기록·개발 로그·검증 결과를 반드시 남긴다. 미검증 항목과 실패한 시도도 숨기지 않는다.
- 두 개발자가 같은 다음 버전을 중복 등록하지 않도록 통합 담당자 한 명이 최신 main 기준으로 버전과 최종 로그를 확정한다. 각 기능 브랜치의 변경·검증 메모는 합칠 때 한 버전 기록으로 정리한다.

## 한 번에 업데이트 기록하기

프로젝트 루트의 PowerShell에서 실행한다.

```powershell
.\tools\update-version.ps1 -Bump patch -Summary '방 참가 오류 수정' -Changes '참가 실패 후 다시 시도할 수 있도록 수정' -Validation '호스트+클라이언트 재참가 확인' -KnownIssues '실제 두 PC 테스트 필요'
```

`-Bump minor`는 기능 묶음 단위다. `-DryRun`을 붙이면 파일을 바꾸지 않고 다음 버전만 확인한다. 이 스크립트에는 major 업데이트 옵션이 없다.

## 한 업데이트에 포함할 파일

1. `VERSION`: 버전의 단일 원본.
2. `ProjectSettings/ProjectSettings.asset`의 `bundleVersion`: Unity가 사용하는 빌드 버전. `ProjectVersionSync`가 `VERSION`을 읽어 반영한다.
3. `CHANGELOG.md`: 최신 버전이 위에 오는 전체 변경 이력.
4. `Docs/Devlog/YYYY-MM-DD-v0.x.y.md`: 목표, 구현, 검증, 남은 문제, 다음 작업.
5. 네트워크 메시지/게임 규칙이 호환되지 않게 바뀌면 `RoomLimits.ProtocolVersion`도 검토한다. 단순 문서 수정마다 프로토콜을 올리지는 않는다.

업데이트 스크립트는 README의 현재 버전 표시도 함께 바꾼다. VERSION과 README/CHANGELOG가 이미 불일치하면 진행을 거절하므로 먼저 기록을 바로잡는다.

빌드 전 `CoOp → Version → Apply VERSION to Player Settings`를 실행하거나 기본 빌드 메뉴를 사용한다. 기본 빌드 도구는 버전을 먼저 반영한다. 테스트 화면은 `Application.version`을 표시한다. 편집기에서 읽는 `ProjectSettings/ProjectVersion.txt`는 **Unity 에디터 버전**으로, 게임 버전과 다르다.

같은 멀티 테스트에 참가하는 개발자는 같은 커밋·버전의 프로젝트/빌드를 사용한다. 접속 승인은 빌드 버전, NGO 프로토콜과 프리팹 일치 여부를 확인한다. 임의로 같은 버전 번호를 붙인 서로 다른 커밋의 호환성까지 보장하는 것은 아니다.

## 열람과 배포

- Unity 메뉴: `CoOp → Version → Open changelog`.
- 저장소: `CHANGELOG.md`와 `Docs/Devlog`.
- 전달 커밋에는 `[v0.x.y]`를 넣는다. 검증된 전달 버전에는 같은 `v0.x.y` Git 태그를 붙인다. 태그를 다시 다른 커밋으로 옮기지 않는다.
- Steam 업데이트에는 같은 빌드 버전과 변경 내역을 사용한다. 아직 Steam AppID/배포 브랜치는 설정하지 않았다.
- 정식 출시가 승인되면 별도의 출시 작업에서 1.0.0 허용, 버전 검사, 출시 태그, Steam 배포를 함께 검토한다. 이 초기 스크립트의 0.x 제한을 조용히 제거하지 않는다.
