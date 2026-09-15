using UnityEditor;
using UnityEngine;

namespace ToonBossRush.Editor
{
    /// <summary>
    /// 맵 컨셉아트 디렉션 문서(5-3, 6장) 기준 아레나 블록아웃을 자동 생성하는 에디터 툴.
    /// 개방형 사각 아레나(20~24m) + 레일링 + 네 모서리 조명 기둥 + 가장자리 상자 +
    /// 마법진 플레이스홀더 + 플레이어/보스 스폰 포인트를 현재 씬에 프리미티브로 배치한다.
    /// 실제 에셋팩(Cyberpunk Neon City) 임포트 전, 스케일과 레이아웃을 먼저 검증하기 위한 용도 —
    /// 오브젝트 이름을 그대로 유지해두면 나중에 에셋팩 프리팹으로 교체할 때 대응이 쉬움.
    /// 메뉴: ToonBossRush > Stage > Build Blockout / Clear Blockout
    /// </summary>
    public static class StageBlockoutBuilder
    {
        private const string RootName = "StageBlockout";
        private const float ArenaSize = 22f; // 20~24m 범위 중간값(문서 6장 기준) — 필요하면 이 값만 바꿔서 재생성
        private const float FloorThickness = 0.4f;
        private const float RailingHeight = 1f;
        private const float RailingThickness = 0.2f;
        private const float PillarHeight = 3f;
        private const float PillarRadius = 0.3f;
        private const float MagicCircleDiameter = 10f;
        private const float MagicCircleYOffset = 0.02f; // Z-fighting 방지용 오프셋(5-2 참고)

        [MenuItem("ToonBossRush/Stage/Build Blockout")]
        public static void BuildBlockout()
        {
            ClearBlockout();

            GameObject root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Stage Blockout");

            float half = ArenaSize * 0.5f;

            CreateFloor(root.transform, half);
            CreateRailings(root.transform, half);
            CreateCornerPillars(root.transform, half);
            CreateEdgeCrates(root.transform, half);
            CreateMagicCirclePlaceholder(root.transform);
            CreateSpawnPoints(root.transform, half);

            Selection.activeGameObject = root;
            Debug.Log($"[StageBlockoutBuilder] 블록아웃 생성 완료 — 아레나 {ArenaSize}m x {ArenaSize}m (맵 컨셉아트 디렉션 문서 5-3/6장 기준)");
        }

        [MenuItem("ToonBossRush/Stage/Clear Blockout")]
        public static void ClearBlockout()
        {
            GameObject existing = GameObject.Find(RootName);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }
        }

        private static void CreateFloor(Transform parent, float half)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(parent);
            floor.transform.localPosition = new Vector3(0f, -FloorThickness * 0.5f, 0f);
            floor.transform.localScale = new Vector3(half * 2f, FloorThickness, half * 2f);
            ApplyLitMaterial(floor, new Color(0.25f, 0.25f, 0.28f));
            Undo.RegisterCreatedObjectUndo(floor, "Build Stage Blockout");
        }

        private static void CreateRailings(Transform parent, float half)
        {
            GameObject group = new GameObject("Railings");
            group.transform.SetParent(parent);

            CreateRailingSegment(group.transform, new Vector3(0f, RailingHeight * 0.5f, half), new Vector3(half * 2f, RailingHeight, RailingThickness));
            CreateRailingSegment(group.transform, new Vector3(0f, RailingHeight * 0.5f, -half), new Vector3(half * 2f, RailingHeight, RailingThickness));
            CreateRailingSegment(group.transform, new Vector3(half, RailingHeight * 0.5f, 0f), new Vector3(RailingThickness, RailingHeight, half * 2f));
            CreateRailingSegment(group.transform, new Vector3(-half, RailingHeight * 0.5f, 0f), new Vector3(RailingThickness, RailingHeight, half * 2f));
        }

        private static void CreateRailingSegment(Transform parent, Vector3 localPos, Vector3 localScale)
        {
            GameObject rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rail.name = "Railing_Segment";
            rail.transform.SetParent(parent);
            rail.transform.localPosition = localPos;
            rail.transform.localScale = localScale;
            ApplyLitMaterial(rail, new Color(0.55f, 0.55f, 0.6f));
            Undo.RegisterCreatedObjectUndo(rail, "Build Stage Blockout");
        }

        private static void CreateCornerPillars(Transform parent, float half)
        {
            GameObject group = new GameObject("CornerLightPillars");
            group.transform.SetParent(parent);

            float inset = half - 1f; // 모서리에서 살짝 안쪽으로
            Vector3[] corners =
            {
                new Vector3(inset, 0f, inset),
                new Vector3(-inset, 0f, inset),
                new Vector3(inset, 0f, -inset),
                new Vector3(-inset, 0f, -inset),
            };

            foreach (Vector3 corner in corners)
            {
                GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pillar.name = "CornerLight_Pillar";
                pillar.transform.SetParent(group.transform);
                pillar.transform.localPosition = corner + new Vector3(0f, PillarHeight * 0.5f, 0f);
                pillar.transform.localScale = new Vector3(PillarRadius * 2f, PillarHeight * 0.5f, PillarRadius * 2f);
                ApplyLitMaterial(pillar, new Color(0.2f, 0.9f, 0.95f)); // 청록/시안 — 4장 컬러 팔레트 체크리스트 참고
                // 6장: 순수 배경 오브젝트는 콜라이더 생략(회피 구르기 동선 방해 방지)
                Object.DestroyImmediate(pillar.GetComponent<Collider>());
                Undo.RegisterCreatedObjectUndo(pillar, "Build Stage Blockout");
            }
        }

        private static void CreateEdgeCrates(Transform parent, float half)
        {
            GameObject group = new GameObject("EdgeCrates");
            group.transform.SetParent(parent);

            float cornerInset = half - 1.5f;
            float edgeInset = half - 1f;
            Vector3[] positions =
            {
                new Vector3(cornerInset, 0f, edgeInset),
                new Vector3(-cornerInset, 0f, edgeInset),
                new Vector3(cornerInset, 0f, -edgeInset),
                new Vector3(-cornerInset, 0f, -edgeInset),
                new Vector3(edgeInset, 0f, 0f),
                new Vector3(-edgeInset, 0f, 0f),
            };

            foreach (Vector3 pos in positions)
            {
                GameObject crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crate.name = "Edge_Crate";
                crate.transform.SetParent(group.transform);
                crate.transform.localPosition = pos + new Vector3(0f, 0.5f, 0f);
                crate.transform.localScale = Vector3.one;
                ApplyLitMaterial(crate, new Color(0.4f, 0.35f, 0.3f));
                // 6장: 순수 배경 오브젝트는 콜라이더 생략(회피 구르기 동선 방해 방지)
                Object.DestroyImmediate(crate.GetComponent<Collider>());
                Undo.RegisterCreatedObjectUndo(crate, "Build Stage Blockout");
            }
        }

        private static void CreateMagicCirclePlaceholder(Transform parent)
        {
            GameObject circle = GameObject.CreatePrimitive(PrimitiveType.Plane);
            circle.name = "MagicCircle_Placeholder";
            circle.transform.SetParent(parent);
            circle.transform.localPosition = new Vector3(0f, MagicCircleYOffset, 0f);
            circle.transform.localScale = new Vector3(MagicCircleDiameter / 10f, 1f, MagicCircleDiameter / 10f); // Plane 기본 크기 10x10
            Object.DestroyImmediate(circle.GetComponent<Collider>());
            ApplyUnlitMaterial(circle, new Color(0.1f, 0.8f, 0.85f));
            Undo.RegisterCreatedObjectUndo(circle, "Build Stage Blockout");
            // TODO: 5-2에서 확정한 탑다운 마법진 텍스처로 교체 → URP Unlit, Surface Type Transparent, Blend Mode Additive
        }

        private static void CreateSpawnPoints(Transform parent, float half)
        {
            GameObject playerSpawn = new GameObject("SpawnPoint_Player");
            playerSpawn.transform.SetParent(parent);
            playerSpawn.transform.localPosition = new Vector3(0f, 0.1f, -(half - 3f));
            playerSpawn.transform.localRotation = Quaternion.identity;
            Undo.RegisterCreatedObjectUndo(playerSpawn, "Build Stage Blockout");

            GameObject bossSpawn = new GameObject("SpawnPoint_Boss");
            bossSpawn.transform.SetParent(parent);
            bossSpawn.transform.localPosition = new Vector3(0f, 0.1f, half - 3f);
            bossSpawn.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Undo.RegisterCreatedObjectUndo(bossSpawn, "Build Stage Blockout");
        }

        private static void ApplyLitMaterial(GameObject go, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return;

            Material material = new Material(shader) { color = color };
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static void ApplyUnlitMaterial(GameObject go, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader == null) return;

            Material material = new Material(shader) { color = color };
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }
    }
}
