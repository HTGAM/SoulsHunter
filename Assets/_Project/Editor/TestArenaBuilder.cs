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
    /// 현재 씬에 게임 전체를 만든다. 이미 만든 적이 있으면 지우고 다시 만든다.
    /// - 안전구역(Hub): 퀘스트 게시판, 출발 관문, 대장간, 방어구 작업대, 훈련용 허수아비
    /// - 위험구역(Colosseum): 원형 투기장. 퀘스트별 몬스터는 웨이브가 시작될 때만 켜진다
    /// - 플레이어, 카메라, HUD, 사냥 관리자(HuntManager)
    /// </summary>
    public static class TestArenaBuilder
    {
        // 예전 버전에서 만든 이름(Arena, Bonfire 등)도 지운다
        static readonly string[] GeneratedRoots =
        {
            "Arena", "Bonfire", "Dummies",
            "Player", "LockOnMarker", "HUD", "Forge", "Armor Bench", "Quest Board",
            "Hub", "Colosseum", "Arena Enemies", "Hunt Manager", "Departure Gate", "Training Ground", "Weapon Rack",
        };

        static readonly Vector3 HubSpawn = new(0f, 0f, -4f);
        static readonly Vector3 ArenaCenter = new(0f, 0f, 200f);
        const float ArenaRadius = 23f;

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

            // 환경: 안전구역 + 투기장
            BuildHub(groundMat, wallMat);
            var arenaSpawn = BuildColosseum(wallMat);

            // 플레이어
            var player = Create("Player");
            player.tag = "Player";
            player.transform.position = HubSpawn;
            var cc = player.AddComponent<CharacterController>();
            cc.center = new Vector3(0f, 1f, 0f);
            cc.height = 2f;
            cc.radius = 0.4f;
            Visual(PrimitiveType.Capsule, "Body", player.transform, new Vector3(0f, 1f, 0f), new Vector3(0.8f, 1f, 0.8f), playerMat);
            Visual(PrimitiveType.Cube, "Nose (정면 표시)", player.transform, new Vector3(0f, 1.5f, 0.4f), new Vector3(0.25f, 0.15f, 0.3f), enemyMat);
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
            player.AddComponent<PlayerGuard>(); // 방패 모델은 무기에 따라 PlayerWeaponLoadout이 만든다

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
            cam.transform.SetPositionAndRotation(HubSpawn + new Vector3(0f, 2.5f, -4.5f), Quaternion.Euler(12f, 0f, 0f));

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
            hud.gameObject.AddComponent<QuestResultScreen>();

            // 안전구역 시설
            BuildForge(new Vector3(5f, 0f, 2f));
            BuildArmorBench(new Vector3(8f, 0f, -2f));
            SetupProgression(player);
            BuildQuestBoard(new Vector3(-4f, 0f, 3f));
            BuildDepartureGate(new Vector3(0f, 0f, 11f));

            // 훈련장 허수아비 (안전구역 왼쪽)
            var training = Create("Training Ground");
            Dummy(training.transform, "Dummy A", new Vector3(-10f, 0f, -3f), 1f, 150f, enemyMat);
            Dummy(training.transform, "Dummy B", new Vector3(-12f, 0f, 1f), 1f, 150f, enemyMat);

            // 퀘스트와 투기장 몬스터
            BuildHuntContent(arenaSpawn, enemyMat, weakMat);
            // 에셋스토어 몬스터 모델이 있으면 도형 대신 입힌다
            MonsterModelSetup.ApplyToScene(out _);

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
            ai.EditorSetBoss(true);
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

        // ───────── 안전구역 ─────────

        /// <summary>안전구역: 잔디 바닥 + 낮은 울타리 + 등불. 몬스터가 없다.</summary>
        static void BuildHub(Material groundMat, Material wallMat)
        {
            var hub = Create("Hub");
            var grassMat = MakeMaterial(new Color(0.3f, 0.42f, 0.28f));
            Primitive(PrimitiveType.Plane, "Ground", hub.transform, Vector3.zero, new Vector3(3.6f, 1f, 3.6f), grassMat);
            Primitive(PrimitiveType.Cube, "Path", hub.transform, new Vector3(0f, 0.01f, 3.5f), new Vector3(3f, 0.02f, 14f), groundMat);

            // 울타리 (네 변)
            const float half = 17.5f;
            for (int side = 0; side < 4; side++)
            {
                var rot = Quaternion.Euler(0f, side * 90f, 0f);
                for (float x = -half; x <= half; x += 3.5f)
                {
                    var post = Primitive(PrimitiveType.Cube, "Fence Post", hub.transform, rot * new Vector3(x, 0.6f, half), new Vector3(0.25f, 1.2f, 0.25f), wallMat);
                    post.transform.localRotation = rot;
                }
                var rail = Primitive(PrimitiveType.Cube, "Fence Rail", hub.transform, rot * new Vector3(0f, 0.9f, half), new Vector3(half * 2f, 0.12f, 0.1f), wallMat);
                rail.transform.localRotation = rot;
            }

            // 등불
            foreach (var pos in new[] { new Vector3(-6f, 0f, -6f), new Vector3(6f, 0f, -6f), new Vector3(-6f, 0f, 8f), new Vector3(6f, 0f, 8f) })
            {
                Primitive(PrimitiveType.Cylinder, "Lantern Pole", hub.transform, pos + new Vector3(0f, 1.2f, 0f), new Vector3(0.12f, 1.2f, 0.12f), wallMat);
                var lightGo = new GameObject("Lantern Light");
                lightGo.transform.SetParent(hub.transform, false);
                lightGo.transform.localPosition = pos + new Vector3(0f, 2.6f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.8f, 0.5f);
                light.range = 9f;
                light.intensity = 1.5f;
            }
        }

        /// <summary>퀘스트 게시판: 기둥 두 개 + 판자 + 의뢰서.</summary>
        static void BuildQuestBoard(Vector3 pos)
        {
            var root = Create("Quest Board");
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, -20f, 0f);

            var woodMat = MakeMaterial(new Color(0.38f, 0.27f, 0.18f));
            var paperMat = MakeMaterial(new Color(0.92f, 0.87f, 0.72f));
            Primitive(PrimitiveType.Cube, "Post L", root.transform, new Vector3(-0.9f, 1f, 0f), new Vector3(0.15f, 2f, 0.15f), woodMat);
            Primitive(PrimitiveType.Cube, "Post R", root.transform, new Vector3(0.9f, 1f, 0f), new Vector3(0.15f, 2f, 0.15f), woodMat);
            Primitive(PrimitiveType.Cube, "Board", root.transform, new Vector3(0f, 1.45f, 0f), new Vector3(1.9f, 1.1f, 0.08f), woodMat);
            Visual(PrimitiveType.Cube, "Notice A", root.transform, new Vector3(-0.45f, 1.5f, -0.05f), new Vector3(0.55f, 0.7f, 0.02f), paperMat);
            Visual(PrimitiveType.Cube, "Notice B", root.transform, new Vector3(0.4f, 1.4f, -0.05f), new Vector3(0.5f, 0.6f, 0.02f), paperMat);
            root.AddComponent<QuestBoard>();
        }

        /// <summary>무기 거치대: 나무 틀에 무기 모양 6개. 상호작용하면 다음 무기를 든다.</summary>
        static void BuildWeaponRack(Vector3 pos)
        {
            var root = Create("Weapon Rack");
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, 200f, 0f);

            var woodMat = MakeMaterial(new Color(0.4f, 0.28f, 0.18f));
            var steelMat = MakeMaterial(new Color(0.75f, 0.77f, 0.8f));
            Primitive(PrimitiveType.Cube, "Post L", root.transform, new Vector3(-1.4f, 0.9f, 0f), new Vector3(0.15f, 1.8f, 0.15f), woodMat);
            Primitive(PrimitiveType.Cube, "Post R", root.transform, new Vector3(1.4f, 0.9f, 0f), new Vector3(0.15f, 1.8f, 0.15f), woodMat);
            Primitive(PrimitiveType.Cube, "Bar", root.transform, new Vector3(0f, 1.6f, 0f), new Vector3(3f, 0.12f, 0.12f), woodMat);
            Primitive(PrimitiveType.Cube, "Base", root.transform, new Vector3(0f, 0.1f, 0f), new Vector3(3f, 0.2f, 0.5f), woodMat);
            for (int i = 0; i < 6; i++)
            {
                float x = -1.1f + i * 0.44f;
                float h = 0.9f + (i % 2) * 0.25f;
                Visual(PrimitiveType.Cube, "Weapon", root.transform, new Vector3(x, 0.2f + h * 0.5f, 0.05f), new Vector3(0.08f, h, 0.06f), steelMat);
            }
            root.AddComponent<WeaponRack>();
        }

        /// <summary>출발 관문: 돌 아치. 퀘스트를 받으면 가운데가 빛난다.</summary>
        static void BuildDepartureGate(Vector3 pos)
        {
            var root = Create("Departure Gate");
            root.transform.position = pos;

            var stoneMat = MakeMaterial(new Color(0.45f, 0.43f, 0.42f));
            Primitive(PrimitiveType.Cube, "Pillar L", root.transform, new Vector3(-1.8f, 2f, 0f), new Vector3(0.8f, 4f, 0.8f), stoneMat);
            Primitive(PrimitiveType.Cube, "Pillar R", root.transform, new Vector3(1.8f, 2f, 0f), new Vector3(0.8f, 4f, 0.8f), stoneMat);
            Primitive(PrimitiveType.Cube, "Lintel", root.transform, new Vector3(0f, 4.3f, 0f), new Vector3(4.6f, 0.6f, 1f), stoneMat);

            var glowMat = MakeMaterial(new Color(1f, 0.6f, 0.2f));
            glowMat.EnableKeyword("_EMISSION");
            glowMat.SetColor("_EmissionColor", new Color(1f, 0.55f, 0.15f) * 2.5f);
            var glow = Visual(PrimitiveType.Cube, "Ready Glow", root.transform, new Vector3(0f, 2f, 0f), new Vector3(2.8f, 4f, 0.05f), glowMat);
            var lightGo = new GameObject("Glow Light");
            lightGo.transform.SetParent(glow.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.25f);
            light.range = 7f;
            light.intensity = 2f;
            glow.SetActive(false);

            var gate = root.AddComponent<DepartureGate>();
            gate.EditorSetup(glow);
            EditorUtility.SetDirty(gate);
        }

        // ───────── 투기장 ─────────

        /// <summary>
        /// 원형 투기장 (안전구역에서 멀리 떨어진 곳). 모래 바닥 + 둥근 벽 + 엄폐용 기둥 4개 + 횃불.
        /// 내비메시는 실행할 때 구운다. 반환값은 출발 시 플레이어가 서는 위치.
        /// </summary>
        static Transform BuildColosseum(Material wallMat)
        {
            var root = Create("Colosseum");
            root.transform.position = ArenaCenter;

            var sandMat = MakeMaterial(new Color(0.72f, 0.62f, 0.45f));
            var darkSandMat = MakeMaterial(new Color(0.6f, 0.5f, 0.36f));
            var stoneMat = MakeMaterial(new Color(0.5f, 0.46f, 0.42f));
            Primitive(PrimitiveType.Cube, "Floor", root.transform, new Vector3(0f, -0.1f, 0f), new Vector3(ArenaRadius * 2f + 6f, 0.2f, ArenaRadius * 2f + 6f), darkSandMat);
            Visual(PrimitiveType.Cylinder, "Sand Circle", root.transform, new Vector3(0f, 0.01f, 0f), new Vector3(ArenaRadius * 2f - 1f, 0.01f, ArenaRadius * 2f - 1f), sandMat);

            // 둥근 벽
            const int segments = 32;
            float segWidth = 2f * Mathf.PI * ArenaRadius / segments + 0.3f;
            for (int i = 0; i < segments; i++)
            {
                float a = i * 360f / segments;
                var rot = Quaternion.Euler(0f, a, 0f);
                var wall = Primitive(PrimitiveType.Cube, "Wall", root.transform, rot * new Vector3(0f, 3f, ArenaRadius + 0.5f), new Vector3(segWidth, 6f, 1f), stoneMat);
                wall.transform.localRotation = rot;
            }

            // 엄폐용 기둥
            for (int i = 0; i < 4; i++)
            {
                var rot = Quaternion.Euler(0f, 45f + i * 90f, 0f);
                Primitive(PrimitiveType.Cube, "Pillar", root.transform, rot * new Vector3(0f, 3f, 11f), new Vector3(1.8f, 6f, 1.8f), wallMat);
            }

            // 횃불
            var fireMat = MakeMaterial(new Color(1f, 0.45f, 0.1f));
            fireMat.EnableKeyword("_EMISSION");
            fireMat.SetColor("_EmissionColor", new Color(1f, 0.45f, 0.1f) * 3f);
            for (int i = 0; i < 8; i++)
            {
                var rot = Quaternion.Euler(0f, i * 45f + 22.5f, 0f);
                Vector3 p = rot * new Vector3(0f, 4.5f, ArenaRadius - 0.3f);
                Visual(PrimitiveType.Sphere, "Torch", root.transform, p, Vector3.one * 0.4f, fireMat);
                var lightGo = new GameObject("Torch Light");
                lightGo.transform.SetParent(root.transform, false);
                lightGo.transform.localPosition = p + rot * new Vector3(0f, 0f, -0.6f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.6f, 0.3f);
                light.range = 14f;
                light.intensity = 2f;
            }

            var surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            root.AddComponent<BakeNavMeshOnAwake>();

            var spawn = new GameObject("Player Spawn");
            spawn.transform.SetParent(root.transform, false);
            spawn.transform.localPosition = new Vector3(0f, 0f, -ArenaRadius + 5f);
            return spawn.transform;
        }

        /// <summary>
        /// 퀘스트 3개와 각자의 투기장 몬스터, 사냥 관리자.
        /// 몬스터는 퀘스트마다 따로 만든다 (같은 몬스터가 여러 퀘스트에 섞이지 않게).
        /// </summary>
        static void BuildHuntContent(Transform arenaSpawn, Material enemyMat, Material weakMat)
        {
            var enemies = Create("Arena Enemies");
            var houndMat = MakeMaterial(new Color(0.45f, 0.42f, 0.4f));
            Vector3 c = ArenaCenter;

            Transform Group(string name)
            {
                var g = new GameObject(name);
                g.transform.SetParent(enemies.transform, false);
                return g.transform;
            }
            Health Hound(Transform g, string name, float x, float z) => BuildHound(g, name, c + new Vector3(x, 0f, z), houndMat, weakMat);
            Health Boss(Transform g, float x, float z) => BuildBoss(g, c + new Vector3(x, 0f, z), enemyMat, weakMat);
            Health Wyrm(Transform g, float x, float z) => BuildWyrm(g, c + new Vector3(x, 0f, z), weakMat);

            // 1. 들개 사냥
            var g1 = Group("Q1 Hounds");
            var q1Wave = new HuntWave { label = "잿빛 들개", enemies = new() { Hound(g1, "Ashen Hound A", -6f, 10f), Hound(g1, "Ashen Hound B", 6f, 10f) } };

            // 2. Ravager 토벌
            var g2 = Group("Q2 Ravager");
            var q2Wave = new HuntWave { label = "Ravager", enemies = new() { Boss(g2, 0f, 12f) } };

            // 3. 보스 러시: 들개 무리 → Ravager → Ravager + 들개
            var g3 = Group("Q3 Boss Rush");
            var rush1 = new HuntWave { label = "들개 무리", enemies = new() { Hound(g3, "Rush Hound 1", -7f, 9f), Hound(g3, "Rush Hound 2", 0f, 12f), Hound(g3, "Rush Hound 3", 7f, 9f) } };
            var rush2 = new HuntWave { label = "철갑의 포식자", enemies = new() { Boss(g3, 0f, 12f) } };
            var rush3 = new HuntWave { label = "잿불의 비룡", enemies = new() { Wyrm(g3, 0f, 14f), Hound(g3, "Rush Hound 4", -9f, 6f), Hound(g3, "Rush Hound 5", 9f, 6f) } };

            // 4. 잿불 비룡 토벌 (원거리 보스)
            var g4 = Group("Q4 Cinder Wyrm");
            var q4Wave = new HuntWave { label = "Cinder Wyrm", enemies = new() { Wyrm(g4, 0f, 14f) } };

            var fur = GetOrCreateItem("AshenFur", "잿빛 털", "잿빛 들개의 거친 털. 흔한 소재.", 1);
            var tooth = GetOrCreateItem("HoundFang", "들개 이빨", "날카로운 송곳니. 무기 강화에 쓴다.", 1);
            var scale = GetOrCreateItem("RavagerScale", "철갑 비늘", "Ravager의 단단한 비늘. 방어구 소재.", 2);
            var hide = GetOrCreateItem("RavagerHide", "철갑 가죽", "질기고 무거운 가죽.", 2);
            var gem = GetOrCreateItem("RavagerGem", "철갑의 보옥", "몸속에서 드물게 자라는 보석. 아주 귀하다.", 6);
            var fang = GetOrCreateItem("RavagerFang", "강철 엄니", "머리를 파괴해야 얻을 수 있는 엄니.", 3);
            var spike = GetOrCreateItem("RavagerTailSpike", "꼬리 가시", "꼬리를 파괴해야 얻을 수 있는 가시.", 3);
            var (wScale, wMembrane, wHorn, wCore) = WyrmItems();

            var quests = new List<HuntQuest>
            {
                new()
                {
                    id = "hunt_hounds", title = "잿빛 들개 사냥", arenaName = "투기장",
                    description = "투기장에 풀린 잿빛 들개 두 마리를 사냥하라.",
                    arenaSpawn = arenaSpawn, waves = new() { q1Wave }, maxFaints = 3, parTime = 90f,
                    rewards = new()
                    {
                        new() { item = fur, min = 2, max = 3 },
                        new() { item = tooth, min = 1, max = 2 },
                    },
                },
                new()
                {
                    id = "hunt_ravager", title = "철갑의 포식자", arenaName = "투기장",
                    description = "Ravager, the Iron Maw를 토벌하라.",
                    arenaSpawn = arenaSpawn, waves = new() { q2Wave }, maxFaints = 3, parTime = 240f,
                    rewards = new()
                    {
                        new() { item = scale, min = 2, max = 3 },
                        new() { item = hide, min = 1, max = 2 },
                        new() { item = gem, min = 1, max = 1, chance = 0.3f },
                    },
                },
                new()
                {
                    id = "rush_colosseum", title = "투기장의 시련 (보스 러시)", arenaName = "투기장",
                    description = "쉬지 않고 이어지는 세 번의 싸움을 버텨라.",
                    arenaSpawn = arenaSpawn, waves = new() { rush1, rush2, rush3 }, maxFaints = 3, parTime = 600f,
                    rewards = new()
                    {
                        new() { item = scale, min = 4, max = 5 },
                        new() { item = hide, min = 3, max = 3 },
                        new() { item = fang, min = 1, max = 2 },
                        new() { item = spike, min = 1, max = 1 },
                        new() { item = gem, min = 1, max = 1, chance = 0.6f },
                        new() { item = wScale, min = 2, max = 3 },
                        new() { item = wCore, min = 1, max = 1, chance = 0.5f },
                    },
                },
                new()
                {
                    id = "hunt_wyrm", title = "잿불의 비룡", arenaName = "투기장",
                    description = "거리를 벌리고 불을 뿜는 Cinder Wyrm을 토벌하라. 파고들어라.",
                    arenaSpawn = arenaSpawn, waves = new() { q4Wave }, maxFaints = 3, parTime = 270f,
                    rewards = new()
                    {
                        new() { item = wScale, min = 2, max = 3 },
                        new() { item = wMembrane, min = 1, max = 1 },
                        new() { item = wCore, min = 1, max = 1, chance = 0.3f },
                    },
                },
            };

            // 게시판 순서: 들개 → Ravager → 비룡 → 보스 러시
            var wyrmQuest = quests[3];
            quests.RemoveAt(3);
            quests.Insert(2, wyrmQuest);

            var managerGo = Create("Hunt Manager");
            var hubSpawn = new GameObject("Hub Spawn");
            hubSpawn.transform.SetParent(managerGo.transform, false);
            hubSpawn.transform.position = HubSpawn;
            var manager = managerGo.AddComponent<HuntManager>();
            manager.EditorSetup(hubSpawn.transform, quests);
            EditorUtility.SetDirty(manager);
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
            var (wScale, wMembrane, wHorn, wCore) = WyrmItems();

            static UpgradableEquipment.UpgradeStep Step(float bonus, params (ItemData item, int amount)[] costs)
            {
                var step = new UpgradableEquipment.UpgradeStep { bonus = bonus };
                foreach (var (item, amount) in costs) step.costs.Add(new UpgradableEquipment.Cost { item = item, amount = amount });
                return step;
            }

            // 무기: 피해 +15% / +15% / +20% / +25%
            var weapon = player.AddComponent<PlayerWeapon>();
            weapon.EditorSetup("무기", new List<UpgradableEquipment.UpgradeStep>
            {
                Step(0.15f, (fur, 2), (tooth, 2)),
                Step(0.15f, (scale, 3), (hide, 1), (tooth, 2)),
                Step(0.20f, (scale, 4), (fang, 1), (hide, 2)),
                Step(0.25f, (fang, 2), (spike, 2), (gem, 1)),
                Step(0.25f, (wHorn, 1), (wScale, 4), (wCore, 1)),
            });
            // 무기는 한손검 하나만 사용 (다른 무기 에셋은 남겨둠)
            var sword = WeaponPresets.GetOrCreateAll()[0];
            weapon.EditorSetupWeapons(new List<WeaponData> { sword }, sword);
            EditorUtility.SetDirty(weapon);
            player.AddComponent<PlayerWeaponSpecial>();
            player.AddComponent<PlayerWeaponLoadout>();

            // 방어구: 받는 피해 -8% / -8% / -10% / -12%
            var armor = player.AddComponent<PlayerArmor>();
            armor.EditorSetup("헌터의 가죽갑옷", new List<UpgradableEquipment.UpgradeStep>
            {
                Step(0.08f, (fur, 3)),
                Step(0.08f, (fur, 4), (hide, 2)),
                Step(0.10f, (scale, 4), (hide, 3)),
                Step(0.12f, (scale, 5), (spike, 1), (gem, 1)),
                Step(0.12f, (wMembrane, 2), (wScale, 4), (wCore, 1)),
            });
            EditorUtility.SetDirty(armor);

            var save = player.AddComponent<SaveSystem>();
            save.EditorSetup(new List<ItemData> { scale, hide, gem, fang, spike, fur, tooth, wScale, wMembrane, wHorn, wCore });
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

        static (ItemData scale, ItemData membrane, ItemData horn, ItemData core) WyrmItems() => (
            GetOrCreateItem("WyrmScale", "잿불 비늘", "뜨겁게 달아오른 비늘. 식어도 온기가 남아 있다.", 3),
            GetOrCreateItem("WyrmMembrane", "잿불 비막", "날개를 파괴해야 얻을 수 있는 얇고 질긴 막.", 4),
            GetOrCreateItem("WyrmHorn", "잿불 뿔", "머리를 파괴해야 얻을 수 있는 불타는 뿔.", 4),
            GetOrCreateItem("WyrmCore", "화염 주머니", "비룡의 불길이 시작되는 기관. 아주 귀하다.", 6));

        /// <summary>
        /// 두 번째 보스 'Cinder Wyrm'(잿불 비룡). 거리를 벌리고 화염구·낙하 화염을 쓰는 원거리형.
        /// 가까이 붙으면 꼬리 휘두르기로 떨쳐내고 뒤로 물러난다.
        /// 부위: 머리(약점·파괴), 날개 2(파괴), 꼬리(파괴), 다리(단단함), 몸통.
        /// </summary>
        static Health BuildWyrm(Transform parent, Vector3 pos, Material weakMat)
        {
            var wyrm = Create("Cinder Wyrm");
            wyrm.transform.SetParent(parent, false);
            wyrm.transform.localPosition = pos;
            wyrm.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            var health = wyrm.AddComponent<Health>();
            SetString(health, "displayName", "Cinder Wyrm");
            SetFloat(health, "maxHealth", 1000f);
            SetFloat(health, "maxPoise", 100f);

            var bodyMat = MakeMaterial(new Color(0.22f, 0.2f, 0.22f));
            var wingMat = MakeMaterial(new Color(0.35f, 0.15f, 0.1f));
            Part(wyrm.transform, PrimitiveType.Capsule, "Body", new Vector3(0f, 2.2f, 0f), new Vector3(80f, 0f, 0f), new Vector3(1.6f, 1.8f, 1.6f), bodyMat, "Body", 1f, 0f);
            var neck = Visual(PrimitiveType.Capsule, "Neck", wyrm.transform, new Vector3(0f, 3.1f, 1.6f), new Vector3(0.6f, 0.9f, 0.6f), bodyMat);
            neck.transform.localRotation = Quaternion.Euler(40f, 0f, 0f);
            Part(wyrm.transform, PrimitiveType.Sphere, "Head (약점)", new Vector3(0f, 3.6f, 2.5f), Vector3.zero, new Vector3(0.9f, 0.8f, 1.2f), weakMat, "Head", 1.5f, 180f);
            Part(wyrm.transform, PrimitiveType.Cube, "Wing L", new Vector3(-2.3f, 3f, 0f), new Vector3(0f, 0f, 15f), new Vector3(3.4f, 0.12f, 2f), wingMat, "Wing", 1.2f, 120f);
            Part(wyrm.transform, PrimitiveType.Cube, "Wing R", new Vector3(2.3f, 3f, 0f), new Vector3(0f, 0f, -15f), new Vector3(3.4f, 0.12f, 2f), wingMat, "Wing", 1.2f, 120f);
            Part(wyrm.transform, PrimitiveType.Capsule, "Tail", new Vector3(0f, 1.6f, -2.8f), new Vector3(70f, 0f, 0f), new Vector3(0.5f, 1.5f, 0.5f), bodyMat, "Tail", 1.1f, 140f);
            foreach (var x in new[] { -0.6f, 0.6f })
                Part(wyrm.transform, PrimitiveType.Cube, "Leg", new Vector3(x, 0.7f, 0.2f), Vector3.zero, new Vector3(0.45f, 1.4f, 0.45f), bodyMat, "Leg", 0.8f, 0f);

            var eyeMat = MakeMaterial(Color.black);
            eyeMat.EnableKeyword("_EMISSION");
            var eyeL = Visual(PrimitiveType.Sphere, "Eye L", wyrm.transform, new Vector3(-0.25f, 3.8f, 3.0f), Vector3.one * 0.16f, eyeMat);
            var eyeR = Visual(PrimitiveType.Sphere, "Eye R", wyrm.transform, new Vector3(0.25f, 3.8f, 3.0f), Vector3.one * 0.16f, eyeMat);

            var lockOn = wyrm.AddComponent<LockOnTarget>();
            SetVector(lockOn, "offset", new Vector3(0f, 2.6f, 0.8f));
            var dummy = wyrm.AddComponent<TrainingDummy>();
            SetFloat(dummy, "respawnTime", 6f);

            var agent = wyrm.AddComponent<UnityEngine.AI.NavMeshAgent>();
            agent.radius = 1.3f;
            agent.height = 3.5f;
            agent.baseOffset = 0f;
            agent.acceleration = 20f;
            agent.angularSpeed = 0f;
            agent.enabled = false;

            var (wScale, wMembrane, wHorn, wCore) = WyrmItems();
            var rewards = wyrm.AddComponent<MonsterRewards>();
            rewards.EditorSetup(
                new List<MonsterRewards.Reward>
                {
                    new() { item = wScale, min = 2, max = 3 },
                    new() { item = wCore, min = 1, max = 1, chance = 0.12f },
                },
                new List<MonsterRewards.PartReward>
                {
                    new() { partName = "Head", rewards = new() { new() { item = wHorn, min = 1, max = 1 } } },
                    new() { partName = "Wing", rewards = new() { new() { item = wMembrane, min = 1, max = 1 } } },
                    new() { partName = "Tail", rewards = new() { new() { item = wScale, min = 1, max = 2 } } },
                });
            EditorUtility.SetDirty(rewards);

            EnemyAttack Ranged(EnemyAttack a, EnemyAttack.Kind kind, int count = 1, float spread = 0f, float speed = 18f,
                               float radius = 0.55f, bool enragedOnly = false, float meteorRadius = 2.5f, float scatter = 6f)
            {
                a.kind = kind;
                a.projectileCount = count;
                a.spreadAngle = spread;
                a.projectileSpeed = speed;
                a.projectileRadius = radius;
                a.muzzleOffset = new Vector3(0f, 3.4f, 3.2f);
                a.meteorRadius = meteorRadius;
                a.meteorScatter = scatter;
                a.enragedOnly = enragedOnly;
                return a;
            }

            var ai = wyrm.AddComponent<EnemyAI>();
            ai.EditorSetBoss(true);
            ai.EditorSetup(new List<EnemyAttack>
            {
                //   이름          최소/최대거리  각도  가중치 쿨   피해 강인  예고   판정   빈틈  추적  전진 전진구간      판정중심           판정크기
                new("Tail Whip",   0f, 4.5f,  360f, 3f,   3f, 22f, 25f, 0.55f, 0.25f, 0.9f, 0f,   0f, Vector2.zero, new(0f, 1f, 0f), new(7f, 2f, 7f)),
                Ranged(new("Fireball",  3f, 28f, 35f, 3f,   0f, 28f, 20f, 0.85f, 0.1f, 0.7f, 200f, 0f, Vector2.zero, Vector3.zero, Vector3.one), EnemyAttack.Kind.Projectile, speed: 20f),
                Ranged(new("Triple Fire", 3f, 22f, 35f, 2f, 4f, 22f, 15f, 1.0f, 0.1f, 0.9f, 160f, 0f, Vector2.zero, Vector3.zero, Vector3.one), EnemyAttack.Kind.Projectile, 3, 32f, 17f),
                Ranged(new("Meteor Rain", 0f, 30f, 360f, 1.5f, 10f, 32f, 30f, 1.1f, 0.1f, 1.2f, 90f, 0f, Vector2.zero, Vector3.zero, Vector3.one), EnemyAttack.Kind.Meteor, 5, meteorRadius: 2.5f, scatter: 6f),
                // 분노 전용
                Ranged(new("Inferno",    3f, 24f, 40f, 2.5f, 6f, 22f, 15f, 1.1f, 0.1f, 1.0f, 140f, 0f, Vector2.zero, Vector3.zero, Vector3.one), EnemyAttack.Kind.Projectile, 7, 75f, 15f, enragedOnly: true),
                Ranged(new("Firestorm",  0f, 30f, 360f, 2f, 12f, 32f, 30f, 1.3f, 0.1f, 1.4f, 90f, 0f, Vector2.zero, Vector3.zero, Vector3.one), EnemyAttack.Kind.Meteor, 10, meteorRadius: 2.2f, scatter: 10f, enragedOnly: true),
            }, speed: 4.5f, engage: 3f, aggro: 30f, staggerTime: 1.6f, enrageAt: 0.5f, keepRange: 11f, projectile: GetOrCreateProjectileMaterial());
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

        const string ProjectileMaterialPath = "Assets/_Project/Materials/EnemyFire.mat";

        /// <summary>화염구·장판 폭발용 발광 머티리얼 (에셋으로 저장해서 빌드에도 포함)</summary>
        static Material GetOrCreateProjectileMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ProjectileMaterialPath);
            if (mat) return mat;
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Materials"))
                AssetDatabase.CreateFolder("Assets/_Project", "Materials");
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");
            mat = EnemyProjectile.CreateGlowMaterial(shader, new Color(1f, 0.45f, 0.1f), 4f);
            mat.name = "EnemyFire";
            AssetDatabase.CreateAsset(mat, ProjectileMaterialPath);
            AssetDatabase.SaveAssets();
            return mat;
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
