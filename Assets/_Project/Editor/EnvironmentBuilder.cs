using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Unity.AI.Navigation;
using SoulsHunter.Core;
using SoulsHunter.World;

namespace SoulsHunter.EditorTools
{
    /// <summary>사냥터 안의 구역 위치 (월드 좌표, 바닥 높이 포함)</summary>
    public class FieldLayout
    {
        public Transform campSpawn;
        public Vector3 glade, gorge, basin;
    }

    /// <summary>
    /// 에셋스토어 환경 팩으로 사냥터와 마을(안전구역)을 만든다. Build Test Arena가 부른다.
    /// - 사냥터 '잿빛 숲': 지형(언덕·절벽·길) + 베이스캠프 / 숲속 공터(들개) / 바위 협곡(Ravager) / 잿불 분지(비룡)
    /// - 마을: 지형 + 중세 집들 + 시설 소품 + 숲
    /// 팩이 없으면 아무것도 하지 않고 false/null을 돌려준다 (예전 도형 맵을 쓴다).
    /// </summary>
    public static class EnvironmentBuilder
    {
        public static readonly Vector3 FieldOrigin = new(1000f, 0f, 0f);
        const float FieldSize = 240f;
        const float FieldHeight = 60f;
        const float VillageSize = 150f;
        const float VillageHeight = 30f;
        const string DataFolder = "Assets/_Project/Data/Environment";

        // ───────── 에셋 ─────────

        const string UNS = "Assets/InnerverseInteractive/Ultimate Nature – Starter/Environment/";
        const string MT = "Assets/Mountain Terrain rocks and tree/";
        const string MV = "Assets/MyDreamGameStudio/Medieval Village Pack Vol.1/Prefabs/URP/";
        const string SKY = "Assets/Fantasy Skybox FREE/";
        const string TerrainTex = SKY + "Scenes/Textures (Terrain)/";

        static GameObject P(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);
        static GameObject[] Many(params string[] paths) => paths.Select(P).Where(p => p).ToArray();

        static GameObject[] Spruces => Many(UNS + "Trees/Fir/Prefabs/UNS_Spruce_01.prefab", UNS + "Trees/Fir/Prefabs/UNS_Spruce_02.prefab");
        static GameObject[] ForestRocks => Many(Enumerable.Range(1, 5).Select(i => $"{UNS}Rocks/Forest/Prefabs/UNS_Standard_Rock_0{i}.prefab").ToArray());
        static GameObject[] TinyRocks => Many(Enumerable.Range(1, 5).Select(i => $"{UNS}Rocks/River/Prefabs/UNS_Tiny_Rock_0{i}.prefab").ToArray());
        static GameObject[] Cliffs => Many(Enumerable.Range(1, 5).Select(i => $"{UNS}Rocks/Cliffs/Prefabs/UNS_Rock_Cliff_0{i}.prefab").ToArray());
        static GameObject[] RockSets => Many(Enumerable.Range(1, 4).Select(i => $"{MT}Prefab/rock_set_0{i}.prefab").ToArray());
        static GameObject Mountain => P(UNS + "Rocks/Mountain/Prefabs/UNS_Mountain.prefab");
        static GameObject[] Bushes => Many(UNS + "Vegetation/Bushes/Prefabs/UNS_Bush.prefab");
        static GameObject[] Logs => Many(UNS + "Props/Logs/Prefabs/UNS_Log.prefab", UNS + "Props/Logs/Prefabs/UNS_Stump.prefab", UNS + "Props/Branches/Prefabs/UNS_Branch.prefab");
        static GameObject Mushrooms => P(UNS + "Vegetation/Mushrooms/Prefabs/UNS_Mushroom_Patch.prefab");
        static GameObject V(string name) => P($"{MV}{name}_URP.prefab");

        static Material Sky(string path) => AssetDatabase.LoadAssetAtPath<Material>(SKY + path);

        public static bool HasNature => Spruces.Length > 0 && TerrainLayers() != null;
        public static bool HasVillage => V("House_1") != null;

        static TerrainLayer[] TerrainLayers()
        {
            var grass = AssetDatabase.LoadAssetAtPath<TerrainLayer>(TerrainTex + "TerrainGrass.terrainlayer");
            var dirt = AssetDatabase.LoadAssetAtPath<TerrainLayer>(TerrainTex + "TerrainDirt.terrainlayer");
            var rock = AssetDatabase.LoadAssetAtPath<TerrainLayer>(TerrainTex + "TerrainRock.terrainlayer");
            if (!grass) grass = AssetDatabase.LoadAssetAtPath<TerrainLayer>(UNS + "Terrain/Layers/UNS_Grass.terrainlayer");
            if (!dirt) dirt = AssetDatabase.LoadAssetAtPath<TerrainLayer>(UNS + "Terrain/Layers/UNS_Dirt.terrainlayer");
            if (!rock) rock = dirt;
            return grass && dirt ? new[] { grass, dirt, rock } : null;
        }

        static Texture2D GrassTex(int i) => AssetDatabase.LoadAssetAtPath<Texture2D>(TerrainTex + (i == 0 ? "Brush_Grass_01.png" : i == 1 ? "Brush_Grass_02.png" : "Brush_Flower.png"));

        // ───────── 사냥터 ─────────

        class Zone
        {
            public string name;
            public Vector2 c;        // 지형 로컬 좌표 (가운데 0,0)
            public float r, floor, dirt;
            public float wall;       // 둘레 벽 높이 (0이면 없음)
            public Vector2 entrance; // 벽이 트인 방향 (중심 기준)
        }

        static readonly Zone Camp = new() { name = "베이스캠프", c = new(0f, -88f), r = 11f, floor = 6f, dirt = 0.7f };
        static readonly Zone Glade = new() { name = "숲속 공터", c = new(0f, -24f), r = 21f, floor = 5f, dirt = 0.3f };
        static readonly Zone Gorge = new() { name = "바위 협곡", c = new(52f, 50f), r = 21f, floor = 4f, dirt = 0.65f, wall = 16f };
        static readonly Zone Basin = new() { name = "잿불 분지", c = new(-52f, 54f), r = 25f, floor = 3f, dirt = 1f, wall = 7f };
        static Zone[] FieldZones => new[] { Camp, Glade, Gorge, Basin };
        static (Zone a, Zone b)[] FieldPaths => new[] { (Camp, Glade), (Glade, Gorge), (Glade, Basin) };

        /// <summary>사냥터를 만든다. 팩이 없으면 null.</summary>
        public static FieldLayout BuildField(System.Func<string, GameObject> create)
        {
            if (!HasNature) return null;
            Gorge.entrance = (Glade.c - Gorge.c).normalized;
            Basin.entrance = (Glade.c - Basin.c).normalized;

            var root = create("Hunting Field");
            root.transform.position = FieldOrigin;
            var rng = new System.Random(7);

            float Height(float x, float z)
            {
                float n = Fbm(x * 0.018f + 31f, z * 0.018f + 17f);
                float h = 7f + n * 11f;
                float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
                if (edge > 86f) h += Mathf.Min(42f, (edge - 86f) * (edge - 86f) * 0.09f);

                // 길: 양 끝 구역 높이 사이로 부드럽게
                foreach (var (a, b) in FieldPaths)
                {
                    float d = DistToSegment(new Vector2(x, z), a.c, b.c, out float t);
                    float w = Smooth(8f, 3.5f, d);
                    if (w > 0f) h = Mathf.Lerp(h, Mathf.Lerp(a.floor, b.floor, t) + n * 1.2f, w);
                }

                foreach (var zn in FieldZones)
                {
                    float d = Vector2.Distance(new Vector2(x, z), zn.c);
                    if (zn.wall > 0f)
                    {
                        var dir = (new Vector2(x, z) - zn.c).normalized;
                        float ang = Vector2.Angle(dir, zn.entrance);
                        float gap = Smooth(18f, 30f, ang);
                        float ring = Smooth(zn.r + 2f, zn.r + 9f, d) * Smooth(zn.r + 26f, zn.r + 14f, d);
                        h = Mathf.Max(h, zn.floor + zn.wall * ring * gap);
                    }
                    h = Mathf.Lerp(h, zn.floor, Smooth(zn.r + 4f, zn.r - 1f, d));
                }
                return h;
            }

            var td = MakeTerrainData("HuntingFieldTerrain", FieldSize, FieldHeight, Height);
            PaintTerrain(td, FieldSize, (x, z) =>
            {
                float dirt = 0f;
                foreach (var (a, b) in FieldPaths)
                    dirt = Mathf.Max(dirt, Smooth(5.5f, 2.5f, DistToSegment(new Vector2(x, z), a.c, b.c, out _)) * 0.9f);
                foreach (var zn in FieldZones)
                    dirt = Mathf.Max(dirt, Smooth(zn.r + 3f, zn.r - 4f, Vector2.Distance(new Vector2(x, z), zn.c)) * zn.dirt);
                return dirt;
            });

            var terrain = CreateTerrain(root.transform, td, FieldOrigin - new Vector3(FieldSize * 0.5f, 0f, FieldSize * 0.5f));
            Vector3 W(Vector2 local) => FieldOrigin + new Vector3(local.x, 0f, local.y);
            Vector3 G(Vector2 local) => Ground(terrain, W(local));

            bool Clear(Vector2 p, float margin)
            {
                foreach (var zn in FieldZones) if (Vector2.Distance(p, zn.c) < zn.r + margin + (zn.wall > 0f ? 12f : 0f)) return false;
                foreach (var (a, b) in FieldPaths) if (DistToSegment(p, a.c, b.c, out _) < 5f + margin) return false;
                return true;
            }

            // 나무 (지형 나무: 가볍고 충돌 포함)
            ScatterTrees(td, terrain, Spruces, rng, 650, FieldSize, (x, z) =>
            {
                var p = new Vector2(x, z);
                if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) > 112f || !Clear(p, 3f)) return 0f;
                float cluster = Mathf.PerlinNoise(x * 0.04f + 5f, z * 0.04f + 9f);
                return cluster > 0.38f ? 1f : 0.25f;
            });

            var props = new GameObject("Props").transform;
            props.SetParent(root.transform, false);

            // 숲 바위·덤불·통나무
            for (int i = 0; i < 70; i++)
            {
                var p = RandomPoint(rng, 105f);
                if (!Clear(p, 1f)) continue;
                Place(Pick(ForestRocks, rng), props, G(p) - Vector3.up * 0.3f, Rand(rng, 0f, 360f), Rand(rng, 0.8f, 1.8f));
            }
            for (int i = 0; i < 60; i++)
            {
                var p = RandomPoint(rng, 105f);
                if (!Clear(p, 0f)) continue;
                Place(Pick(Bushes, rng), props, G(p), Rand(rng, 0f, 360f), Rand(rng, 0.8f, 1.4f));
            }
            // 길가 작은 돌
            foreach (var (a, b) in FieldPaths)
                for (int i = 0; i < 14; i++)
                {
                    float t = Rand(rng, 0.1f, 0.9f);
                    var along = Vector2.Lerp(a.c, b.c, t);
                    var side = new Vector2(-(b.c - a.c).y, (b.c - a.c).x).normalized * (rng.Next(2) == 0 ? -1f : 1f) * Rand(rng, 4.5f, 6.5f);
                    Place(Pick(TinyRocks, rng), props, G(along + side), Rand(rng, 0f, 360f), Rand(rng, 0.8f, 1.6f));
                }

            // 베이스캠프: 수레·통·탁자·모닥불
            var camp = new GameObject("Base Camp").transform;
            camp.SetParent(root.transform, false);
            camp.position = G(Camp.c);
            PlaceVillageProp("Wooden_Cart", camp, G(Camp.c + new Vector2(-6f, 2f)), 70f);
            PlaceVillageProp("Wooden_Barrel", camp, G(Camp.c + new Vector2(-5f, -2.5f)), 0f);
            PlaceVillageProp("Wooden_Barrel", camp, G(Camp.c + new Vector2(-4f, -3.6f)), 40f);
            PlaceVillageProp("Wooden_Table", camp, G(Camp.c + new Vector2(5.5f, 1f)), -90f);
            PlaceVillageProp("Pot_1", camp, G(Camp.c + new Vector2(5.5f, 3.2f)), 0f);
            PlaceVillageProp("Weapon_Stand", camp, G(Camp.c + new Vector2(6f, -3f)), -100f);
            Campfire(camp, G(Camp.c + new Vector2(0f, -3f)), rng);

            var spawn = new GameObject("Camp Spawn").transform;
            spawn.SetParent(camp, true);
            spawn.position = G(Camp.c + new Vector2(0f, 2f)) + Vector3.up * 0.1f;
            spawn.rotation = Quaternion.LookRotation(W(Glade.c) - W(Camp.c));

            // 숲속 공터: 쓰러진 통나무·버섯
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f + Rand(rng, -15f, 15f);
                var p = Glade.c + (Vector2)(Quaternion.Euler(0f, 0f, a) * Vector2.up) * (Glade.r - 2f);
                if (DistToSegment(p, Camp.c, Glade.c, out _) < 7f || DistToSegment(p, Glade.c, Gorge.c, out _) < 7f || DistToSegment(p, Glade.c, Basin.c, out _) < 7f) continue;
                Place(Pick(Logs, rng), props, G(p), Rand(rng, 0f, 360f), 1f);
                if (Mushrooms) Place(Mushrooms, props, G(p + new Vector2(1.5f, 1f)), Rand(rng, 0f, 360f), 1f);
            }

            // 바위 협곡: 둘레 절벽 + 안쪽 엄폐 바위
            RingOfCliffs(Gorge, props, G, rng, 14);
            for (int i = 0; i < 3; i++)
            {
                var p = Gorge.c + (Vector2)(Quaternion.Euler(0f, 0f, i * 120f + 30f) * Vector2.up) * 10f;
                Place(Pick(RockSets, rng), props, G(p) - Vector3.up * 0.4f, Rand(rng, 0f, 360f), 1f);
            }

            // 잿불 분지: 낮은 바위 테두리 + 검은 바위 + 불씨
            RingOfCliffs(Basin, props, G, rng, 10, 0.7f);
            for (int i = 0; i < 4; i++)
            {
                var p = Basin.c + (Vector2)(Quaternion.Euler(0f, 0f, i * 90f + 45f) * Vector2.up) * Rand(rng, 12f, 17f);
                Place(Pick(RockSets, rng), props, G(p) - Vector3.up * 0.4f, Rand(rng, 0f, 360f), 1.2f);
            }
            Embers(props, Basin, G, rng);

            // 멀리 보이는 산
            if (Mountain)
                foreach (var corner in new[] { new Vector2(-170f, 160f), new Vector2(170f, 170f), new Vector2(190f, -60f), new Vector2(-190f, -40f), new Vector2(0f, 210f) })
                {
                    var m = Place(Mountain, props, W(corner) - Vector3.up * 5f, Rand(rng, 0f, 360f), 1f);
                    FitSize(m, Rand(rng, 140f, 190f));
                }

            // 내비메시 (실행할 때 굽는다)
            var surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            root.AddComponent<BakeNavMeshOnAwake>();

            var atmo = root.AddComponent<AreaAtmosphere>();
            atmo.EditorSetup("잿빛 숲", 260f, Sky("Panoramics/FS013/FS013_Day.mat"), new Color(0.62f, 0.68f, 0.72f), 50f, 260f,
                             new Color(0.5f, 0.55f, 0.6f), new Vector2(38f, -40f), new Color(1f, 0.93f, 0.82f), 1.25f);

            return new FieldLayout { campSpawn = spawn, glade = G(Glade.c), gorge = G(Gorge.c), basin = G(Basin.c) };
        }

        static void RingOfCliffs(Zone zn, Transform parent, System.Func<Vector2, Vector3> ground, System.Random rng, int count, float scale = 1f)
        {
            var cliffs = Cliffs;
            if (cliffs.Length == 0) return;
            for (int i = 0; i < count; i++)
            {
                float a = i * 360f / count + Rand(rng, -6f, 6f);
                var dir = (Vector2)(Quaternion.Euler(0f, 0f, a) * Vector2.up);
                if (Vector2.Angle(dir, zn.entrance) < 32f) continue; // 입구는 비운다
                var p = zn.c + dir * (zn.r + Rand(rng, 4f, 7f));
                var go = Place(Pick(cliffs, rng), parent, ground(p) - Vector3.up * 1.5f, 0f, scale * Rand(rng, 0.9f, 1.3f));
                // 절벽 면이 구역 안쪽을 보게
                var inward = new Vector3(-dir.x, 0f, -dir.y);
                go.transform.rotation = Quaternion.LookRotation(inward) * Quaternion.Euler(0f, Rand(rng, -20f, 20f), 0f) * go.transform.rotation;
            }
        }

        static void Campfire(Transform parent, Vector3 pos, System.Random rng)
        {
            var fire = new GameObject("Campfire").transform;
            fire.SetParent(parent, true);
            fire.position = pos;
            var logs = Logs;
            for (int i = 0; i < 3 && logs.Length > 0; i++)
            {
                var l = Place(logs[0], fire, pos + Quaternion.Euler(0f, i * 120f, 0f) * new Vector3(0f, 0.1f, 0.4f), i * 120f + 90f, 0.5f);
                foreach (var c in l.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            }
            var flameMat = Glow(new Color(1f, 0.5f, 0.15f), 4f);
            var flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(flame.GetComponent<Collider>());
            flame.name = "Flame";
            flame.transform.SetParent(fire, false);
            flame.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            flame.transform.localScale = new Vector3(0.5f, 0.7f, 0.5f);
            flame.GetComponent<Renderer>().sharedMaterial = flameMat;
            var lightGo = new GameObject("Fire Light");
            lightGo.transform.SetParent(fire, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.3f);
            light.range = 10f;
            light.intensity = 2.5f;
        }

        static void Embers(Transform parent, Zone zn, System.Func<Vector2, Vector3> ground, System.Random rng)
        {
            var mat = Glow(new Color(1f, 0.35f, 0.08f), 3f);
            var root = new GameObject("Embers").transform;
            root.SetParent(parent, false);
            for (int i = 0; i < 14; i++)
            {
                var p = zn.c + RandomPoint(rng, zn.r - 3f) * 0.9f;
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.name = "Ember";
                go.transform.SetParent(root, false);
                go.transform.position = ground(p) + Vector3.up * 0.02f;
                go.transform.localScale = new Vector3(Rand(rng, 0.6f, 1.6f), 0.05f, Rand(rng, 0.6f, 1.6f));
                go.GetComponent<Renderer>().sharedMaterial = mat;
            }
            for (int i = 0; i < 3; i++)
            {
                var lightGo = new GameObject("Ember Light");
                lightGo.transform.SetParent(root, false);
                lightGo.transform.position = ground(zn.c + RandomPoint(rng, zn.r * 0.6f)) + Vector3.up * 1.5f;
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.45f, 0.15f);
                light.range = 14f;
                light.intensity = 1.6f;
            }
        }

        // ───────── 마을 (안전구역) ─────────

        /// <summary>
        /// 안전구역을 마을로 꾸민다: 지형 + 집 + 시설 소품 + 숲 + 보이지 않는 경계.
        /// 팩이 없으면 false (예전 울타리 맵을 쓴다).
        /// </summary>
        public static bool BuildVillage(System.Func<string, GameObject> create)
        {
            if (!HasNature || !HasVillage) return false;
            var root = create("Village");
            var rng = new System.Random(11);

            float Height(float x, float z)
            {
                float d = new Vector2(x, z).magnitude;
                float n = Fbm(x * 0.03f + 3f, z * 0.03f + 8f);
                float hills = Smooth(30f, 62f, d) * (6f + n * 14f);
                return hills;
            }

            var td = MakeTerrainData("VillageTerrain", VillageSize, VillageHeight, Height);
            PaintTerrain(td, VillageSize, (x, z) =>
            {
                var p = new Vector2(x, z);
                float plaza = Smooth(10f, 7f, Vector2.Distance(p, new Vector2(0f, 0f)));
                float road = Smooth(3.2f, 1.8f, DistToSegment(p, new Vector2(0f, -6f), new Vector2(0f, 16f), out _));
                float side = Mathf.Max(
                    Smooth(2.2f, 1.2f, DistToSegment(p, new Vector2(0f, 0f), new Vector2(6f, -8f), out _)),
                    Smooth(2.2f, 1.2f, DistToSegment(p, new Vector2(0f, 0f), new Vector2(-6f, -8f), out _)));
                float ring = Smooth(2.4f, 1.2f, Mathf.Abs(p.magnitude - 15f)) * 0.6f;
                return Mathf.Max(plaza * 0.85f, road, side * 0.8f, ring);
            });
            var terrain = CreateTerrain(root.transform, td, new Vector3(-VillageSize * 0.5f, 0f, -VillageSize * 0.5f));
            Vector3 G(Vector2 p) => Ground(terrain, new Vector3(p.x, 0f, p.y));

            // 숲 (마을 바깥)
            ScatterTrees(td, terrain, Spruces, rng, 420, VillageSize, (x, z) =>
            {
                float d = new Vector2(x, z).magnitude;
                return d < 30f || Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) > 72f ? 0f : Smooth(30f, 40f, d);
            });

            var houses = new GameObject("Houses").transform;
            houses.SetParent(root.transform, false);
            // 북쪽(출발 관문) 쪽은 비우고 둥글게 집을 세운다
            var ring = new (float angle, string prefab)[]
            {
                (40f, "House_1"), (68f, "Blacksmith"), (100f, "House_3"), (130f, "House_4"), (158f, "House_5"),
                (180f, "Tavern"), (205f, "House_6"), (232f, "House_7"), (262f, "Animal_Shed"), (292f, "House_8"), (322f, "House_9"),
            };
            foreach (var (angle, name) in ring)
            {
                var dir = (Vector2)(Quaternion.Euler(0f, 0f, -angle) * Vector2.up);
                float radius = name == "Tavern" ? 25f : 23f;
                var p = dir * radius;
                var go = PlaceVillageProp(name, houses, G(p), 0f, collider: true);
                if (!go) continue;
                FitFootprint(go, 7f, 13f);
                // 정면이 광장을 보게
                go.transform.rotation = Quaternion.LookRotation(new Vector3(-dir.x, 0f, -dir.y)) * V(name).transform.rotation;
                go.transform.position = G(p);
            }
            var mill = PlaceVillageProp("WindMill", houses, G(new Vector2(-34f, 30f)), 140f, collider: true);
            if (mill) FitFootprint(mill, 8f, 14f);
            var wheel = PlaceVillageProp("Water_Wheel", houses, G(new Vector2(36f, 26f)), -130f, collider: true);
            if (wheel) FitFootprint(wheel, 5f, 10f);

            // 길가 소품
            var props = new GameObject("Props").transform;
            props.SetParent(root.transform, false);
            var clutter = new (string prefab, Vector2 pos, float yaw)[]
            {
                ("Wooden_Cart", new(-9f, 12f), 30f), ("Wooden_Barrel", new(-11.5f, 9f), 0f), ("Wooden_Barrel", new(-12.3f, 10.2f), 50f),
                ("Wooden_Bucket", new(10f, 10f), 0f), ("Wood_Tyre", new(11.4f, 11f), 70f), ("Wooden_Barrel", new(13f, -12f), 20f),
                ("Wooden_Spinning_Wheel", new(-13f, -12f), 150f), ("Stump_A", new(-8f, -1.5f), 0f), ("Wooden_Tub", new(-14f, -4f), 0f),
                ("Wooden_Table", new(-6.5f, 4.5f), 90f),
            };
            foreach (var (prefab, pos, yaw) in clutter) PlaceVillageProp(prefab, props, G(pos), yaw);

            // 등불
            var lampMat = Glow(new Color(1f, 0.75f, 0.4f), 3f);
            foreach (var p in new[] { new Vector2(-4f, -14f), new Vector2(4f, -14f), new Vector2(-3f, 9f), new Vector2(3f, 9f), new Vector2(-14f, 2f), new Vector2(14f, 2f) })
                Lantern(props, G(p), lampMat);

            // 보이지 않는 경계 (마을 밖으로 못 나가게)
            var bounds = new GameObject("Boundary").transform;
            bounds.SetParent(root.transform, false);
            const int segs = 28;
            const float rad = 29f;
            for (int i = 0; i < segs; i++)
            {
                float a = i * 360f / segs;
                var rot = Quaternion.Euler(0f, a, 0f);
                var wall = new GameObject("Wall");
                wall.transform.SetParent(bounds, false);
                wall.transform.SetPositionAndRotation(rot * new Vector3(0f, 3f, rad), rot);
                wall.AddComponent<BoxCollider>().size = new Vector3(2f * Mathf.PI * rad / segs + 0.5f, 8f, 1f);
            }

            var atmo = root.AddComponent<AreaAtmosphere>();
            atmo.EditorSetup("마을", 140f, Sky("Panoramics/FS002/FS002_Day.mat"), new Color(0.7f, 0.74f, 0.78f), 60f, 240f,
                             new Color(0.58f, 0.6f, 0.62f), new Vector2(42f, 30f), new Color(1f, 0.95f, 0.86f), 1.3f);
            return true;
        }

        /// <summary>투기장 분위기 (해질녘)</summary>
        public static void AddColosseumAtmosphere(GameObject colosseum)
        {
            var sky = Sky("Panoramics/FS003/FS003_Sunset.mat");
            if (!sky) return;
            var atmo = colosseum.AddComponent<AreaAtmosphere>();
            atmo.EditorSetup("투기장", 90f, sky, new Color(0.75f, 0.55f, 0.42f), 40f, 180f,
                             new Color(0.6f, 0.5f, 0.45f), new Vector2(18f, 60f), new Color(1f, 0.72f, 0.5f), 1.1f);
        }

        /// <summary>마을 시설(대장간·방어구 작업대·약재상)의 도형을 숨기고 진짜 소품을 둔다. 시설을 만든 뒤에 부른다.</summary>
        public static void DressHubFacilities()
        {
            var village = GameObject.Find("Village");
            if (!village) return;
            var props = new GameObject("Facility Props").transform;
            props.SetParent(village.transform, false);
            Vector3 G(Vector2 p) => new(p.x, 0f, p.y); // 마을 가운데는 평평 (높이 0)
            DressFacility("Forge", "Anvil", props, G, new[] { ("Anvil", new Vector2(0f, 0f), 90f), ("Blacksmith_Table", new Vector2(-1.8f, 1.6f), 0f), ("Hammer", new Vector2(1.9f, -1.6f), 30f), ("Smoker", new Vector2(-2f, -1.5f), 0f) });
            DressFacility("Armor Bench", "", props, G, new[] { ("Wooden_Table", new Vector2(0f, 0f), 0f), ("Weapon_Stand", new Vector2(-1.8f, 1.2f), 0f), ("Shield", new Vector2(2f, 1.4f), 0f), ("Sword", new Vector2(2.4f, 0.6f), 0f), ("Spear", new Vector2(-2.4f, -0.8f), 0f) });
            DressFacility("Apothecary", null, props, G, new[] { ("Pot_1", new Vector2(1.4f, -1.2f), 0f), ("Pot_2", new Vector2(-1.5f, -1.3f), 0f), ("Wooden_Bucket", new Vector2(1.9f, 0.4f), 0f) });
        }

        static void DressFacility(string rootName, string hidePrefix, Transform parent, System.Func<Vector2, Vector3> ground, (string prefab, Vector2 offset, float yaw)[] items)
        {
            var facility = GameObject.Find(rootName);
            if (!facility) return;
            // 도형 몸체는 숨기고(충돌은 유지) 진짜 소품을 둔다. 빛·발광 부품은 남긴다.
            foreach (var r in facility.GetComponentsInChildren<MeshRenderer>())
            {
                var mat = r.sharedMaterial;
                bool glows = mat && mat.IsKeywordEnabled("_EMISSION");
                if (hidePrefix != null && !glows && r.name.StartsWith(hidePrefix)) r.enabled = false;
            }
            var t = facility.transform;
            foreach (var (prefab, offset, yaw) in items)
            {
                Vector3 world = t.TransformPoint(new Vector3(offset.x, 0f, offset.y));
                var go = PlaceVillageProp(prefab, parent, ground(new Vector2(world.x, world.z)), t.eulerAngles.y + yaw);
                if (go && (prefab == "Anvil" || prefab == "Weapon_Stand" || prefab == "Blacksmith_Table" || prefab.StartsWith("Pot"))) FitFootprint(go, 0.6f, 2.2f);
            }
        }

        static void Lantern(Transform parent, Vector3 pos, Material lampMat)
        {
            var go = new GameObject("Lantern");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            pole.transform.SetParent(go.transform, false);
            pole.transform.localPosition = new Vector3(0f, 1.3f, 0f);
            pole.transform.localScale = new Vector3(0.12f, 1.3f, 0.12f);
            pole.GetComponent<Renderer>().sharedMaterial = Plain(new Color(0.25f, 0.2f, 0.16f));
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(lamp.GetComponent<Collider>());
            lamp.name = "Lamp";
            lamp.transform.SetParent(go.transform, false);
            lamp.transform.localPosition = new Vector3(0f, 2.7f, 0f);
            lamp.transform.localScale = Vector3.one * 0.32f;
            lamp.GetComponent<Renderer>().sharedMaterial = lampMat;
            var light = new GameObject("Lantern Light").AddComponent<Light>();
            light.transform.SetParent(go.transform, false);
            light.transform.localPosition = new Vector3(0f, 2.7f, 0f);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.78f, 0.5f);
            light.range = 9f;
            light.intensity = 1.6f;
        }

        // ───────── 지형 공통 ─────────

        static TerrainData MakeTerrainData(string name, float size, float height, System.Func<float, float, float> heightAt)
        {
            if (!AssetDatabase.IsValidFolder(DataFolder)) AssetDatabase.CreateFolder("Assets/_Project/Data", "Environment");
            string path = $"{DataFolder}/{name}.asset";
            AssetDatabase.DeleteAsset(path);

            var td = new TerrainData { heightmapResolution = 257 };
            td.size = new Vector3(size, height, size);
            int res = td.heightmapResolution;
            var h = new float[res, res];
            for (int zi = 0; zi < res; zi++)
                for (int xi = 0; xi < res; xi++)
                {
                    float x = -size * 0.5f + xi / (float)(res - 1) * size;
                    float z = -size * 0.5f + zi / (float)(res - 1) * size;
                    h[zi, xi] = Mathf.Clamp01(heightAt(x, z) / height);
                }
            td.SetHeights(0, 0, h);
            AssetDatabase.CreateAsset(td, path);
            return td;
        }

        /// <summary>잔디 / 흙(dirtAt) / 바위(가파른 곳) 칠하기 + 풀 디테일</summary>
        static void PaintTerrain(TerrainData td, float size, System.Func<float, float, float> dirtAt)
        {
            var layers = TerrainLayers();
            td.terrainLayers = layers;
            td.alphamapResolution = 256;
            int res = td.alphamapResolution;
            var map = new float[res, res, 3];
            var grassMask = new float[res, res];
            for (int zi = 0; zi < res; zi++)
                for (int xi = 0; xi < res; xi++)
                {
                    float nx = xi / (float)(res - 1), nz = zi / (float)(res - 1);
                    float x = -size * 0.5f + nx * size, z = -size * 0.5f + nz * size;
                    float slope = td.GetSteepness(nx, nz);
                    float rock = Smooth(28f, 42f, slope);
                    float dirt = Mathf.Clamp01(dirtAt(x, z) + (Mathf.PerlinNoise(x * 0.15f, z * 0.15f) - 0.5f) * 0.25f) * (1f - rock);
                    float grass = Mathf.Max(0f, 1f - rock - dirt);
                    map[zi, xi, 0] = grass;
                    map[zi, xi, 1] = dirt;
                    map[zi, xi, 2] = rock;
                    grassMask[zi, xi] = grass;
                }
            td.SetAlphamaps(0, 0, map);

            // 풀 디테일
            var protos = new List<DetailPrototype>();
            for (int i = 0; i < 3; i++)
            {
                var tex = GrassTex(i);
                if (!tex) continue;
                protos.Add(new DetailPrototype
                {
                    prototypeTexture = tex,
                    renderMode = DetailRenderMode.Grass,
                    usePrototypeMesh = false,
                    minWidth = i == 2 ? 0.4f : 0.7f, maxWidth = i == 2 ? 0.7f : 1.2f,
                    minHeight = i == 2 ? 0.35f : 0.5f, maxHeight = i == 2 ? 0.6f : 0.95f,
                    healthyColor = new Color(0.75f, 0.85f, 0.6f),
                    dryColor = new Color(0.75f, 0.7f, 0.45f),
                    noiseSpread = 0.3f,
                });
            }
            if (protos.Count == 0) return;
            td.SetDetailResolution(512, 16);
            td.detailPrototypes = protos.ToArray();
            int dres = td.detailResolution;
            var rng = new System.Random(3);
            for (int layer = 0; layer < protos.Count; layer++)
            {
                var d = new int[dres, dres];
                for (int zi = 0; zi < dres; zi++)
                    for (int xi = 0; xi < dres; xi++)
                    {
                        float g = grassMask[zi * res / dres, xi * res / dres];
                        if (g < 0.6f) continue;
                        bool flower = layer == 2;
                        if (flower ? rng.NextDouble() < 0.97 : rng.NextDouble() < 0.35) continue;
                        d[zi, xi] = flower ? 1 : rng.Next(1, 4);
                    }
                td.SetDetailLayer(0, 0, layer, d);
            }
            td.wavingGrassAmount = 0.3f;
            td.wavingGrassStrength = 0.4f;
            td.wavingGrassTint = new Color(0.8f, 0.85f, 0.7f);
        }

        static Terrain CreateTerrain(Transform parent, TerrainData td, Vector3 position)
        {
            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Terrain";
            go.transform.SetParent(parent, true);
            go.transform.position = position;
            var t = go.GetComponent<Terrain>();
            t.drawInstanced = true;
            t.treeDistance = 300f;
            t.treeBillboardDistance = 90f;
            t.detailObjectDistance = 70f;
            t.detailObjectDensity = 0.8f;
            t.heightmapPixelError = 4f;
            if (!t.materialTemplate)
            {
                var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
                if (shader)
                {
                    string matPath = $"{DataFolder}/TerrainLit.mat";
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (!mat)
                    {
                        mat = new Material(shader);
                        AssetDatabase.CreateAsset(mat, matPath);
                    }
                    t.materialTemplate = mat;
                }
            }
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.NavigationStatic);
            return t;
        }

        static void ScatterTrees(TerrainData td, Terrain terrain, GameObject[] prefabs, System.Random rng, int count, float size,
                                 System.Func<float, float, float> chance)
        {
            if (prefabs.Length == 0) return;
            td.treePrototypes = prefabs.Select(p => new TreePrototype { prefab = p }).ToArray();
            var trees = new List<TreeInstance>();
            var taken = new HashSet<(int, int)>();
            for (int tries = 0; tries < count * 8 && trees.Count < count; tries++)
            {
                float nx = (float)rng.NextDouble(), nz = (float)rng.NextDouble();
                float x = -size * 0.5f + nx * size, z = -size * 0.5f + nz * size;
                if (rng.NextDouble() > chance(x, z)) continue;
                if (td.GetSteepness(nx, nz) > 30f) continue;
                var cell = (Mathf.FloorToInt(x / 3.2f), Mathf.FloorToInt(z / 3.2f));
                if (!taken.Add(cell)) continue;
                float s = Rand(rng, 0.8f, 1.35f);
                trees.Add(new TreeInstance
                {
                    position = new Vector3(nx, td.GetInterpolatedHeight(nx, nz) / td.size.y, nz),
                    prototypeIndex = rng.Next(prefabs.Length),
                    widthScale = s,
                    heightScale = s * Rand(rng, 0.9f, 1.15f),
                    rotation = Rand(rng, 0f, Mathf.PI * 2f),
                    color = Color.white,
                    lightmapColor = Color.white,
                });
            }
            td.SetTreeInstances(trees.ToArray(), true);
            terrain.Flush();
        }

        static Vector3 Ground(Terrain t, Vector3 world)
        {
            world.y = t.SampleHeight(world) + t.transform.position.y;
            return world;
        }

        // ───────── 배치 도우미 ─────────

        static GameObject Place(GameObject prefab, Transform parent, Vector3 pos, float yaw, float scale)
        {
            if (!prefab) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f) * prefab.transform.rotation);
            go.transform.localScale = prefab.transform.localScale * scale;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        /// <summary>마을 팩 소품을 놓는다. collider면 외곽 상자 충돌체를 붙인다 (집처럼 충돌체가 없는 모델용).</summary>
        static GameObject PlaceVillageProp(string name, Transform parent, Vector3 pos, float yaw, bool collider = false)
        {
            var prefab = V(name);
            if (!prefab) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(pos, prefab.transform.rotation);
            if (collider || go.GetComponentInChildren<Collider>() == null)
            {
                var b = LocalBounds(go);
                if (b.size.sqrMagnitude > 0.01f)
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.center = b.center;
                    box.size = b.size;
                }
            }
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * prefab.transform.rotation;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        /// <summary>바닥 넓이(가로·세로 중 큰 쪽)가 min~max 사이가 되도록 크기 조절</summary>
        static void FitFootprint(GameObject go, float min, float max)
        {
            var b = WorldBounds(go);
            float foot = Mathf.Max(b.size.x, b.size.z);
            if (foot < 0.01f) return;
            float target = Mathf.Clamp(foot, min, max);
            if (Mathf.Approximately(target, foot)) return;
            go.transform.localScale *= target / foot;
        }

        static void FitSize(GameObject go, float size)
        {
            if (!go) return;
            var b = WorldBounds(go);
            float s = Mathf.Max(b.size.x, b.size.z);
            if (s > 0.01f) go.transform.localScale *= size / s;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

/// <summary>루트 로컬 좌표에서 모든 메시를 감싸는 상자 (BoxCollider용)</summary>
        static Bounds LocalBounds(GameObject go)
        {
            var toRoot = go.transform.worldToLocalMatrix;
            bool any = false;
            var b = new Bounds();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh) continue;
                var m = toRoot * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        static Shader LitShader => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        static Material Plain(Color c)
        {
            var m = new Material(LitShader);
            m.SetColor("_BaseColor", c);
            m.color = c;
            return m;
        }

        static Material Glow(Color c, float intensity)
        {
            var m = Plain(c);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * intensity);
            return m;
        }

        // ───────── 수학 ─────────

        static float Fbm(float x, float z)
        {
            float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
            for (int i = 0; i < 4; i++)
            {
                sum += Mathf.PerlinNoise(x * freq, z * freq) * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2.1f;
            }
            return sum / norm;
        }

        /// <summary>from에서 0, to에서 1 (from > to여도 됨)</summary>
        static float Smooth(float from, float to, float v)
        {
            float t = Mathf.Clamp01((v - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            var ab = b - a;
            t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        static float Rand(System.Random r, float min, float max) => min + (float)r.NextDouble() * (max - min);
        static GameObject Pick(GameObject[] arr, System.Random r) => arr.Length == 0 ? null : arr[r.Next(arr.Length)];
        static Vector2 RandomPoint(System.Random r, float range) => new(Rand(r, -range, range), Rand(r, -range, range));
    }
}
