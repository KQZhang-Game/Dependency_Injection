using System;
using System.Collections.Generic;
using System.Linq;

namespace DependencyInjection
{
    public static class Sorter
    {
        /// <summary>
        /// Kahn 拓扑排序。入度表用 IReadOnlyDictionary 接收，内部拷贝一份再递减，
        /// 避免破坏调用方（Injector）持有的依赖图状态。
        /// outEdgeItems 语义：依赖类型 -> 依赖它的消费者类型。
        /// </summary>
        public static IList<Type> PerformKahnAlgorithm(
            IReadOnlyDictionary<Type, int> inEdgeCounts,
            IReadOnlyDictionary<Type, HashSet<Type>> outEdgeItems)
        {
            var result = new List<Type>();
            if (inEdgeCounts == null || inEdgeCounts.Count == 0) return result;

            var counts = new Dictionary<Type, int>(inEdgeCounts);

            var queue = new Queue<Type>();
            foreach (var kv in counts)
            {
                if (kv.Value == 0) queue.Enqueue(kv.Key);
            }

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                result.Add(node);

                // 叶子节点可能没有出边，用 TryGetValue 避免 KeyNotFoundException
                if (outEdgeItems == null || !outEdgeItems.TryGetValue(node, out var consumers)) continue;

                foreach (var consumer in consumers)
                {
                    if (!counts.ContainsKey(consumer)) continue;
                    counts[consumer]--;
                    if (counts[consumer] == 0) queue.Enqueue(consumer);
                }
            }

            if (result.Count < counts.Count)
            {
                var stuck = counts
                    .Where(kv => kv.Value > 0)
                    .Select(kv => $"{kv.Key.FullName} 还需要 {kv.Value} 个依赖");
                throw new InvalidOperationException(
                    $"依赖成环，无法完成拓扑排序。剩余节点：\n{string.Join("\n", stuck)}");
            }

            return result;
        }
    }
}
