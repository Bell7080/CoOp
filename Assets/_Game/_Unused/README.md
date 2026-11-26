# _Unused

게임 코드·씬·에디터 셋업 스크립트 어디에서도 참조하지 않는 에셋을 지우지 않고 모아 둔 곳입니다.
원래 경로를 그대로 따라 하위 폴더를 만들었고, `.meta`를 함께 옮겨 GUID가 유지됩니다.
다시 쓰려면 원래 경로로 되돌리고 코드에서 참조하면 됩니다.

- `Art/`, `Materials/`, `ThirdParty/`: 더 이상 쓰지 않는 모델·텍스처·머티리얼 (코드선 플러그, 멀티탭, 공용 발전기, 정산 리프트, 우주선 콘솔/코어 엔진 등)
- `FormerResources/Audio/ElevenLabs/`: 플러그 꽂기/뽑기, 발전기 크랭크 효과음 (`ProtoSound`에서 제거, Resources 밖이라 빌드에 포함되지 않음)
- `RemovedScripts~/`: 앞선 작업에서 지운 스크립트(CordPlug, Generator, PowerGrid, PlayerInventory) 원본. 폴더 이름이 `~`로 끝나 Unity가 가져오거나 컴파일하지 않습니다.
- `KrRiceCooker_beforeHoleFill.fbx~`: 옆·뒷면이 뚫려 있던 밥솥 원본 모델. `tools/blender/fill_prop_holes.py`로 구멍을 메운 버전이 원래 자리에 있습니다. 이름이 `~`로 끝나 Unity가 가져오지 않습니다.
