using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SoulsHunter.CameraSystem;
using SoulsHunter.Combat;
using SoulsHunter.Player;
using SoulsHunter.UI;

namespace SoulsHunter.EditorTools
{
    /// <summary>
    /// 메뉴 SoulsHunter > Build Test Arena:
    /// 현재 씬에 바닥, 플레이어(캡슐), 카메라, HUD, 허수아비(부위 판정 포함)를 만든다.
    /// 이미 만든 적이 있으면 지우고 다시 만든다.
    /// </summary>
    public static class TestArenaBuilder
    {
        static readonly string[] GeneratedRoots = { "Arena", "Player", "Dummies", "LockOnMarker", "HUD" };

        [MenuItem("SoulsHunter/Build Test Arena")]
        static void Build()
        {
            Undo.SetCurrentGroupName("Build Test Arena");
            int group = Undo.GetCurrentGroup();

            if (!RemoveOld()) return;

            var groundMat = MakeMaterial(new Color(0.35f, 0.37f, 0.4f));
            var wallMat = MakeMaterial(new Color(0.55f, 0.5f, 0.45f));
            var playerMat = MakeMaterial(new Color(0.2f, 0.45f, 0.85f));
            var enemyMat = MakeMaterial(new Color(0.8f, 0.2f, 0.2f));
            var weakMat = MakeMaterial(new Color(0.95f, 0.45f, 0.2f));

            // 환경
            var arena = Create("Arena");
            Primitive(PrimitiveType.Plane, "Ground", arena.transform, Vector3.zero, new Vector3(10f, 1f, 10f), groundMat);
            Primitive(PrimitiveType.Cube, "Wall", arena.transform, new Vector3(6f, 2f, 4f), new Vector3(1f, 4f, 8f), wallMat);
            Primitive(PrimitiveType.Cube, "Pillar A", arena.transform, new Vector3(-5f, 2.5f, 8f), new Vector3(1.5f, 5f, 1.5f), wallMat);
            Primitive(PrimitiveType.Cube, "Pillar B", arena.transform, new Vector3(-8f, 2.5f, -3f), new Vector3(1.5f, 5f, 1.5f), wallMat);
            var ramp = Primitive(PrimitiveType.Cube, "Ramp", arena.transform, new Vector3(0f, 0.6f, -8f), new Vector3(4f, 0.2f, 6f), wallMat);
            ramp.transform.rotation = Quaternion.Euler(-15f, 0f, 0f);

            // 플레이어
            var player = Create("Player");
            player.tag = "Player";
            var cc = player.AddComponent<CharacterController>();
            cc.center = new Vector3(0f, 1f, 0f);
            cc.height = 2f;
            cc.radius = 0.4f;
            Visual(PrimitiveType.Capsule, "Body", player.transform, new Vector3(0f, 1f, 0f), new Vector3(0.8f, 1f, 0.8f), playerMat);
            Visual(PrimitiveType.Cube, "Nose (정면 표시)", player.transform, new Vector3(0f, 1.5f, 0.4f), new Vector3(0.25f, 0.15f, 0.3f), enemyMat);
            Visual(PrimitiveType.Cube, "Sword", player.transform, new Vector3(0.45f, 1.1f, 0.6f), new Vector3(0.08f, 0.08f, 1.1f), MakeMaterial(new Color(0.85f, 0.85f, 0.9f)));
            var inputHandler = player.AddComponent<PlayerInputHandler>();
            var stamina = player.AddComponent<PlayerStamina>();
            var playerHealth = player.AddComponent<Health>();
            SetString(playerHealth, "displayName", "Hunter");
            player.AddComponent<Hurtbox>();
            var locomotion = player.AddComponent<PlayerLocomotion>();
            player.AddComponent<PlayerCombat>();

            // 카메라
            var cam = Camera.main;
            if (!cam)
            {
                var camGo = Create("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            var tpc = cam.GetComponent<ThirdPersonCamera>();
            if (!tpc) tpc = Undo.AddComponent<ThirdPersonCamera>(cam.gameObject);
            Undo.RecordObject(cam.transform, "camera");
            cam.transform.SetPositionAndRotation(new Vector3(0f, 2.5f, -4.5f), Quaternion.Euler(12f, 0f, 0f));

            var marker = Visual(PrimitiveType.Sphere, "LockOnMarker", null, Vector3.zero, Vector3.one * 0.2f, MakeMaterial(Color.white));
            Undo.RegisterCreatedObjectUndo(marker, "marker");

            SetRef(tpc, "target", player.transform);
            SetRef(tpc, "input", inputHandler);
            SetRef(tpc, "lockMarker", marker.transform);
            SetRef(locomotion, "playerCamera", tpc);

            // HUD
            var hud = Create("HUD").AddComponent<PlayerHUD>();
            SetRef(hud, "playerHealth", playerHealth);
            SetRef(hud, "playerStamina", stamina);
            SetRef(hud, "playerCamera", tpc);

            // 허수아비
            var dummies = Create("Dummies");
            Dummy(dummies.transform, "Dummy A", new Vector3(-3f, 0f, 7f), 1f, 150f, enemyMat);
            Dummy(dummies.transform, "Dummy B", new Vector3(3f, 0f, 9f), 1f, 150f, enemyMat);
            var big = Dummy(dummies.transform, "Big Monster", new Vector3(0f, 0f, 16f), 2.5f, 1200f, enemyMat);
            SetFloat(big.GetComponent<Health>(), "maxPoise", 120f);

            // 큰 몬스터: 머리(약점, 부위 파괴 가능) — 몬헌식 부위 판정 예시
            var head = Primitive(PrimitiveType.Sphere, "Head (약점)", big.transform, new Vector3(0f, 0.9f, 0.35f), Vector3.one * 0.55f, weakMat);
            var headBox = head.AddComponent<Hurtbox>();
            SetString(headBox, "partName", "Head");
            SetFloat(headBox, "damageMultiplier", 1.5f);
            SetFloat(headBox, "breakThreshold", 200f);

            Selection.activeGameObject = player;
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(player.scene);
            Debug.Log("[SoulsHunter] 테스트 아레나 생성 완료. Play를 누르세요.");
        }

        static bool RemoveOld()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var old = new System.Collections.Generic.List<GameObject>();
            foreach (var go in scene.GetRootGameObjects())
                if (System.Array.IndexOf(GeneratedRoots, go.name) >= 0) old.Add(go);
            if (old.Count == 0) return true;

            if (!EditorUtility.DisplayDialog("SoulsHunter",
                    "이전에 만든 테스트 아레나(Arena, Player, Dummies, HUD, LockOnMarker)를 지우고 다시 만들까요?\n(Ctrl+Z로 되돌릴 수 있어요)",
                    "다시 만들기", "취소"))
                return false;

            foreach (var go in old) Undo.DestroyObjectImmediate(go);
            return true;
        }

        static GameObject Create(string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            return go;
        }

        static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>콜라이더 없는 보기용 프리미티브</summary>
        static GameObject Visual(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = Primitive(type, name, parent, pos, scale, mat);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static GameObject Dummy(Transform parent, string name, Vector3 pos, float size, float hp, Material mat)
        {
            var go = Primitive(PrimitiveType.Capsule, name, parent, pos + Vector3.up * size, Vector3.one * size, mat);
            var health = go.AddComponent<Health>();
            SetFloat(health, "maxHealth", hp);
            go.AddComponent<Hurtbox>();
            var lockOn = go.AddComponent<LockOnTarget>();
            SetVector(lockOn, "offset", new Vector3(0f, size * 0.4f, 0f));
            go.AddComponent<TrainingDummy>();
            return go;
        }

        static Material MakeMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");
            var mat = new Material(shader) { color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            return mat;
        }

        static SerializedProperty Prop(Object owner, string field, out SerializedObject so)
        {
            so = new SerializedObject(owner);
            var p = so.FindProperty(field);
            if (p == null) Debug.LogError($"[SoulsHunter] {owner.GetType().Name}.{field} 필드를 찾지 못했어요.");
            return p;
        }

        static void SetRef(Object owner, string field, Object value)
        {
            var p = Prop(owner, field, out var so);
            if (p == null) return;
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetVector(Object owner, string field, Vector3 value)
        {
            var p = Prop(owner, field, out var so);
            if (p == null) return;
            p.vector3Value = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetFloat(Object owner, string field, float value)
        {
            var p = Prop(owner, field, out var so);
            if (p == null) return;
            p.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetString(Object owner, string field, string value)
        {
            var p = Prop(owner, field, out var so);
            if (p == null) return;
            p.stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
