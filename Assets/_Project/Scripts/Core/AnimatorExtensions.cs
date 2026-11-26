using System.Collections.Generic;
using UnityEngine;

namespace SoulsHunter.Core
{
    /// <summary>
    /// 애니메이터에 해당 파라미터가 없으면 조용히 무시하는 Set 함수들.
    /// 모델/애니메이션이 아직 없거나 일부만 있어도 코드가 에러 없이 돈다.
    /// </summary>
    public static class AnimatorExtensions
    {
        static readonly Dictionary<RuntimeAnimatorController, HashSet<int>> cache = new();

        public static bool HasParam(this Animator animator, int hash)
        {
            if (!animator || !animator.runtimeAnimatorController) return false;
            var controller = animator.runtimeAnimatorController;
            if (!cache.TryGetValue(controller, out var set))
            {
                set = new HashSet<int>();
                foreach (var p in animator.parameters) set.Add(p.nameHash);
                cache[controller] = set;
            }
            return set.Contains(hash);
        }

        public static void TrySetFloat(this Animator a, int hash, float v, float damp = 0f)
        {
            if (!a.HasParam(hash)) return;
            if (damp > 0f) a.SetFloat(hash, v, damp, Time.deltaTime);
            else a.SetFloat(hash, v);
        }

        public static void TrySetBool(this Animator a, int hash, bool v)
        {
            if (a.HasParam(hash)) a.SetBool(hash, v);
        }

        public static void TrySetInt(this Animator a, int hash, int v)
        {
            if (a.HasParam(hash)) a.SetInteger(hash, v);
        }

        public static void TrySetTrigger(this Animator a, int hash)
        {
            if (a.HasParam(hash)) a.SetTrigger(hash);
        }
    }
}
