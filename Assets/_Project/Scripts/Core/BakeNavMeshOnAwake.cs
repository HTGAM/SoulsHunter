using Unity.AI.Navigation;
using UnityEngine;

namespace SoulsHunter.Core
{
    /// <summary>
    /// 게임 시작 시 NavMeshSurface를 굽는다. 테스트 아레나처럼 코드로 만든 맵용.
    /// 실제 맵은 에디터에서 Bake 해두면 이 컴포넌트는 필요 없다.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(NavMeshSurface))]
    public class BakeNavMeshOnAwake : MonoBehaviour
    {
        void Awake()
        {
            var surface = GetComponent<NavMeshSurface>();
            if (!surface.navMeshData) surface.BuildNavMesh();
        }
    }
}
