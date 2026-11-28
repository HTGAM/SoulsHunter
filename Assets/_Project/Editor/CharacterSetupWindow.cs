using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables; // SetTime, SetSourcePlayable 확장 메서드
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
                if (GUILayout.Button("④ 공격 타격 지점 자동 찾기 (손 움직임 분석)", GUILayout.Height(28))) { log = ""; DetectHitPoints(animationSet); }
            }
            using (new EditorGUI.DisabledScope(!animationSet))
            {
                if (GUILayout.Button("⑤ 검·방패 위치만 다시 맞추기", GUILayout.Height(28))) RefitSocketsOnly();
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
            // 처음부터 다시 분류
            foreach (var f in typeof(CharacterAnimationSet).GetFields())
                if (f.FieldType == typeof(AnimationClip)) f.SetValue(set, null);
            set.attacks.Clear();

            var lights = new List<AnimationClip>();
            var heavies = new List<AnimationClip>();
            var moves = new List<(AnimationClip clip, string name)>();
            var impacts = new List<AnimationClip>();
            var blocks = new List<AnimationClip>();

            // "idle" → "idle (2)" → "idle (3)" 순서가 되도록 정렬 (번호 없는 기본 동작 우선)
            var files = AnimationFiles().OrderBy(p => BaseName(p)).ThenBy(p => SuffixNumber(p));

            foreach (var path in files)
            {
                string file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                {
                    if (clip.name.StartsWith("__preview__")) continue;
                    string key = file + " " + clip.name.ToLowerInvariant();
                    string slot = Classify(key);
                    string label = $"{Path.GetFileName(path)}";
                    switch (slot)
                    {
                        case null: Log($"  분류 못 함: {label}"); continue;
                        case "skip": Log($"  안 씀:     {label}"); continue;
                        case "light": lights.Add(clip); break;
                        case "heavy": heavies.Add(clip); break;
                        case "move": moves.Add((clip, key)); continue; // 아래에서 방향으로 분류
                        case "impact": impacts.Add(clip); break;
                        case "block": blocks.Add(clip); break;
                        default:
                            if (!TryAssign(set, slot, clip)) { Log($"  중복:     {label} ({slot} 이미 있음)"); continue; }
                            break;
                    }
                    Log($"{slot,-11} ← {label}");
                }
            }

            AssignMoves(set, moves);

            // 피격 여러 개: 첫째 = 피격, 둘째 = 경직
            if (impacts.Count > 0) TryAssign(set, "hit", impacts[0]);
            if (impacts.Count > 1) TryAssign(set, "stagger", impacts[1]);
            // 막기 동작(반복 아닌 것): 첫째 = 패링, 둘째 = 가드 붕괴
            if (blocks.Count > 0) TryAssign(set, "parry", blocks[0]);
            if (blocks.Count > 1) TryAssign(set, "guardBreak", blocks[1]);

            // 한쪽 공격이 하나도 없으면 다른 쪽과 나눠 쓴다
            if (lights.Count == 0) lights.AddRange(heavies);
            if (heavies.Count == 0) heavies.AddRange(lights);
            foreach (var clip in lights)
                set.attacks.Add(new CharacterAnimationSet.AttackClip { type = PlayerCombat.AttackType.Light, clip = clip, impactPoint = 0.4f });
            foreach (var clip in heavies)
                set.attacks.Add(new CharacterAnimationSet.AttackClip { type = PlayerCombat.AttackType.Heavy, clip = clip, impactPoint = 0.45f });

            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();

            Log("\n── 결과 ──");
            foreach (var f in typeof(CharacterAnimationSet).GetFields().Where(f => f.FieldType == typeof(AnimationClip)))
            {
                var c = (AnimationClip)f.GetValue(set);
                Log($"{f.Name,-12} {(c ? ClipLabel(c) : "(없음)")}");
            }
            Log($"약공격 {lights.Count}개, 강공격 {heavies.Count}개");
            if (model) DetectHitPoints(set);
            Selection.activeObject = set;
            return set;
        }

        // ───────── ④ 타격 지점 자동 찾기 ─────────

        /// <summary>
        /// 공격 클립을 모델에 재생해 보면서 오른손(무기)이 가장 빠르게 움직이는 순간들을 찾는다.
        /// 그 순간이 휘두르기의 한가운데 = 타격 지점. 여러 번 휘두르는 클립은 여러 개가 나온다.
        /// </summary>
        void DetectHitPoints(CharacterAnimationSet set)
        {
            if (!model || !set) return;
            var go = (GameObject)Instantiate(model);
            go.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy; // 샘플링이 되도록 편집 가능한 상태로 둔다
            go.transform.SetPositionAndRotation(new Vector3(0f, -1000f, 0f), Quaternion.identity);
            bool startedMode = !AnimationMode.InAnimationMode();
            if (startedMode) AnimationMode.StartAnimationMode();
            try
            {
                var animator = go.GetComponent<Animator>();
                if (!animator || !animator.isHuman)
                {
                    Log("타격 지점 찾기 실패: 모델이 Humanoid가 아니에요. ① 버튼을 먼저 눌러주세요.");
                    return;
                }
                animator.applyRootMotion = false;
                var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);

                // 서 있을 때(바인드 포즈) 골반 높이 → 앉아서 하는 동작 판별 기준
                float standingHips = hips ? hips.position.y - go.transform.position.y : 1f;
                crouchClips.Clear();

                Log("\n── 타격 지점 ──");
                foreach (var entry in set.attacks)
                {
                    if (!entry.clip) continue;
                    var clip = entry.clip;
                    int samples = Mathf.Clamp(Mathf.RoundToInt(clip.length * 60f), 30, 600);
                    float dt = clip.length / samples;

                    // 1차: Animation 창과 같은 방식(AnimationMode). 안 움직이면 2차: Playable 그래프
                    var hipsY = new float[samples + 1];
                    var positions = SampleWithAnimationMode(go, clip, hand, hips, samples, dt, hipsY);
                    string method = "AnimationMode";
                    if (TotalMovement(positions) < 0.01f)
                    {
                        positions = SampleWithPlayables(animator, clip, hand, hips, samples, dt, hipsY);
                        method = "Playables";
                    }

                    // 동작 대부분을 골반이 낮게(서 있을 때의 80% 미만) 있으면 앉아서 하는 공격
                    float baseY = go.transform.position.y;
                    float medianHips = hipsY.Select(y => y - baseY).OrderBy(y => y).ElementAt(hipsY.Length / 2);
                    bool crouching = standingHips > 0.1f && medianHips < standingHips * 0.8f;
                    if (crouching) crouchClips.Add(clip);

                    entry.hitPoints = FindSwingPeaks(positions, dt, clip.length, out float maxSpeed);
                    if (entry.hitPoints.Count > 0) entry.impactPoint = Mathf.Clamp(entry.hitPoints[0], 0.05f, 0.95f);
                    string pts = string.Join(", ", entry.hitPoints.Select(p => p.ToString("0.00")));
                    Log($"{entry.type,-6} {entry.hitPoints.Count}타 @ {pts,-22} 손 최고속도 {maxSpeed:0.0} m/s, 길이 {clip.length:0.00}초" +
                        $"{(crouching ? "  [앉은 자세]" : "")} [{method}]  ({ClipLabel(clip)})");
                }
                ArrangeAttacks(set);
                EditorUtility.SetDirty(set);
                AssetDatabase.SaveAssets();
                Log("어색한 곳은 세트 에셋의 Hit Points 값을 직접 고치면 돼요 (0~1, 클립 길이 대비).");
            }
            finally
            {
                if (startedMode) AnimationMode.StopAnimationMode();
                DestroyImmediate(go);
            }
        }

        const int MinLightCombo = 3;
        const float MaxLightLength = 2f;
        readonly HashSet<AnimationClip> crouchClips = new();

        /// <summary>
        /// 공격 클립을 콤보에 어울리게 정리한다.
        /// - 약공격: 짧고(2초 이하) 한 번 휘두르는 베기 → 짧은 순서대로 (빠른 연타)
        /// - 강공격: 나머지(긴 동작, 여러 번 휘두르는 동작, 큰 공격) → 짧은 순서대로
        /// 콤보는 목록 순서대로 쓰이므로(1타 = 첫 번째) 순서가 곧 콤보 순서다.
        /// </summary>
        void ArrangeAttacks(CharacterAnimationSet set)
        {
            var generic = set.attacks.Where(a => a.clip && !a.weapon).ToList();
            if (generic.Count == 0) return;

            static bool LooksLight(CharacterAnimationSet.AttackClip a)
            {
                string n = ClipLabel(a.clip).ToLowerInvariant();
                return Has(n, "slash", "swing", "stab", "thrust", "strike", "punch");
            }
            static int Hits(CharacterAnimationSet.AttackClip a) => a.hitPoints?.Count > 0 ? a.hitPoints.Count : 1;

            // 앉아서 하는 공격은 콤보에 어울리지 않으므로 맨 뒤(쓰이지 않는 자리)로 보낸다
            bool Crouch(CharacterAnimationSet.AttackClip a) => crouchClips.Contains(a.clip);

            var lights = generic.Where(a => !Crouch(a) && LooksLight(a) && Hits(a) == 1 && a.clip.length <= MaxLightLength)
                                .OrderBy(a => a.clip.length).ToList();
            var heavies = generic.Except(lights).OrderBy(a => Crouch(a)).ThenBy(a => a.clip.length).ToList();

            // 약공격이 모자라면 강공격 중 짧은 한 번 휘두르기를 데려온다
            while (lights.Count < MinLightCombo)
            {
                var pick = heavies.FirstOrDefault(a => !Crouch(a) && Hits(a) == 1) ?? heavies.FirstOrDefault(a => !Crouch(a));
                if (pick == null) break;
                heavies.Remove(pick);
                lights.Add(pick);
            }

            foreach (var a in lights) { a.type = PlayerCombat.AttackType.Light; a.comboIndex = -1; }
            foreach (var a in heavies) { a.type = PlayerCombat.AttackType.Heavy; a.comboIndex = -1; }

            set.attacks.RemoveAll(a => generic.Contains(a));
            set.attacks.InsertRange(0, lights.Concat(heavies));

            Log("\n── 콤보 순서 (자동 정리) ──");
            for (int i = 0; i < lights.Count; i++)
                Log($"약공격 {i + 1}: {ClipLabel(lights[i].clip),-34} {lights[i].clip.length:0.00}초 {Hits(lights[i])}타");
            for (int i = 0; i < heavies.Count; i++)
                Log($"강공격 {i + 1}: {ClipLabel(heavies[i].clip),-34} {heavies[i].clip.length:0.00}초 {Hits(heavies[i])}타");
            Log("※ 무기의 콤보가 N단계면 앞에서부터 N개를 쓰고, 그보다 많으면 처음부터 다시 돌아요.");
        }

        // 몸통 이동은 빼고 손이 몸 기준으로 어디 있는지
        static Vector3 HandPoint(Transform hand, Transform hips) =>
            hand.position - (hips ? new Vector3(hips.position.x, 0f, hips.position.z) : Vector3.zero);

        static Vector3[] SampleWithAnimationMode(GameObject go, AnimationClip clip, Transform hand, Transform hips, int samples, float dt, float[] hipsY)
        {
            var pos = new Vector3[samples + 1];
            for (int i = 0; i <= samples; i++)
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(go, clip, i * dt);
                AnimationMode.EndSampling();
                pos[i] = HandPoint(hand, hips);
                hipsY[i] = hips ? hips.position.y : 0f;
            }
            return pos;
        }

        static Vector3[] SampleWithPlayables(Animator animator, AnimationClip clip, Transform hand, Transform hips, int samples, float dt, float[] hipsY)
        {
            var pos = new Vector3[samples + 1];
            animator.Rebind();
            var graph = PlayableGraph.Create("HitPointScan");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "out", animator);
                var playable = UnityEngine.Animations.AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false);
                output.SetSourcePlayable(playable);
                graph.Play();
                for (int i = 0; i <= samples; i++)
                {
                    playable.SetTime(i * dt);
                    graph.Evaluate();
                    pos[i] = HandPoint(hand, hips);
                    hipsY[i] = hips ? hips.position.y : 0f;
                }
            }
            finally
            {
                graph.Destroy();
            }
            return pos;
        }

        static float TotalMovement(Vector3[] pos)
        {
            float sum = 0f;
            for (int i = 1; i < pos.Length; i++) sum += (pos[i] - pos[i - 1]).magnitude;
            return sum;
        }

        static List<float> FindSwingPeaks(Vector3[] positions, float dt, float length, out float maxSpeed)
        {
            var result = new List<float>();
            maxSpeed = 0f;
            if (positions.Length < 3 || length <= 0f) return result;
            var clip = new { length };

            {
                var speed = new float[positions.Length];
                for (int i = 1; i < positions.Length; i++) speed[i] = (positions[i] - positions[i - 1]).magnitude / dt;
                speed[0] = speed[1];

                // 살짝 부드럽게 (튀는 값 제거)
                var smooth = new float[speed.Length];
                for (int i = 0; i < speed.Length; i++)
                {
                    float sum = 0f; int n = 0;
                    for (int k = -2; k <= 2; k++)
                    {
                        int j = i + k;
                        if (j < 0 || j >= speed.Length) continue;
                        sum += speed[j]; n++;
                    }
                    smooth[i] = sum / n;
                }

                float max = smooth.Max();
                maxSpeed = max;
                if (max < 0.5f) return result; // 거의 안 움직이는 클립
                float threshold = max * 0.55f;
                float minGap = Mathf.Max(0.3f, clip.length * 0.12f); // 휘두르기 사이 최소 간격(초). 더 가까우면 한 번 휘두른 것으로 본다

                // 임계값을 넘는 구간마다 가장 빠른 지점 하나
                int bestIndex = -1;
                for (int i = 0; i < smooth.Length; i++)
                {
                    if (smooth[i] >= threshold)
                    {
                        if (bestIndex < 0 || smooth[i] > smooth[bestIndex]) bestIndex = i;
                    }
                    else if (bestIndex >= 0)
                    {
                        AddPeak(result, bestIndex * dt / clip.length, minGap / clip.length);
                        bestIndex = -1;
                    }
                }
                if (bestIndex >= 0) AddPeak(result, bestIndex * dt / clip.length, minGap / clip.length);

                // 너무 처음/끝에 있는 건 준비·마무리 동작이므로 제외, 최대 5타
                result.RemoveAll(p => p < 0.08f || p > 0.92f);
                if (result.Count > 5) result = result.Take(5).ToList();
            }
            return result;
        }

        static void AddPeak(List<float> peaks, float t, float minGap)
        {
            if (peaks.Count > 0 && t - peaks[^1] < minGap) return;
            peaks.Add(t);
        }

        static bool TryAssign(CharacterAnimationSet set, string slot, AnimationClip clip)
        {
            var field = typeof(CharacterAnimationSet).GetField(slot);
            if (field == null || (field.GetValue(set) is AnimationClip existing && existing)) return false;
            field.SetValue(set, clip);
            return true;
        }

        static string ClipLabel(AnimationClip c) => Path.GetFileName(AssetDatabase.GetAssetPath(c));

        /// <summary>
        /// 걷기/달리기/옆걸음: 이름에 방향이 없는 경우가 많아서(“strafe (2)”)
        /// 애니메이션이 실제로 움직이는 방향과 속도(루트 모션)로 분류한다.
        /// </summary>
        void AssignMoves(CharacterAnimationSet set, List<(AnimationClip clip, string name)> moves)
        {
            var fwd = new List<(AnimationClip c, float speed, string name)>();
            var back = new List<(AnimationClip c, float speed, string name)>();
            var left = new List<(AnimationClip c, float speed, string name)>();
            var right = new List<(AnimationClip c, float speed, string name)>();

            foreach (var (clip, name) in moves)
            {
                Vector3 v = clip.averageSpeed;
                float speed = new Vector2(v.x, v.z).magnitude;
                string dir;
                if (speed < 0.05f) dir = Has(name, "back") ? "back" : Has(name, "left") ? "left" : Has(name, "right") ? "right" : "fwd";
                else if (Mathf.Abs(v.z) >= Mathf.Abs(v.x)) dir = v.z > 0f ? "fwd" : "back";
                else dir = v.x > 0f ? "right" : "left";

                (dir switch { "fwd" => fwd, "back" => back, "left" => left, _ => right }).Add((clip, speed, name));
                Log($"{"이동",-11} ← {ClipLabel(clip)}  방향 {dir}, 속도 {speed:0.00} m/s");
            }

            if (moves.Count > 0 && moves.All(m => m.clip.averageSpeed.sqrMagnitude < 0.0025f))
                Log("경고: 이동 애니메이션의 루트 모션이 없어요. ① 버튼을 먼저 눌렀는지 확인하세요 (In Place 클립이면 이름으로만 분류됨).");

            // 앞: 가장 느린 것 = 걷기, 가장 빠른 것 = 달리기, 그보다 훨씬 빠르면 전력 질주
            fwd.Sort((a, b) => a.speed.CompareTo(b.speed));
            if (fwd.Count == 1)
                TryAssign(set, Has(fwd[0].name, "walk") ? "walk" : "run", fwd[0].c);
            else if (fwd.Count >= 2)
            {
                TryAssign(set, "walk", fwd[0].c);
                var fastest = fwd[^1];
                var second = fwd[^2];
                if (fwd.Count >= 3 && fastest.speed > second.speed * 1.35f)
                {
                    TryAssign(set, "sprint", fastest.c);
                    TryAssign(set, "run", second.c);
                }
                else TryAssign(set, "run", fastest.c);
            }

            // 뒤/옆: 블렌드 트리에서 달리기 속도 위치에 두므로 가장 빠른 것
            if (back.Count > 0) TryAssign(set, "runBackward", back.OrderBy(x => x.speed).Last().c);
            if (left.Count > 0) TryAssign(set, "strafeLeft", left.OrderBy(x => x.speed).Last().c);
            if (right.Count > 0) TryAssign(set, "strafeRight", right.OrderBy(x => x.speed).Last().c);
        }

        static string BaseName(string path)
        {
            string n = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            int i = n.LastIndexOf(" (");
            return i > 0 && n.EndsWith(")") ? n.Substring(0, i) : n;
        }

        static int SuffixNumber(string path)
        {
            string n = Path.GetFileNameWithoutExtension(path);
            int i = n.LastIndexOf(" (");
            return i > 0 && n.EndsWith(")") && int.TryParse(n.Substring(i + 2, n.Length - i - 3), out int k) ? k : 1;
        }

        static bool Has(string s, params string[] words) => words.Any(s.Contains);

        /// <summary>
        /// 파일/클립 이름으로 분류한다 (Mixamo 이름 기준).
        /// "move"는 방향을 루트 모션으로 다시 판단하고, "skip"은 쓰지 않는 동작이다.
        /// </summary>
        static string Classify(string n)
        {
            if (Has(n, "crouch", "draw sword", "sheath", "turn", "casting", "power up", "kick", "t-pose", "tpose")) return "skip";
            if (Has(n, "death", "dying")) return "death";
            if (Has(n, "drink", "potion")) return "drink";
            if (Has(n, "parry")) return "parry";
            if (Has(n, "counter")) return "counter";
            if (Has(n, "block", "guard")) return Has(n, "idle") ? "guard" : "block";
            if (Has(n, "backstep", "step back", "hop back") || (Has(n, "dodge", "step") && Has(n, "back"))) return "backstep";
            if (Has(n, "roll", "dodge", "dive")) return "roll";
            if (Has(n, "stagger", "knock", "stumble")) return "stagger";
            if (Has(n, "hit", "impact", "react")) return "impact";
            // 짧고 빠른 베기 = 약공격, 큰 동작(attack, 내려찍기 등) = 강공격
            if (Has(n, "slash", "swing", "stab", "thrust", "combo", "strike", "punch"))
                return Has(n, "heavy", "power", "charge", "overhead", "downward") ? "heavy" : "light";
            if (Has(n, "attack")) return "heavy";
            if (Has(n, "jump")) return "jump";
            if (Has(n, "sprint")) return "sprint";
            if (Has(n, "strafe", "walk", "run", "jog")) return "move";
            if (Has(n, "idle")) return "idle";
            return null;
        }

        static bool IsLooping(string slot) => slot is "idle" or "move" or "sprint" or "guard";

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
            var controller = BuildController(animationSet, player, out var slotA, out var slotB, out var clipSpeeds);
            ApplyToPlayer(player, controller, slotA, slotB, clipSpeeds);
            // Build Test Arena가 플레이어를 다시 만들 때 같은 모델을 입히도록 기억해 둔다
            EditorPrefs.SetString(ModelPathPref, AssetDatabase.GetAssetPath(model));
        }

        const string ModelPathPref = "SoulsHunter.CharacterModelPath";
        const string DefaultModelPath = "Assets/_Project/Art/character.fbx";

        /// <summary>
        /// 씬의 Player에 캐릭터 모델을 다시 입힌다 (Build Test Arena가 플레이어를 새로 만든 뒤 호출).
        /// 모델은 preferred → 마지막으로 Character Setup에서 쓴 모델 → 기본 경로 순으로 찾는다.
        /// 애니메이션 세트가 아직 없으면(Character Setup을 한 번도 안 했으면) 아무것도 하지 않는다.
        /// </summary>
        /// <returns>입혔으면 true</returns>
        public static bool ReapplyToScene(GameObject preferredModel, out string report)
        {
            report = "";
            var player = GameObject.FindWithTag("Player");
            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(SetPath);
            var modelAsset = preferredModel;
            if (!modelAsset) modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(EditorPrefs.GetString(ModelPathPref, ""));
            if (!modelAsset) modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultModelPath);
            if (!player || !set || !modelAsset)
            {
                report = "캐릭터 모델 또는 애니메이션 세트가 없어서 건너뛰었어요 (SoulsHunter/Character Setup).";
                return false;
            }

            EnsureCharacterMaterial(modelAsset, out _);
            var tool = CreateInstance<CharacterSetupWindow>();
            try
            {
                tool.model = modelAsset;
                tool.animationSet = set;
                EnsureFolder();
                var controller = tool.BuildController(set, player, out var slotA, out var slotB, out var clipSpeeds);
                tool.ApplyToPlayer(player, controller, slotA, slotB, clipSpeeds);
                report = tool.log;
            }
            finally
            {
                DestroyImmediate(tool);
            }
            EditorPrefs.SetString(ModelPathPref, AssetDatabase.GetAssetPath(modelAsset));
            return true;
        }

        [MenuItem("SoulsHunter/Fix Character Materials")]
        static void FixMaterialsMenu()
        {
            var model = CurrentModelAsset();
            if (!model) model = AssetDatabase.LoadAssetAtPath<GameObject>(EditorPrefs.GetString(ModelPathPref, DefaultModelPath));
            if (!model) model = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultModelPath);
            var mat = EnsureCharacterMaterial(model, out string report);
            EditorUtility.DisplayDialog("SoulsHunter", mat ? $"캐릭터 재질을 URP로 만들었어요.\n{report}" : $"재질을 만들지 못했어요.\n{report}", "확인");
        }

        /// <summary>
        /// 모델 FBX에 묻혀 있는 텍스처를 꺼내 URP Lit 재질을 만들고, 모델의 모든 재질을 그걸로 바꾼다 (하얗게 보이는 문제 해결).
        /// 이미 바꿔 두었으면 그대로 둔다.
        /// </summary>
        public static Material EnsureCharacterMaterial(GameObject modelAsset, out string report)
        {
            report = "";
            string path = modelAsset ? AssetDatabase.GetAssetPath(modelAsset) : "";
            if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
            {
                report = "캐릭터 모델 FBX를 찾지 못했어요.";
                return null;
            }

            string dir = Path.GetDirectoryName(path).Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(path);
            string folder = $"{dir}/{name}_Textures";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder(dir, $"{name}_Textures");
                importer.ExtractTextures(folder);
                AssetDatabase.Refresh();
            }

            Texture2D diffuse = null, normal = null, spec = null;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                string tp = AssetDatabase.GUIDToAssetPath(guid);
                string lower = Path.GetFileNameWithoutExtension(tp).ToLowerInvariant();
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
                if (lower.Contains("normal") || lower.Contains("nrm"))
                {
                    if (AssetImporter.GetAtPath(tp) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
                    {
                        ti.textureType = TextureImporterType.NormalMap;
                        ti.SaveAndReimport();
                    }
                    normal = tex;
                }
                else if (lower.Contains("spec") || lower.Contains("gloss")) spec = tex;
                else if (lower.Contains("diffuse") || lower.Contains("albedo") || lower.Contains("base") || lower.Contains("color") || !diffuse) diffuse = tex;
            }
            if (!diffuse)
            {
                report = "모델 안에 텍스처가 없어요.";
                return null;
            }

            string matPath = $"{folder}/{name}_URP.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", diffuse);
            mat.SetTexture("_MainTex", diffuse);
            mat.SetColor("_BaseColor", Color.white);
            if (normal)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (spec)
            {
                // 스페큘러 워크플로 (Mixamo 텍스처 구성)
                mat.SetFloat("_WorkflowMode", 0f);
                mat.SetTexture("_SpecGlossMap", spec);
                mat.EnableKeyword("_SPECULAR_SETUP");
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_Smoothness", 0.45f);
            }
            else
            {
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Smoothness", 0.35f);
            }
            EditorUtility.SetDirty(mat);

            // 모델의 재질을 전부 이 재질로
            bool changed = false;
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name);
                if (importer.GetExternalObjectMap().TryGetValue(id, out var existing) && existing == mat) continue;
                importer.AddRemap(id, mat);
                changed = true;
            }
            if (changed) importer.SaveAndReimport();
            AssetDatabase.SaveAssets();
            report = $"{Path.GetFileName(path)} → {matPath}";
            return mat;
        }

        /// <summary>Player 아래에 입혀 둔 캐릭터 모델의 원본 에셋 (없으면 null)</summary>
        public static GameObject CurrentModelAsset()
        {
            var player = GameObject.FindWithTag("Player");
            var model = player ? player.transform.Find("Model") : null;
            return model ? PrefabUtility.GetCorrespondingObjectFromOriginalSource(model.gameObject) : null;
        }

        /// <summary>컴포넌트의 숫자 필드 값을 읽는다 (없으면 기본값)</summary>
        static float ReadFloat<T>(GameObject go, string field, float fallback) where T : Component
        {
            var c = go.GetComponent<T>();
            var p = c ? new SerializedObject(c).FindProperty(field) : null;
            return p != null ? p.floatValue : fallback;
        }

        /// <summary>클립이 실제로 움직이는 속도(m/s, 수평). 루트 모션이 없으면 0</summary>
        static Vector2 ClipVelocity(AnimationClip c) => c ? new Vector2(c.averageSpeed.x, c.averageSpeed.z) : Vector2.zero;

        AnimatorController BuildController(CharacterAnimationSet set, GameObject player,
                                           out AnimationClip slotA, out AnimationClip slotB, out Vector4 clipSpeeds)
        {
            float runSpeed = ReadFloat<PlayerLocomotion>(player, "runSpeed", 4.5f);
            float sprintSpeed = ReadFloat<PlayerLocomotion>(player, "sprintSpeed", 7f);
            float drinkDuration = ReadFloat<PlayerFlask>(player, "drinkDuration", 1.1f);
            float staggerDuration = ReadFloat<PlayerDamageHandler>(player, "staggerDuration", 0.6f);
            // 열린 Animator 창이 지워진 옛 컨트롤러를 그리려다 NullReferenceException을 내므로 먼저 닫는다
            bool reopenAnimatorWindow = CloseAnimatorWindows();
            if (AssetDatabase.LoadAssetAtPath<Object>(ControllerPath)) AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            if (reopenAnimatorWindow) EditorApplication.delayCall += () => EditorApplication.ExecuteMenuItem("Window/Animation/Animator");

            foreach (var f in new[] { "Speed", "MoveX", "MoveY", "AttackTime", "DodgeTime" })
                ctrl.AddParameter(f, AnimatorControllerParameterType.Float);
            foreach (var b in new[] { "LockedOn", "Attacking", "Dodging", "Dead", "Guard", "Grounded", "Charging", "DemonMode" })
                ctrl.AddParameter(b, AnimatorControllerParameterType.Bool);
            foreach (var t in new[] { "AttackA", "AttackB", "Roll", "Backstep", "Hit", "Stagger", "Drink", "Parry", "GuardBreak", "Vault", "Counter" })
                ctrl.AddParameter(t, AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("ComboIndex", AnimatorControllerParameterType.Int);
            ctrl.AddParameter(new AnimatorControllerParameter { name = "LocoSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

            // 공격 슬롯용 빈 클립 (실행 중 실제 공격 클립으로 갈아 끼움)
            slotA = new AnimationClip { name = "__AttackSlotA" };
            slotB = new AnimationClip { name = "__AttackSlotB" };
            AssetDatabase.AddObjectToAsset(slotA, ctrl);
            AssetDatabase.AddObjectToAsset(slotB, ctrl);

            var sm = ctrl.layers[0].stateMachine;

            // 이동: 2D 블렌드 트리 (MoveX = 옆, MoveY = 앞뒤, 단위 = 코드의 달리기 속도)
            // 각 클립을 '실제로 움직이는 속도' 위치에 둬서 걸음과 이동 속도가 맞게 한다.
            var loco = ctrl.CreateBlendTreeInController("Locomotion", out var tree, 0);
            loco.iKOnFeet = true; // 발이 땅에 붙어 보이게 (Humanoid 발 IK)
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = "MoveX";
            tree.blendParameterY = "MoveY";
            void Add(AnimationClip c, Vector2 fallback)
            {
                if (!c) return;
                Vector2 v = ClipVelocity(c);
                Vector2 pos = v.sqrMagnitude > 0.01f ? v / runSpeed : fallback;
                tree.AddChild(c, pos);
                if (c != set.idle) Log($"블렌드 위치 {c.name,-28} ({pos.x:0.00}, {pos.y:0.00})  ← {v.magnitude:0.00} m/s");
            }
            Add(set.idle, Vector2.zero);
            Add(set.walk, new Vector2(0f, 0.45f));
            Add(set.run, new Vector2(0f, 1f));
            // 전력 질주는 루트 모션 속도와 상관없이 실제 질주 속도 자리에 둔다 (달리기와 겹치면 둘이 섞여 어색해진다)
            if (set.sprint)
            {
                tree.AddChild(set.sprint, new Vector2(0f, Mathf.Max(1.2f, sprintSpeed / runSpeed)));
                Log($"블렌드 위치 {set.sprint.name,-28} (0.00, {Mathf.Max(1.2f, sprintSpeed / runSpeed):0.00})  ← 전력 질주 {sprintSpeed:0.0} m/s");
            }
            Add(set.runBackward, new Vector2(0f, -1f));
            Add(set.strafeLeft, new Vector2(-1f, 0f));
            Add(set.strafeRight, new Vector2(1f, 0f));
            sm.defaultState = loco;

            // 가장 바깥 클립보다 빨리 움직이면(전력 질주 등) 애니메이션을 빨리 재생한다 → PlayerAnimationDriver가 LocoSpeed 계산
            loco.speedParameterActive = true;
            loco.speedParameter = "LocoSpeed";
            float Speed(params AnimationClip[] clips)
            {
                foreach (var c in clips) { float s = ClipVelocity(c).magnitude; if (s > 0.1f) return s; }
                return runSpeed;
            }
            clipSpeeds = new Vector4(
                Speed(set.run, set.walk), // 앞 방향 기준은 달리기 (질주 구간은 PlayerAnimationDriver가 따로 계산)
                Speed(set.runBackward),
                Speed(set.strafeLeft),
                Speed(set.strafeRight));

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
                t.hasFixedDuration = true;
                t.duration = duration;
                t.canTransitionToSelf = false;
                t.AddCondition(mode, 0f, param);
                return t;
            }
            // 전환 시간은 모두 초 단위 (몬헌처럼 동작 사이를 부드럽게 섞는다)
            void BackByCondition(AnimatorState s, string param, AnimatorConditionMode mode, float duration = 0.25f)
            {
                var t = s.AddTransition(loco);
                t.hasExitTime = false;
                t.hasFixedDuration = true;
                t.duration = duration;
                t.AddCondition(mode, 0f, param);
            }
            void BackByExitTime(AnimatorState s, float exitTime = 0.85f)
            {
                var t = s.AddTransition(loco);
                t.hasExitTime = true;
                t.exitTime = exitTime;
                t.hasFixedDuration = true;
                t.duration = 0.25f;
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
                AnyTo(s, 0.15f, name);   // 이전 동작(이동·이전 공격)에서 부드럽게 넘어감
                BackByCondition(s, "Attacking", AnimatorConditionMode.IfNot, 0.3f); // 마무리 후 대기 자세로 천천히
            }

            // 회피: 재생 위치를 DodgeTime으로 구동
            foreach (var (name, clip) in new[] { ("Roll", set.roll), ("Backstep", set.backstep ? set.backstep : set.roll) })
            {
                if (!clip) continue;
                var s = State(name, clip, name == "Roll" ? 3 : 4, 0);
                s.timeParameterActive = true;
                s.timeParameter = "DodgeTime";
                AnyTo(s, 0.08f, name);
                BackByCondition(s, "Dodging", AnimatorConditionMode.IfNot, 0.22f);
            }

            // 한 번 재생하고 돌아오는 동작들. targetDuration > 0이면 코드의 동작 시간에 맞춰 재생 속도를 바꾼다.
            int row = 1;
            foreach (var (name, clip, trigger, targetDuration, cancelByMoving) in new[]
                     {
                         ("Hit", set.hit, "Hit", 0.5f, true),
                         ("Stagger", set.stagger ? set.stagger : set.hit, "Stagger", staggerDuration, false),
                         ("Drink", set.drink, "Drink", drinkDuration, false),
                         ("Parry", set.parry, "Parry", 0f, true),
                         ("GuardBreak", set.guardBreak ? set.guardBreak : set.hit, "GuardBreak", 0f, true),
                         ("Counter", set.counter ? set.counter : set.parry, "Counter", 0f, true),
                         ("Jump", set.jump, "Vault", 0f, false),
                     })
            {
                if (!clip) continue;
                var s = State(name, clip, row++, 2);
                if (targetDuration > 0f && clip.length > 0f)
                {
                    // 클립의 85% 지점에서 돌아가므로 그 시점이 targetDuration이 되도록
                    s.speed = Mathf.Clamp(clip.length * 0.85f / targetDuration, 0.5f, 6f);
                    Log($"{name} 재생 속도 x{s.speed:0.00} (클립 {clip.length:0.00}초 → 약 {targetDuration:0.00}초)");
                }
                AnyTo(s, 0.1f, trigger);
                BackByExitTime(s);
                if (cancelByMoving)
                {
                    // 경직이 없는 가벼운 반응은 움직이기 시작하면 바로 끊는다 (미끄러지는 것처럼 보이지 않게)
                    var t = s.AddTransition(loco);
                    t.hasExitTime = false;
                    t.duration = 0.15f;
                    t.AddCondition(AnimatorConditionMode.Greater, 1f, "Speed");
                }
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

        void ApplyToPlayer(GameObject player, AnimatorController controller, AnimationClip slotA, AnimationClip slotB, Vector4 clipSpeeds)
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
            var dso = new SerializedObject(driver);
            dso.FindProperty("locomotionClipSpeeds").vector4Value = clipSpeeds;
            dso.FindProperty("sprintPlayback").floatValue = animationSet && animationSet.sprint ? 1.15f : 0f;
            dso.ApplyModifiedProperties();

            // 검은 오른손, 방패는 왼쪽 팔뚝에 붙인다
            FitWeaponSockets(player, animator);

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(player.scene);
            Selection.activeGameObject = inst;
            Log("완료: Player에 캐릭터를 적용했어요. 씬을 저장하고 Play 해보세요.");
        }

        const string RightSocketName = WeaponSocketFitter.RightSocketName;
        const string LeftSocketName = WeaponSocketFitter.LeftSocketName;

        /// <summary>
        /// 무기 소켓을 손·팔 뼈에 맞춰 만든다.
        /// - 검(오른손): 주먹 가운데, 칼날은 새끼손가락 → 검지 방향(실제로 검을 쥐었을 때의 축)
        /// - 방패(왼팔): 팔뚝 바깥쪽, 팔뚝을 따라 붙고 앞면은 몸 바깥을 향함 (대기 자세 기준)
        /// 무기 부품 좌표의 손잡이·방패 중심이 소켓에 오도록 Grip도 같이 맞춘다.
        /// </summary>
        void FitWeaponSockets(GameObject player, Animator animator)
        {
            var loadout = player.GetComponent<PlayerWeaponLoadout>();
            if (!loadout || !animator || !animator.isHuman) return;
            // 대기 자세로 잠깐 포즈를 잡고 계산 (끝나면 원래대로 돌아감). 계산식은 실행 중 자동 정렬과 같다.
            WeaponSocketFitter.Socket right, left;
            bool started = !AnimationMode.InAnimationMode();
            if (started) AnimationMode.StartAnimationMode();
            try
            {
                if (animationSet && animationSet.idle)
                {
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(animator.gameObject, animationSet.idle, 0f);
                    AnimationMode.EndSampling();
                }
                WeaponSocketFitter.Compute(animator, player.transform, out right, out left);
            }
            finally
            {
                if (started) AnimationMode.StopAnimationMode();
            }

            // 예전 소켓(무기 모델 포함)을 지우고 새로 만든다
            foreach (var t in animator.GetComponentsInChildren<Transform>(true))
                if (t && (t.name == RightSocketName || t.name == LeftSocketName)) Undo.DestroyObjectImmediate(t.gameObject);

            Transform Make(string name, WeaponSocketFitter.Socket s)
            {
                if (!s.valid) return null;
                var t = new GameObject(name).transform;
                Undo.RegisterCreatedObjectUndo(t.gameObject, name);
                t.SetParent(s.parent, false);
                t.localPosition = s.localPosition;
                t.localRotation = s.localRotation;
                return t;
            }

            SetRef(loadout, "rightHandSocket", Make(RightSocketName, right));
            SetRef(loadout, "leftHandSocket", Make(LeftSocketName, left));

            var so = new SerializedObject(loadout);
            so.FindProperty("rightGrip").vector3Value = WeaponSocketFitter.SwordGrip;
            so.FindProperty("leftGrip").vector3Value = WeaponSocketFitter.ShieldGrip;
            so.ApplyModifiedProperties();
            Log("무기 소켓: 검 = 오른손 주먹, 방패 = 왼쪽 팔뚝 바깥쪽");
        }

        void RefitSocketsOnly()
        {
            log = "";
            var player = GameObject.FindWithTag("Player");
            var model = player ? player.transform.Find("Model") : null;
            var animator = model ? model.GetComponent<Animator>() : null;
            if (!animator)
            {
                EditorUtility.DisplayDialog("Character Setup", "Player 아래에 Model(캐릭터)이 없어요. ③을 먼저 해주세요.", "확인");
                return;
            }
            Undo.SetCurrentGroupName("Refit Weapon Sockets");
            FitWeaponSockets(player, animator);
            EditorSceneManager.MarkSceneDirty(player.scene);
            Log("완료: 씬을 저장하고 Play 해보세요.");
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

        /// <returns>닫은 창이 있으면 true</returns>
        static bool CloseAnimatorWindows()
        {
            bool closed = false;
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (w && w.GetType().Name == "AnimatorControllerTool")
                {
                    w.Close();
                    closed = true;
                }
            }
            // 창이 쓰던 그래프 데이터도 지워야 Play 때 오류가 다시 안 난다
            AnimatorWindowRepair.DestroyOrphanGraphs();
            return closed;
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data")) AssetDatabase.CreateFolder("Assets/_Project", "Data");
            if (!AssetDatabase.IsValidFolder(OutFolder)) AssetDatabase.CreateFolder("Assets/_Project/Data", "Character");
        }
    }
}
