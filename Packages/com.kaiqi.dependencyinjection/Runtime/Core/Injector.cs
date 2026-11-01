using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DependencyInjection
{
    /// <summary>
    /// DI 容器门面：只做编排与生命周期，不负责建图（DependencyGraph）、也不负责成员注入（MemberInjector）。
    ///
    /// 一轮装配的顺序固定为：
    ///   建图与校验 -> 拓扑排序 -> 创建并注册 -> 成员注入 -> 就绪回调
    ///
    /// 这里保留三种真正属于容器的状态：
    ///   - providerHosts：Provider 宿主缓存（普通类宿主按声明类型共享一个实例）
    ///   - nodeScopes：注册时解析出的作用域，销毁阶段要按作用域筛选
    ///   - initializationOrder：本轮拓扑序，销毁走反序
    /// </summary>
    public class Injector
    {
        readonly IServiceRegistry registry;
        readonly IDependencyRuntimeAdapter runtime;
        readonly DependencyGraph graph;
        readonly MemberInjector memberInjector;

        // Provide 方法的声明类型 -> 宿主实例。普通类宿主按声明类型只创建一次，多个 [Provide] 方法共享；
        // MonoBehaviour 宿主始终从场景查找，不进缓存（过场景后场景对象会换新）。
        readonly Dictionary<Type, object> providerHosts = new Dictionary<Type, object>();

        // 节点 -> 注册时解析出的作用域。销毁阶段需要按作用域筛选，不能靠类型名反推
        readonly Dictionary<Type, E_ServiceType> nodeScopes = new Dictionary<Type, E_ServiceType>();

        IList<Type> initializationOrder = new List<Type>();

        public Injector(IServiceRegistry registry, IDependencyRuntimeAdapter runtime)
            : this(registry, runtime, null) { }

        /// <summary>
        /// scanSource 为 null 时使用全量扫描。组合根可以传入带过滤器的 ScannerScanSource
        /// 来收敛扫描范围，传进来的扫描源也决定本次装配的可见类型集合。
        /// </summary>
        public Injector(IServiceRegistry registry, IDependencyRuntimeAdapter runtime, IDependencyScanSource scanSource)
        {
            this.registry = registry;
            this.runtime = runtime;
            graph = scanSource == null
                ? new DependencyGraph(runtime)
                : new DependencyGraph(runtime, scanSource);
            memberInjector = new MemberInjector(registry, graph, runtime);
        }

        public void Initialize()
        {
            // 过场景后 Bootstrap 会再次调用 Initialize，所以每次都要从干净的状态重建。
            // 图与注入成员由 DependencyGraph.Build 自己清空，这里只需清容器自己的状态。
            providerHosts.Clear();
            nodeScopes.Clear();

            graph.Build();
            memberInjector.BeginRound();

            initializationOrder = Sorter.PerformKahnAlgorithm(graph.InEdgeCounts, graph.OutEdges);

            Register();
            memberInjector.InjectAll(initializationOrder);
            memberInjector.NotifyReady(initializationOrder);
        }

        /// <summary>按拓扑序创建服务：Provider 走工厂方法，Constructor 走 [Inject] 构造函数。</summary>
        private void Register()
        {
            foreach (var node in initializationOrder)
            {
                switch (graph.NodeKinds[node])
                {
                    case E_NodeKind.Provider:
                    {
                        // 宿主永远看声明类型（接口没有实例）；作用域先看 node，再回查声明类型
                        var method = graph.ProvideMethods[node];
                        var scope = graph.GetScope(node, method.DeclaringType);
                        nodeScopes[node] = scope;

                        // Root 级服务跨场景复用：注册表里已有实例就不再创建，
                        // 也不标记为本轮创建，因此不会被重复注入 / 重复 Ready。
                        if (IsReusableRoot(node, scope)) break;

                        var host = GetProviderHost(method.DeclaringType);
                        var args = graph.Dependencies[node].Select(t => Resolve(t, $"{method.DeclaringType.Name}.{method.Name}()")).ToArray();
                        var instance = method.Invoke(host, args);
                        registry.Register(node, instance, scope);
                        memberInjector.MarkCreated(node);
                        break;
                    }
                    case E_NodeKind.Constructor:
                    {
                        var scope = graph.GetScope(node, node);   // node 就是实现类型
                        nodeScopes[node] = scope;

                        if (IsReusableRoot(node, scope)) break;

                        var ctor = graph.InjectConstructors[node];
                        var args = graph.Dependencies[node].Select(t => Resolve(t, $"{node.Name} 的 [Inject] 构造函数")).ToArray();
                        var instance = ctor.Invoke(args);
                        registry.Register(node, instance, scope);
                        memberInjector.MarkCreated(node);
                        break;
                    }
                    // SceneBehaviour：容器不创建，留给注入阶段去场景里找实例
                    // Unregistered：容器既不创建也不注入，由业务方创建后调用 InjectExternal
                }
            }
        }

        /// <summary>
        /// 过场景清理场景级服务：按「反拓扑序」依次调用 IDisposableService.Dispose()，再清空 scene 注册表。
        ///
        /// 为什么是反序：拓扑序是「被依赖方 -> 依赖方」，销毁必须反过来，
        /// 让依赖方先释放，否则它的 Dispose 里访问到的依赖可能已经被销毁。
        ///
        /// 只处理已注册且作用域为 Scene 的服务：
        /// - Root 级服务保留，跨场景复用；
        /// - 消费者（ClassA/ClassB）本来就没注册，自然跳过；
        /// - 没实现 IDisposableService 的服务只清注册表，不调用 Dispose。
        /// </summary>
        public void DisposeSceneServices()
        {
            for (int i = initializationOrder.Count - 1; i >= 0; i--)
            {
                var node = initializationOrder[i];
                if (!nodeScopes.TryGetValue(node, out var scope) || scope != E_ServiceType.Scene) continue;
                if (!registry.TryGetValue(node, out var instance) || instance == null) continue;
                if (!(instance is IDisposableService disposable)) continue;

                // 单个服务 Dispose 失败不应阻断其余服务的清理
                try
                {
                    disposable.Dispose();
                }
                catch (Exception e)
                {
                    runtime.LogError($"{node.Name} 在 Dispose 时抛出异常：{e}");
                }
            }

            registry.ClearAllSceneService();
            nodeScopes.Clear();
        }

        /// <summary>
        /// 拿 Provider 宿主。MonoBehaviour 只能来自场景，普通类由容器负责创建与复用。
        /// </summary>
        private object GetProviderHost(Type declaringType)
        {
            // Mono 宿主：场景里找，不缓存 —— 过场景后是新的场景对象，缓存会拿到已销毁的旧实例
            if (runtime.IsSceneBehaviour(declaringType))
                return runtime.FindSceneObject(declaringType);

            // 普通类宿主：同一声明类型只创建一个，多个 [Provide] 方法共享同一个实例
            if (providerHosts.TryGetValue(declaringType, out var host) && host != null)
                return host;

            // 声明了 [Inject] 构造函数的宿主已由 Register 的 Constructor 分支创建并注册，直接复用
            if (registry.TryGetValue(declaringType, out var registered) && registered != null)
                host = registered;
            else
                host = Activator.CreateInstance(declaringType);

            providerHosts[declaringType] = host;
            return host;
        }

        /// <summary>Root 级服务若已注册且实例仍存活，就复用，不再创建。</summary>
        private bool IsReusableRoot(Type node, E_ServiceType scope) =>
            scope == E_ServiceType.Root
            && registry.TryGetValue(node, out var existing)
            && existing != null;

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

        /// <summary>给容器不创建的对象注入，转发给成员注入器。</summary>
        public void InjectExternal(object host) => memberInjector.InjectExternal(host);

        public IReadOnlyDictionary<Type, HashSet<Type>> OutEdges => graph.OutEdges;
        public IReadOnlyDictionary<Type, int> InEdgeCounts => graph.InEdgeCounts;
        public IReadOnlyDictionary<Type, MethodInfo> ProvideMethods => graph.ProvideMethods;
        public IReadOnlyDictionary<Type, ConstructorInfo> InjectConstructors => graph.InjectConstructors;
        public IReadOnlyDictionary<Type, List<Type>> Dependencies => graph.Dependencies;
        public IReadOnlyDictionary<Type, List<FieldInfo>> InjectFields => graph.InjectFields;
        public IReadOnlyDictionary<Type, List<PropertyInfo>> InjectProperties => graph.InjectProperties;
        public IReadOnlyDictionary<Type, List<MethodInfo>> InjectMethods => graph.InjectMethods;
        public IReadOnlyDictionary<Type, E_NodeKind> NodeKinds => graph.NodeKinds;
        public IEnumerable<Type> Nodes => graph.Nodes;

        public string DescribeGraph() => graph.DescribeGraph();
    }
}
