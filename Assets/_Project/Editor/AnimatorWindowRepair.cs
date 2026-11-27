using System;
using UnityEditor;
using UnityEngine;

namespace SoulsHunter.EditorTools
{
    /// <summary>
    /// Animator 창이 지워진 옛 애니메이터를 붙잡고 NullReferenceException(Graph.WakeUp)을 내는 경우를 정리한다.
    /// - 이 PC에서 처음 컴파일될 때 한 번 자동으로 실행
    /// - 이후에는 메뉴 SoulsHunter > Repair Animator Window 로 언제든 실행
    /// 하는 일: 열린 Animator 창 닫기 → Console 지우기 → Animator 창 다시 열기
    /// </summary>
    [InitializeOnLoad]
    static class AnimatorWindowRepair
    {
        const string DoneKey = "SoulsHunter.AnimatorWindowRepair.v1";

        static AnimatorWindowRepair()
        {
            if (EditorPrefs.GetBool(DoneKey, false)) return;
            EditorPrefs.SetBool(DoneKey, true);
            EditorApplication.delayCall += Repair;
        }

        [MenuItem("SoulsHunter/Repair Animator Window")]
        static void Repair()
        {
            bool hadWindow = false;
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (w && w.GetType().Name == "AnimatorControllerTool")
                {
                    w.Close();
                    hadWindow = true;
                }
            }

            ClearConsole();

            if (hadWindow)
                EditorApplication.delayCall += () => EditorApplication.ExecuteMenuItem("Window/Animation/Animator");
            Debug.Log("[SoulsHunter] Animator 창을 다시 열고 Console을 정리했어요.");
        }

        static void ClearConsole()
        {
            // UnityEditor.LogEntries.Clear()는 공개 API가 아니어서 리플렉션으로 호출
            var type = Type.GetType("UnityEditor.LogEntries, UnityEditor");
            type?.GetMethod("Clear")?.Invoke(null, null);
        }
    }
}
