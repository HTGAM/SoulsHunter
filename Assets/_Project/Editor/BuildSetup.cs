using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

using SoulsHunter.UI;

namespace SoulsHunter.EditorTools
{
    /// <summary>
    /// 게임 빌드 준비.
    /// - 이펙트 머티리얼을 Resources/FX에 저장 (빌드에서 셰이더가 빠져 분홍색/안 보이는 문제 방지)
    /// - 타이틀 씬 생성, Build Settings 씬 목록(타이틀 → 게임) 설정
    /// - 메뉴 한 번으로 Windows 실행 파일 빌드
    /// </summary>
    [InitializeOnLoad]
    public static class BuildSetup
    {
        const string FxFolder = "Assets/_Project/Resources/FX";
        const string TitleScenePath = "Assets/Scenes/Title.unity";
        const string GameScenePath = "Assets/Scenes/test.unity";
        const string BuildPath = "Builds/Windows/SoulsHunter.exe";
        const string DragonPrefab = "Assets/FourEvilDragonsPBR/Prefab/DragonSoulEater/Red.prefab";
        const string DragonController = "Assets/_Project/Data/Monsters/CinderWyrm.controller";

        static BuildSetup()
        {
            // 컴파일 직후 이펙트 머티리얼이 없으면 만들어 둔다
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) EnsureFxMaterials();
            };
        }

        // ───────── 이펙트 머티리얼 ─────────

        public static void EnsureFxMaterials()
        {
            bool created = false;
            foreach (var (name, additive) in SoulsHunter.Feedback.FxResources.Templates)
            {
                string path = $"{FxFolder}/{name}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path)) continue;
                if (!AssetDatabase.IsValidFolder(FxFolder))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/_Project/Resources")) AssetDatabase.CreateFolder("Assets/_Project", "Resources");
                    AssetDatabase.CreateFolder("Assets/_Project/Resources", "FX");
                }
                AssetDatabase.CreateAsset(SoulsHunter.Feedback.FxResources.CreateTemplate(additive, name), path);
                created = true;
            }
            if (created)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("[SoulsHunter] 이펙트 머티리얼을 Resources/FX에 만들었어요 (빌드용).");
            }
        }

        // ───────── 씬 ─────────

        [MenuItem("SoulsHunter/Build/Create Title Scene")]
        static void CreateTitleSceneMenu()
        {
            if (File.Exists(TitleScenePath) &&
                !EditorUtility.DisplayDialog("SoulsHunter", "타이틀 씬이 이미 있어요. 새로 만들까요?", "새로 만들기", "취소"))
                return;
            CreateTitleScene();
            SetupBuildScenes();
            EditorUtility.DisplayDialog("SoulsHunter", "타이틀 씬을 만들고 Build Settings에 넣었어요.\n(Assets/Scenes/Title.unity)", "확인");
        }

        /// <summary>
        /// 타이틀 씬: 어두운 바닥 위에 드래곤이 숨쉬고 카메라가 천천히 돈다. 위에 타이틀 메뉴.
        /// 지금 열린 씬은 그대로 두고 옆에 만들어 저장한 뒤 닫는다.
        /// </summary>
        static void CreateTitleScene()
        {
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.22f, 0.2f, 0.24f);
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = new Color(0.07f, 0.06f, 0.07f);
                RenderSettings.fogStartDistance = 15f;
                RenderSettings.fogEndDistance = 55f;
                RenderSettings.skybox = null;

                var camGo = new GameObject("Title Camera", typeof(Camera), typeof(AudioListener));
                var cam = camGo.GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = RenderSettings.fogColor;
                cam.fieldOfView = 45f;
                camGo.tag = "MainCamera";
                var backdrop = camGo.AddComponent<TitleBackdrop>();

                var sunGo = new GameObject("Sun", typeof(Light));
                var sun = sunGo.GetComponent<Light>();
                sun.type = LightType.Directional;
                sun.color = new Color(1f, 0.72f, 0.5f);
                sun.intensity = 1.4f;
                sun.shadows = LightShadows.Soft;
                sunGo.transform.rotation = Quaternion.Euler(28f, -35f, 0f);

                var rimGo = new GameObject("Rim Light", typeof(Light));
                var rim = rimGo.GetComponent<Light>();
                rim.type = LightType.Directional;
                rim.color = new Color(0.45f, 0.55f, 1f);
                rim.intensity = 0.6f;
                rimGo.transform.rotation = Quaternion.Euler(15f, 150f, 0f);

                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Ground";
                ground.transform.localScale = new Vector3(12f, 1f, 12f);
                var groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                groundMat.SetColor("_BaseColor", new Color(0.16f, 0.13f, 0.11f));
                groundMat.SetFloat("_Smoothness", 0.15f);
                ground.GetComponent<Renderer>().sharedMaterial = groundMat;

                // 배경 몬스터: 드래곤 (없으면 빈 화면에 카메라만 돈다)
                var dragonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DragonPrefab);
                Transform focus = null;
                if (dragonPrefab)
                {
                    var holder = new GameObject("Dragon").transform;
                    holder.rotation = Quaternion.Euler(0f, 160f, 0f);
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(dragonPrefab, holder);
                    PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    model.transform.localPosition = Vector3.zero;
                    model.transform.localRotation = Quaternion.identity;

                    var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(DragonController);
                    var animator = model.GetComponent<Animator>();
                    if (animator && controller) animator.runtimeAnimatorController = controller;

                    var b = MonsterModelSetup.LocalBounds(holder, model);
                    float size = Mathf.Max(b.size.y, b.size.z);
                    if (size > 0.001f) model.transform.localScale = Vector3.one * (8f / size);
                    var idle = controller ? controller.animationClips.FirstOrDefault(c => c.name.ToLowerInvariant().Contains("idle")) : null;
                    float lowest = MonsterModelSetup.LowestPointInPose(holder, model, idle);
                    if (float.IsInfinity(lowest)) lowest = MonsterModelSetup.LocalBounds(holder, model).min.y;
                    model.transform.localPosition = new Vector3(0f, -lowest, 0f);
                    focus = holder;
                }
                backdrop.EditorSetup(focus);

                new GameObject("Title Screen").AddComponent<TitleScreen>();

                EditorSceneManager.SaveScene(scene, TitleScenePath);
            }
            finally
            {
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("SoulsHunter/Build/Setup Build Scenes")]
        static void SetupBuildScenesMenu()
        {
            SetupBuildScenes();
            EditorUtility.DisplayDialog("SoulsHunter",
                "Build Settings 씬 목록:\n" + string.Join("\n", EditorBuildSettings.scenes.Select(s => "· " + s.path)), "확인");
        }

        /// <summary>타이틀(있으면) → 게임 씬 순서로 Build Settings를 맞춘다</summary>
        public static void SetupBuildScenes()
        {
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>();
            if (File.Exists(TitleScenePath)) list.Add(new EditorBuildSettingsScene(TitleScenePath, true));
            if (File.Exists(GameScenePath)) list.Add(new EditorBuildSettingsScene(GameScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ───────── 빌드 ─────────

        [MenuItem("SoulsHunter/Build/Build Windows Game")]
        static void BuildWindows()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFxMaterials();
            if (!File.Exists(TitleScenePath)) CreateTitleScene();
            SetupBuildScenes();

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = BuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                float mb = summary.totalSize / (1024f * 1024f);
                if (EditorUtility.DisplayDialog("SoulsHunter",
                        $"빌드 완료! ({mb:0} MB, {summary.totalTime.TotalMinutes:0.0}분)\n{Path.GetFullPath(BuildPath)}", "폴더 열기", "닫기"))
                    EditorUtility.RevealInFinder(BuildPath);
            }
            else
            {
                EditorUtility.DisplayDialog("SoulsHunter",
                    $"빌드 실패 ({summary.result}). 오류 {summary.totalErrors}개 — Console 창을 확인하세요.", "확인");
            }
        }

        /// <summary>다른 방법(File > Build)으로 빌드할 때도 이펙트 머티리얼이 있는지 확인</summary>
        class Preprocess : IPreprocessBuildWithReport
        {
            public int callbackOrder => 0;

            public void OnPreprocessBuild(BuildReport report)
            {
                foreach (var (name, _) in SoulsHunter.Feedback.FxResources.Templates)
                    if (!AssetDatabase.LoadAssetAtPath<Material>($"{FxFolder}/{name}.mat"))
                        Debug.LogWarning($"[SoulsHunter] {FxFolder}/{name}.mat 이 없어요. 빌드에서 이펙트가 안 보일 수 있어요. " +
                                         "SoulsHunter/Build/Build Windows Game 메뉴로 빌드하세요.");
            }
        }
    }
}
