# 카툰 액션 보스러시 — 스타터 킷

일본 게임업계 이직 포트폴리오용 Unity 프로젝트의 초기 스캐폴드입니다.
자세한 기획 배경은 `docs/기획서.md` 참고.

## 이 킷에 포함된 것

```
Assets/
  Scripts/
    Player/
      PlayerController.cs   - 카메라 상대 8방향 이동 + 회피(무적프레임)
      PlayerCombat.cs        - 약공격 3단 콤보 + 강공격
    Combat/
      Health.cs              - 데미지/스태거/사망 이벤트
      HitboxController.cs    - 애니메이션 이벤트로 켜고 끄는 히트박스
      HitStop.cs              - 타격 시 짧은 타임스케일 정지
      ToonHitFlash.cs         - 피격 시 셰이더 컬러 플래시 연동
    Boss/
      BossAttackPatternSO.cs - 보스 패턴을 데이터(ScriptableObject)로 정의
      BossStateMachine.cs     - Idle→Telegraph→Attack→Recover→Stagger 상태머신
    Camera/
      CameraShake.cs          - 타격 시 카메라 흔들림
  Shaders/
    ToonLit.shader            - 커스텀 카툰 셰이더 (램프 셰이딩 + 림 라이트 + 아웃라인, HLSL 직접 작성)
docs/
  기획서.md                   - 전체 게임 기획 문서
```

## 이 프로젝트에 새로 만들어야 하는 Unity 폴더에 복사하는 방법

1. Unity Hub에서 **Unity 2022 LTS 이상 + Universal RP 템플릿**으로 새 프로젝트 생성
2. 이 킷의 `Assets/` 폴더 내용을 새 프로젝트의 `Assets/` 안에 그대로 복사
3. Window > Package Manager에서 Universal RP가 설치되어 있는지 확인 (템플릿으로 만들었다면 이미 포함)

## 씬 세팅 체크리스트 (에디터에서 직접 해야 하는 작업)

이 킷은 순수 C#/셰이더 코드만 포함하고 있어서, 실제로 플레이하려면 에디터에서 아래 배선 작업이 필요합니다.

1. **플레이어 오브젝트**
   - `CharacterController` + `PlayerController` + `PlayerCombat` + `Health` 컴포넌트 부착
   - `PlayerController`의 Camera Transform에 메인 카메라 연결 (비워두면 `Camera.main` 자동 사용)
   - 자식 오브젝트로 약공격/강공격용 히트박스(Collider + `HitboxController`) 2개 배치, `PlayerCombat`에 연결
2. **애니메이터**
   - Animator Controller에 파라미터 추가: `MoveSpeed`(float), `Dodge`(trigger), `LightAttack`(trigger), `HeavyAttack`(trigger), `ComboIndex`(int)
   - 각 공격 클립에 애니메이션 이벤트 2개 추가: 히트박스 켜는 프레임에 `HitboxController.Activate`, 끝나는 프레임에 `Deactivate` + `PlayerCombat.EndAttack`
3. **보스 오브젝트**
   - `Health` + `BossStateMachine` 부착, `BossAttackPatternSO` 에셋을 1~3개 만들어 리스트에 연결 (우클릭 > Create > ToonBossRush > Boss Attack Pattern)
   - Animator에 각 패턴의 `animatorTrigger` 이름과 `Idle`/`Telegraph`/`Recover`/`Stagger`/`Dead` 트리거 추가
4. **씬 오브젝트**
   - 빈 오브젝트에 `HitStop` 컴포넌트 부착 (싱글턴, 씬에 1개만)
   - 메인 카메라의 부모(리그) 오브젝트에 `CameraShake` 부착하고, `Health.OnDamaged`에서 `Shake()` 호출하도록 연결 (간단한 브릿지 스크립트를 추가하거나 UnityEvent로 노출해도 됨)
5. **머티리얼**
   - `ToonLit` 셰이더로 머티리얼 생성 후 캐릭터/보스 모델에 적용
   - `_RampThreshold`, `_ShadowColor`, `_RimColor` 등을 조정하며 원하는 카툰 톤 찾기
   - 각 렌더러에 `ToonHitFlash` 컴포넌트를 부착하면 피격 시 자동으로 플래시

## 왜 이렇게 설계했는가 (포트폴리오 설명용 메모)

- **ScriptableObject 기반 보스 패턴**: 보스마다 코드를 새로 안 짜고 데이터 에셋만 추가해서 확장 가능하도록 설계. 실제 서비스 개발에서 콘텐츠 확장 비용을 줄이는 흔한 패턴이며, UE5 포트폴리오의 Ai.Mi 리캐스트 스테이트머신과 같은 문제(적 행동 패턴을 데이터로 관리)를 Unity/C#으로 접근한 것.
- **HLSL 커스텀 라이팅**: `ToonLit.shader`는 Shader Graph 대신 URP의 `Lighting.hlsl`을 직접 include해서 `GetMainLight()`로 받은 라이트 방향/그림자 감쇠를 손으로 계산. 램프 셰이딩(N·L 스텝 함수), 프레넬 림 라이트, Inverted Hull 아웃라인까지 전부 코드로 구현되어 있어 셰이더 코드 리뷰용으로 그대로 제출 가능.
- **히트스탑/카메라셰이크는 unscaledDeltaTime 기반**: `Time.timeScale`을 0에 가깝게 내려도 흔들림 연출이 끊기지 않도록 실시간 델타를 사용 — 타격감 튜닝에서 자주 나오는 실수 포인트를 미리 처리.

## 다음 작업 (기획서 마일스톤과 연결)

- 1주차: 위 체크리스트 배선 + 임시 캐릭터로 이동/카메라 확인
- 2주차: 콤보/회피/히트박스 튜닝 + ToonLit 셰이더 값 다듬기
- 3주차: 보스 패턴 2~3개 제작 + 아웃라인/VFX
- 4주차: UI, 폴리싱, itch.io 빌드 업로드
