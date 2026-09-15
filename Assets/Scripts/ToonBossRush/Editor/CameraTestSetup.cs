using UnityEditor;
using UnityEngine;
using ToonBossRush.Camera;
using ToonBossRush.Player;

namespace ToonBossRush.Editor
{
    /// <summary>
    /// 블록아웃(StageBlockoutBuilder) 이후, 5-3 6단계(카메라 확인)를 위해
    /// SpawnPoint_Player 위치에 테스트용 캐릭터를 스폰하고 Main Camera에
    /// ThirdPersonCameraRig를 붙여 시야각/프레이밍을 바로 확인할 수 있게 하는 툴.
    /// 메뉴: ToonBossRush > Stage > Spawn Player For Camera Test
    /// </summary>
    public static class CameraTestSetup
    {
        private const string PlayerPrefabPath = "Assets/_Project/Art/Character/VRM/ToonBossRush_Female.prefab";
        private const string AnimatorControllerPath = "Assets/_Project/Animator/PlayerAnimator.controller";
        private const string SpawnPointName = "SpawnPoint_Player";
        private const string SpawnedPlayerName = "Player_CameraTest";

        [MenuItem("ToonBossRush/Stage/Spawn Player For Camera Test")]
        public static void SpawnPlayerForCameraTest()
        {
            GameObject spawnPoint = GameObject.Find(SpawnPointName);
            if (spawnPoint == null)
            {
                Debug.LogError($"[CameraTestSetup] '{SpawnPointName}'을 찾을 수 없습니다 — 먼저 ToonBossRush > Stage > Build Blockout을 실행해주세요.");
                return;
            }

            // 재실행 시 이전 테스트 캐릭터 정리
            GameObject existing = GameObject.Find(SpawnedPlayerName);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[CameraTestSetup] 플레이어 프리팹을 찾을 수 없습니다: {PlayerPrefabPath}");
                return;
            }

            GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            player.name = SpawnedPlayerName;
            player.transform.SetPositionAndRotation(spawnPoint.transform.position, spawnPoint.transform.rotation);
            player.tag = "Player";
            Undo.RegisterCreatedObjectUndo(player, "Spawn Player For Camera Test");

            SetupCharacterController(player);
            SetupPlayerController(player);
            SetupAnimatorController(player);
            SetupCameraRig();

            Selection.activeGameObject = player;
            Debug.Log("[CameraTestSetup] 카메라 확인용 플레이어 스폰 완료 — Play 모드로 ThirdPersonCameraRig 시야각을 확인하세요.");
        }

        private static void SetupCharacterController(GameObject player)
        {
            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller == null)
            {
                controller = player.AddComponent<CharacterController>();
                controller.height = 1.7f;
                controller.radius = 0.3f;
                controller.center = new Vector3(0f, 0.85f, 0f);
            }
        }

        private static void SetupPlayerController(GameObject player)
        {
            if (player.GetComponent<PlayerController>() == null)
            {
                player.AddComponent<PlayerController>();
            }
        }

        private static void SetupAnimatorController(GameObject player)
        {
            Animator animator = player.GetComponent<Animator>();
            if (animator == null)
            {
                animator = player.AddComponent<Animator>();
            }

            if (animator.runtimeAnimatorController == null)
            {
                RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AnimatorControllerPath);
                if (controller != null)
                {
                    animator.runtimeAnimatorController = controller;
                }
                else
                {
                    Debug.LogWarning($"[CameraTestSetup] 애니메이터 컨트롤러를 찾지 못했습니다: {AnimatorControllerPath} — 애니메이션 없이 이동만 테스트됩니다.");
                }
            }
        }

        private static void SetupCameraRig()
        {
            UnityEngine.Camera mainCamera = UnityEngine.Camera.main;
            if (mainCamera == null)
            {
                Debug.LogError("[CameraTestSetup] 씬에서 Main Camera를 찾을 수 없습니다.");
                return;
            }

            if (mainCamera.GetComponent<ThirdPersonCameraRig>() == null)
            {
                Undo.AddComponent<ThirdPersonCameraRig>(mainCamera.gameObject);
            }
        }
    }
}
