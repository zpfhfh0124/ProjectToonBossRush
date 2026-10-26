using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ToonBossRush.Editor
{
    /// <summary>
    /// 2026-10-26: 기본 이동 = 달리기, Ctrl = 걷기로 바뀌면서 PlayerAnimator.controller에
    /// Running / Jump / Dodge / LightAttack0~2 / HeavyAttack 상태를 추가한다.
    /// 먼저 PlayerAnimatorTurnStrafeBuilder.Build()로 걷기 계열(Idle/Walking/Turn/Strafe)을
    /// 갱신한 뒤(ActionLocked 조건 포함), 그 위에 달리기/액션 상태를 얹는 구조.
    ///
    /// - PlayerController가 매 프레임 "ActionLocked"(회피/공격/공중)를 보내고, 이동 계열 AnyState
    ///   트랜지션은 ActionLocked가 false일 때만 동작 — 안 그러면 공격/회피/점프 모션이 시작되자마자
    ///   MoveSpeed 조건 때문에 Idle/Running으로 곧장 덮어써짐.
    /// - 공격 상태에는 "Attack" 태그를 붙임. PlayerCombat이 이 태그 상태가 끝나는 시점을
    ///   공격 종료로 판단함(Exit Time으로 Idle 복귀).
    /// - Running.fbx / Jump.fbx는 아직 없을 수 있음(Mixamo에서 받아 Art/Animations에 넣으면 됨).
    ///   없으면 Running은 Walking 클립으로 임시 대체, Jump 상태는 건너뜀.
    ///   새로 넣은 FBX는 이 스크립트가 Humanoid 리그/루프 설정까지 맞춰줌.
    /// 메뉴: ToonBossRush > Player > Build Locomotion & Action States
    /// (재실행해도 안전 — 기존 상태/트랜지션을 찾아 갱신만 함)
    /// </summary>
    public static class PlayerAnimatorLocomotionBuilder
    {
        private const string ControllerPath = "Assets/_Project/Animator/PlayerAnimator.controller";
        private const string AnimFolder = "Assets/_Project/Art/Animations/";

        private const string RunClipFile = "Running.fbx";
        private const string JumpClipFile = "Jump.fbx";
        private const string DodgeClipFile = "Dive Roll.fbx";
        private static readonly string[] LightAttackClipFiles =
        {
            "Sword And Shield Downward Slash.fbx",
            "Sword And Shield Cross Slash.fbx",
            "Sword And Shield Power Slash.fbx",
        };
        private const string HeavyAttackClipFile = "Sword And Shield Slash Combo.fbx";

        // Mixamo 공격 클립은 1.5~3.5초로 액션 게임 템포에 비해 느려서 배속 재생.
        // Play 테스트하면서 취향껏 조정.
        private const float LightAttackPlaybackSpeed = 1.5f;
        private const float HeavyAttackPlaybackSpeed = 1.3f;
        private const float AttackExitTime = 0.9f; // 정규화 시간 — 이 지점에서 Idle로 복귀 시작
        // Dive Roll 클립(약 3.9초)을 이 시간 안에 재생되도록 배속 — PlayerController.dodgeDuration과 비슷하게 맞출 것
        private const float DodgeAnimSeconds = 0.6f;

        private static readonly string[] ActionTriggers = { "Jump", "Dodge", "LightAttack", "HeavyAttack" };

        [MenuItem("ToonBossRush/Player/Build Locomotion & Action States")]
        public static void Build()
        {
            ConfigureImporterIfPresent(RunClipFile, loop: true);
            ConfigureImporterIfPresent(JumpClipFile, loop: false);

            // 걷기 계열 먼저 갱신(ActionLocked / RunThreshold 조건 반영)
            PlayerAnimatorTurnStrafeBuilder.Build();

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                Debug.LogError($"[PlayerAnimatorLocomotionBuilder] 컨트롤러를 찾을 수 없습니다: {ControllerPath}");
                return;
            }

            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            AnimatorState idleState = PlayerAnimatorTurnStrafeBuilder.FindState(sm, "Idle");
            AnimatorState walkingState = PlayerAnimatorTurnStrafeBuilder.FindState(sm, "Walking");
            if (idleState == null || walkingState == null)
            {
                Debug.LogError("[PlayerAnimatorLocomotionBuilder] Idle/Walking 상태를 찾을 수 없습니다 — PlayerAnimator.controller 구조를 확인해주세요.");
                return;
            }

            PlayerAnimatorTurnStrafeBuilder.AddParameterIfMissing(controller, "ActionLocked", AnimatorControllerParameterType.Bool);
            PlayerAnimatorTurnStrafeBuilder.AddParameterIfMissing(controller, "IsGrounded", AnimatorControllerParameterType.Bool);
            PlayerAnimatorTurnStrafeBuilder.AddParameterIfMissing(controller, "Jump", AnimatorControllerParameterType.Trigger);
            PlayerAnimatorTurnStrafeBuilder.AddParameterIfMissing(controller, "Dodge", AnimatorControllerParameterType.Trigger);
            PlayerAnimatorTurnStrafeBuilder.AddParameterIfMissing(controller, "LightAttack", AnimatorControllerParameterType.Trigger);
            PlayerAnimatorTurnStrafeBuilder.AddParameterIfMissing(controller, "HeavyAttack", AnimatorControllerParameterType.Trigger);
            PlayerAnimatorTurnStrafeBuilder.AddParameterIfMissing(controller, "ComboIndex", AnimatorControllerParameterType.Int);

            // ── Running ──
            AnimationClip runClip = LoadClip(RunClipFile, warnIfMissing: false);
            if (runClip == null)
            {
                Debug.LogWarning($"[PlayerAnimatorLocomotionBuilder] {RunClipFile}이 없어 Running 상태를 Walking 클립으로 임시 대체합니다. Mixamo에서 달리기 클립을 받아 넣은 뒤 이 메뉴를 다시 실행하세요.");
                runClip = walkingState.motion as AnimationClip;
            }
            AnimatorState running = PlayerAnimatorTurnStrafeBuilder.GetOrAddState(sm, "Running", runClip, new Vector3(600, 220, 0));
            PlayerAnimatorTurnStrafeBuilder.SetSpeedParameter(running, "AnimSpeedMultiplier");
            PlayerAnimatorTurnStrafeBuilder.AddAnyStateTransition(sm, running, PlayerAnimatorTurnStrafeBuilder.MoveEntryOffset,
                ("MoveSpeed", AnimatorConditionMode.Greater, PlayerAnimatorTurnStrafeBuilder.RunThreshold),
                ("ActionLocked", AnimatorConditionMode.IfNot, 0f));

            // ── Jump ── (착지하면 ActionLocked가 풀리면서 이동 계열 AnyState 트랜지션이 받아감)
            AnimationClip jumpClip = LoadClip(JumpClipFile, warnIfMissing: false);
            AnimatorState jump = null;
            if (jumpClip != null)
            {
                jump = PlayerAnimatorTurnStrafeBuilder.GetOrAddState(sm, "Jump", jumpClip, new Vector3(600, 300, 0));
                AddTriggerTransition(sm, jump, "Jump");
            }
            else
            {
                Debug.LogWarning($"[PlayerAnimatorLocomotionBuilder] {JumpClipFile}이 없어 Jump 상태는 건너뜁니다(점프 자체는 동작, 공중에서는 직전 모션 유지).");
            }

            // ── Dodge ──
            AnimatorState dodge = null;
            AnimationClip dodgeClip = LoadClip(DodgeClipFile, warnIfMissing: true);
            if (dodgeClip != null)
            {
                dodge = PlayerAnimatorTurnStrafeBuilder.GetOrAddState(sm, "Dodge", dodgeClip, new Vector3(600, 380, 0));
                dodge.speedParameterActive = false;
                dodge.speed = dodgeClip.length / DodgeAnimSeconds;
                AddTriggerTransition(sm, dodge, "Dodge");
                AddExitToIdle(dodge, idleState);
            }

            // ── 공격 ──
            for (int i = 0; i < LightAttackClipFiles.Length; i++)
            {
                AnimationClip clip = LoadClip(LightAttackClipFiles[i], warnIfMissing: true);
                if (clip == null) continue;
                AnimatorState attack = PlayerAnimatorTurnStrafeBuilder.GetOrAddState(sm, $"LightAttack{i}", clip, new Vector3(860, 220 + i * 80, 0));
                SetupAttackState(attack, LightAttackPlaybackSpeed, idleState);
                AddTriggerTransition(sm, attack, "LightAttack", ("ComboIndex", AnimatorConditionMode.Equals, i));
            }

            AnimationClip heavyClip = LoadClip(HeavyAttackClipFile, warnIfMissing: true);
            if (heavyClip != null)
            {
                AnimatorState heavy = PlayerAnimatorTurnStrafeBuilder.GetOrAddState(sm, "HeavyAttack", heavyClip, new Vector3(860, 460, 0));
                SetupAttackState(heavy, HeavyAttackPlaybackSpeed, idleState);
                AddTriggerTransition(sm, heavy, "HeavyAttack");
            }

            PrioritizeTriggerTransitions(sm);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("[PlayerAnimatorLocomotionBuilder] Running/Jump/Dodge/LightAttack0~2/HeavyAttack 상태 구성 완료. " +
                      "Play 모드에서 PlayerController의 runSpeed/referenceRunClipSpeed와 이 스크립트의 배속 상수를 취향껏 조정하세요.");
        }

        private static void SetupAttackState(AnimatorState state, float playbackSpeed, AnimatorState idleState)
        {
            state.tag = "Attack";
            state.speedParameterActive = false;
            state.speed = playbackSpeed;
            AddExitToIdle(state, idleState);
        }

        private static void AddExitToIdle(AnimatorState from, AnimatorState idleState)
        {
            foreach (AnimatorStateTransition existing in from.transitions.Where(t => t.destinationState == idleState).ToList())
            {
                from.RemoveTransition(existing);
            }

            AnimatorStateTransition transition = from.AddTransition(idleState);
            transition.hasExitTime = true;
            transition.exitTime = AttackExitTime;
            transition.duration = 0.15f;
        }

        /// <summary>트리거 기반 AnyState 트랜지션(공격/회피/점프). 조건이 바뀌어도 도중에 끊기지 않도록 interruption은 기본값(None).</summary>
        private static void AddTriggerTransition(AnimatorStateMachine sm, AnimatorState destination, string trigger, params (string param, AnimatorConditionMode mode, float threshold)[] extraConditions)
        {
            foreach (AnimatorStateTransition existing in sm.anyStateTransitions.Where(t => t.destinationState == destination).ToList())
            {
                sm.RemoveAnyStateTransition(existing);
            }

            AnimatorStateTransition transition = sm.AddAnyStateTransition(destination);
            transition.hasExitTime = false;
            transition.duration = 0.1f;
            transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            foreach ((string param, AnimatorConditionMode mode, float threshold) in extraConditions)
            {
                transition.AddCondition(mode, threshold, param);
            }
        }

        /// <summary>
        /// 같은 프레임에 트리거와 이동 조건이 동시에 참이면 AnyState 트랜지션 목록의 앞쪽이 이김 —
        /// 트리거 기반(공격/회피/점프) 트랜지션을 맨 앞으로 옮겨 항상 액션이 우선되게 함.
        /// </summary>
        private static void PrioritizeTriggerTransitions(AnimatorStateMachine sm)
        {
            AnimatorStateTransition[] all = sm.anyStateTransitions;
            sm.anyStateTransitions = all
                .OrderBy(t => t.conditions.Any(c => c.mode == AnimatorConditionMode.If && ActionTriggers.Contains(c.parameter)) ? 0 : 1)
                .ToArray();
        }

        /// <summary>
        /// Mixamo에서 새로 받은 FBX는 기본 Generic 리그로 임포트됨 — 기존 클립(Walking.fbx 등)과 같이
        /// Humanoid + 자체 아바타 생성, Y 높이 원본 유지로 맞춰줌. 이미 Humanoid면 건드리지 않음.
        /// </summary>
        private static void ConfigureImporterIfPresent(string fbxFileName, bool loop)
        {
            string path = AnimFolder + fbxFileName;
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) return;
            if (importer.animationType == ModelImporterAnimationType.Human) return;

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.SaveAndReimport();

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.loopTime = loop;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = false;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            Debug.Log($"[PlayerAnimatorLocomotionBuilder] {path} 임포트 설정을 Humanoid(loop={loop})로 변경했습니다.");
        }

        private static AnimationClip LoadClip(string fbxFileName, bool warnIfMissing)
        {
            string path = AnimFolder + fbxFileName;
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            AnimationClip clip = assets.OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null && warnIfMissing)
                Debug.LogWarning($"[PlayerAnimatorLocomotionBuilder] 클립을 찾을 수 없음: {path}");
            return clip;
        }
    }
}
