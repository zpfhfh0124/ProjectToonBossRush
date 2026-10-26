using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ToonBossRush.Editor
{
    /// <summary>
    /// 보스 InfernoWolf 모델/애니메이션 임포트 설정 + BossAnimator 컨트롤러 자동 구성.
    ///
    /// 1) Boss_InfernoWolf.fbx(Meshy 자동 리그 — Hips/Spine/LeftArm... 사람형 본 구조)와
    ///    Mixamo 애니메이션 FBX를 전부 Humanoid로 설정해 리타게팅이 되게 함.
    ///    보스는 지금 제자리 공격만 하므로(BossStateMachine에 이동 로직 없음) 애니메이션의
    ///    루트 이동/회전은 전부 포즈에 베이크 — Jump Attack처럼 앞으로 뛰는 모션도 오브젝트 위치는 고정.
    /// 2) BossStateMachine이 호출하는 트리거(Idle/Telegraph/AttackA/AttackB/Stagger/Dead)마다
    ///    상태를 만들고 AnyState 트랜지션으로 연결. AttackA/AttackB는 BossAttackPatternSO.animatorTrigger 값.
    /// 3) 공격 클립의 실제 타격 구간에 AttackHitboxOn/AttackHitboxOff 애니메이션 이벤트를 심음
    ///    (보스 모델의 Animator 오브젝트에 BossAnimationEventRelay가 있어야 수신됨).
    ///
    /// AnyState 트랜지션의 canTransitionToSelf를 true로 두는 이유: 이미 Idle인 상태에서
    /// BeginEncounter()가 Idle 트리거를 세팅하면, 자기 자신 전환이 막혀 있을 경우 트리거가 소비되지 않고
    /// 남아 있다가 Telegraph로 넘어가는 순간 곧바로 Idle로 되돌아가 버림 — 자기 전환을 허용해 항상 소비되게 함.
    ///
    /// 메뉴: ToonBossRush > Boss > Build InfernoWolf Animator
    /// (재실행해도 안전 — 임포트 설정은 덮어쓰고, 컨트롤러는 상태/트랜지션을 찾아 갱신만 함)
    /// </summary>
    public static class BossAnimatorBuilder
    {
        private const string ModelPath = "Assets/_Project/Art/Character/Boss_InfernoWolf.fbx";
        private const string AnimFolder = "Assets/_Project/Art/Animations/Boss/";
        private const string ControllerPath = "Assets/_Project/Animator/BossAnimator_InfernoWolf.controller";

        private const float CrossFadeDuration = 0.15f;

        // (상태 이름 = 트리거 이름, 클립 파일명, 루프 여부, 히트박스 on/off 시각(초, 없으면 음수), 그래프 위치)
        // Recover 상태는 두지 않음 — 공격 클립 후반부에 회복 동작이 포함돼 있어 BossStateMachine이 Recover 트리거를 보내지 않음.
        // 히트 구간(2026-09-25 측정): 보스 모델에 클립을 샘플링해 손/골반 궤적으로 찾은 값.
        //   JumpAttack: 0.55s 도약 → 1.6s 착지(골반 높이 최저) — 내려찍는 1.55~1.80s
        //   Swipe: 왼손 속도 피크(최대 10.7m/s) 1.15~1.45s
        private static readonly (string state, string clipFile, bool loop, float hitOn, float hitOff, Vector3 pos)[] StateTable =
        {
            ("Idle",      "InfernoWolf_Idle",       true,  -1f,   -1f,   new Vector3(300,   0, 0)),
            ("Telegraph", "InfernoWolf_Roar",       false, -1f,   -1f,   new Vector3(550,  80, 0)),
            ("AttackA",   "InfernoWolf_JumpAttack", false, 1.55f, 1.80f, new Vector3(550, 160, 0)),
            ("AttackB",   "InfernoWolf_Swipe",      false, 1.15f, 1.45f, new Vector3(550, 240, 0)),
            ("Stagger",   "InfernoWolf_HitReact",   false, -1f,   -1f,   new Vector3(550, 320, 0)),
            ("Dead",      "InfernoWolf_Death",      false, -1f,   -1f,   new Vector3(550, 400, 0)),
        };

        // 이전 버전 빌더가 만들었던, 지금은 쓰지 않는 상태/파라미터 — 재실행 시 정리
        private static readonly string[] ObsoleteStates = { "Recover" };

        [MenuItem("ToonBossRush/Boss/Build InfernoWolf Animator")]
        public static void Build()
        {
            if (!ConfigureModelImporter())
                return;

            foreach ((_, string clipFile, bool loop, float hitOn, float hitOff, _) in StateTable)
            {
                if (!ConfigureAnimationImporter(AnimFolder + clipFile + ".fbx", loop, hitOn, hitOff))
                    return;
            }

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                                            ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            foreach (string obsolete in ObsoleteStates)
            {
                AnimatorState state = FindState(sm, obsolete);
                if (state != null) sm.RemoveState(state); // 연결된 트랜지션도 함께 제거됨
                int paramIndex = System.Array.FindIndex(controller.parameters, p => p.name == obsolete);
                if (paramIndex >= 0) controller.RemoveParameter(paramIndex);
            }

            foreach ((string stateName, string clipFile, _, _, _, Vector3 pos) in StateTable)
            {
                AnimationClip clip = LoadClip(AnimFolder + clipFile + ".fbx");
                if (clip == null)
                {
                    Debug.LogError($"[BossAnimatorBuilder] 클립을 찾을 수 없습니다: {AnimFolder}{clipFile}.fbx");
                    return;
                }

                AddParameterIfMissing(controller, stateName, AnimatorControllerParameterType.Trigger);
                AnimatorState state = GetOrAddState(sm, stateName, clip, pos);
                SetAnyStateTransition(sm, state, stateName);
            }

            sm.defaultState = FindState(sm, "Idle");

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BossAnimatorBuilder] InfernoWolf Humanoid 임포트 설정 + {ControllerPath} 구성 완료.");
        }

        private static bool ConfigureModelImporter()
        {
            ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[BossAnimatorBuilder] 보스 모델을 찾을 수 없습니다: {ModelPath}");
                return false;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false; // 모델 FBX엔 애니메이션이 없음
            importer.SaveAndReimport();

            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError("[BossAnimatorBuilder] Boss_InfernoWolf Humanoid 아바타 생성 실패 — 모델 임포트 설정의 Rig > Configure에서 본 매핑을 확인해주세요.");
                return false;
            }
            return true;
        }

        private static bool ConfigureAnimationImporter(string path, bool loop, float hitOn, float hitOff)
        {
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[BossAnimatorBuilder] 애니메이션 FBX를 찾을 수 없습니다: {path}");
                return false;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.loopTime = loop;
                clip.loopPose = loop;
                // 루트 이동/회전을 전부 포즈에 베이크 (원본 기준 유지) — 보스 오브젝트 위치/방향은 코드가 관리
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = true;
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalPositionXZ = true;

                // 임포터 이벤트 time은 0~1 정규화 값이라 클립 길이(초)로 나눠서 넣음
                float length = (clip.lastFrame - clip.firstFrame) / LoadClip(path).frameRate;
                clip.events = hitOn < 0f
                    ? new AnimationEvent[0]
                    : new[]
                    {
                        new AnimationEvent { functionName = "AttackHitboxOn", time = hitOn / length },
                        new AnimationEvent { functionName = "AttackHitboxOff", time = hitOff / length },
                    };
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            return true;
        }

        private static AnimationClip LoadClip(string path)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        private static void AddParameterIfMissing(AnimatorController controller, string name, AnimatorControllerParameterType type)
        {
            if (controller.parameters.Any(p => p.name == name)) return;
            controller.AddParameter(name, type);
        }

        private static AnimatorState FindState(AnimatorStateMachine sm, string name)
        {
            return sm.states.FirstOrDefault(s => s.state.name == name).state;
        }

        private static AnimatorState GetOrAddState(AnimatorStateMachine sm, string name, Motion motion, Vector3 position)
        {
            AnimatorState state = FindState(sm, name) ?? sm.AddState(name, position);
            state.motion = motion;
            state.writeDefaultValues = true;
            return state;
        }

        private static void SetAnyStateTransition(AnimatorStateMachine sm, AnimatorState destination, string trigger)
        {
            // 재실행 시 중복 생성 방지 — 같은 목적지로 가는 기존 AnyState 트랜지션을 지우고 재생성
            foreach (AnimatorStateTransition existing in sm.anyStateTransitions.Where(t => t.destinationState == destination).ToList())
            {
                sm.RemoveAnyStateTransition(existing);
            }

            AnimatorStateTransition transition = sm.AddAnyStateTransition(destination);
            transition.hasExitTime = false;
            transition.duration = CrossFadeDuration;
            transition.canTransitionToSelf = true;
            transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        }
    }
}
