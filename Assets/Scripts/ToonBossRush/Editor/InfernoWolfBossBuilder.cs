using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using ToonBossRush.Boss;
using ToonBossRush.Combat;
using ToonBossRush.Core;

namespace ToonBossRush.Editor
{
    /// <summary>
    /// 캡슐 플레이스홀더(Boss_Prototype) 자리를 실제 보스 InfernoWolf로 교체하는 에디터 툴.
    ///
    /// 메뉴 한 번(Build InfernoWolf Boss)으로:
    ///   1) Boss_InfernoWolf 프리팹 생성/갱신 (Assets/_Project/Prefabs)
    ///      - 루트: Health / BossStateMachine / 피격용 CapsuleCollider / 키네마틱 Rigidbody (Boss 레이어)
    ///      - Model 자식: 보스 FBX + Animator(BossAnimator_InfernoWolf) + BossAnimationEventRelay
    ///      - AttackHitbox 자식: 플레이어 레이어만 때리는 HitboxController
    ///   2) 씬의 Boss_Prototype(캡슐)을 지우고 SpawnPoint_Boss 위치에 프리팹 인스턴스를 배치
    ///   3) 씬의 GameFlow → GameFlowManager.encounters[0]을 InfernoWolf로 교체
    ///
    /// 선행 조건: ToonBossRush > Boss > Build InfernoWolf Animator(컨트롤러/아바타),
    ///           Build Boss Battle Prototype(레이어/패턴 에셋/GameFlow/플레이어 전투 리그)를 한 번씩 실행해둔 상태.
    /// 콜라이더/히트박스 크기는 모델 렌더러 바운드에서 자동 계산 — 플레이 테스트 후 프리팹에서 직접 미세조정.
    /// (재실행해도 안전 — 프리팹은 덮어쓰고, 씬 인스턴스는 지우고 다시 배치)
    ///
    /// 메뉴: ToonBossRush > Boss > Build InfernoWolf Boss
    /// </summary>
    public static class InfernoWolfBossBuilder
    {
        private const string ModelPath = "Assets/_Project/Art/Character/Boss_InfernoWolf.fbx";
        private const string ControllerPath = "Assets/_Project/Animator/BossAnimator_InfernoWolf.controller";
        private const string PrefabFolder = "Assets/_Project/Prefabs";
        private const string PrefabPath = PrefabFolder + "/Boss_InfernoWolf.prefab";
        private const string PatternFolder = "Assets/_Project/Data/BossPatterns";
        private const string StageScenePath = "Assets/Scenes/01_Stage.unity";

        private const string BossName = "Boss_InfernoWolf";
        private const string PrototypeBossName = "Boss_Prototype";
        private const string BossSpawnPointName = "SpawnPoint_Boss";
        private const string GameFlowName = "GameFlow";
        private const string DisplayName = "불꽃늑대 (InfernoWolf)";

        // Meshy 원본 모델은 키 약 1.2m로 플레이어(VRoid, 약 1.6m)보다 작음 — 캡슐 프로토타입(약 3.2m)과 비슷한 위압감이 나도록 키를 맞춤
        private const float TargetHeight = 3.0f;

        private const string PlayerLayerName = "Player";
        private const string BossLayerName = "Boss";

        [MenuItem("ToonBossRush/Boss/Build InfernoWolf Boss")]
        public static void Build()
        {
            int playerLayer = LayerMask.NameToLayer(PlayerLayerName);
            int bossLayer = LayerMask.NameToLayer(BossLayerName);
            if (playerLayer < 0 || bossLayer < 0)
            {
                Debug.LogError("[InfernoWolfBossBuilder] Player/Boss 레이어가 없습니다 — 먼저 ToonBossRush > Boss > Build Boss Battle Prototype을 실행해주세요.");
                return;
            }

            GameObject bossSpawn = GameObject.Find(BossSpawnPointName);
            if (bossSpawn == null)
            {
                Debug.LogError($"[InfernoWolfBossBuilder] '{BossSpawnPointName}'을 찾을 수 없습니다 — 01_Stage 씬을 열고 실행해주세요.");
                return;
            }

            GameObject prefab = BuildPrefab(playerLayer, bossLayer);
            if (prefab == null) return;

            BossStateMachine boss = PlaceInScene(prefab, bossSpawn.transform);
            WireGameFlow(boss);

            EditorSceneManager.MarkSceneDirty(boss.gameObject.scene);
            Selection.activeGameObject = boss.gameObject;
            Debug.Log($"[InfernoWolfBossBuilder] {PrefabPath} 생성 + 씬 배치 + GameFlow 연결 완료 — 씬을 저장하고 Play 모드로 확인하세요.");
        }

        /// <summary>배치 모드 진입점: unity run ... -executeMethod ToonBossRush.Editor.InfernoWolfBossBuilder.BuildInStageScene</summary>
        public static void BuildInStageScene()
        {
            EditorSceneManager.OpenScene(StageScenePath, OpenSceneMode.Single);
            Build();
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }

        private static GameObject BuildPrefab(int playerLayer, int bossLayer)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            if (model == null || controller == null || avatar == null || !avatar.isHuman)
            {
                Debug.LogError("[InfernoWolfBossBuilder] 보스 모델/Humanoid 아바타/애니메이터 컨트롤러를 찾을 수 없습니다 — 먼저 ToonBossRush > Boss > Build InfernoWolf Animator를 실행해주세요.");
                return null;
            }

            BossAttackPatternSO heavySlam = AssetDatabase.LoadAssetAtPath<BossAttackPatternSO>($"{PatternFolder}/BossA_HeavySlam.asset");
            BossAttackPatternSO wideSweep = AssetDatabase.LoadAssetAtPath<BossAttackPatternSO>($"{PatternFolder}/BossA_WideSweep.asset");
            if (heavySlam == null || wideSweep == null)
            {
                Debug.LogError("[InfernoWolfBossBuilder] 보스 패턴 에셋(BossA_HeavySlam/BossA_WideSweep)이 없습니다 — 먼저 Build Boss Battle Prototype을 실행해주세요.");
                return null;
            }

            GameObject root = new GameObject(BossName);
            try
            {
                GameObject modelGo = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(modelGo, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                modelGo.name = "Model";
                modelGo.transform.SetParent(root.transform, false);

                Animator animator = modelGo.GetComponent<Animator>() ?? modelGo.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.avatar = avatar;
                animator.applyRootMotion = false; // 루트 이동은 클립에 베이크(BossAnimatorBuilder) — 위치는 코드가 관리
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // 화면 밖에서도 애니메이션 이벤트(히트박스)가 빠지지 않게
                modelGo.AddComponent<BossAnimationEventRelay>();

                // 피격 플래시 — ToonLit 계열(_HitFlashAmount) 머티리얼일 때만 실제로 보임. 다른 셰이더면 무시됨.
                foreach (Renderer r in modelGo.GetComponentsInChildren<Renderer>())
                {
                    r.gameObject.AddComponent<ToonHitFlash>();
                }

                Bounds modelBounds = CalculateLocalBounds(root);
                modelGo.transform.localScale = Vector3.one * (TargetHeight / modelBounds.size.y);
                Bounds bounds = CalculateLocalBounds(root);

                // 피격 판정용 몸통 콜라이더 — 플레이어 HitboxController가 OnTriggerEnter로 감지
                CapsuleCollider body = root.AddComponent<CapsuleCollider>();
                body.direction = bounds.size.z > bounds.size.y ? 2 : 1; // 네발 자세면 Z축 캡슐
                body.center = bounds.center;
                body.height = Mathf.Max(bounds.size.y, bounds.size.z);
                body.radius = Mathf.Min(bounds.size.x, body.direction == 2 ? bounds.size.y : bounds.size.z) * 0.5f;

                Rigidbody rb = root.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;

                Health health = root.AddComponent<Health>();
                SerializedObject healthSo = new SerializedObject(health);
                healthSo.FindProperty("maxHealth").floatValue = 250f;
                healthSo.FindProperty("staggerThreshold").floatValue = 60f;
                healthSo.ApplyModifiedProperties();

                // 공격 히트박스 — 몸 앞쪽. 점프 공격은 루트 이동이 포즈에 베이크돼 착지 지점이 피벗보다 앞이라 앞으로 길게 잡음.
                float reach = Mathf.Max(1.5f, bounds.size.z);
                HitboxController attackHitbox = CreateHitbox(
                    root.transform, "AttackHitbox",
                    localPos: new Vector3(bounds.center.x, bounds.center.y, bounds.max.z + reach * 0.4f),
                    boxSize: new Vector3(bounds.size.x * 1.4f, bounds.size.y, reach),
                    targetLayers: 1 << playerLayer, damage: 15f);

                BossStateMachine bossSm = root.AddComponent<BossStateMachine>();
                SerializedObject smSo = new SerializedObject(bossSm);
                SerializedProperty patternsProp = smSo.FindProperty("patterns");
                patternsProp.arraySize = 2;
                patternsProp.GetArrayElementAtIndex(0).objectReferenceValue = heavySlam;
                patternsProp.GetArrayElementAtIndex(1).objectReferenceValue = wideSweep;
                smSo.FindProperty("animator").objectReferenceValue = animator;
                smSo.FindProperty("attackHitbox").objectReferenceValue = attackHitbox;
                smSo.ApplyModifiedProperties();

                SetLayerRecursively(root, bossLayer);

                if (!AssetDatabase.IsValidFolder(PrefabFolder))
                    AssetDatabase.CreateFolder("Assets/_Project", "Prefabs");

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static BossStateMachine PlaceInScene(GameObject prefab, Transform spawnPoint)
        {
            foreach (string name in new[] { PrototypeBossName, BossName })
            {
                GameObject existing = GameObject.Find(name);
                if (existing != null) Undo.DestroyObjectImmediate(existing);
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, "Build InfernoWolf Boss");
            instance.transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
            return instance.GetComponent<BossStateMachine>();
        }

        private static void WireGameFlow(BossStateMachine boss)
        {
            GameObject flowGo = GameObject.Find(GameFlowName);
            GameFlowManager flow = flowGo != null ? flowGo.GetComponent<GameFlowManager>() : null;
            if (flow == null)
            {
                Debug.LogWarning($"[InfernoWolfBossBuilder] '{GameFlowName}'(GameFlowManager)를 찾지 못해 보스만 배치했습니다 — Build Boss Battle Prototype으로 GameFlow를 만든 뒤 이 메뉴를 다시 실행하세요.");
                return;
            }

            SerializedObject so = new SerializedObject(flow);
            SerializedProperty encountersProp = so.FindProperty("encounters");
            if (encountersProp.arraySize == 0) encountersProp.arraySize = 1;
            SerializedProperty element = encountersProp.GetArrayElementAtIndex(0);
            element.FindPropertyRelative("displayName").stringValue = DisplayName;
            element.FindPropertyRelative("boss").objectReferenceValue = boss;
            element.FindPropertyRelative("rootToActivate").objectReferenceValue = null;
            so.ApplyModifiedProperties();
        }

        /// <summary>루트 기준 로컬 좌표의 렌더러 바운드(바인드 포즈) — 콜라이더/히트박스 크기 계산용.</summary>
        private static Bounds CalculateLocalBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(Vector3.up, new Vector3(1.5f, 2f, 2.5f));

            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);

            // root는 원점/무회전/단위 스케일로 생성되므로 월드 바운드 = 로컬 바운드
            return bounds;
        }

        private static HitboxController CreateHitbox(Transform parent, string name, Vector3 localPos, Vector3 boxSize, LayerMask targetLayers, float damage)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = boxSize;

            HitboxController hitbox = go.AddComponent<HitboxController>();
            SerializedObject so = new SerializedObject(hitbox);
            so.FindProperty("targetLayers").intValue = targetLayers.value;
            so.FindProperty("damage").floatValue = damage;
            so.ApplyModifiedProperties();

            return hitbox;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
    }
}
