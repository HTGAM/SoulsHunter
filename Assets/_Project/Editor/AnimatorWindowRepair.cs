using System;
using UnityEditor;
using UnityEngine;

namespace SoulsHunter.EditorTools
{
    /// <summary>
    /// Animator 창 오류(NullReferenceException at UnityEditor.Graphs.Edge.WakeUp) 정리.
    ///
    /// 원인: 애니메이터 파일을 지우고 다시 만들면, Animator 창이 쓰던 그래프 데이터가
    /// 창을 닫아도 메모리에 남는다. Play를 누를 때(스크립트 다시 불러오기)마다 그 그래프가
    /// 이미 없는 상태로 이어진 연결을 깨우려다 오류를 낸다.
    ///
    /// 하는 일: Animator 창 닫기 → 남은 그래프 데이터 삭제 → Console 지우기 → 창 다시 열기
    /// - PC마다 처음 컴파일될 때 한 번 자동 실행
    /// - Play 직전에 Animator 창이 닫혀 있으면 남은 그래프를 자동으로 치움 (다시 생기는 것 방지)
    /// - 메뉴 SoulsHunter > Repair Animator Window 로 언제든 실행
    /// </summary>
    [InitializeOnLoad]
    static class AnimatorWindowRepair
    {
        const string DoneKey = "SoulsHunter.AnimatorWindowRepair.v2";
        const string AnimatorWindowType = "AnimatorControllerTool";

        static AnimatorWindowRepair()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            if (EditorPrefs.GetBool(DoneKey, false)) return;
            EditorPrefs.SetBool(DoneKey, true);
            EditorApplication.delayCall += Repair;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            // Play 직전: 창이 닫혀 있는데 남아 있는 그래프는 쓰는 곳이 없으므로 지운다
            if (change == PlayModeStateChange.ExitingEditMode && !IsAnimatorWindowOpen())
                DestroyOrphanGraphs();
        }

        [MenuItem("SoulsHunter/Repair Animator Window")]
        static void Repair()
        {
            bool hadWindow = false;
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (w && w.GetType().Name == AnimatorWindowType)
                {
                    w.Close();
                    hadWindow = true;
                }
            }

            int removed = DestroyOrphanGraphs();
            ClearConsole();

            if (hadWindow)
                EditorApplication.delayCall += () => EditorApplication.ExecuteMenuItem("Window/Animation/Animator");
            Debug.Log($"[SoulsHunter] Animator 창을 정리했어요 (남은 그래프 {removed}개 삭제).");
        }

        static bool IsAnimatorWindowOpen()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (w && w.GetType().Name == AnimatorWindowType) return true;
            return false;
        }

        /// <summary>UnityEditor.Graphs 의 그래프·노드 객체를 지운다 (Animator 창이 다시 열리면 새로 만든다)</summary>
        internal static int DestroyOrphanGraphs()
        {
            int count = 0;
            foreach (var obj in Resources.FindObjectsOfTypeAll<ScriptableObject>())
            {
                if (!obj) continue;
                var ns = obj.GetType().Namespace;
                if (ns == null || !ns.StartsWith("UnityEditor.Graphs")) continue;
                if (EditorUtility.IsPersistent(obj)) continue; // 에셋 파일에 저장된 건 건드리지 않음
                UnityEngine.Object.DestroyImmediate(obj);
                count++;
            }
            return count;
        }

        static void ClearConsole()
        {
            // UnityEditor.LogEntries.Clear()는 공개 API가 아니어서 리플렉션으로 호출
            var type = Type.GetType("UnityEditor.LogEntries, UnityEditor");
            type?.GetMethod("Clear")?.Invoke(null, null);
        }
    }
}
