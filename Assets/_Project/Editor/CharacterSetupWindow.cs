using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using SoulsHunter.Animations;
using SoulsHunter.Player;

namespace SoulsHunter.EditorTools
{
    /// <summary>
    /// SoulsHunter > Character Setup
    /// 1) 모델·애니메이션 FBX를 Humanoid로 설정
    /// 2) 폴더 안 애니메이션을 파일 이름으로 자동 분류해 CharacterAnimationSet 생성
    /// 3) 애니메이터 컨트롤러를 만들고 씬의 Player에 모델을 붙인다
    /// </summary>
    public class CharacterSetupWindow : EditorWindow
    {
        const string OutFolder = "Assets/_Project/Data/Character";
        const string SetPath = OutFolder + "/PlayerAnimations.asset";
        const string ControllerPath = OutFolder + "/PlayerAnimator.controller";

        GameObject model;
        DefaultAsset animationFolder;
        CharacterAnimationSet animationSet;
        Vector2 scroll;
        string log = "";

        [MenuItem("SoulsHunter/Character Setup")]
        static void Open() => GetWindow<CharacterSetupWindow>("Character Setup");

        void OnEnable()
        {
            if (!animationSet) animationSet = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(SetPath);
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "1. 캐릭터 모델 FBX 하나와, 애니메이션 FBX들을 넣은 폴더를 지정하세요.\n" +
                "2. 버튼을 위에서부터 순서대로 누르면 됩니다.\n" +
                "   (Mixamo에서 받은 파일이면 이름으로 자동 분류됩니다)", MessageType.Info);

            model = (GameObject)EditorGUILayout.ObjectField("캐릭터 모델 (FBX)", model, typeof(GameObject), false);
            animationFolder = (DefaultAsset)EditorGUILayout.ObjectField("애니메이션 폴더", animationFolder, typeof(DefaultAsset), false);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!model || !animationFolder))
            {
                if (GUILayout.Button("① Humanoid로 임포트 설정", GUILayout.Height(28))) ConfigureImports();
                if (GUILayout.Button("② 애니메이션 세트 자동 생성", GUILayout.Height(28))) animationSet = BuildAnimationSet();
            }

            EditorGUILayout.Space();
            animationSet = (CharacterAnimationSet)EditorGUILayout.ObjectField("애니메이션 세트", animationSet, typeof(CharacterAnimationSet), false);
            using (new EditorGUI.DisabledScope(!model || !animationSet))
            {
                if (GUILayout.Button("③ 애니메이터 만들고 씬의 Player에 적용", GUILayout.Height(28))) BuildAndApply();
            }

            EditorGUILayout.Space();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.TextArea(log, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        void Log(string line)
        {
            log += line + "\n";
            Debug.Log("[Character Setup] " + line);
            Repaint();
        }

        // ───────── ① 임포트 설정 ─────────

        void ConfigureImports()
        {
            log = "";
            string modelPath = AssetDatabase.GetAssetPath(model);
            SetHumanoid(modelPath, false);
            foreach (var path in AnimationFiles())
                if (path != modelPath) SetHumanoid(path, true);
            Log("완료: 모델과 애니메이션을 Humanoid로 설정했어요.");
        }

        void SetHumanoid(string path, bool isAnimation)
        {
            if (AssetImporter.GetAtPath(path) is not ModelImporter imp) return;
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importAnimation = true;

            var clips = imp.defaultClipAnimations;
            string fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            foreach (var c in clips)
            {
                c.loopTime = isAnimation && IsLooping(Classify(fileName + " " + c.name.ToLowerInvariant()));
                // 이동은 코드가 하므로 회전·높이는 포즈에 굽고, 수평 이동은 버린다(applyRootMotion 꺼짐)
                c.lockRootRotation = true;
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = true;
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = false;
            }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
            Log($"Humanoid: {Path.GetFileName(path)}");
        }

        IEnumerable<string> AnimationFiles()
        {
            string folder = AssetDatabase.GetAssetPath(animationFolder);
            return AssetDatabase.FindAssets("t:Model", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
                .Concat(AssetDatabase.FindAssets("t:AnimationClip", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath))
                .Distinct();
        }

        // ───────── ② 애니메이션 자동 분류 ─────────

        CharacterAnimationSet BuildAnimationSet()
        {
            log = "";
            EnsureFolder();
            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(SetPath);
            if (!set)
            {
                set = CreateInstance<CharacterAnimationSet>();
                AssetDatabase.CreateAsset(set, SetPath);
            }
            set.attacks.Clear();

            var lights = new List<(string key, AnimationClip clip)>();
            var heavies = new List<(string key, AnimationClip clip)>();

            foreach (var path in AnimationFiles())
            {
                string file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                {
                    if (clip.name.StartsWith("__preview__")) continue;
                    string key = file + " " + clip.name.ToLowerInvariant();
                    string slot = Classify(key);
                    switch (slot)
                    {
                        case "light": lights.Add((key, clip)); break;
                        case "heavy": heavies.Add((key, clip)); break;
                        case null: Log($"분류 못 함 (직접 넣어주세요): {Path.GetFileName(path)} / {clip.name}"); continue;
                        default:
                            var field = typeof(CharacterAnimationSet).GetField(slot);
                            if (field.GetValue(set) is AnimationClip existing && existing)
                            {
                                Log($"{slot}: 이미 {existing.name} 있음 → {clip.name} 건너뜀");
                                continue;
                            }
                            field.SetValue(set, clip);
                            break;
                    }
                    Log($"{slot,-12} ← {clip.name}  ({Path.GetFileName(path)})");
                }
            }

            foreach (var (_, clip) in lights.OrderBy(l => l.key))
                set.attacks.Add(new CharacterAnimationSet.AttackClip { type = PlayerCombat.AttackType.Light, clip = clip });
            foreach (var (_, clip) in heavies.OrderBy(h => h.key))
                set.attacks.Add(new CharacterAnimationSet.AttackClip { type = PlayerCombat.AttackType.Heavy, clip = clip });

            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            Log($"완료: 약공격 {lights.Count}개, 강공격 {heavies.Count}개. 세트 에셋에서 타격 지점(Impact Point)을 조정하세요.");
            Selection.activeObject = set;
            return set;
        }

        static bool Has(string s, params string[] words) => words.Any(s.Contains);

        /// <summary>파일/클립 이름으로 슬롯을 추측한다 (Mixamo 이름 기준).</summary>
        static string Classify(string n)
        {
            if (Has(n, "death", "dying", "die")) return "death";
            if (Has(n, "drink", "potion")) return "drink";
            if (Has(n, "parry")) return "parry";
            if (Has(n, "block", "guard") && Has(n, "impact", "hit", "break")) return "guardBreak";
            if (Has(n, "block", "guard")) return "guard";
            if (Has(n, "counter")) return "counter";
            if (Has(n, "backstep", "step back", "hop back") || (Has(n, "dodge", "step") && Has(n, "back"))) return "backstep";
            if (Has(n, "roll", "dodge", "dive")) return "roll";
            if (Has(n, "stagger", "knock", "stumble")) return "stagger";
            if (Has(n, "hit", "impact", "react")) return "hit";
            if (Has(n, "attack", "slash", "swing", "combo", "stab", "thrust", "strike", "punch", "kick"))
                return Has(n, "heavy", "power", "charge", "overhead", "downward", "jump attack", "spin") ? "heavy" : "light";
            if (Has(n, "jump")) return "jump";
            if (Has(n, "sprint")) return "sprint";
            if (Has(n, "run", "jog", "walk") && Has(n, "back")) return "runBackward";
            if (Has(n, "strafe", "walk", "run", "jog") && Has(n, "left")) return "strafeLeft";
            if (Has(n, "strafe", "walk", "run", "jog") && Has(n, "right")) return "strafeRight";
            if (Has(n, "run", "jog")) return "run";
            if (Has(n, "walk")) return "walk";
            if (Has(n, "idle")) return "idle";
            return null;
        }

        static bool IsLooping(string slot) =>
            slot is "idle" or "walk" or "run" or "sprint" or "runBackward" or "strafeLeft" or "strafeRight" or "guard";

        // ───────── ③ 컨트롤러 생성 + 적용 ─────────

        void BuildAndApply()
        {
            var player = GameObject.FindWithTag("Player");
            if (!player)
            {
                EditorUtility.DisplayDialog("Character Setup", "씬에 Player 태그 오브젝트가 없어요. 테스트 씬을 열고 다시 해주세요.", "확인");
                return;
            }
            EnsureFolder();
            var controller = BuildController(animationSet, out var slotA, out var slotB);
            ApplyToPlayer(player, controller, slotA, slotB);
        }

        AnimatorController BuildController(CharacterAnimationSet set, out AnimationClip slotA, out AnimationClip slotB)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(ControllerPath)) AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            foreach (var f in new[] { "Speed", "MoveX", "MoveY", "AttackTime", "DodgeTime" })
                ctrl.AddParameter(f, AnimatorControllerParameterType.Float);
            foreach (var b in new[] { "LockedOn", "Attacking", "Dodging", "Dead", "Guard", "Grounded", "Charging", "DemonMode" })
                ctrl.AddParameter(b, AnimatorControllerParameterType.Bool);
            foreach (var t in new[] { "AttackA", "AttackB", "Roll", "Backstep", "Hit", "Stagger", "Drink", "Parry", "GuardBreak", "Vault", "Counter" })
                ctrl.AddParameter(t, AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("ComboIndex", AnimatorControllerParameterType.Int);

            // 공격 슬롯용 빈 클립 (실행 중 실제 공격 클립으로 갈아 끼움)
            slotA = new AnimationClip { name = "__AttackSlotA" };
            slotB = new AnimationClip { name = "__AttackSlotB" };
            AssetDatabase.AddObjectToAsset(slotA, ctrl);
            AssetDatabase.AddObjectToAsset(slotB, ctrl);

            var sm = ctrl.layers[0].stateMachine;

            // 이동: 2D 블렌드 트리 (MoveX = 옆, MoveY = 앞뒤, 달리기 속도 = 1)
            var loco = ctrl.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = "MoveX";
            tree.blendParameterY = "MoveY";
            void Add(AnimationClip c, float x, float y) { if (c) tree.AddChild(c, new Vector2(x, y)); }
            Add(set.idle, 0f, 0f);
            Add(set.walk, 0f, 0.45f);
            Add(set.run, 0f, 1f);
            Add(set.sprint, 0f, 1.6f);
            Add(set.runBackward, 0f, -1f);
            Add(set.strafeLeft, -1f, 0f);
            Add(set.strafeRight, 1f, 0f);
            sm.defaultState = loco;

            AnimatorState State(string name, Motion m, int row, int col)
            {
                var s = sm.AddState(name, new Vector3(300 + col * 220, row * 60, 0));
                s.motion = m;
                return s;
            }
            AnimatorStateTransition AnyTo(AnimatorState s, float duration, string param, AnimatorConditionMode mode = AnimatorConditionMode.If)
            {
                var t = sm.AddAnyStateTransition(s);
                t.hasExitTime = false;
                t.duration = duration;
                t.canTransitionToSelf = false;
                t.AddCondition(mode, 0f, param);
                return t;
            }
            void BackByCondition(AnimatorState s, string param, AnimatorConditionMode mode)
            {
                var t = s.AddTransition(loco);
                t.hasExitTime = false;
                t.duration = 0.2f;
                t.AddCondition(mode, 0f, param);
            }
            void BackByExitTime(AnimatorState s, float exitTime = 0.85f)
            {
                var t = s.AddTransition(loco);
                t.hasExitTime = true;
                t.exitTime = exitTime;
                t.duration = 0.2f;
            }

            // 죽음 (가장 먼저 검사)
            if (set.death)
            {
                var death = State("Death", set.death, 0, 1);
                AnyTo(death, 0.15f, "Dead");
                BackByCondition(death, "Dead", AnimatorConditionMode.IfNot);
            }

            // 공격 A/B: 재생 위치를 AttackTime으로 구동
            foreach (var (name, clip) in new[] { ("AttackA", slotA), ("AttackB", slotB) })
            {
                var s = State(name, clip, name == "AttackA" ? 1 : 2, 0);
                s.timeParameterActive = true;
                s.timeParameter = "AttackTime";
                AnyTo(s, 0.08f, name);
                BackByCondition(s, "Attacking", AnimatorConditionMode.IfNot);
            }

            // 회피: 재생 위치를 DodgeTime으로 구동
            foreach (var (name, clip) in new[] { ("Roll", set.roll), ("Backstep", set.backstep ? set.backstep : set.roll) })
            {
                if (!clip) continue;
                var s = State(name, clip, name == "Roll" ? 3 : 4, 0);
                s.timeParameterActive = true;
                s.timeParameter = "DodgeTime";
                AnyTo(s, 0.05f, name);
                BackByCondition(s, "Dodging", AnimatorConditionMode.IfNot);
            }

            // 한 번 재생하고 돌아오는 동작들
            int row = 1;
            foreach (var (name, clip, trigger) in new[]
                     {
                         ("Hit", set.hit, "Hit"), ("Stagger", set.stagger ? set.stagger : set.hit, "Stagger"),
                         ("Drink", set.drink, "Drink"), ("Parry", set.parry, "Parry"),
                         ("GuardBreak", set.guardBreak ? set.guardBreak : set.hit, "GuardBreak"),
                         ("Counter", set.counter ? set.counter : set.parry, "Counter"), ("Jump", set.jump, "Vault"),
                     })
            {
                if (!clip) continue;
                var s = State(name, clip, row++, 2);
                AnyTo(s, 0.1f, trigger);
                BackByExitTime(s);
            }

            // 가드: 상체만 덮는 레이어
            if (set.guard)
            {
                var mask = new AvatarMask { name = "UpperBody" };
                for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                    mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
                foreach (var part in new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm,
                                             AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers })
                    mask.SetHumanoidBodyPartActive(part, true);
                AssetDatabase.AddObjectToAsset(mask, ctrl);

                ctrl.AddLayer("Guard");
                var layers = ctrl.layers;
                var layer = layers[^1];
                layer.defaultWeight = 1f;
                layer.avatarMask = mask;
                layers[^1] = layer;
                ctrl.layers = layers;

                var gsm = layer.stateMachine;
                var empty = gsm.AddState("Empty");
                var guard = gsm.AddState("Guard");
                guard.motion = set.guard;
                gsm.defaultState = empty;
                var on = empty.AddTransition(guard);
                on.hasExitTime = false; on.duration = 0.12f; on.AddCondition(AnimatorConditionMode.If, 0f, "Guard");
                var off = guard.AddTransition(empty);
                off.hasExitTime = false; off.duration = 0.15f; off.AddCondition(AnimatorConditionMode.IfNot, 0f, "Guard");
            }

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            Log($"애니메이터 생성: {ControllerPath}");
            return ctrl;
        }

        void ApplyToPlayer(GameObject player, AnimatorController controller, AnimationClip slotA, AnimationClip slotB)
        {
            Undo.SetCurrentGroupName("Apply Character");
            int group = Undo.GetCurrentGroup();

            // 이전 모델 제거, 캡슐 몸통 숨기기
            var old = player.transform.Find("Model");
            if (old) Undo.DestroyObjectImmediate(old.gameObject);
            foreach (Transform child in player.transform)
                if (child.name == "Body" || child.name.StartsWith("Nose"))
                {
                    Undo.RecordObject(child.gameObject, "hide");
                    child.gameObject.SetActive(false);
                }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, player.transform);
            Undo.RegisterCreatedObjectUndo(inst, "model");
            inst.name = "Model";
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            foreach (var c in inst.GetComponentsInChildren<Collider>()) Undo.DestroyObjectImmediate(c);

            var animator = inst.GetComponent<Animator>();
            if (!animator) animator = Undo.AddComponent<Animator>(inst);
            if (!animator.avatar)
                animator.avatar = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(model)).OfType<Avatar>().FirstOrDefault();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (!animator.avatar || !animator.isHuman)
                Log("경고: 모델에 Humanoid 아바타가 없어요. ① 버튼을 먼저 눌러주세요.");

            SetRef(player.GetComponent<PlayerLocomotion>(), "animator", animator);

            var driver = player.GetComponent<PlayerAnimationDriver>();
            if (!driver) driver = Undo.AddComponent<PlayerAnimationDriver>(player);
            SetRef(driver, "animator", animator);
            SetRef(driver, "animationSet", animationSet);
            SetRef(driver, "model", inst.transform);
            SetRef(driver, "attackSlotA", slotA);
            SetRef(driver, "attackSlotB", slotB);

            // 무기를 손에 붙인다. 소켓 방향은 바인드 포즈 기준 캐릭터 정면 → 어색하면 소켓을 돌려서 맞추면 됨
            var loadout = player.GetComponent<PlayerWeaponLoadout>();
            if (loadout && animator.isHuman)
            {
                SetRef(loadout, "rightHandSocket", MakeSocket(animator, HumanBodyBones.RightHand, "Weapon Socket R", player.transform));
                SetRef(loadout, "leftHandSocket", MakeSocket(animator, HumanBodyBones.LeftHand, "Weapon Socket L", player.transform));
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(player.scene);
            Selection.activeGameObject = inst;
            Log("완료: Player에 캐릭터를 적용했어요. 씬을 저장하고 Play 해보세요.");
        }

        static Transform MakeSocket(Animator animator, HumanBodyBones bone, string name, Transform root)
        {
            var hand = animator.GetBoneTransform(bone);
            if (!hand) return null;
            var socket = new GameObject(name).transform;
            Undo.RegisterCreatedObjectUndo(socket.gameObject, name);
            socket.SetParent(hand, false);
            socket.localPosition = Vector3.zero;
            socket.rotation = root.rotation;
            return socket;
        }

        static void SetRef(Object owner, string field, Object value)
        {
            if (!owner) return;
            var so = new SerializedObject(owner);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[Character Setup] {owner.GetType().Name}.{field} 없음"); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data")) AssetDatabase.CreateFolder("Assets/_Project", "Data");
            if (!AssetDatabase.IsValidFolder(OutFolder)) AssetDatabase.CreateFolder("Assets/_Project/Data", "Character");
        }
    }
}
