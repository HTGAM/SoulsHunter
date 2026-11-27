using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.AI.Navigation;
using SoulsHunter.CameraSystem;
using SoulsHunter.Combat;
using SoulsHunter.Core;
using SoulsHunter.Enemy;
using SoulsHunter.Items;
using SoulsHunter.World;
using System.Collections.Generic;
using SoulsHunter.Player;
using SoulsHunter.Save;
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
        static readonly string[] GeneratedRoots = { "Arena", "Player", "Dummies", "LockOnMarker", "HUD", "Bonfire", "Forge", "Armor Bench", "Quest Board" };

        [MenuItem("SoulsHunter/Delete Save Data")]
        static void DeleteSaveData()
        {
            if (!EditorUtility.DisplayDialog("SoulsHunter", $"저장 파일을 지울까요?\n{SaveSystem.SavePath}", "지우기", "취소")) return;
            SaveSystem.DeleteSave();
            Debug.Log("[SoulsHunter] 저장 파일을 지웠어요.");
        }

        [MenuItem("SoulsHunter/Open Save Folder")]
        static void OpenSaveFolder() => EditorUtility.RevealInFinder(SaveSystem.SavePath);

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
            var surface = arena.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            arena.AddComponent<BakeNavMeshOnAwake>();

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
            SetFloat(playerHealth, "maxPoise", 20f);
            player.AddComponent<Hurtbox>();
            var locomotion = player.AddComponent<PlayerLocomotion>();
            player.AddComponent<PlayerCombat>();
            player.AddComponent<PlayerDamageHandler>();
            player.AddComponent<PlayerFlask>();
            player.AddComponent<PlayerInteractor>();
            player.AddComponent<PlayerInventory>();
            var shield = Visual(PrimitiveType.Cube, "Shield", player.transform, new Vector3(-0.5f, 1.0f, 0.35f), new Vector3(0.08f, 0.7f, 0.55f), MakeMaterial(new Color(0.45f, 0.32f, 0.2f)));
            var guard = player.AddComponent<PlayerGuard>();
            SetRef(guard, "shield", shield.transform);

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
            if (!cam.GetComponent<CameraFeedback>()) Undo.AddComponent<CameraFeedback>(cam.gameObject);
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
            hud.gameObject.AddComponent<GameplayHUD>();
            hud.gameObject.AddComponent<PauseMenu>();
            hud.gameObject.AddComponent<DamageNumbers>();

            // 화톳불 (시작 지점 옆)
            BuildBonfire(new Vector3(-3f, 0f, -3f));

            // 허수아비
            var dummies = Create("Dummies");
            Dummy(dummies.transform, "Dummy A", new Vector3(-3f, 0f, 7f), 1f, 150f, enemyMat);
            Dummy(dummies.transform, "Dummy B", new Vector3(3f, 0f, 9f), 1f, 150f, enemyMat);
            var boss = BuildBoss(dummies.transform, new Vector3(0f, 0f, 20f), enemyMat, weakMat);

            // 잡몹: 잿빛 들개 (초반 강화 소재)
            var houndMat = MakeMaterial(new Color(0.45f, 0.42f, 0.4f));
            var houndA = BuildHound(dummies.transform, "Ashen Hound A", new Vector3(-9f, 0f, 11f), houndMat, weakMat);
            var houndB = BuildHound(dummies.transform, "Ashen Hound B", new Vector3(9f, 0f, 12f), houndMat, weakMat);

            // 대장간(무기) + 방어구 작업대 + 장비 강화 + 저장
            BuildForge(new Vector3(3f, 0f, -4f));
            BuildArmorBench(new Vector3(6f, 0f, -6.5f));
            SetupProgression(player);

            // 퀘스트 게시판
            BuildQuestBoard(new Vector3(-6f, 0f, -5f), boss, houndA, houndB);

            Selection.activeGameObject = player;
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(player.scene);
            Debug.Log("[SoulsHunter] 테스트 아레나 생성 완료. Play를 누르세요.");
        }

        /// <summary>
        /// 네 발 몬스터 보스. 루트는 발 위치(크기 1), 몸 부위는 자식으로 둔다.
        /// 부위마다 Hurtbox: 머리(약점·파괴), 꼬리(파괴), 다리(단단함), 몸통.
        /// </summary>
        static Health BuildBoss(Transform parent, Vector3 pos, Material bodyMat, Material weakMat)
        {
            var boss = Create("Ravager");
            boss.transform.SetParent(parent, false);
            boss.transform.localPosition = pos;
            boss.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // 플레이어 쪽을 보게

            var health = boss.AddComponent<Health>();
            SetString(health, "displayName", "Ravager, the Iron Maw");
            SetFloat(health, "maxHealth", 1200f);
            SetFloat(health, "maxPoise", 120f);

            Part(boss.transform, PrimitiveType.Capsule, "Body", new Vector3(0f, 2f, 0f), new Vector3(90f, 0f, 0f), new Vector3(2.4f, 2f, 2.4f), bodyMat, "Body", 1f, 0f);
            Part(boss.transform, PrimitiveType.Sphere, "Head (약점)", new Vector3(0f, 1.8f, 2.6f), Vector3.zero, Vector3.one * 1.4f, weakMat, "Head", 1.5f, 200f);
            Part(boss.transform, PrimitiveType.Capsule, "Tail", new Vector3(0f, 1.6f, -3.4f), new Vector3(90f, 0f, 0f), new Vector3(0.7f, 1.2f, 0.7f), bodyMat, "Tail", 1.2f, 150f);
            foreach (var (x, z) in new[] { (-0.9f, 1.2f), (0.9f, 1.2f), (-0.9f, -1.2f), (0.9f, -1.2f) })
                Part(boss.transform, PrimitiveType.Cube, "Leg", new Vector3(x, 0.5f, z), Vector3.zero, new Vector3(0.6f, 1f, 0.6f), bodyMat, "Leg", 0.8f, 0f);

            // 공격 예고용 눈
            var eyeMat = MakeMaterial(Color.black);
            eyeMat.EnableKeyword("_EMISSION");
            eyeMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            var eyeL = Visual(PrimitiveType.Sphere, "Eye L", boss.transform, new Vector3(-0.35f, 2.1f, 3.2f), Vector3.one * 0.22f, eyeMat);
            var eyeR = Visual(PrimitiveType.Sphere, "Eye R", boss.transform, new Vector3(0.35f, 2.1f, 3.2f), Vector3.one * 0.22f, eyeMat);

            var lockOn = boss.AddComponent<LockOnTarget>();
            SetVector(lockOn, "offset", new Vector3(0f, 2f, 0.5f));
            var dummy = boss.AddComponent<TrainingDummy>();
            SetFloat(dummy, "respawnTime", 6f);

            var agent = boss.AddComponent<UnityEngine.AI.NavMeshAgent>();
            agent.radius = 1.5f;
            agent.height = 3.5f;
            agent.baseOffset = 0f;
            agent.acceleration = 20f;
            agent.angularSpeed = 0f;
            agent.enabled = false; // 내비메시가 구워진 뒤 EnemyAI.Start에서 켠다

            // 보상 (토벌 + 부위 파괴)
            var scale = GetOrCreateItem("RavagerScale", "철갑 비늘", "Ravager의 단단한 비늘. 방어구 소재.", 2);
            var hide = GetOrCreateItem("RavagerHide", "철갑 가죽", "질기고 무거운 가죽.", 2);
            var gem = GetOrCreateItem("RavagerGem", "철갑의 보옥", "몸속에서 드물게 자라는 보석. 아주 귀하다.", 6);
            var fang = GetOrCreateItem("RavagerFang", "강철 엄니", "머리를 파괴해야 얻을 수 있는 엄니.", 3);
            var spike = GetOrCreateItem("RavagerTailSpike", "꼬리 가시", "꼬리를 파괴해야 얻을 수 있는 가시.", 3);
            var rewards = boss.AddComponent<MonsterRewards>();
            rewards.EditorSetup(
                new List<MonsterRewards.Reward>
                {
                    new() { item = scale, min = 2, max = 3 },
                    new() { item = hide, min = 1, max = 2 },
                    new() { item = gem, min = 1, max = 1, chance = 0.1f },
                },
                new List<MonsterRewards.PartReward>
                {
                    new() { partName = "Head", rewards = new() { new() { item = fang, min = 1, max = 2 } } },
                    new() { partName = "Tail", rewards = new() { new() { item = spike, min = 1, max = 1 } } },
                });
            EditorUtility.SetDirty(rewards);

            var ai = boss.AddComponent<EnemyAI>();
            SetFloat(ai, "enrageThreshold", 0.5f); // 체력 50% 이하에서 분노
            SetRef(ai, "telegraphMaterial", GetOrCreateTelegraphMaterial());
            var so = new SerializedObject(ai);
            var arr = so.FindProperty("telegraphRenderers");
            arr.arraySize = 2;
            arr.GetArrayElementAtIndex(0).objectReferenceValue = eyeL.GetComponent<Renderer>();
            arr.GetArrayElementAtIndex(1).objectReferenceValue = eyeR.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();
            return health;
        }

        static void BuildBonfire(Vector3 pos)
        {
            var root = Create("Bonfire");
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, 45f, 0f);

            Primitive(PrimitiveType.Cylinder, "Stones", root.transform, new Vector3(0f, 0.1f, 0f), new Vector3(1.2f, 0.1f, 1.2f), MakeMaterial(new Color(0.3f, 0.28f, 0.26f)));
            var sword = Visual(PrimitiveType.Cube, "Coiled Sword", root.transform, new Vector3(0f, 0.7f, 0f), new Vector3(0.08f, 1.2f, 0.08f), MakeMaterial(new Color(0.4f, 0.35f, 0.3f)));
            sword.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);

            var flameMat = MakeMaterial(new Color(1f, 0.45f, 0.1f));
            flameMat.EnableKeyword("_EMISSION");
            flameMat.SetColor("_EmissionColor", new Color(1f, 0.45f, 0.1f) * 3f);
            var flame = Visual(PrimitiveType.Sphere, "Flame", root.transform, new Vector3(0f, 0.45f, 0f), new Vector3(0.5f, 0.7f, 0.5f), flameMat);

            var lightGo = new GameObject("Fire Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.25f);
            light.range = 8f;
            light.intensity = 3f;

            var bonfire = root.AddComponent<Bonfire>();
            SetString(bonfire, "bonfireName", "아레나 입구");
            SetRef(bonfire, "fireLight", light);
            SetRef(bonfire, "flame", flame);
        }

        static void BuildForge(Vector3 pos)
        {
            var root = Create("Forge");
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, -30f, 0f);

            var ironMat = MakeMaterial(new Color(0.22f, 0.22f, 0.25f));
            Primitive(PrimitiveType.Cube, "Anvil Base", root.transform, new Vector3(0f, 0.3f, 0f), new Vector3(0.5f, 0.6f, 0.4f), ironMat);
            Primitive(PrimitiveType.Cube, "Anvil Top", root.transform, new Vector3(0f, 0.7f, 0f), new Vector3(1.1f, 0.2f, 0.45f), ironMat);
            var horn = Visual(PrimitiveType.Cylinder, "Anvil Horn", root.transform, new Vector3(0.65f, 0.7f, 0f), new Vector3(0.18f, 0.2f, 0.18f), ironMat);
            horn.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            var coalMat = MakeMaterial(new Color(1f, 0.3f, 0.05f));
            coalMat.EnableKeyword("_EMISSION");
            coalMat.SetColor("_EmissionColor", new Color(1f, 0.3f, 0.05f) * 2f);
            Primitive(PrimitiveType.Cube, "Furnace", root.transform, new Vector3(-1.3f, 0.6f, 0.3f), new Vector3(0.9f, 1.2f, 0.9f), MakeMaterial(new Color(0.35f, 0.3f, 0.28f)));
            Visual(PrimitiveType.Cube, "Coals", root.transform, new Vector3(-1.3f, 0.6f, -0.16f), new Vector3(0.6f, 0.35f, 0.05f), coalMat);

            var lightGo = new GameObject("Forge Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(-1.1f, 1f, -0.6f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.45f, 0.15f);
            light.range = 6f;
            light.intensity = 2f;

            var forge = root.AddComponent<Forge>();
            forge.EditorSetup("대장간", Forge.Slot.Weapon, light);
            EditorUtility.SetDirty(forge);
        }

        /// <summary>퀘스트 게시판: 기둥 두 개 + 판자 + 의뢰서. 들개 사냥 → Ravager 토벌 순서.</summary>
        static void BuildQuestBoard(Vector3 pos, Health boss, Health houndA, Health houndB)
        {
            var root = Create("Quest Board");
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, 30f, 0f);

            var woodMat = MakeMaterial(new Color(0.38f, 0.27f, 0.18f));
            var paperMat = MakeMaterial(new Color(0.92f, 0.87f, 0.72f));
            Primitive(PrimitiveType.Cube, "Post L", root.transform, new Vector3(-0.9f, 1f, 0f), new Vector3(0.15f, 2f, 0.15f), woodMat);
            Primitive(PrimitiveType.Cube, "Post R", root.transform, new Vector3(0.9f, 1f, 0f), new Vector3(0.15f, 2f, 0.15f), woodMat);
            Primitive(PrimitiveType.Cube, "Board", root.transform, new Vector3(0f, 1.45f, 0f), new Vector3(1.9f, 1.1f, 0.08f), woodMat);
            Visual(PrimitiveType.Cube, "Notice A", root.transform, new Vector3(-0.45f, 1.5f, -0.05f), new Vector3(0.55f, 0.7f, 0.02f), paperMat);
            Visual(PrimitiveType.Cube, "Notice B", root.transform, new Vector3(0.4f, 1.4f, -0.05f), new Vector3(0.5f, 0.6f, 0.02f), paperMat);

            var fur = GetOrCreateItem("AshenFur", "잿빛 털", "잿빛 들개의 거친 털. 흔한 소재.", 1);
            var tooth = GetOrCreateItem("HoundFang", "들개 이빨", "날카로운 송곳니. 무기 강화에 쓴다.", 1);
            var scale = GetOrCreateItem("RavagerScale", "철갑 비늘", "Ravager의 단단한 비늘. 방어구 소재.", 2);
            var hide = GetOrCreateItem("RavagerHide", "철갑 가죽", "질기고 무거운 가죽.", 2);
            var gem = GetOrCreateItem("RavagerGem", "철갑의 보옥", "몸속에서 드물게 자라는 보석. 아주 귀하다.", 6);

            var board = root.AddComponent<QuestBoard>();
            board.EditorSetup(new List<HuntQuest>
            {
                new()
                {
                    id = "hunt_hounds", title = "잿빛 들개 사냥",
                    description = "아레나를 떠도는 잿빛 들개 두 마리를 사냥하라.",
                    targets = new() { houndA, houndB }, requiredKills = 2,
                    timeLimit = 600f, maxFaints = 3,
                    rewards = new()
                    {
                        new() { item = fur, min = 2, max = 3 },
                        new() { item = tooth, min = 1, max = 2 },
                    },
                },
                new()
                {
                    id = "hunt_ravager", title = "철갑의 포식자",
                    description = "Ravager, the Iron Maw를 토벌하라.",
                    targets = new() { boss }, requiredKills = 1,
                    timeLimit = 900f, maxFaints = 3,
                    rewards = new()
                    {
                        new() { item = scale, min = 2, max = 3 },
                        new() { item = hide, min = 1, max = 2 },
                        new() { item = gem, min = 1, max = 1, chance = 0.3f },
                    },
                },
            });
            EditorUtility.SetDirty(board);
        }

        /// <summary>방어구 작업대: 탁자 + 갑옷 걸이. 방어구를 강화한다.</summary>
        static void BuildArmorBench(Vector3 pos)
        {
            var root = Create("Armor Bench");
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, -60f, 0f);

            var woodMat = MakeMaterial(new Color(0.42f, 0.3f, 0.2f));
            var steelMat = MakeMaterial(new Color(0.6f, 0.6f, 0.65f));
            Primitive(PrimitiveType.Cube, "Table", root.transform, new Vector3(0f, 0.45f, 0f), new Vector3(1.6f, 0.9f, 0.7f), woodMat);
            Visual(PrimitiveType.Cube, "Plate", root.transform, new Vector3(-0.3f, 0.93f, 0f), new Vector3(0.5f, 0.06f, 0.4f), steelMat);
            Visual(PrimitiveType.Cube, "Hammer", root.transform, new Vector3(0.4f, 0.93f, 0.1f), new Vector3(0.12f, 0.08f, 0.45f), steelMat);

            // 갑옷 걸이 (탁자 옆)
            Primitive(PrimitiveType.Cylinder, "Stand Pole", root.transform, new Vector3(1.4f, 0.8f, 0f), new Vector3(0.08f, 0.8f, 0.08f), woodMat);
            Visual(PrimitiveType.Capsule, "Cuirass", root.transform, new Vector3(1.4f, 1.3f, 0f), new Vector3(0.55f, 0.4f, 0.35f), steelMat);
            Visual(PrimitiveType.Sphere, "Helm", root.transform, new Vector3(1.4f, 1.85f, 0f), Vector3.one * 0.3f, steelMat);

            var lightGo = new GameObject("Bench Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.8f, -0.5f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.85f, 0.6f);
            light.range = 5f;
            light.intensity = 1.2f;

            var bench = root.AddComponent<Forge>();
            bench.EditorSetup("방어구 작업대", Forge.Slot.Armor, light);
            EditorUtility.SetDirty(bench);
        }

        /// <summary>
        /// 잡몹 '잿빛 들개'. 빠르고 약하다. 물기(근거리)와 덮치기(돌진).
        /// 초반 강화 소재(털, 이빨)를 준다. 토벌 배너는 띄우지 않는다.
        /// </summary>
        static Health BuildHound(Transform parent, string name, Vector3 pos, Material bodyMat, Material weakMat)
        {
            var hound = Create(name);
            hound.transform.SetParent(parent, false);
            hound.transform.localPosition = pos;
            hound.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            var health = hound.AddComponent<Health>();
            SetString(health, "displayName", "잿빛 들개");
            SetFloat(health, "maxHealth", 120f);
            SetFloat(health, "maxPoise", 25f);

            Part(hound.transform, PrimitiveType.Capsule, "Body", new Vector3(0f, 0.7f, 0f), new Vector3(90f, 0f, 0f), new Vector3(0.7f, 0.7f, 0.7f), bodyMat, "Body", 1f, 0f);
            Part(hound.transform, PrimitiveType.Sphere, "Head", new Vector3(0f, 0.85f, 0.75f), Vector3.zero, Vector3.one * 0.5f, weakMat, "Head", 1.4f, 0f);
            foreach (var (x, z) in new[] { (-0.2f, 0.35f), (0.2f, 0.35f), (-0.2f, -0.35f), (0.2f, -0.35f) })
                Visual(PrimitiveType.Cube, "Leg", hound.transform, new Vector3(x, 0.2f, z), new Vector3(0.12f, 0.4f, 0.12f), bodyMat);

            var eyeMat = MakeMaterial(Color.black);
            eyeMat.EnableKeyword("_EMISSION");
            var eyeL = Visual(PrimitiveType.Sphere, "Eye L", hound.transform, new Vector3(-0.12f, 0.95f, 0.97f), Vector3.one * 0.08f, eyeMat);
            var eyeR = Visual(PrimitiveType.Sphere, "Eye R", hound.transform, new Vector3(0.12f, 0.95f, 0.97f), Vector3.one * 0.08f, eyeMat);

            var lockOn = hound.AddComponent<LockOnTarget>();
            SetVector(lockOn, "offset", new Vector3(0f, 0.8f, 0f));
            var dummy = hound.AddComponent<TrainingDummy>();
            SetFloat(dummy, "respawnTime", 20f);

            var agent = hound.AddComponent<UnityEngine.AI.NavMeshAgent>();
            agent.radius = 0.5f;
            agent.height = 1.2f;
            agent.baseOffset = 0f;
            agent.acceleration = 30f;
            agent.angularSpeed = 0f;
            agent.enabled = false;

            var fur = GetOrCreateItem("AshenFur", "잿빛 털", "잿빛 들개의 거친 털. 흔한 소재.", 1);
            var tooth = GetOrCreateItem("HoundFang", "들개 이빨", "날카로운 송곳니. 무기 강화에 쓴다.", 1);
            var rewards = hound.AddComponent<MonsterRewards>();
            rewards.EditorSetup(
                new List<MonsterRewards.Reward>
                {
                    new() { item = fur, min = 1, max = 2 },
                    new() { item = tooth, min = 1, max = 1, chance = 0.6f },
                },
                new List<MonsterRewards.PartReward>(),
                announce: false);
            EditorUtility.SetDirty(rewards);

            var ai = hound.AddComponent<EnemyAI>();
            ai.EditorSetup(new List<EnemyAttack>
            {
                //   이름     최소/최대거리  각도  가중치 쿨  피해 강인  예고   판정   빈틈  추적 전진  전진구간              판정중심              판정크기
                new("Bite",   0f, 2.6f,  70f, 3f,   0f, 14f, 10f, 0.45f, 0.15f, 0.70f, 0f, 0f, Vector2.zero,      new(0f, 0.6f, 1.2f), new(1.2f, 1.2f, 1.4f)),
                new("Pounce", 3.5f, 8f,  30f, 1.5f, 4f, 20f, 20f, 0.65f, 0.35f, 1.00f, 0f, 4f, new(0.65f, 1.0f), new(0f, 0.6f, 0.9f), new(1.4f, 1.2f, 1.4f), isDash: true),
            }, speed: 5f, engage: 2f, aggro: 11f, staggerTime: 1f, enrageAt: 0f);
            EditorUtility.SetDirty(ai);
            SetRef(ai, "telegraphMaterial", GetOrCreateTelegraphMaterial());
            var so = new SerializedObject(ai);
            var arr = so.FindProperty("telegraphRenderers");
            arr.arraySize = 2;
            arr.GetArrayElementAtIndex(0).objectReferenceValue = eyeL.GetComponent<Renderer>();
            arr.GetArrayElementAtIndex(1).objectReferenceValue = eyeR.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();
            return health;
        }

        /// <summary>무기 강화 단계(재료)와 저장 시스템을 플레이어에 붙인다.</summary>
        static void SetupProgression(GameObject player)
        {
            var scale = GetOrCreateItem("RavagerScale", "철갑 비늘", "Ravager의 단단한 비늘. 방어구 소재.", 2);
            var hide = GetOrCreateItem("RavagerHide", "철갑 가죽", "질기고 무거운 가죽.", 2);
            var gem = GetOrCreateItem("RavagerGem", "철갑의 보옥", "몸속에서 드물게 자라는 보석. 아주 귀하다.", 6);
            var fang = GetOrCreateItem("RavagerFang", "강철 엄니", "머리를 파괴해야 얻을 수 있는 엄니.", 3);
            var spike = GetOrCreateItem("RavagerTailSpike", "꼬리 가시", "꼬리를 파괴해야 얻을 수 있는 가시.", 3);
            var fur = GetOrCreateItem("AshenFur", "잿빛 털", "잿빛 들개의 거친 털. 흔한 소재.", 1);
            var tooth = GetOrCreateItem("HoundFang", "들개 이빨", "날카로운 송곳니. 무기 강화에 쓴다.", 1);

            static UpgradableEquipment.UpgradeStep Step(float bonus, params (ItemData item, int amount)[] costs)
            {
                var step = new UpgradableEquipment.UpgradeStep { bonus = bonus };
                foreach (var (item, amount) in costs) step.costs.Add(new UpgradableEquipment.Cost { item = item, amount = amount });
                return step;
            }

            // 무기: 피해 +15% / +15% / +20% / +25%
            var weapon = player.AddComponent<PlayerWeapon>();
            weapon.EditorSetup("헌터의 대검", new List<UpgradableEquipment.UpgradeStep>
            {
                Step(0.15f, (fur, 2), (tooth, 2)),
                Step(0.15f, (scale, 3), (hide, 1), (tooth, 2)),
                Step(0.20f, (scale, 4), (fang, 1), (hide, 2)),
                Step(0.25f, (fang, 2), (spike, 2), (gem, 1)),
            });
            EditorUtility.SetDirty(weapon);

            // 방어구: 받는 피해 -8% / -8% / -10% / -12%
            var armor = player.AddComponent<PlayerArmor>();
            armor.EditorSetup("헌터의 가죽갑옷", new List<UpgradableEquipment.UpgradeStep>
            {
                Step(0.08f, (fur, 3)),
                Step(0.08f, (fur, 4), (hide, 2)),
                Step(0.10f, (scale, 4), (hide, 3)),
                Step(0.12f, (scale, 5), (spike, 1), (gem, 1)),
            });
            EditorUtility.SetDirty(armor);

            var save = player.AddComponent<SaveSystem>();
            save.EditorSetup(new List<ItemData> { scale, hide, gem, fang, spike, fur, tooth });
            EditorUtility.SetDirty(save);
        }

        const string ItemFolder = "Assets/_Project/Data/Items";

        static ItemData GetOrCreateItem(string file, string name, string desc, int rarity)
        {
            string path = $"{ItemFolder}/{file}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (item) return item;

            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data")) AssetDatabase.CreateFolder("Assets/_Project", "Data");
            if (!AssetDatabase.IsValidFolder(ItemFolder)) AssetDatabase.CreateFolder("Assets/_Project/Data", "Items");
            item = ScriptableObject.CreateInstance<ItemData>();
            item.EditorSetup(name, desc, rarity);
            AssetDatabase.CreateAsset(item, path);
            AssetDatabase.SaveAssets();
            return item;
        }

        const string TelegraphMaterialPath = "Assets/_Project/Materials/AttackTelegraph.mat";

        /// <summary>바닥 공격 범위용 머티리얼을 에셋으로 저장 (빌드에 셰이더가 포함되도록)</summary>
        static Material GetOrCreateTelegraphMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(TelegraphMaterialPath);
            if (mat) return mat;
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Materials"))
                AssetDatabase.CreateFolder("Assets/_Project", "Materials");
            mat = AttackTelegraph.CreateMaterial();
            AssetDatabase.CreateAsset(mat, TelegraphMaterialPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static void Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 euler, Vector3 scale,
                         Material mat, string partName, float multiplier, float breakThreshold)
        {
            var go = Primitive(type, name, parent, pos, scale, mat);
            go.transform.localRotation = Quaternion.Euler(euler);
            var hb = go.AddComponent<Hurtbox>();
            SetString(hb, "partName", partName);
            SetFloat(hb, "damageMultiplier", multiplier);
            SetFloat(hb, "breakThreshold", breakThreshold);
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
