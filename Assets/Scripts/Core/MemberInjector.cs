using System;
using System.Collections.Generic;
using System.Linq;

namespace DependencyInjection
{
    /// <summary>
    /// 成员注入：把注册表里的服务赋值到各个宿主的 [Inject] 字段 / 属性 / 方法，
    /// 并在全部注入完成后触发 IServiceReady。不做创建，也不做依赖图构建。
    ///
    /// 它只关心「实例在哪里」和「成员有哪些」，实例从哪里来由 DependencyGraph 的节点来源决定。
    /// </summary>
    public class MemberInjector
    {
        readonly IServiceRegistry registry;
        readonly DependencyGraph graph;

        // 本次 Initialize 真正创建并注册的节点。Root 服务跨场景复用时不在这个集合里，
        // 注入与就绪回调据此跳过，避免对同一个实例重复注入、重复回调。
        readonly HashSet<Type> createdThisRound = new HashSet<Type>();

        public MemberInjector(IServiceRegistry registry, DependencyGraph graph)
        {
            this.registry = registry;
            this.graph = graph;
        }

        /// <summary>开始新的一轮装配，清掉上一轮的创建记录。</summary>
        public void BeginRound()
        {
            createdThisRound.Clear();
        }

        /// <summary>标记某个节点在本轮被容器真正创建。</summary>
        public void MarkCreated(Type node)
        {
            createdThisRound.Add(node);
        }

        /// <summary>
        /// 注入阶段：在所有服务都进注册表之后执行，按拓扑序给每个带 [Inject] 成员的宿主赋值。
        /// 顺序沿用初始化顺序，保证宿主依赖的服务一定已经注册。
        /// </summary>
        public void InjectAll(IList<Type> initializationOrder)
        {
            foreach (var node in initializationOrder)
            {
                if (!graph.InjectFields.ContainsKey(node)
                    && !graph.InjectProperties.ContainsKey(node)
                    && !graph.InjectMethods.ContainsKey(node))
                    continue;

                // 复用的 Root 服务在上一轮已经注入过，不再重复注入
                if (graph.NodeKinds[node] != E_NodeKind.SceneBehaviour && !createdThisRound.Contains(node))
                    continue;

                foreach (var host in GetInjectionHosts(node))
                    Inject(host, node);
            }
        }

        /// <summary>
        /// 全部服务注册并注入完成后，按拓扑序调用就绪回调（依赖先就绪）。
        /// 覆盖所有注入宿主：容器创建的服务从注册表取，场景 Mono 从场景查找。
        /// 单个服务抛异常直接中断启动 —— 半初始化的容器继续跑，只会把问题推到更靠后的地方。
        /// </summary>
        public void NotifyReady(IList<Type> initializationOrder)
        {
            foreach (var node in initializationOrder)
            {
                var kind = graph.NodeKinds[node];
                if (kind == E_NodeKind.Unregistered) continue;

                // SceneBehaviour 每轮都是场景里的对象，需要回调；
                // 复用的 Root 服务已经 Ready 过，不再重复回调。
                if (kind != E_NodeKind.SceneBehaviour && !createdThisRound.Contains(node)) continue;

                foreach (var host in GetInjectionHosts(node))
                {
                    if (host is IServiceReady ready)
                        ready.OnServicesReady();
                }
            }
        }

        /// <summary>
        /// 注入宿主。来源由节点来源决定，不靠类型探测：
        /// Provider / Constructor 由容器创建过，注册表里就是实例；
        /// SceneBehaviour 容器不创建，只能去场景里找 Mono 实例（可能多个）。
        /// Unregistered 既不创建也不在这里注入，由业务方调用 InjectExternal。
        /// </summary>
        private IEnumerable<object> GetInjectionHosts(Type hostType)
        {
            switch (graph.NodeKinds[hostType])
            {
                case E_NodeKind.Provider:
                case E_NodeKind.Constructor:
                    if (registry.TryGetValue(hostType, out var instance) && instance != null)
                        yield return instance;
                    yield break;

                case E_NodeKind.SceneBehaviour:
                    foreach (var behaviour in UnityEngine.Object.FindObjectsByType(hostType, UnityEngine.FindObjectsSortMode.None))
                        yield return behaviour;
                    yield break;
            }
        }

        /// <summary>按节点类型而非 host.GetType() 查成员：宿主可能是子类，注入点仍按声明类型登记。</summary>
        private void Inject(object host, Type hostType)
        {
            if (graph.InjectFields.TryGetValue(hostType, out var fields))
                foreach (var field in fields)
                    field.SetValue(host, Resolve(field.FieldType, $"{hostType.Name}.{field.Name}"));

            if (graph.InjectProperties.TryGetValue(hostType, out var properties))
                foreach (var property in properties)
                    if (property.CanWrite)
                        property.SetValue(host, Resolve(property.PropertyType, $"{hostType.Name}.{property.Name}"));

            if (graph.InjectMethods.TryGetValue(hostType, out var methods))
                foreach (var method in methods)
                    method.Invoke(host, method.GetParameters().Select(p => Resolve(p.ParameterType, $"{hostType.Name}.{method.Name}")).ToArray());
        }

        /// <summary>
        /// 给「容器不创建、由外部代码 new 出来」的对象注入。
        /// 适用于 Unregistered 类型，也适用于本来就不该由容器管理生命周期的对象。
        /// 必须在 Initialize 之后调用：那时依赖服务才在注册表里。
        /// </summary>
        public void InjectExternal(object host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            Inject(host, host.GetType());
        }

        /// <summary>
        /// 从注册表解析服务。解析失败直接抛异常。
        /// 不返回 null 或只打警告：注入 null 会把问题推迟到使用该服务的地方才爆，
        /// 那时已经看不出是装配失败；异常里带上「谁在依赖它」，定位更直接。
        /// </summary>
        private object Resolve(Type type, string requester)
        {
            if (registry.TryGetValue(type, out var instance) && instance != null)
                return instance;

            throw new InvalidOperationException(
                $"DI 无法解析服务 {type.FullName}：未注册或实例为 null（依赖方：{requester}）");
        }
    }
}
