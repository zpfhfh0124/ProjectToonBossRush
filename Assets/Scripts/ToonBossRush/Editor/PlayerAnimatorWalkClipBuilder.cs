using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ToonBossRush.Editor
{
    /// <summary>
    /// 2026-09-20: 기존 Walk.fbx(원본 Take가 391프레임 — "직진 걷기 → 턴 → 반대 방향으로 복귀"가
    /// 통째로 들어있었음)를 Walking 상태의 루프 클립으로 그대로 썼더니, 루프가 돌아 그 턴 구간을
    /// 지날 때마다 캐릭터 몸(Hips)이 TurnAngle/Animator 상태 전이와 무관하게 클립 자체에 저장된
    /// 회전을 따라가며 순간적으로 튀어 보이는 버그가 있었음(Apply Root Motion은 꺼져 있어 Transform
    /// 이동/방향은 안 바뀌지만, 스켈레톤 포즈는 클립 그대로 재생되기 때문).
    /// Mixamo에서 순수 제자리 반복 "Walking"(Feminine Walk Forward, In Place, Without Skin) 클립을
    /// 새로 받아 Walking.fbx로 추가 — 이 스크립트로 Walking 상태의 모션만 그 클립으로 갈아끼움.
    /// 기존 Walk.fbx/Walk.fbx.meta는 더 이상 참조되지 않으니 프로젝트에서 삭제해도 됨.
    /// 메뉴: ToonBossRush > Player > Set Walking Clip
    /// (재실행해도 안전 — Walking 상태를 찾아 모션/스피드 파라미터만 갱신)
    /// </summary>
    public static class PlayerAnimatorWalkClipBuilder
    {
        private const string ControllerPath = "Assets/_Project/Animator/PlayerAnimator.controller";
        private const string NewWalkClipPath = "Assets/_Project/Art/Animations/Walking.fbx";

        [MenuItem("ToonBossRush/Player/Set Walking Clip")]
        public static void SetWalkingClip()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                Debug.LogError($"[PlayerAnimatorWalkClipBuilder] 컨트롤러를 찾을 수 없습니다: {ControllerPath}");
                return;
            }

            AnimationClip newWalkClip = LoadClip(NewWalkClipPath);
            if (newWalkClip == null)
            {
                Debug.LogError($"[PlayerAnimatorWalkClipBuilder] 새 Walking 클립을 찾을 수 없습니다: {NewWalkClipPath} — 파일이 폴더에 있는지, import가 끝났는지 확인해주세요.");
                return;
            }

            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            AnimatorState walkingState = FindState(sm, "Walking");
            if (walkingState == null)
            {
                Debug.LogError("[PlayerAnimatorWalkClipBuilder] Walking 상태를 찾을 수 없습니다 — PlayerAnimator.controller 구조를 확인해주세요.");
                return;
            }

            walkingState.motion = newWalkClip;
            walkingState.speed = 1f;
            walkingState.speedParameterActive = true;
            walkingState.speedParameter = "AnimSpeedMultiplier";
            walkingState.writeDefaultValues = true;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("[PlayerAnimatorWalkClipBuilder] Walking 상태의 모션을 Walking.fbx(순수 반복 걷기, In Place)로 교체 완료. " +
                      "기존 Walk.fbx/Walk.fbx.meta는 더 이상 쓰이지 않으니 삭제해도 됩니다. Play 모드에서 확인해보세요.");
        }

        private static AnimationClip LoadClip(string path)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            AnimationClip clip = assets.OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null)
                Debug.LogWarning($"[PlayerAnimatorWalkClipBuilder] 클립을 찾을 수 없음: {path}");
            return clip;
        }

        private static AnimatorState FindState(AnimatorStateMachine sm, string name)
        {
            return sm.states.FirstOrDefault(s => s.state.name == name).state;
        }
    }
}
