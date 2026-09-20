using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ToonBossRush.Editor
{
    /// <summary>
    /// 1주차 미해결 항목 마무리: PlayerAnimator.controller에 Turn/Strafe 상태 추가.
    /// PlayerController.cs가 매 프레임 보내는 "TurnAngle"(몸이 향한 방향 대비 실제
    /// 이동 방향의 부호 있는 오차각)을 기준으로 Walking/TurnLeft/TurnRight/StrafeLeft/StrafeRight를
    /// AnyState 트랜지션으로 실시간 전환한다. rotationSpeed가 매 프레임 이 오차각을 0으로
    /// 좁혀가므로, 방향을 급히 꺾을수록 Strafe → Turn → Walking 순으로 자연스럽게 전환됨.
    /// 추가로 각 이동 상태의 Speed Parameter를 "AnimSpeedMultiplier"로 연결해,
    /// 클립 자체 재생 속도가 아니라 실제 moveSpeed에 맞춰 발 애니메이션이 재생되도록 보정함
    /// (안 그러면 CharacterController 이동 속도와 클립 보폭 속도가 달라 발이 미끄러지는 것처럼 보임).
    /// 메뉴: ToonBossRush > Player > Build Turn-Strafe States
    /// (재실행해도 안전 — 기존 상태/트랜지션을 찾아 갱신만 함)
    /// </summary>
    public static class PlayerAnimatorTurnStrafeBuilder
    {
        private const string ControllerPath = "Assets/_Project/Animator/PlayerAnimator.controller";
        private const string AnimFolder = "Assets/_Project/Art/Animations/";

        // 오차각 임계값 (도) — Play 테스트하면서 취향껏 조정 권장
        private const float TurnThreshold = 35f;   // 이 각도 미만이면 Walking 유지
        private const float StrafeThreshold = 100f; // 이 각도 이상이면 Strafe로 전환

        // Idle에서 이동 상태로 들어갈 때 클립을 정규화 시간 0(기본 자세)이 아니라
        // 이 지점부터 재생 시작 — 이동을 시작하는 순간 이미 보폭 중간(발이 나가 있는 자세)부터
        // 보이게 해서 "움직이자마자 바로 발을 내딛는" 것처럼 느껴지게 함.
        // Play 테스트하면서 실제 클립에 맞게 0~1 사이로 취향껏 조정.
        private const float MoveEntryOffset = 0.25f;

        [MenuItem("ToonBossRush/Player/Build Turn-Strafe States")]
        public static void Build()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                Debug.LogError($"[PlayerAnimatorTurnStrafeBuilder] 컨트롤러를 찾을 수 없습니다: {ControllerPath}");
                return;
            }

            AnimationClip turnLeftClip = LoadClip("Walking Left Turn.fbx");
            AnimationClip turnRightClip = LoadClip("Walking Right Turn.fbx");
            AnimationClip strafeLeftClip = LoadClip("Left Strafe Walking.fbx");
            AnimationClip strafeRightClip = LoadClip("Right Strafe Walking.fbx");

            if (turnLeftClip == null || turnRightClip == null || strafeLeftClip == null || strafeRightClip == null)
            {
                Debug.LogError("[PlayerAnimatorTurnStrafeBuilder] 일부 애니메이션 클립을 찾지 못했습니다 — 위 경고 로그를 확인하고 Art/Animations 폴더 배치를 다시 확인해주세요.");
                return;
            }

            AddParameterIfMissing(controller, "TurnAngle", AnimatorControllerParameterType.Float);
            AddParameterIfMissing(controller, "AnimSpeedMultiplier", AnimatorControllerParameterType.Float);

            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            AnimatorState idleState = FindState(sm, "Idle");
            AnimatorState walkingState = FindState(sm, "Walking");
            if (idleState == null || walkingState == null)
            {
                Debug.LogError("[PlayerAnimatorTurnStrafeBuilder] 기존 Idle/Walking 상태를 찾을 수 없습니다 — PlayerAnimator.controller 구조를 확인해주세요.");
                return;
            }

            AnimatorState turnLeft = GetOrAddState(sm, "TurnLeft", turnLeftClip, new Vector3(360, 300, 0));
            AnimatorState turnRight = GetOrAddState(sm, "TurnRight", turnRightClip, new Vector3(360, 380, 0));
            AnimatorState strafeLeft = GetOrAddState(sm, "StrafeLeft", strafeLeftClip, new Vector3(360, 460, 0));
            AnimatorState strafeRight = GetOrAddState(sm, "StrafeRight", strafeRightClip, new Vector3(360, 540, 0));

            // 실제 이동 속도에 맞춰 발 애니메이션 재생 속도를 보정(발 미끄러짐 방지).
            // Idle은 제자리 동작이라 제외.
            SetSpeedParameter(walkingState, "AnimSpeedMultiplier");
            SetSpeedParameter(turnLeft, "AnimSpeedMultiplier");
            SetSpeedParameter(turnRight, "AnimSpeedMultiplier");
            SetSpeedParameter(strafeLeft, "AnimSpeedMultiplier");
            SetSpeedParameter(strafeRight, "AnimSpeedMultiplier");

            // AnyState → 각 이동 상태 (실시간 각도 변화에 반응, 짧은 크로스페이드)
            // MoveEntryOffset: 이동 시작 즉시 보폭 중간부터 보이도록 진입 지점을 당겨둠.
            AddAnyStateTransition(sm, walkingState, MoveEntryOffset,
                ("MoveSpeed", AnimatorConditionMode.Greater, 0.1f),
                ("TurnAngle", AnimatorConditionMode.Greater, -TurnThreshold),
                ("TurnAngle", AnimatorConditionMode.Less, TurnThreshold));

            AddAnyStateTransition(sm, turnRight, MoveEntryOffset,
                ("MoveSpeed", AnimatorConditionMode.Greater, 0.1f),
                ("TurnAngle", AnimatorConditionMode.Greater, TurnThreshold),
                ("TurnAngle", AnimatorConditionMode.Less, StrafeThreshold));

            AddAnyStateTransition(sm, turnLeft, MoveEntryOffset,
                ("MoveSpeed", AnimatorConditionMode.Greater, 0.1f),
                ("TurnAngle", AnimatorConditionMode.Less, -TurnThreshold),
                ("TurnAngle", AnimatorConditionMode.Greater, -StrafeThreshold));

            AddAnyStateTransition(sm, strafeRight, MoveEntryOffset,
                ("MoveSpeed", AnimatorConditionMode.Greater, 0.1f),
                ("TurnAngle", AnimatorConditionMode.Greater, StrafeThreshold));

            AddAnyStateTransition(sm, strafeLeft, MoveEntryOffset,
                ("MoveSpeed", AnimatorConditionMode.Greater, 0.1f),
                ("TurnAngle", AnimatorConditionMode.Less, -StrafeThreshold));

            // AnyState → Idle (모든 이동 상태 공통 복귀 경로) — Idle은 제자리 자세라 오프셋 불필요.
            AddAnyStateTransition(sm, idleState, 0f,
                ("MoveSpeed", AnimatorConditionMode.Less, 0.1f));

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("[PlayerAnimatorTurnStrafeBuilder] TurnLeft/TurnRight/StrafeLeft/StrafeRight 상태 + AnyState 트랜지션 + AnimSpeedMultiplier 연동 구성 완료. Play 모드에서 방향 전환/발 미끄러짐을 확인하며 임계값(TurnThreshold/StrafeThreshold)과 PlayerController의 referenceWalkClipSpeed를 취향껏 조정하세요.");
        }

        private static AnimationClip LoadClip(string fbxFileName)
        {
            string path = AnimFolder + fbxFileName;
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            AnimationClip clip = assets.OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null)
                Debug.LogWarning($"[PlayerAnimatorTurnStrafeBuilder] 클립을 찾을 수 없음: {path}");
            return clip;
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
            AnimatorState state = FindState(sm, name);
            if (state == null)
            {
                state = sm.AddState(name, position);
            }
            state.motion = motion;
            state.writeDefaultValues = true;
            return state;
        }

        private static void SetSpeedParameter(AnimatorState state, string parameterName)
        {
            state.speed = 1f;
            state.speedParameterActive = true;
            state.speedParameter = parameterName;
            Debug.Log($"현재 애니메이션 : {state} - {parameterName}");
        }

        private static void AddAnyStateTransition(AnimatorStateMachine sm, AnimatorState destination, float offset, params (string param, AnimatorConditionMode mode, float threshold)[] conditions)
        {
            // 재실행 시 중복 생성 방지 — 같은 목적지로 가는 기존 AnyState 트랜지션을 지우고 재생성
            foreach (AnimatorStateTransition existing in sm.anyStateTransitions.Where(t => t.destinationState == destination).ToList())
            {
                sm.RemoveAnyStateTransition(existing);
            }

            AnimatorStateTransition transition = sm.AddAnyStateTransition(destination);
            transition.hasExitTime = false;
            transition.duration = 0.1f;
            transition.offset = offset;
            transition.canTransitionToSelf = false;

            // 2026-09-20: 기본값 None이면, 이미 시작된 AnyState 전환(예: Walking→TurnRight)이
            // 도중에 조건이 반대로 바뀌어도(예: 회전이 빨라 TurnAngle이 곧장 임계값 밑으로 복귀)
            // 절대 중간에 취소되지 않고 무조건 끝까지 재생된 뒤에야 복귀 전환이 시작됨 —
            // 순간적인 각도 스파이크 한 프레임이 최소 duration*2(약 0.2초)짜리 Turn 포즈
            // "왕복"으로 증폭되어, 실제로는 방향 전환이 없었는데도 몸이 잠깐 좌우로
            // 튀는 것처럼 보이는 원인이었음. Source(전환 시작 시점의 원래 상태 기준)로
            // 바꾸면 진행 중인 전환도 다른 AnyState 조건이 참이 되는 즉시 인터럽트될 수
            // 있어 이 "강제 완주"가 사라짐.
            transition.interruptionSource = TransitionInterruptionSource.Source;

            foreach ((string param, AnimatorConditionMode mode, float threshold) in conditions)
            {
                transition.AddCondition(mode, threshold, param);
            }
        }
    }
}
