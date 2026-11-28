using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using SoulsHunter.Animations;
using SoulsHunter.Combat;
using SoulsHunter.Enemy;

namespace SoulsHunter.EditorTools
{
    /// <summary>
    /// 에셋스토어 몬스터 모델을 테스트 아레나의 도형 몬스터에 입힌다.
    /// 1) 클립 반복 설정, 머티리얼을 URP로 변환
    /// 2) 몬스터별 애니메이션 세트 + 컨트롤러 생성 (공격마다 때리는 순간을 자동으로 찾음)
    /// 3) 씬의 몬스터에서 도형을 지우고 모델을 붙임. 부위 판정(머리·꼬리·다리·날개)은 해당 뼈에 붙여 같이 움직이게 한다
    /// 메뉴: SoulsHunter/Apply Monster Models (Build Test Arena 때도 자동 실행)
    /// </summary>
    public static class MonsterModelSetup
    {
        const string DataFolder = "Assets/_Project/Data/Monsters";
        const string ModelChildName = "Model";

        class Profile
        {
            public string id;
            public string[] enemyNamePrefixes;
            public string prefab;
            public string clipFolder;
            [Tooltip("모델 크기: 키와 몸길이 중 큰 쪽(m)")]
            public float size;
            public string idle, walk, run, hit, stagger, death, roar;
            public (string attack, string clip)[] attacks;
            public bool flinch;
            public string[] muzzleBones;
        }

        static readonly Profile[] Profiles =
        {
            new()
            {
                id = "Ravager", enemyNamePrefixes = new[] { "Ravager" },
                prefab = "Assets/Hatogame_new/BossMonsterPack1/Wolfboss/Perfabs/Wolfboss_A.prefab",
                clipFolder = "Assets/Hatogame_new/BossMonsterPack1/Wolfboss/Animation",
                size = 5.5f,
                idle = "battleidle", walk = "walk", run = "run", hit = "hit", stagger = "sturn", death = "die", roar = null,
                attacks = new[] { ("Swipe", "attack1"), ("Slam", "attack2"), ("Tail Spin", "attack3"), ("Quake", "attack2"), ("Charge", (string)null) },
            },
            new()
            {
                id = "AshenHound", enemyNamePrefixes = new[] { "Ashen Hound", "Rush Hound" },
                prefab = "Assets/Wolf_Animated/Prefabs/Wolf.prefab",
                clipFolder = "Assets/Wolf_Animated/Model",
                size = 1.9f,
                idle = "breathes", walk = "walk", run = "run", hit = "damege", stagger = "damege", death = "die", roar = "howl",
                attacks = new[] { ("Bite", "attack1"), ("Pounce", "attack2") },
                flinch = true,
            },
            new()
            {
                id = "CinderWyrm", enemyNamePrefixes = new[] { "Cinder Wyrm" },
                prefab = "Assets/FourEvilDragonsPBR/Prefab/DragonSoulEater/Red.prefab",
                clipFolder = "Assets/FourEvilDragonsPBR/Animations/DragonSoulEater",
                size = 7f,
                idle = "idle", walk = "walk", run = "run", hit = "get hit", stagger = "get hit", death = "die", roar = "scream",
                attacks = new[]
                {
                    ("Tail Whip", "tail attack"), ("Fireball", "fireball shoot"), ("Triple Fire", "fireball shoot"),
                    ("Inferno", "fireball shoot"), ("Meteor Rain", "scream"), ("Firestorm", "scream"),
                },
                muzzleBones = new[] { "JawTip", "UpperMouth", "Head" },
            },
        };

        [MenuItem("SoulsHunter/Apply Monster Models")]
        static void ApplyMenu()
        {
            int applied = ApplyToScene(out var report);
            EditorUtility.DisplayDialog("SoulsHunter",
                applied > 0 ? $"몬스터 {applied}마리에 모델을 입혔어요.\n\n{report}\n씬을 저장한 뒤 Play 하세요."
                            : $"모델을 입힐 몬스터를 찾지 못했어요.\n{report}\n먼저 SoulsHunter/Build Test Arena로 아레나를 만드세요.",
                "확인");
        }

        /// <summary>열린 씬의 몬스터들에 모델을 입힌다. 입힌 수를 돌려준다.</summary>
        public static int ApplyToScene(out string report)
        {
            var lines = new List<string>();
            int count = 0;
            var enemies = Object.FindObjectsByType<EnemyAI>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var p in Profiles)
            {
                var targets = enemies.Where(e => p.enemyNamePrefixes.Any(n => e.name.StartsWith(n))).ToList();
                if (targets.Count == 0) continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p.prefab);
                if (!prefab) { lines.Add($"· {p.id}: 모델 없음 ({p.prefab})"); continue; }

                var set = BuildAnimationSet(p, prefab, out var controller, out string setReport);
                ConvertMaterials(prefab);
                foreach (var e in targets)
                {
                    ApplyModel(e, p, prefab, set, controller);
                    count++;
                }
                lines.Add($"· {p.id} ×{targets.Count}: {setReport}");
            }

            if (count > 0)
            {
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }
            report = string.Join("\n", lines);
            Debug.Log($"[SoulsHunter] 몬스터 모델 적용 {count}마리\n{report}");
            return count;
        }

        // ───────── 애니메이션 세트 + 컨트롤러 ─────────

        static MonsterAnimationSet BuildAnimationSet(Profile p, GameObject prefab, out AnimatorController controller, out string report)
        {
            if (!AssetDatabase.IsValidFolder(DataFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Data", "Monsters");

            var clips = LoadClips(p.clipFolder);
            AnimationClip Clip(string key, bool loop)
            {
                if (string.IsNullOrEmpty(key)) return null;
                var c = FindClip(clips, key);
                if (c) SetLoop(c, loop);
                return c ? FindClip(LoadClips(p.clipFolder), key) : null; // 다시 불러온 클립 (반복 설정 반영)
            }

            string setPath = $"{DataFolder}/{p.id}Animations.asset";
            var set = AssetDatabase.LoadAssetAtPath<MonsterAnimationSet>(setPath);
            bool isNew = !set;
            if (isNew)
            {
                set = ScriptableObject.CreateInstance<MonsterAnimationSet>();
                AssetDatabase.CreateAsset(set, setPath);
            }

            // 처음 만들 때만 클립을 채운다 (이후엔 사람이 바꿔 끼운 걸 존중)
            if (isNew)
            {
                set.idle = Clip(p.idle, true);
                set.walk = Clip(p.walk, true);
                set.run = Clip(p.run, true);
                set.hit = Clip(p.hit, false);
                set.stagger = Clip(p.stagger, false);
                set.death = Clip(p.death, false);
                set.roar = Clip(p.roar, false);
                set.flinchOnHit = p.flinch;
                set.attacks = p.attacks.Select(a => new MonsterAnimationSet.AttackAnim { attack = a.attack, clip = Clip(a.clip, false) }).ToList();
            }
            else
            {
                foreach (var c in new[] { set.idle, set.walk, set.run }) if (c) SetLoop(c, true);
                foreach (var c in new[] { set.hit, set.stagger, set.death, set.roar }) if (c) SetLoop(c, false);
                foreach (var a in set.attacks) if (a.clip) SetLoop(a.clip, false);
            }

            // 때리는 순간 찾기
            var hitReport = new List<string>();
            foreach (var a in set.attacks)
            {
                if (!a.clip) { hitReport.Add($"{a.attack}=이동"); continue; }
                a.hitPoint = DetectHitPoint(prefab, a.clip);
                hitReport.Add($"{a.attack}={a.clip.name}@{a.hitPoint:0.00}");
            }
            EditorUtility.SetDirty(set);

            controller = BuildController(p.id, set);
            report = string.Join(", ", hitReport);
            return set;
        }

        static List<AnimationClip> LoadClips(string folder)
        {
            var list = new List<AnimationClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is AnimationClip c && !c.name.StartsWith("__preview__")) list.Add(c);
            }
            return list;
        }

        /// <summary>파일 이름(…@attack1, Wolf_attack1, Fireball Shoot)으로 클립을 찾는다</summary>
        static AnimationClip FindClip(List<AnimationClip> clips, string key)
        {
            key = key.ToLowerInvariant();
            foreach (var c in clips)
            {
                string file = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(c)).ToLowerInvariant();
                if (file == key || file.EndsWith("@" + key) || file.EndsWith("_" + key)) return c;
            }
            return null;
        }

        static void SetLoop(AnimationClip clip, bool loop)
        {
            string path = AssetDatabase.GetAssetPath(clip);
            if (AssetImporter.GetAtPath(path) is not ModelImporter importer) return;
            var anims = importer.clipAnimations;
            if (anims == null || anims.Length == 0) anims = importer.defaultClipAnimations;
            bool changed = false;
            foreach (var a in anims)
            {
                if (a.loopTime == loop) continue;
                a.loopTime = loop;
                changed = true;
            }
            if (!changed) return;
            importer.clipAnimations = anims;
            importer.SaveAndReimport();
        }

        static AnimatorController BuildController(string id, MonsterAnimationSet set)
        {
            string path = $"{DataFolder}/{id}.controller";
            CharacterSetupWindowAccess.CloseAnimatorWindows();
            if (AssetDatabase.LoadAssetAtPath<Object>(path)) AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("Move", AnimatorControllerParameterType.Float);
            ctrl.AddParameter(new AnimatorControllerParameter { name = "LocoPlayback", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            ctrl.AddParameter(new AnimatorControllerParameter { name = "AttackSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

            var sm = ctrl.layers[0].stateMachine;

            // 이동: 0 = 대기, 1 = 걷기, 2 = 달리기
            var loco = ctrl.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Move";
            tree.useAutomaticThresholds = false;
            var idle = set.idle ? set.idle : set.walk;
            if (idle) tree.AddChild(idle, 0f);
            if (set.walk) tree.AddChild(set.walk, 1f);
            if (set.run) tree.AddChild(set.run, 2f);
            loco.speedParameterActive = true;
            loco.speedParameter = "LocoPlayback";
            sm.defaultState = loco;

            AnimatorState OneShot(string name, AnimationClip clip, bool returnToLoco, string speedParam = null)
            {
                if (!clip) return null;
                var s = sm.AddState(name);
                s.motion = clip;
                if (speedParam != null) { s.speedParameterActive = true; s.speedParameter = speedParam; }
                if (returnToLoco)
                {
                    var t = s.AddTransition(loco);
                    t.hasExitTime = true;
                    t.exitTime = 0.92f;
                    t.hasFixedDuration = true;
                    t.duration = 0.25f;
                }
                return s;
            }

            OneShot("Hit", set.hit, true);
            OneShot("Stagger", set.stagger, true);
            OneShot("Roar", set.roar, true);
            OneShot("Death", set.death, false);
            foreach (var a in set.attacks)
                if (a.clip) OneShot(MonsterAnimationSet.StateName(a.attack), a.clip, true, "AttackSpeed");

            AssetDatabase.SaveAssets();
            return ctrl;
        }

        /// <summary>
        /// 클립을 샘플링해서 몸이 가장 빠르게 움직이는 순간(= 휘두르거나 내려찍는 순간)을 찾는다.
        /// 앞뒤 10%는 빼고 본다.
        /// </summary>
        static float DetectHitPoint(GameObject prefab, AnimationClip clip)
        {
            var go = (GameObject)Object.Instantiate(prefab);
            go.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;
            bool started = !AnimationMode.InAnimationMode();
            if (started) AnimationMode.StartAnimationMode();
            try
            {
                var bones = go.GetComponentsInChildren<Transform>().Where(t => t != go.transform).ToArray();
                const int samples = 60;
                float dt = clip.length / samples;
                var prev = new Vector3[bones.Length];
                var speed = new float[samples + 1];
                for (int i = 0; i <= samples; i++)
                {
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(go, clip, i * dt);
                    AnimationMode.EndSampling();
                    float best = 0f;
                    for (int b = 0; b < bones.Length; b++)
                    {
                        var pos = go.transform.InverseTransformPoint(bones[b].position);
                        if (i > 0) best = Mathf.Max(best, (pos - prev[b]).magnitude);
                        prev[b] = pos;
                    }
                    speed[i] = best;
                }
                int from = Mathf.RoundToInt(samples * 0.1f), to = Mathf.RoundToInt(samples * 0.9f);
                int peak = from;
                for (int i = from; i <= to; i++)
                {
                    // 앞뒤 프레임과 평균 (튀는 값 무시)
                    float s = (speed[i - 1] + speed[i] + speed[Mathf.Min(samples, i + 1)]) / 3f;
                    float b = (speed[peak - 1] + speed[peak] + speed[Mathf.Min(samples, peak + 1)]) / 3f;
                    if (s > b) peak = i;
                }
                return peak / (float)samples;
            }
            finally
            {
                if (started) AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(go);
            }
        }

        // ───────── 머티리얼 ─────────

        /// <summary>기본(Built-in) 셰이더 머티리얼을 URP Lit으로 바꾼다 (안 바꾸면 분홍색으로 보임)</summary>
        static void ConvertMaterials(GameObject prefab)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (!lit) return;
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (!m || m.shader.name.StartsWith("Universal Render Pipeline")) continue;
                    if (!AssetDatabase.Contains(m)) continue;

                    Texture Tex(string n) => m.HasProperty(n) ? m.GetTexture(n) : null;
                    float F(string n, float d) => m.HasProperty(n) ? m.GetFloat(n) : d;
                    var albedo = Tex("_MainTex");
                    var color = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
                    var normal = Tex("_BumpMap");
                    float normalScale = F("_BumpScale", 1f);
                    var metallicMap = Tex("_MetallicGlossMap");
                    var specMap = Tex("_SpecGlossMap");
                    var specColor = m.HasProperty("_SpecColor") ? m.GetColor("_SpecColor") : Color.gray;
                    float metallic = F("_Metallic", 0f);
                    float smoothness = F("_Glossiness", 0.5f);
                    float glossScale = F("_GlossMapScale", 1f);
                    var occlusion = Tex("_OcclusionMap");
                    var emissionMap = Tex("_EmissionMap");
                    var emission = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
                    bool specular = m.shader.name.Contains("Specular");

                    m.shader = lit;
                    m.SetTexture("_BaseMap", albedo);
                    m.SetColor("_BaseColor", color);
                    if (normal) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", normalScale); m.EnableKeyword("_NORMALMAP"); }
                    if (specular)
                    {
                        m.SetFloat("_WorkflowMode", 0f);
                        m.EnableKeyword("_SPECULAR_SETUP");
                        m.SetColor("_SpecColor", specColor);
                        if (specMap) { m.SetTexture("_SpecGlossMap", specMap); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
                        m.SetFloat("_Smoothness", specMap ? glossScale : smoothness);
                    }
                    else
                    {
                        m.SetFloat("_WorkflowMode", 1f);
                        m.SetFloat("_Metallic", metallic);
                        if (metallicMap) { m.SetTexture("_MetallicGlossMap", metallicMap); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
                        m.SetFloat("_Smoothness", metallicMap ? glossScale : smoothness);
                    }
                    if (occlusion) { m.SetTexture("_OcclusionMap", occlusion); m.EnableKeyword("_OCCLUSIONMAP"); }
                    if (emissionMap || emission.maxColorComponent > 0.01f)
                    {
                        m.SetTexture("_EmissionMap", emissionMap);
                        m.SetColor("_EmissionColor", emission);
                        m.EnableKeyword("_EMISSION");
                    }
                    EditorUtility.SetDirty(m);
                }
        }

        // ───────── 씬의 몬스터에 모델 입히기 ─────────

        static void ApplyModel(EnemyAI enemy, Profile p, GameObject prefab, MonsterAnimationSet set, AnimatorController controller)
        {
            var root = enemy.transform;
            Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Apply Monster Model");
            var hurtboxes = root.GetComponentsInChildren<Hurtbox>(true);

            // 이전 모델 제거 (부위 판정은 먼저 루트로 빼낸다)
            foreach (var hb in hurtboxes) hb.transform.SetParent(root, true);
            var old = root.Find(ModelChildName);
            if (old) Object.DestroyImmediate(old.gameObject);

            // 도형 보기용 조각(눈, 다리, 목) 제거, 부위 판정의 도형 표시도 제거
            foreach (Transform child in root.Cast<Transform>().ToList())
                if (!child.GetComponent<Hurtbox>() && child.GetComponent<Renderer>()) Object.DestroyImmediate(child.gameObject);
            foreach (var hb in hurtboxes)
            {
                var mr = hb.GetComponent<MeshRenderer>();
                if (mr) Object.DestroyImmediate(mr);
                var mf = hb.GetComponent<MeshFilter>();
                if (mf) Object.DestroyImmediate(mf);
            }

            // 모델 생성
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = ModelChildName;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            var animator = model.GetComponent<Animator>();
            if (!animator) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var bones = model.GetComponentsInChildren<Transform>().Where(t => t != model.transform && !t.GetComponent<Renderer>()).ToList();
            var head = FindHead(model.transform, bones);
            var tail = FindTail(bones);

            // 방향 맞추기: 꼬리 → 머리가 앞(+Z)
            if (head && tail)
            {
                Vector3 f = model.transform.InverseTransformPoint(head.position) - model.transform.InverseTransformPoint(tail.position);
                f.y = 0f;
                if (f.sqrMagnitude > 0.0001f)
                {
                    float yaw = Mathf.Round(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg / 90f) * 90f;
                    model.transform.localRotation = Quaternion.Euler(0f, -yaw, 0f);
                }
            }

            // 크기: 키와 몸길이 중 큰 쪽을 맞추고 발을 바닥에
            var b = LocalBounds(root, model);
            float current = Mathf.Max(b.size.y, b.size.z);
            if (current > 0.001f) model.transform.localScale = Vector3.one * (p.size / current);
            // 발 높이: 대기 동작을 실제로 입혀서 가장 낮은 뼈(발끝)를 바닥에 맞춘다
            // (기본 자세와 대기 자세의 다리 높이가 달라 떠 보이는 것 방지)
            float lowest = LowestPointInPose(root, model, set.idle ? set.idle : set.walk);
            var bodyBounds = LocalBounds(root, model);
            if (float.IsInfinity(lowest) || lowest < bodyBounds.min.y - bodyBounds.size.y || lowest > bodyBounds.max.y)
                lowest = bodyBounds.min.y; // 값이 말이 안 되면 예전 방식
            model.transform.localPosition += new Vector3(0f, -lowest, 0f);
            b = LocalBounds(root, model);

            // 부위 판정을 뼈에 붙인다
            var body = FindBody(root, bones, b);
            foreach (var hb in hurtboxes)
            {
                string part = new SerializedObject(hb).FindProperty("partName").stringValue;
                Transform bone = part switch
                {
                    "Head" => head,
                    "Tail" => tail,
                    "Leg" => FindLeg(root, bones, hb.transform.localPosition, b),
                    "Wing" => FindWing(root, bones, hb.transform.localPosition.x),
                    _ => body,
                };
                if (!bone) bone = Nearest(bones, hb.transform.position);
                if (!bone) continue;

                Vector3 center = bone.position;
                if (part is "Head" or "Tail" or "Leg" or "Wing")
                {
                    // 뼈 시작점과 끝(가장 먼 자식) 사이 가운데에 두면 부위를 더 잘 덮는다
                    var tip = FarthestDescendant(bone);
                    if (tip) center = Vector3.Lerp(bone.position, tip.position, part == "Tail" ? 0.35f : 0.5f);
                }
                hb.transform.position = center;
                hb.transform.SetParent(bone, true);
            }

            // 조준점, 투사체 발사 위치, 죽는 모습 보여줄 시간
            var lockOn = root.GetComponent<LockOnTarget>();
            if (lockOn && body)
            {
                var so = new SerializedObject(lockOn);
                so.FindProperty("lockPoint").objectReferenceValue = body;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            Transform muzzle = null;
            if (p.muzzleBones != null)
                foreach (var n in p.muzzleBones)
                    if (muzzle = bones.FirstOrDefault(t => t.name == n)) break;
            enemy.EditorSetModel(muzzle);
            EditorUtility.SetDirty(enemy);

            var dummy = root.GetComponent<TrainingDummy>();
            if (dummy)
            {
                dummy.EditorSetHideDelay(set.death ? set.death.length + 1f : 0f);
                EditorUtility.SetDirty(dummy);
            }

            var driver = root.GetComponent<EnemyAnimationDriver>();
            if (!driver) driver = Undo.AddComponent<EnemyAnimationDriver>(root.gameObject);
            driver.EditorSetup(set, animator);
            EditorUtility.SetDirty(driver);
        }

        static readonly string[] LimbWords = { "leg", "arm", "hand", "foot", "feet", "toe", "finger", "platform", "ankle", "thigh", "calf", "clavicle", "wing", "twist", "horselink" };

        static bool IsLimb(Transform t)
        {
            string n = t.name.ToLowerInvariant();
            return LimbWords.Any(n.Contains);
        }

        static Transform FindHead(Transform model, List<Transform> bones)
        {
            var named = bones.FirstOrDefault(t => t.name.ToLowerInvariant() is "head" or "bip01 head")
                        ?? bones.FirstOrDefault(t => t.name.ToLowerInvariant().EndsWith(" head") || t.name.ToLowerInvariant().EndsWith("_head"));
            if (named) return named;

            // 이름이 없으면: 꼬리 반대쪽 끝에서 가장 멀리 나간, 팔다리가 아닌 뼈
            var tail = FindTail(bones);
            var body = bones.Where(t => !IsLimb(t) && !t.name.ToLowerInvariant().Contains("tail")).ToList();
            if (body.Count == 0) return null;
            Vector3 from = tail ? tail.position : model.position;
            var tip = body.OrderByDescending(t => (t.position - from).sqrMagnitude).First();
            return tip.parent && body.Contains(tip.parent) ? tip.parent : tip;
        }

        static Transform FindTail(List<Transform> bones)
        {
            var tails = bones.Where(t => t.name.ToLowerInvariant().Contains("tail")).OrderBy(Depth).ToList();
            if (tails.Count == 0) return null;
            return tails[Mathf.Min(tails.Count - 1, tails.Count / 3)];
        }

        static Transform FindBody(Transform root, List<Transform> bones, Bounds b)
        {
            var spines = bones.Where(t =>
            {
                string n = t.name.ToLowerInvariant();
                return (n.Contains("spine") || n.Contains("chest") || n.Contains("pelvis")) && !IsLimb(t);
            }).ToList();
            var center = root.TransformPoint(b.center);
            if (spines.Count > 0) return spines.OrderBy(t => (t.position - center).sqrMagnitude).First();
            return Nearest(bones, center);
        }

        static Transform FindLeg(Transform root, List<Transform> bones, Vector3 partLocal, Bounds b)
        {
            var legs = bones.Where(t =>
            {
                string n = t.name.ToLowerInvariant();
                return (n.Contains("leg") || n.Contains("thigh") || n.Contains("calf") || n.Contains("upperarm") ||
                        n.Contains("forearm") || n.Contains("horselink") || n.Contains("lowerarm"))
                       && !n.Contains("platform") && !n.Contains("twist") && !n.Contains("wing");
            }).ToList();
            if (legs.Count == 0) return null;

            // 같은 쪽(좌우, 앞뒤)에 있고 무릎 높이에 가까운 뼈
            float sx = Mathf.Sign(partLocal.x);
            bool useZ = Mathf.Abs(partLocal.z) > 0.3f;
            float sz = Mathf.Sign(partLocal.z);
            Vector3 target = new(sx * b.extents.x * 0.4f, b.size.y * 0.3f, useZ ? b.center.z + sz * b.extents.z * 0.35f : b.center.z);
            return legs.OrderBy(t =>
            {
                var lp = root.InverseTransformPoint(t.position);
                float side = Mathf.Sign(lp.x) == sx ? 0f : 100f;
                return side + (lp - target).sqrMagnitude;
            }).First();
        }

        static Transform FindWing(Transform root, List<Transform> bones, float side)
        {
            var wings = bones.Where(t => t.name.ToLowerInvariant().Contains("wing") &&
                                         Mathf.Sign(root.InverseTransformPoint(t.position).x) == Mathf.Sign(side)).ToList();
            if (wings.Count == 0) return null;
            var named = wings.FirstOrDefault(t => t.name.ToLowerInvariant().StartsWith("wing02"));
            if (named) return named;
            return wings.OrderBy(Depth).ElementAt(wings.Count / 3);
        }

        static Transform Nearest(List<Transform> bones, Vector3 p) =>
            bones.Count == 0 ? null : bones.OrderBy(t => (t.position - p).sqrMagnitude).First();

        static Transform FarthestDescendant(Transform bone)
        {
            Transform best = null;
            float bestD = 0f;
            foreach (var t in bone.GetComponentsInChildren<Transform>())
            {
                float d = (t.position - bone.position).sqrMagnitude;
                if (d > bestD) { bestD = d; best = t; }
            }
            return best;
        }

        static int Depth(Transform t)
        {
            int d = 0;
            while (t.parent) { d++; t = t.parent; }
            return d;
        }

        /// <summary>
        /// 클립의 첫 프레임 자세에서 가장 낮은 뼈의 높이 (루트 기준).
        /// 발끝 뼈가 바닥에 닿게 하려고 쓴다. 실패하면 +무한대.
        /// (메시 범위는 실제보다 넓게 잡혀 있는 경우가 많아서 뼈로 본다)
        /// </summary>
        internal static float LowestPointInPose(Transform root, GameObject model, AnimationClip clip)
        {
            var bones = new HashSet<Transform>();
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                foreach (var bone in skin.bones)
                    if (bone) bones.Add(bone);
            if (bones.Count == 0) return float.PositiveInfinity;

            bool started = !AnimationMode.InAnimationMode();
            if (started) AnimationMode.StartAnimationMode();
            float lowest = float.PositiveInfinity;
            try
            {
                if (clip)
                {
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(model, clip, 0f);
                    AnimationMode.EndSampling();
                }
                foreach (var bone in bones)
                    lowest = Mathf.Min(lowest, root.InverseTransformPoint(bone.position).y);
            }
            finally
            {
                if (started) AnimationMode.StopAnimationMode(); // 자세를 원래대로
            }
            return lowest;
        }

        /// <summary>모델의 보이는 범위 (루트 기준 좌표)</summary>
        internal static Bounds LocalBounds(Transform root, GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            bool any = false;
            var bounds = new Bounds();
            foreach (var r in renderers)
            {
                var wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var lp = root.InverseTransformPoint(corner);
                    if (!any) { bounds = new Bounds(lp, Vector3.zero); any = true; }
                    else bounds.Encapsulate(lp);
                }
            }
            return bounds;
        }
    }

    /// <summary>CharacterSetupWindow의 Animator 창 정리 기능을 다른 도구에서 쓰기 위한 통로</summary>
    static class CharacterSetupWindowAccess
    {
        public static void CloseAnimatorWindows()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (w && w.GetType().Name == "AnimatorControllerTool") w.Close();
            AnimatorWindowRepair.DestroyOrphanGraphs();
        }
    }
}
