using UnityEditor;
using UnityEngine;
using ToonBossRush.Boss;
using ToonBossRush.Combat;
using ToonBossRush.Core;
using ToonBossRush.Player;

namespace ToonBossRush.Editor
{
    /// <summary>
    /// 게임 로직 설계 문서(게임로직_설계_게임플로우.md) 4장 "씬 세팅 방법"과
    /// 6장 "다음 액션 1번"(보스 1체라도 배치해서 흐름 확인)을 자동화하는 에디터 툴.
    ///
    /// 보스 아트(Meshy/VARCO 파이프라인)는 아직 후순위 보류 상태(캐릭터 컨셉아트 디렉션 문서 3장)라,
    /// StageBlockoutBuilder와 같은 원칙으로 프리미티브 플레이스홀더를 쓴다 —
    /// 보스 A(그림자 기사, 육중형) 컨셉의 실루엣/컬러만 캡슐로 대략 표현.
    ///
    /// 메뉴 한 번(Build Boss Battle Prototype)으로:
    ///   1) Player/Boss 레이어가 없으면 새로 만들고
    ///   2) SpawnPoint_Boss 위치에 보스 프로토타입(Health/HitboxController/BossStateMachine)을 배치하고
    ///   3) BossAttackPatternSO 에셋 2종(HeavySlam/WideSweep)이 없으면 생성하고
    ///   4) CameraTestSetup으로 스폰된 Player_CameraTest에 전투 컴포넌트(Health/PlayerCombat/공격 히트박스)를 추가하고
    ///   5) GameFlow 오브젝트를 만들어 GameFlowManager를 위 보스/플레이어로 배선한다.
    ///
    /// 실행 순서: StageBlockoutBuilder(Build Blockout) → CameraTestSetup(Spawn Player For Camera Test)
    ///           → 이 툴(Build Boss Battle Prototype) → Play 모드로 확인.
    /// (플레이어를 아직 안 스폰했다면 보스만 배치하고, 다음에 플레이어 스폰 후 이 메뉴를 다시 실행하면
    ///  나머지가 자동으로 이어서 배선됨 — 재실행해도 안전한 구조.)
    ///
    /// 메뉴: ToonBossRush > Boss > Build Boss Battle Prototype / Clear Boss Battle Prototype
    /// </summary>
    public static class BossPrototypeBuilder
    {
        private const string BossSpawnPointName = "SpawnPoint_Boss";
        private const string PlayerSpawnPointName = "SpawnPoint_Player";
        private const string TestPlayerName = "Player_CameraTest";
        private const string BossName = "Boss_Prototype";
        private const string GameFlowName = "GameFlow";
        private const string PatternFolder = "Assets/_Project/Data/BossPatterns";

        private const string PlayerLayerName = "Player";
        private const string BossLayerName = "Boss";

        [MenuItem("ToonBossRush/Boss/Build Boss Battle Prototype")]
        public static void BuildBossBattlePrototype()
        {
            GameObject bossSpawn = GameObject.Find(BossSpawnPointName);
            if (bossSpawn == null)
            {
                Debug.LogError($"[BossPrototypeBuilder] '{BossSpawnPointName}'을 찾을 수 없습니다 — 먼저 ToonBossRush > Stage > Build Blockout을 실행해주세요.");
                return;
            }

            int playerLayer = EnsureLayer(PlayerLayerName);
            int bossLayer = EnsureLayer(BossLayerName);
            if (playerLayer < 0 || bossLayer < 0)
            {
                Debug.LogError("[BossPrototypeBuilder] 레이어 생성에 실패해 중단합니다 — Edit > Project Settings > Tags and Layers에서 빈 User Layer 슬롯을 확보한 뒤 다시 시도해주세요.");
                return;
            }
            AssetDatabase.SaveAssets();

            ClearBossBattlePrototype();

            BossAttackPatternSO patternA = CreateOrLoadPattern(
                "BossA_HeavySlam", "육중한 내려찍기", telegraph: 1.0f, attack: 0.6f, recover: 0.8f, damage: 20f, minReuse: 3f, trigger: "AttackA");
            BossAttackPatternSO patternB = CreateOrLoadPattern(
                "BossA_WideSweep", "넓은 휩쓸기", telegraph: 0.9f, attack: 0.7f, recover: 0.9f, damage: 15f, minReuse: 3.5f, trigger: "AttackB");
            AssetDatabase.SaveAssets();

            BossStateMachine boss = CreateBoss(bossSpawn.transform, bossLayer, playerLayer, patternA, patternB);

            GameObject player = GameObject.Find(TestPlayerName);
            if (player == null)
            {
                Debug.LogWarning($"[BossPrototypeBuilder] '{TestPlayerName}'를 찾지 못해 보스만 배치했습니다 — ToonBossRush > Stage > Spawn Player For Camera Test 실행 후 이 메뉴를 다시 실행하면 GameFlow까지 자동 배선됩니다.");
                Selection.activeGameObject = boss.gameObject;
                return;
            }

            Health playerHealth = AddPlayerCombatRig(player, playerLayer, bossLayer);

            GameObject playerSpawn = GameObject.Find(PlayerSpawnPointName);
            if (playerSpawn == null)
            {
                Debug.LogWarning($"[BossPrototypeBuilder] '{PlayerSpawnPointName}'을 찾지 못해 GameFlowManager의 Player Spawn Point는 비워둡니다.");
            }

            WireGameFlow(playerHealth, player.GetComponent<PlayerController>(), playerSpawn != null ? playerSpawn.transform : null, boss);

            Selection.activeGameObject = boss.gameObject;
            Debug.Log("[BossPrototypeBuilder] 보스 전투 프로토타입 배치 완료 — Play 모드로 확인하세요 (좌클릭 약공격 / 우클릭 강공격, 보스 Idle→Telegraph→Attack→Recover 순환과 플레이어/보스 상호 피격을 확인).");
        }

        [MenuItem("ToonBossRush/Boss/Clear Boss Battle Prototype")]
        public static void ClearBossBattlePrototype()
        {
            GameObject boss = GameObject.Find(BossName);
            if (boss != null) Undo.DestroyObjectImmediate(boss);

            GameObject flow = GameObject.Find(GameFlowName);
            if (flow != null) Undo.DestroyObjectImmediate(flow);
        }

        private static BossStateMachine CreateBoss(Transform spawnPoint, int bossLayer, int playerLayer, BossAttackPatternSO patternA, BossAttackPatternSO patternB)
        {
            GameObject bossGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            bossGo.name = BossName;
            Undo.RegisterCreatedObjectUndo(bossGo, "Build Boss Battle Prototype");

            Vector3 scale = new Vector3(1.8f, 1.6f, 1.8f);
            bossGo.transform.localScale = scale;
            Vector3 standingPos = spawnPoint.position + Vector3.up * scale.y; // 캡슐 피벗이 중심이라 바닥에 서 있도록 절반 높이만큼 띄움
            bossGo.transform.SetPositionAndRotation(standingPos, spawnPoint.rotation);

            // 다크 퍼플 + 무광 스틸 그레이 — 보스A(그림자 기사) 팔레트(캐릭터 컨셉아트 디렉션 문서 3장)
            ApplyLitMaterial(bossGo, new Color(0.22f, 0.1f, 0.3f));

            Rigidbody rb = bossGo.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            SetLayerRecursively(bossGo, bossLayer);

            Health health = bossGo.AddComponent<Health>();
            SerializedObject healthSo = new SerializedObject(health);
            healthSo.FindProperty("maxHealth").floatValue = 250f;
            healthSo.FindProperty("staggerThreshold").floatValue = 60f;
            healthSo.ApplyModifiedProperties();

            bossGo.AddComponent<ToonHitFlash>(); // 캡슐 자체에 MeshRenderer가 있어 RequireComponent(Renderer) 충족됨

            HitboxController attackHitbox = CreateChildHitbox(
                bossGo.transform, "AttackHitbox",
                localPos: new Vector3(0f, 0f, 0.8f), boxSize: new Vector3(1.0f, 1.2f, 1.0f),
                targetLayers: 1 << playerLayer, damage: 15f);

            BossStateMachine bossSm = bossGo.AddComponent<BossStateMachine>();
            SerializedObject smSo = new SerializedObject(bossSm);
            SerializedProperty patternsProp = smSo.FindProperty("patterns");
            patternsProp.arraySize = 2;
            patternsProp.GetArrayElementAtIndex(0).objectReferenceValue = patternA;
            patternsProp.GetArrayElementAtIndex(1).objectReferenceValue = patternB;
            smSo.FindProperty("attackHitbox").objectReferenceValue = attackHitbox;
            smSo.ApplyModifiedProperties();

            return bossSm;
        }

        private static Health AddPlayerCombatRig(GameObject player, int playerLayer, int bossLayer)
        {
            SetLayerRecursively(player, playerLayer);

            Health health = player.GetComponent<Health>();
            if (health == null) health = player.AddComponent<Health>();

            PlayerCombat combat = player.GetComponent<PlayerCombat>();
            if (combat == null) combat = player.AddComponent<PlayerCombat>();

            // Player_CameraTest 루트는 VRM 리그의 루트라 Renderer가 보통 자식(메시) 쪽에 있어서
            // ToonHitFlash(RequireComponent(Renderer))는 여기서 자동으로 붙이지 않음 —
            // 원하면 실제 바디 메시 오브젝트에 직접 추가하고 Health 필드만 이 오브젝트로 연결할 것.

            HitboxController lightHitbox = GetOrCreateChildHitbox(
                player.transform, "LightHitbox",
                localPos: new Vector3(0f, 1f, 1.0f), boxSize: new Vector3(0.9f, 1.2f, 1.0f),
                targetLayers: 1 << bossLayer, damage: 8f);
            HitboxController heavyHitbox = GetOrCreateChildHitbox(
                player.transform, "HeavyHitbox",
                localPos: new Vector3(0f, 1f, 1.1f), boxSize: new Vector3(1.1f, 1.4f, 1.3f),
                targetLayers: 1 << bossLayer, damage: 22f);

            SerializedObject combatSo = new SerializedObject(combat);
            combatSo.FindProperty("lightHitbox").objectReferenceValue = lightHitbox;
            combatSo.FindProperty("heavyHitbox").objectReferenceValue = heavyHitbox;
            combatSo.ApplyModifiedProperties();

            return health;
        }

        private static void WireGameFlow(Health playerHealth, PlayerController playerController, Transform playerSpawn, BossStateMachine boss)
        {
            GameObject flowGo = new GameObject(GameFlowName);
            Undo.RegisterCreatedObjectUndo(flowGo, "Build Boss Battle Prototype");

            GameFlowManager flow = flowGo.AddComponent<GameFlowManager>();
            SerializedObject so = new SerializedObject(flow);
            so.FindProperty("playerHealth").objectReferenceValue = playerHealth;
            so.FindProperty("playerController").objectReferenceValue = playerController;
            so.FindProperty("playerSpawnPoint").objectReferenceValue = playerSpawn;

            SerializedProperty encountersProp = so.FindProperty("encounters");
            encountersProp.arraySize = 1;
            SerializedProperty element = encountersProp.GetArrayElementAtIndex(0);
            element.FindPropertyRelative("displayName").stringValue = "보스 A - 그림자 기사";
            element.FindPropertyRelative("boss").objectReferenceValue = boss;
            element.FindPropertyRelative("rootToActivate").objectReferenceValue = null;

            so.ApplyModifiedProperties();
        }

        private static HitboxController CreateChildHitbox(Transform parent, string name, Vector3 localPos, Vector3 boxSize, LayerMask targetLayers, float damage)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            Undo.RegisterCreatedObjectUndo(go, "Build Boss Battle Prototype");

            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = boxSize;

            HitboxController hitbox = go.AddComponent<HitboxController>();
            SerializedObject so = new SerializedObject(hitbox);
            so.FindProperty("targetLayers").intValue = targetLayers.value;
            so.FindProperty("damage").floatValue = damage;
            so.ApplyModifiedProperties();

            return hitbox;
        }

        private static HitboxController GetOrCreateChildHitbox(Transform parent, string name, Vector3 localPos, Vector3 boxSize, LayerMask targetLayers, float damage)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                HitboxController existingHitbox = existing.GetComponent<HitboxController>();
                if (existingHitbox != null) return existingHitbox;
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            return CreateChildHitbox(parent, name, localPos, boxSize, targetLayers, damage);
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private static BossAttackPatternSO CreateOrLoadPattern(string assetName, string patternName, float telegraph, float attack, float recover, float damage, float minReuse, string trigger)
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data"))
                AssetDatabase.CreateFolder("Assets/_Project", "Data");
            if (!AssetDatabase.IsValidFolder(PatternFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Data", "BossPatterns");

            string path = $"{PatternFolder}/{assetName}.asset";
            BossAttackPatternSO existing = AssetDatabase.LoadAssetAtPath<BossAttackPatternSO>(path);
            if (existing != null) return existing;

            BossAttackPatternSO pattern = ScriptableObject.CreateInstance<BossAttackPatternSO>();
            pattern.patternName = patternName;
            pattern.telegraphDuration = telegraph;
            pattern.attackDuration = attack;
            pattern.recoverDuration = recover;
            pattern.damage = damage;
            pattern.minReuseInterval = minReuse;
            pattern.animatorTrigger = trigger;
            pattern.designNote = "BossPrototypeBuilder가 자동 생성한 프로토타입용 패턴 — 실제 보스 애니메이션/밸런스 확정되면 수치 조정 필요.";

            AssetDatabase.CreateAsset(pattern, path);
            return pattern;
        }

        /// <summary>
        /// TagManager.asset에 레이어가 없으면 빈 User Layer 슬롯(8번부터)에 추가하고 인덱스를 반환.
        /// 이미 있으면 기존 인덱스를 그대로 반환 — 여러 번 실행해도 안전.
        /// </summary>
        private static int EnsureLayer(string layerName)
        {
            SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layersProp = tagManager.FindProperty("layers");

            for (int i = 0; i < layersProp.arraySize; i++)
            {
                if (layersProp.GetArrayElementAtIndex(i).stringValue == layerName) return i;
            }

            for (int i = 8; i < layersProp.arraySize; i++) // 0~7은 유니티 내장 레이어라 건드리지 않음
            {
                SerializedProperty slot = layersProp.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(slot.stringValue))
                {
                    slot.stringValue = layerName;
                    tagManager.ApplyModifiedProperties();
                    return i;
                }
            }

            return -1;
        }

        private static void ApplyLitMaterial(GameObject go, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return;

            Material material = new Material(shader) { color = color };
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
        }
    }
}
