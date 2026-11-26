using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SoulsHunter.CameraSystem;
using SoulsHunter.Combat;
using SoulsHunter.Player;

namespace SoulsHunter.EditorTools
{
    /// <summary>
    /// 메뉴 SoulsHunter > Build Test Arena: 현재 씬에 바닥, 플레이어(캡슐), 카메라, 락온 더미를 만든다.
    /// </summary>
    public static class TestArenaBuilder
    {
        [MenuItem("SoulsHunter/Build Test Arena")]
        static void Build()
        {
            if (GameObject.FindWithTag("Player"))
            {
                EditorUtility.DisplayDialog("SoulsHunter", "씬에 이미 Player 태그 오브젝트가 있어요.", "확인");
                return;
            }

            Undo.SetCurrentGroupName("Build Test Arena");
            int group = Undo.GetCurrentGroup();

            var groundMat = MakeMaterial(new Color(0.35f, 0.37f, 0.4f));
            var wallMat = MakeMaterial(new Color(0.55f, 0.5f, 0.45f));
            var playerMat = MakeMaterial(new Color(0.2f, 0.45f, 0.85f));
            var enemyMat = MakeMaterial(new Color(0.8f, 0.2f, 0.2f));

            // 환경
            var arena = Create("Arena");
            var ground = Primitive(PrimitiveType.Plane, "Ground", arena.transform, Vector3.zero, new Vector3(10f, 1f, 10f), groundMat);
            Primitive(PrimitiveType.Cube, "Wall", arena.transform, new Vector3(6f, 2f, 4f), new Vector3(1f, 4f, 8f), wallMat);
            Primitive(PrimitiveType.Cube, "Pillar A", arena.transform, new Vector3(-5f, 2.5f, 8f), new Vector3(1.5f, 5f, 1.5f), wallMat);
            Primitive(PrimitiveType.Cube, "Pillar B", arena.transform, new Vector3(-8f, 2.5f, -3f), new Vector3(1.5f, 5f, 1.5f), wallMat);
            var ramp = Primitive(PrimitiveType.Cube, "Ramp", arena.transform, new Vector3(0f, 0.6f, -8f), new Vector3(4f, 0.2f, 6f), wallMat);
            ramp.transform.rotation = Quaternion.Euler(-15f, 0f, 0f);
            _ = ground;

            // 플레이어
            var player = Create("Player");
            player.tag = "Player";
            var cc = player.AddComponent<CharacterController>();
            cc.center = new Vector3(0f, 1f, 0f);
            cc.height = 2f;
            cc.radius = 0.4f;
            Visual(PrimitiveType.Capsule, "Body", player.transform, new Vector3(0f, 1f, 0f), new Vector3(0.8f, 1f, 0.8f), playerMat);
            Visual(PrimitiveType.Cube, "Nose (정면 표시)", player.transform, new Vector3(0f, 1.5f, 0.4f), new Vector3(0.25f, 0.15f, 0.3f), enemyMat);
            var inputHandler = player.AddComponent<PlayerInputHandler>();
            player.AddComponent<PlayerStamina>();
            var locomotion = player.AddComponent<PlayerLocomotion>();

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
            cam.transform.SetPositionAndRotation(new Vector3(0f, 2.5f, -4.5f), Quaternion.Euler(12f, 0f, 0f));

            var marker = Visual(PrimitiveType.Sphere, "LockOnMarker", null, Vector3.zero, Vector3.one * 0.2f, MakeMaterial(Color.white));
            Undo.RegisterCreatedObjectUndo(marker, "marker");

            SetRef(tpc, "target", player.transform);
            SetRef(tpc, "input", inputHandler);
            SetRef(tpc, "lockMarker", marker.transform);
            SetRef(locomotion, "playerCamera", tpc);

            // 락온 더미 (작은 적 2 + 큰 몬스터 1)
            var enemies = Create("Dummies");
            Dummy(enemies.transform, "Dummy A", new Vector3(-3f, 0f, 7f), 1f, enemyMat);
            Dummy(enemies.transform, "Dummy B", new Vector3(3f, 0f, 9f), 1f, enemyMat);
            Dummy(enemies.transform, "Big Monster", new Vector3(0f, 0f, 16f), 2.5f, enemyMat);

            Selection.activeGameObject = player;
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(player.scene);
            Debug.Log("[SoulsHunter] 테스트 아레나 생성 완료. Play를 누르세요.");
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

        static void Dummy(Transform parent, string name, Vector3 pos, float size, Material mat)
        {
            var go = Primitive(PrimitiveType.Capsule, name, parent, pos + Vector3.up * size, Vector3.one * size, mat);
            var lockOn = go.AddComponent<LockOnTarget>();
            SetValue(lockOn, "offset", new Vector3(0f, size * 0.4f, 0f));
        }

        static Material MakeMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            return mat;
        }

        static void SetRef(Object owner, string field, Object value)
        {
            var so = new SerializedObject(owner);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetValue(Object owner, string field, Vector3 value)
        {
            var so = new SerializedObject(owner);
            so.FindProperty(field).vector3Value = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
