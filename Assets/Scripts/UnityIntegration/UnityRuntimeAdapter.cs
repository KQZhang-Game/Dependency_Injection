using System;
using System.Collections.Generic;
using UnityEngine;

namespace DependencyInjection
{
    /// <summary>
    /// Unity 适配层实现：引擎层唯一会碰到 UnityEngine 的地方（Bootstrap 除外）。
    /// 场景对象判定、场景查找、日志都走 UnityEngine API。
    /// </summary>
    public sealed class UnityRuntimeAdapter : IDependencyRuntimeAdapter
    {
        public bool IsSceneBehaviour(Type type) => typeof(MonoBehaviour).IsAssignableFrom(type);

        public object FindSceneObject(Type type)
        {
            var found = UnityEngine.Object.FindFirstObjectByType(type);

            // Unity 的「假 null」：对象已销毁时托管引用仍在，但 == null 为 true。
            // 在这里统一转成真正的 null，引擎层只需做普通判空。
            return found == null ? null : found;
        }

        public IEnumerable<object> FindSceneObjects(Type type)
        {
            foreach (var behaviour in UnityEngine.Object.FindObjectsByType(type, FindObjectsSortMode.None))
            {
                if (behaviour == null) continue;
                yield return behaviour;
            }
        }

        public void LogWarning(string message) => Debug.LogWarning(message);

        public void LogError(string message) => Debug.LogError(message);
    }
}
