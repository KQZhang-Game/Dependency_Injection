using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace DependencyInjection
{
    /// <summary>
    /// 依赖图：把 Scanner 的扫描结果整理成图，不做实例创建、不做成员注入、不碰注册表。
    ///
    /// 产物是只读数据：节点集合、出边/入度、每个节点的实例来源（E_NodeKind）、
    /// 创建所需的依赖参数、待注入成员。构建期校验也放在这里，因为校验只需要图信息。
    ///
    /// 每次 Build 都从头重建：过场景后 Bootstrap 会再次调用，旧数据必须丢弃，
    /// 否则注入成员列表会重复登记，同一个字段被赋两次、同一个方法被调两次。
    /// </summary>
    public class DependencyGraph
    {
        readonly Dictionary<Type, HashSet<Type>> outEdgeItems = new Dictionary<Type, HashSet<Type>>();
        readonly Dictionary<Type, int> inEdgeCounts = new Dictionary<Type, int>();

        // 类型 -> 标注的 [ServiceScope]。由 Scanner 扫描一次，Build 不清空
        readonly Dictionary<Type, E_ServiceType> serviceTypeDict;

        // 节点 -> 实例来源。创建、注入、展示共用同一份判定，避免各写一套 if
        readonly Dictionary<Type, E_NodeKind> nodeKinds = new Dictionary<Type, E_NodeKind>();

        // 服务类型 -> 创建方式。Provider 与 Constructor 二选一，二者都没有的属于消费者（只注入、不创建）
        readonly Dictionary<Type, MethodInfo> provideMethods = new Dictionary<Type, MethodInfo>();
        readonly Dictionary<Type, ConstructorInfo> injectCtors = new Dictionary<Type, ConstructorInfo>();

        // 节点 -> 有序的依赖参数类型（构造参数 / provider 方法参数，保留顺序与重复，供实例化传参）
        readonly Dictionary<Type, List<Type>> dependencies = new Dictionary<Type, List<Type>>();

        // 宿主类型 -> 待注入成员。按宿主分组，因为注入的执行单位是「实例」；
        // 成员的依赖类型不额外记录，注入时从 FieldType / PropertyType / GetParameters() 现读。
        readonly Dictionary<Type, List<FieldInfo>> injectFields = new Dictionary<Type, List<FieldInfo>>();
        readonly Dictionary<Type, List<PropertyInfo>> injectProperties = new Dictionary<Type, List<PropertyInfo>>();
        readonly Dictionary<Type, List<MethodInfo>> injectMethods = new Dictionary<Type, List<MethodInfo>>();

        public DependencyGraph()
        {
            serviceTypeDict = Scanner.ScanServiceScopes();
        }

        /// <summary>重建整张图，并在构建期把确定的配置错误直接抛出来。</summary>
        public void Build()
        {
            outEdgeItems.Clear();
            inEdgeCounts.Clear();
            dependencies.Clear();
            provideMethods.Clear();
            injectCtors.Clear();
            injectFields.Clear();
            injectProperties.Clear();
            injectMethods.Clear();
            nodeKinds.Clear();

            ProvideDependencyEdges();
            ConstructDependencyEdges();
            FieldDependencyEdges();
            PropertyDependencyEdges();
            MethodDependencyEdges();
            BuildInEdgeCounts();
            ClassifyNodes();            // 判定每个节点的实例来源
            ValidateProviderHosts();    // 普通类宿主只能靠 [Inject] 构造函数拿依赖，否则一定是 null
            ValidateRootDependencies(); // Root 复用后仍持有旧依赖，所以不能依赖 Scene
        }

        /// <summary>
        /// 查作用域：节点自身优先（服务类型自己标了 [ServiceScope]），
        /// 查不到再回查声明类型（接口键 / 由工厂提供的服务，作用域写在实现类或工厂类上）。
        /// </summary>
        public E_ServiceType GetScope(Type node, Type declaringType)
        {
            if (serviceTypeDict.TryGetValue(node, out var scope)) return scope;
            if (serviceTypeDict.TryGetValue(declaringType, out scope)) return scope;
            throw new InvalidOperationException($"{node.FullName} 与其声明类型 {declaringType.FullName} 都没有 [ServiceScope]");
        }

        /// <summary>
        /// 收集 [Provide] 方法：节点 key 为方法返回类型，依赖为方法参数类型。
        /// 这样「provider 方法本身需要参数」的情况也会进入依赖图，不会被误判为零依赖。
        /// </summary>
        private void ProvideDependencyEdges()
        {
            foreach (var method in Scanner.ScanProvideMethods())
            {
                var serviceType = method.ReturnType;
                if (serviceType == typeof(void)) continue;

                if (provideMethods.ContainsKey(serviceType))
                    Debug.LogWarning($"{serviceType.FullName} 存在多个 [Provide] 方法，后扫描到的将覆盖先扫描到的");

                provideMethods[serviceType] = method;
                var parameterTypes = method.GetParameters().Select(p => p.ParameterType).ToList();
                dependencies[serviceType] = parameterTypes;

                foreach (var dependency in parameterTypes)
                {
                    if (!outEdgeItems.ContainsKey(dependency)) outEdgeItems[dependency] = new HashSet<Type>();
                    outEdgeItems[dependency].Add(serviceType);
                }
            }
        }

        /// <summary>
        /// 收集 [Inject] 构造函数：节点 key 为实现类型，依赖为构造参数类型。
        ///
        /// 一个类型同时有 [Provide] 工厂和 [Inject] 构造函数属于手误，没有正当用法：
        /// 有 [Inject] 构造函数就会被容器创建并按自身类型注册，[Provide] 因此是多余的。
        /// 这里先收集全部冲突再统一抛出，避免多个手误一次只能看到一个。
        /// </summary>
        private void ConstructDependencyEdges()
        {
            var conflicts = new List<string>();

            foreach (var injectable in Scanner.ScanInjectableCtor())
            {
                var injectableItemType = injectable.DeclaringType;
                var parameterTypes = injectable.GetParameters().Select(p => p.ParameterType).ToList();

                if (provideMethods.TryGetValue(injectableItemType, out var provider))
                {
                    conflicts.Add(
                        $"{injectableItemType.FullName} 同时声明了 [Inject] 构造函数和 [Provide] 工厂"
                        + $"（工厂在 {provider.DeclaringType.FullName}.{provider.Name}()）");
                    continue;
                }

                if (injectCtors.ContainsKey(injectableItemType))
                    Debug.LogWarning($"{injectableItemType.FullName} 存在多个 [Inject] 构造函数，后扫描到的将覆盖先扫描到的");
                injectCtors[injectableItemType] = injectable;
                dependencies[injectableItemType] = parameterTypes;

                foreach (var type in parameterTypes)
                {
                    if (!outEdgeItems.ContainsKey(type)) outEdgeItems[type] = new HashSet<Type>();
                    outEdgeItems[type].Add(injectableItemType);
                }
            }

            // Provider 宿主先于它提供的服务创建：宿主走 Constructor 分支创建并注册，
            // 服务的创建才能复用到同一个宿主实例。
            // 只有声明了 [Inject] 构造函数的普通类宿主需要这条边：无构造注入的宿主没有依赖，创建顺序无关。
            foreach (var entry in provideMethods)
            {
                var hostType = entry.Value.DeclaringType;
                if (typeof(MonoBehaviour).IsAssignableFrom(hostType)) continue;
                if (!injectCtors.ContainsKey(hostType)) continue;

                if (!outEdgeItems.ContainsKey(hostType)) outEdgeItems[hostType] = new HashSet<Type>();
                outEdgeItems[hostType].Add(entry.Key);
            }

            if (conflicts.Count > 0)
                throw new InvalidOperationException(
                    "以下类型同时声明了 [Inject] 构造函数和 [Provide] 工厂，二者只能选一个：\n  "
                    + string.Join("\n  ", conflicts)
                    + "\n删掉 [Provide] 走构造注入，或删掉 [Inject] 构造函数走工厂。");
        }

        private void FieldDependencyEdges()
        {
            foreach (var injectable in Scanner.ScanInjectableField())
            {
                var injectableItemType = injectable.DeclaringType;
                var fieldType = injectable.FieldType;
                if (!outEdgeItems.ContainsKey(fieldType)) outEdgeItems[fieldType] = new HashSet<Type>();
                outEdgeItems[fieldType].Add(injectableItemType);

                if (!injectFields.TryGetValue(injectableItemType, out var fields))
                    injectFields[injectableItemType] = fields = new List<FieldInfo>();
                fields.Add(injectable);
            }
        }

        private void PropertyDependencyEdges()
        {
            foreach (var injectable in Scanner.ScanInjectableProperties())
            {
                var injectableItemType = injectable.DeclaringType;
                var propertyType = injectable.PropertyType;
                if (!outEdgeItems.ContainsKey(propertyType)) outEdgeItems[propertyType] = new HashSet<Type>();
                outEdgeItems[propertyType].Add(injectableItemType);

                if (!injectProperties.TryGetValue(injectableItemType, out var properties))
                    injectProperties[injectableItemType] = properties = new List<PropertyInfo>();
                properties.Add(injectable);
            }
        }

        private void MethodDependencyEdges()
        {
            foreach (var injectable in Scanner.ScanInjectableMethods())
            {
                var injectableItemType = injectable.DeclaringType;
                foreach (var type in injectable.GetParameters().Select(p => p.ParameterType))
                {
                    if (!outEdgeItems.ContainsKey(type)) outEdgeItems[type] = new HashSet<Type>();
                    outEdgeItems[type].Add(injectableItemType);
                }

                if (!injectMethods.TryGetValue(injectableItemType, out var methods))
                    injectMethods[injectableItemType] = methods = new List<MethodInfo>();
                methods.Add(injectable);
            }
        }

        /// <summary>
        /// 根据出边表重建入度表。这样做保证三件事：
        /// 1. 所有节点都进入入度表：只被依赖、自身没有依赖的服务（如 ServiceA/ServiceB）
        ///    会得到入度 0，拓扑排序才能找到起点；
        /// 2. 入度只统计去重后的边，避免同一依赖被字段/属性/方法/构造重复引用时
        ///    计数偏大，导致节点永远不会变成 0（被误判成循环依赖）；
        /// 3. 入度表与出边表始终一致，不需要在每个扫描方法里手工维护。
        /// </summary>
        private void BuildInEdgeCounts()
        {
            inEdgeCounts.Clear();

            // 节点全集 = 出边表两端 ∪ provider 返回类型 ∪ 构造注入类型。
            // 后两者即便没有任何边，也必须作为零入度节点存在，否则拓扑排序会漏掉它们。
            var allNodes = new HashSet<Type>();
            foreach (var kv in outEdgeItems)
            {
                allNodes.Add(kv.Key);
                allNodes.UnionWith(kv.Value);
            }
            allNodes.UnionWith(provideMethods.Keys);
            allNodes.UnionWith(injectCtors.Keys);
            // 注入宿主的声明类型也必须进节点集。只带零参数 [Inject] 方法的宿主没有任何边，
            // 不并进来就会在注入阶段被整个跳过。
            allNodes.UnionWith(injectFields.Keys);
            allNodes.UnionWith(injectProperties.Keys);
            allNodes.UnionWith(injectMethods.Keys);

            foreach (var node in allNodes)
                inEdgeCounts[node] = 0;

            // 再按去重后的出边累加入度
            foreach (var kv in outEdgeItems)
                foreach (var consumer in kv.Value)
                    inEdgeCounts[consumer] = inEdgeCounts[consumer] + 1;
        }

        /// <summary>
        /// 给每个节点判定实例来源。
        /// Unregistered 不创建也不注入：从类型上无法区分「漏注册的服务」和「外部创建的对象」，
        /// 自动创建会把前者悄悄换成新实例，所以这里只告警，由业务方显式处理。
        /// </summary>
        private void ClassifyNodes()
        {
            var externalHosts = new List<Type>();

            foreach (var node in inEdgeCounts.Keys)
            {
                var kind = Classify(node);
                nodeKinds[node] = kind;

                if (kind == E_NodeKind.Unregistered && IsInjectionHost(node))
                    externalHosts.Add(node);
            }

            if (externalHosts.Count > 0)
                Debug.LogWarning(
                    "以下类型有 [Inject] 成员，但既无 [Provide]、无 [Inject] 构造函数、也不是 MonoBehaviour，"
                    + "容器不会创建也不会注入它们：\n  "
                    + string.Join("\n  ", externalHosts.Select(t => t.FullName))
                    + "\n若它们是服务，请补 [Provide]；若是外部创建的对象，请在创建后调用 injector.InjectExternal(instance)。");
        }

        private E_NodeKind Classify(Type node)
        {
            if (provideMethods.ContainsKey(node)) return E_NodeKind.Provider;
            if (injectCtors.ContainsKey(node)) return E_NodeKind.Constructor;
            if (typeof(MonoBehaviour).IsAssignableFrom(node)) return E_NodeKind.SceneBehaviour;
            return E_NodeKind.Unregistered;
        }

        /// <summary>该节点是否有需要注入的成员，用来区分「注入宿主」和「纯依赖类型」。</summary>
        private bool IsInjectionHost(Type node) =>
            injectFields.ContainsKey(node) || injectProperties.ContainsKey(node) || injectMethods.ContainsKey(node);

        /// <summary>
        /// 普通类 Provider 宿主若没有 [Inject] 构造函数，容器只能 Activator 无参创建，它不会进入注入流程；
        /// 而 [Provide] 方法在服务创建阶段执行，成员注入要等到全部服务注册之后，
        /// 所以宿主上的 [Inject] 字段/属性/方法在工厂执行时必然是 null。这是确定的配置错误，直接抛。
        /// </summary>
        private void ValidateProviderHosts()
        {
            var invalid = provideMethods.Values
                .Select(method => method.DeclaringType)
                .Distinct()
                .Where(hostType =>
                    !typeof(MonoBehaviour).IsAssignableFrom(hostType)
                    && !injectCtors.ContainsKey(hostType)
                    && IsInjectionHost(hostType))
                .ToList();

            if (invalid.Count > 0)
                throw new InvalidOperationException(
                    "以下 [Provide] 宿主不是 MonoBehaviour，也没有 [Inject] 构造函数，却声明了 [Inject] 成员：\n  "
                    + string.Join("\n  ", invalid.Select(t => t.FullName))
                    + "\n[Provide] 方法在服务创建阶段执行，成员注入要等到全部服务注册之后，此时宿主上的依赖一定是 null。"
                    + "\n请给宿主声明 [Inject] 构造函数，或把宿主改成 MonoBehaviour。");
        }

        /// <summary>查询节点作用域，查不到返回 false。用于校验，不抛异常。</summary>
        private bool TryGetScope(Type node, out E_ServiceType scope)
        {
            if (serviceTypeDict.TryGetValue(node, out scope)) return true;
            var declaringType = provideMethods.TryGetValue(node, out var method) ? method.DeclaringType : node;
            return serviceTypeDict.TryGetValue(declaringType, out scope);
        }

        /// <summary>
        /// Root 级服务跨场景复用，但 Scene 级服务会在过场景时被销毁重建。
        /// Root 若依赖 Scene，复用时持有的是已废弃的旧实例，问题只在运行时才暴露，所以构建期就拦掉。
        /// </summary>
        private void ValidateRootDependencies()
        {
            var invalid = new List<string>();

            foreach (var node in inEdgeCounts.Keys)
            {
                if (!TryGetScope(node, out var scope) || scope != E_ServiceType.Root) continue;

                foreach (var dependency in dependencies[node])
                {
                    if (TryGetScope(dependency, out var dependencyScope) && dependencyScope == E_ServiceType.Scene)
                        invalid.Add($"{node.FullName}（Root）依赖了 {dependency.FullName}（Scene）");
                }
            }

            if (invalid.Count > 0)
                throw new InvalidOperationException(
                    "Root 级服务不能依赖 Scene 级服务：Root 跨场景复用，Scene 依赖会在过场景时被销毁重建。\n  "
                    + string.Join("\n  ", invalid)
                    + "\n请把依赖方也改成 Scene，或把被依赖的服务改成 Root。");
        }

        public IReadOnlyDictionary<Type, HashSet<Type>> OutEdges => outEdgeItems;
        public IReadOnlyDictionary<Type, int> InEdgeCounts => inEdgeCounts;
        public IReadOnlyDictionary<Type, MethodInfo> ProvideMethods => provideMethods;
        public IReadOnlyDictionary<Type, ConstructorInfo> InjectConstructors => injectCtors;
        public IReadOnlyDictionary<Type, List<Type>> Dependencies => dependencies;
        public IReadOnlyDictionary<Type, List<FieldInfo>> InjectFields => injectFields;
        public IReadOnlyDictionary<Type, List<PropertyInfo>> InjectProperties => injectProperties;
        public IReadOnlyDictionary<Type, List<MethodInfo>> InjectMethods => injectMethods;
        public IReadOnlyDictionary<Type, E_NodeKind> NodeKinds => nodeKinds;
        public IEnumerable<Type> Nodes => inEdgeCounts.Keys;

        public string DescribeGraph()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("节点与创建方式:");
            foreach (var node in inEdgeCounts.Keys)
            {
                var kind = nodeKinds[node];
                var deps = dependencies.TryGetValue(node, out var list)
                    ? string.Join(", ", list.Select(t => t.Name))
                    : "-";
                sb.AppendLine($"  {node.Name} [{kind}] 入度={inEdgeCounts[node]} 依赖=[{deps}]");
            }
            sb.AppendLine("outEdgeItems (被依赖类型 -> 依赖它的类型):");
            foreach (var kv in outEdgeItems)
                sb.AppendLine($"  {kv.Key.Name} -> [{string.Join(", ", kv.Value.Select(t => t.Name))}]");
            sb.AppendLine("inEdgeCounts (节点 -> 依赖数量):");
            foreach (var kv in inEdgeCounts)
                sb.AppendLine($"  {kv.Key.Name} : {kv.Value}");
            return sb.ToString();
        }
    }
}
