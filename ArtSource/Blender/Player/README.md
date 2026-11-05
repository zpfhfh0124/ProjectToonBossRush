# 플레이어 이동 애니메이션 원본

`PlayerLocomotion.blend` — 블렌더 3.6에서 만든 플레이어 걷기/달리기 원본 파일입니다.

- 모델: `Assets/_Project/Art/Character/VRM/ToonBossRush_Female.vrm` (VRM 애드온으로 임포트)
- 액션
  - `Walking` → `Assets/_Project/Art/Animations/Player/Walking.fbx` (30프레임, 1초 주기, 보폭 1.1m)
  - `Running` → `Assets/_Project/Art/Animations/Player/Running.fbx` (20프레임, 약 0.67초 주기, 보폭 2.67m)
- 모두 제자리 루프입니다(루트 이동 없음). 실제 이동은 `PlayerController`의 CharacterController가 처리합니다.

수정 후 다시 내보낼 때
1. 같은 이름의 FBX로 덮어씁니다.
2. Unity에서 `ToonBossRush > Player > Build Turn-Strafe States`, `Build Locomotion & Action States`를 다시 실행합니다.
3. 보폭이나 주기가 바뀌었으면 `PlayerController`의 `referenceWalkClipSpeed` / `referenceRunClipSpeed`를 (보폭 ÷ 주기)로 맞춥니다.

`ArtSource/`는 `Assets` 바깥이라 Unity가 임포트하지 않습니다. `*.blend`는 git LFS로 관리됩니다.
