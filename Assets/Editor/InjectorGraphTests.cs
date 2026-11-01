using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DependencyInjection
{
    public static class InjectorGraphTests
    {
        [MenuItem("Tools/DI/Run Dependency Graph Test")]
        public static void Run()
        {
            // 静态计数，同一域内连续运行菜单会累加
            HostingProvider.InstanceCount = 0;
            ReadyService.ReadyCount = 0;

            var registry = new ServiceRegistry();
            var injector = new Injector(registry);
            injector.Initialize();

            Debug.Log("=== 依赖图可视化 ===\n" + injector.DescribeGraph());

            var errors = new List<string>();

            // 字段注入: ClassB -> ServiceA / ServiceB / EnvironmentSystem
            // 属性注入: ClassA.serviceB -> ServiceB
            // 方法注入: ClassA.Init(ServiceA) -> ServiceA
            // 构造注入: CtorService(ServiceA) -> ServiceA
            ExpectOutEdge(injector, typeof(ServiceA), new[] { typeof(ClassA), typeof(ClassB), typeof(CtorService) }, errors, "ServiceA 的消费者应为 ClassA、ClassB 和 CtorService");
            ExpectOutEdge(injector, typeof(ServiceB), new[] { typeof(ClassA), typeof(ClassB) }, errors, "ServiceB 的消费者应为 ClassA 和 ClassB");
            ExpectOutEdge(injector, typeof(EnvironmentSystem), new[] { typeof(ClassB) }, errors, "EnvironmentSystem 的消费者应为 ClassB");
            ExpectInEdge(injector, typeof(ClassA), 2, errors, "ClassA 入度应为 2");
            ExpectInEdge(injector, typeof(ClassB), 3, errors, "ClassB 入度应为 3");
            ExpectInEdge(injector, typeof(CtorService), 1, errors, "CtorService 入度应为 1");

            // 只被依赖、自身无依赖的服务必须出现在入度表中且为 0，否则拓扑排序找不到起点
            ExpectInEdge(injector, typeof(ServiceA), 0, errors, "ServiceA 作为叶子服务，入度应为 0");
            ExpectInEdge(injector, typeof(ServiceB), 0, errors, "ServiceB 作为叶子服务，入度应为 0");
            ExpectInEdge(injector, typeof(EnvironmentSystem), 0, errors, "EnvironmentSystem 作为叶子服务，入度应为 0");

            // 构造注入：应恰好命中 1 个 [Inject] 构造函数（排除 Editor 测试专用类型）
            var ctorCount = Scanner.ScanInjectableCtor().Count(c => !IsTestOnlyType(c.DeclaringType));
            Debug.Log($"[构造注入] ScanInjectableCtor 命中 {ctorCount} 个（预期 1 个：CtorService）");
            if (ctorCount != 1)
                errors.Add($"构造注入扫描数量应为 1，实际为 {ctorCount}");

            RunSorterTests(injector, errors);

            // 节点来源分类：每个节点都必须有明确来源，且分类与真实创建方式一致
            ExpectKind(injector, typeof(ServiceA), E_NodeKind.Provider, errors);
            ExpectKind(injector, typeof(ServiceB), E_NodeKind.Provider, errors);
            ExpectKind(injector, typeof(EnvironmentSystem), E_NodeKind.Provider, errors);
            ExpectKind(injector, typeof(CtorService), E_NodeKind.Constructor, errors);
            ExpectKind(injector, typeof(ClassA), E_NodeKind.SceneBehaviour, errors);
            ExpectKind(injector, typeof(ClassB), E_NodeKind.SceneBehaviour, errors);

            // Unregistered：容器既不创建也不注入，必须由业务方创建后调用 InjectExternal
            ExpectKind(injector, typeof(DefaultCtorConsumer), E_NodeKind.Unregistered, errors);
            if (registry.TryGetValue(typeof(DefaultCtorConsumer), out _))
                errors.Add("DefaultCtorConsumer 属于 Unregistered，不应被容器自动创建或注册");

            var defaultConsumer = new DefaultCtorConsumer();
            injector.InjectExternal(defaultConsumer);
            if (!defaultConsumer.Injected)
                errors.Add("InjectExternal 没有执行 DefaultCtorConsumer 的零参数 [Inject] 方法");

            // 普通类 Provider 宿主：多个 [Provide] 方法必须复用同一个宿主，且宿主依赖来自 [Inject] 构造函数
            ExpectKind(injector, typeof(HostingProvider), E_NodeKind.Constructor, errors);
            object hostedOne = null, hostedTwo = null;
            registry.TryGetValue(typeof(HostedServiceOne), out hostedOne);
            registry.TryGetValue(typeof(HostedServiceTwo), out hostedTwo);
            if (hostedOne is HostedServiceOne one && hostedTwo is HostedServiceTwo two)
            {
                if (HostingProvider.InstanceCount != 1)
                    errors.Add($"普通类 Provider 宿主应只创建 1 次，实际创建 {HostingProvider.InstanceCount} 次");
                if (!ReferenceEquals(one.Host, two.Host))
                    errors.Add("两个 [Provide] 方法拿到的宿主实例不是同一个");
                if (one.Dependency == null)
                    errors.Add("宿主没有通过 [Inject] 构造函数拿到依赖");
            }
            else
            {
                errors.Add("HostedServiceOne / HostedServiceTwo 未被注册");
            }

            // IServiceReady：所有注入完成后回调，且构造依赖与成员注入都已就位
            if (ReadyService.ReadyCount != 1)
                errors.Add($"IServiceReady 应被调用 1 次，实际 {ReadyService.ReadyCount} 次");
            if (!ReadyService.DependenciesAvailable)
                errors.Add("IServiceReady 回调时依赖尚未注入");

            if (errors.Count == 0)
                Debug.Log("<color=green>PASS: 依赖图构建与拓扑排序正确</color>");
            else
                foreach (var e in errors)
                    Debug.LogError("FAIL: " + e);
        }

        private static void ExpectKind(Injector injector, Type node, E_NodeKind expected, List<string> errors)
        {
            if (!injector.NodeKinds.TryGetValue(node, out var actual))
                errors.Add($"{node.Name} 不在依赖图节点中，无法判定实例来源");
            else if (actual != expected)
                errors.Add($"{node.Name} 的来源分类应为 {expected}，实际为 {actual}");
        }

        /// <summary>
        /// 注册功能测试：验证 Injector.Register 是否把「Provider 方法创建的」和「构造注入创建的」
        /// 服务按正确的键与作用域放进 ServiceRegistry，消费者不被注册，依赖先于消费者注册。
        /// </summary>
        [MenuItem("Tools/DI/Run Registration Test")]
        public static void RunRegistrationTest()
        {
            var errors = new List<string>();
            var tempObjects = new List<GameObject>();

            // Provider / EnvironmentSystem 是 Mono 宿主，Register 时要从场景里找实例。
            // 场景里没有就临时建一个，测完销毁，保证测试不依赖具体场景。
            if (UnityEngine.Object.FindFirstObjectByType<Provider>() == null)
            {
                var go = new GameObject("[DI-Test] Provider");
                go.AddComponent<Provider>();
                tempObjects.Add(go);
            }
            var environment = UnityEngine.Object.FindFirstObjectByType<EnvironmentSystem>();
            if (environment == null)
            {
                var go = new GameObject("[DI-Test] EnvironmentSystem");
                environment = go.AddComponent<EnvironmentSystem>();
                tempObjects.Add(go);
            }

            try
            {
                var registry = new ServiceRegistry();
                var injector = new Injector(registry);

                Debug.Log("=== 作用域预检（创建节点 -> 作用域）===\n" + DescribeScopes(injector));

                try
                {
                    injector.Initialize();
                }
                catch (Exception e)
                {
                    errors.Add($"Initialize() 抛出异常，注册中断：{e.GetType().Name}: {e.Message}");
                    Report(errors);
                    return;
                }

                // 1. Provider 方法返回的服务必须以「返回类型」为键注册
                foreach (var node in injector.ProvideMethods.Keys)
                    ExpectRegistered(registry, node, errors, $"Provider 返回的服务 {node.Name} 应被注册");

                // 2. 构造注入创建的服务必须以「实现类型」为键注册
                foreach (var node in injector.InjectConstructors.Keys)
                    ExpectRegistered(registry, node, errors, $"构造注入的服务 {node.Name} 应被注册");

                // 3. Root 级服务：键就是具体类型，值是场景中的那个实例
                if (registry.TryGetValue(typeof(EnvironmentSystem), out var envInstance)
                    && !ReferenceEquals(envInstance, environment))
                {
                    errors.Add("EnvironmentSystem 注册的实例不是场景中那个实例");
                }

                // 4. 构造注入的依赖必须来自注册表（同时证明 ServiceA 先于 CtorService 注册）
                if (registry.TryGetValue(typeof(CtorService), out var ctorService)
                    && registry.TryGetValue(typeof(ServiceA), out var serviceA))
                {
                    var field = typeof(CtorService).GetField("serviceA", BindingFlags.Instance | BindingFlags.NonPublic);
                    var injected = field?.GetValue(ctorService);
                    if (injected == null)
                        errors.Add("CtorService.serviceA 为 null，构造注入没拿到依赖");
                    else if (!ReferenceEquals(injected, serviceA))
                        errors.Add("CtorService 注入的 ServiceA 与注册表里的实例不是同一个");
                }

                // 5. 纯消费者不应被注册
                ExpectNotRegistered(registry, typeof(ClassA), errors, "ClassA 是纯消费者，不应被注册");
                ExpectNotRegistered(registry, typeof(ClassB), errors, "ClassB 是纯消费者，不应被注册");

                // 6. 作用域归属：清空 scene 服务后 Scene 级消失、Root 级保留
                registry.ClearAllSceneService();
                ExpectNotRegistered(registry, typeof(ServiceA), errors, "ServiceA 应为 Scene 级，清空后应移除");
                ExpectNotRegistered(registry, typeof(ServiceB), errors, "ServiceB 应为 Scene 级，清空后应移除");
                ExpectNotRegistered(registry, typeof(CtorService), errors, "CtorService 应为 Scene 级，清空后应移除");
                ExpectRegistered(registry, typeof(EnvironmentSystem), errors, "EnvironmentSystem 应为 Root 级，清空后应保留");

                Report(errors, "注册功能正确");
            }
            finally
            {
                foreach (var go in tempObjects)
                    UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// 按当前约定（作用域以 ServiceScopeAttribute 为准，节点自身优先、其次声明类型）
        /// 打印每个创建节点能查到的作用域，用于提前暴露「缺少 ServiceScope」的问题。
        /// </summary>
        private static string DescribeScopes(Injector injector)
        {
            var scopes = Scanner.ScanServiceScopes();
            var sb = new System.Text.StringBuilder();

            foreach (var kv in injector.ProvideMethods)
            {
                var node = kv.Key;
                var declaringType = kv.Value.DeclaringType;
                sb.AppendLine($"  Provider 节点 {node.Name}（声明类型 {declaringType.Name}）-> {ScopeText(scopes, node, declaringType)}");
            }
            foreach (var node in injector.InjectConstructors.Keys)
                sb.AppendLine($"  Constructor 节点 {node.Name} -> {ScopeText(scopes, node, node)}");

            return sb.ToString();
        }

        private static string ScopeText(Dictionary<Type, E_ServiceType> scopes, Type node, Type declaringType)
        {
            if (scopes.TryGetValue(node, out var nodeScope))
                return nodeScope + "（来自节点自身）";
            if (scopes.TryGetValue(declaringType, out var declaringScope))
                return declaringScope + "（来自声明类型）";
            return "无 [ServiceScope] —— Register 时会抛异常";
        }

        private static void ExpectRegistered(IServiceRegistry registry, Type type, List<string> errors, string desc)
        {
            if (!registry.TryGetValue(type, out var instance))
                errors.Add($"{desc}，但注册表里没有 {type.Name}");
            else if (instance == null)
                errors.Add($"{desc}，但 {type.Name} 注册的是 null");
            else
                Debug.Log($"[注册] {type.Name} -> {instance.GetType().Name} OK");
        }

        private static void ExpectNotRegistered(IServiceRegistry registry, Type type, List<string> errors, string desc)
        {
            if (registry.TryGetValue(type, out var instance))
                errors.Add($"{desc}，实际注册了 {instance?.GetType().Name ?? "null"}");
        }

        private static void Report(List<string> errors, string successMessage = null)
        {
            if (errors.Count == 0)
                Debug.Log($"<color=green>PASS: {successMessage}</color>");
            else
                foreach (var e in errors)
                    Debug.LogError("FAIL: " + e);
        }

        /// <summary>
        /// 注入功能测试：验证字段 / 属性 / 方法三种注入都生效，
        /// 且注入进去的实例必须来自注册表（而不是重新 new 的）。
        /// </summary>
        [MenuItem("Tools/DI/Run Injection Test")]
        public static void RunInjectionTest()
        {
            var errors = new List<string>();
            var tempObjects = new List<GameObject>();

            try
            {
                // 消费者是纯 Mono 宿主，容器不创建它们，注入时要从场景里找，所以先保证场景里有
                if (UnityEngine.Object.FindFirstObjectByType<Provider>() == null)
                    tempObjects.Add(NewHost<Provider>("[DI-Test] Provider"));
                if (UnityEngine.Object.FindFirstObjectByType<EnvironmentSystem>() == null)
                    tempObjects.Add(NewHost<EnvironmentSystem>("[DI-Test] EnvironmentSystem"));

                var classA = UnityEngine.Object.FindFirstObjectByType<ClassA>();
                if (classA == null)
                {
                    var go = NewHost<ClassA>("[DI-Test] ClassA");
                    tempObjects.Add(go);
                    classA = go.GetComponent<ClassA>();
                }
                var classB = UnityEngine.Object.FindFirstObjectByType<ClassB>();
                if (classB == null)
                {
                    var go = NewHost<ClassB>("[DI-Test] ClassB");
                    tempObjects.Add(go);
                    classB = go.GetComponent<ClassB>();
                }

                var registry = new ServiceRegistry();
                var injector = new Injector(registry);
                try
                {
                    injector.Initialize();
                }
                catch (Exception e)
                {
                    errors.Add($"Initialize() 抛出异常，注入中断：{e.GetType().Name}: {e.Message}");
                    Report(errors);
                    return;
                }

                // ClassA：属性注入 + 方法注入
                ExpectSameAsRegistered(registry, typeof(ServiceB), classA.serviceB, errors, "ClassA.serviceB（属性注入）");
                ExpectSameAsRegistered(registry, typeof(ServiceA), ReadField(classA, "serviceA"), errors, "ClassA.Init(ServiceA)（方法注入）");

                // ClassB：字段注入
                ExpectSameAsRegistered(registry, typeof(ServiceA), ReadField(classB, "serviceA"), errors, "ClassB.serviceA（字段注入）");
                ExpectSameAsRegistered(registry, typeof(ServiceB), ReadField(classB, "serviceB"), errors, "ClassB.serviceB（字段注入）");
                ExpectSameAsRegistered(registry, typeof(EnvironmentSystem), ReadField(classB, "environmentSystem"), errors, "ClassB.environmentSystem（字段注入）");

                Report(errors, "字段/属性/方法注入正确");
            }
            finally
            {
                foreach (var go in tempObjects)
                    UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static GameObject NewHost<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.AddComponent<T>();
            return go;
        }

        private static object ReadField(object target, string name)
        {
            return target.GetType()
                .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                .GetValue(target);
        }

        private static void ExpectSameAsRegistered(IServiceRegistry registry, Type serviceType, object injected, List<string> errors, string desc)
        {
            if (injected == null)
            {
                errors.Add($"{desc} 注入为 null");
                return;
            }
            if (!registry.TryGetValue(serviceType, out var expected))
            {
                errors.Add($"{desc} 校验失败：注册表里没有 {serviceType.Name}");
                return;
            }
            if (!ReferenceEquals(injected, expected))
                errors.Add($"{desc} 注入的不是注册表里的实例");
            else
                Debug.Log($"[注入] {desc} -> {serviceType.Name} OK");
        }

        /// <summary>
        /// 销毁功能测试：验证场景级服务按「反拓扑序」调用 Dispose，
        /// 即依赖方（DisposableHostService）先于被依赖方（DisposableLeafService）销毁；
        /// Root 级服务不参与清理，未实现 IDisposableService 的服务只清注册表。
        /// </summary>
        [MenuItem("Tools/DI/Run Scene Dispose Test")]
        public static void RunSceneDisposeTest()
        {
            var errors = new List<string>();
            var tempObjects = new List<GameObject>();

            DisposableLeafService.Events.Clear();
            DisposableRootService.DisposeCount = 0;

            try
            {
                if (UnityEngine.Object.FindFirstObjectByType<Provider>() == null)
                    tempObjects.Add(NewHost<Provider>("[DI-Test] Provider"));
                if (UnityEngine.Object.FindFirstObjectByType<EnvironmentSystem>() == null)
                    tempObjects.Add(NewHost<EnvironmentSystem>("[DI-Test] EnvironmentSystem"));

                var registry = new ServiceRegistry();
                var injector = new Injector(registry);
                injector.Initialize();

                Debug.Log("[销毁测试] 创建顺序: " + string.Join(" -> ", DisposableLeafService.Events));

                // 创建阶段：被依赖方先创建
                ExpectEventOrder("Create:Leaf", "Create:Host", errors, "创建阶段");

                if (!registry.TryGetValue(typeof(DisposableRootService), out _))
                    errors.Add("DisposableRootService（Root 级）应被注册");

                injector.DisposeSceneServices();

                Debug.Log("[销毁测试] 完整顺序: " + string.Join(" -> ", DisposableLeafService.Events));

                // 销毁阶段：依赖方先销毁，这是反拓扑序的核心断言
                ExpectEventOrder("Dispose:Host", "Dispose:Leaf", errors, "销毁阶段");

                // 场景级服务已清空
                ExpectNotRegistered(registry, typeof(ServiceA), errors, "清空后 ServiceA 应被移除");
                ExpectNotRegistered(registry, typeof(DisposableLeafService), errors, "清空后 DisposableLeafService 应被移除");
                ExpectNotRegistered(registry, typeof(DisposableHostService), errors, "清空后 DisposableHostService 应被移除");

                // Root 级服务保留，且不应被调用 Dispose
                ExpectRegistered(registry, typeof(DisposableRootService), errors, "DisposableRootService 是 Root 级，清空后应保留");
                if (DisposableRootService.DisposeCount != 0)
                    errors.Add($"DisposableRootService 是 Root 级，不应被 Dispose，实际调用 {DisposableRootService.DisposeCount} 次");

                // 重复调用应当安全：nodeScopes 已清，不应再次触发 Dispose
                var eventCountBefore = DisposableLeafService.Events.Count;
                injector.DisposeSceneServices();
                if (DisposableLeafService.Events.Count != eventCountBefore)
                    errors.Add("重复调用 DisposeSceneServices 不应再次调用 Dispose");

                Report(errors, "场景级服务按反拓扑序销毁正确");
            }
            finally
            {
                foreach (var go in tempObjects)
                    UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// 过场景重建测试：模拟 Bootstrap 的「DisposeSceneServices -> Initialize」循环，
        /// 验证第二次初始化不会重复登记注入成员、服务能重建、Root 级仍可用且实例被复用、
        /// 销毁顺序不变。
        /// </summary>
        [MenuItem("Tools/DI/Run Reinitialize Test")]
        public static void RunReinitializeTest()
        {
            var errors = new List<string>();
            var tempObjects = new List<GameObject>();

            DisposableLeafService.Events.Clear();

            try
            {
                if (UnityEngine.Object.FindFirstObjectByType<Provider>() == null)
                    tempObjects.Add(NewHost<Provider>("[DI-Test] Provider"));
                if (UnityEngine.Object.FindFirstObjectByType<EnvironmentSystem>() == null)
                    tempObjects.Add(NewHost<EnvironmentSystem>("[DI-Test] EnvironmentSystem"));

                var classB = UnityEngine.Object.FindFirstObjectByType<ClassB>();
                if (classB == null)
                {
                    var go = NewHost<ClassB>("[DI-Test] ClassB");
                    tempObjects.Add(go);
                    classB = go.GetComponent<ClassB>();
                }

                var registry = new ServiceRegistry();
                var injector = new Injector(registry);
                injector.Initialize();

                var fieldCount = injector.InjectFields.Values.Sum(list => list.Count);
                var propertyCount = injector.InjectProperties.Values.Sum(list => list.Count);
                var methodCount = injector.InjectMethods.Values.Sum(list => list.Count);

                // Root 级服务跨场景复用：清理前后必须是同一个实例
                registry.TryGetValue(typeof(DisposableRootService), out var rootBefore);

                // 模拟过场景
                injector.DisposeSceneServices();
                ExpectNotRegistered(registry, typeof(ServiceA), errors, "清理后 ServiceA 应被移除");

                // 新场景重建
                injector.Initialize();

                if (injector.InjectFields.Values.Sum(list => list.Count) != fieldCount)
                    errors.Add("重复初始化后字段注入成员被重复登记");
                if (injector.InjectProperties.Values.Sum(list => list.Count) != propertyCount)
                    errors.Add("重复初始化后属性注入成员被重复登记");
                if (injector.InjectMethods.Values.Sum(list => list.Count) != methodCount)
                    errors.Add("重复初始化后方法注入成员被重复登记");

                ExpectRegistered(registry, typeof(ServiceA), errors, "重建后 ServiceA 应重新注册");
                ExpectRegistered(registry, typeof(EnvironmentSystem), errors, "重建后 Root 级服务应仍在注册表");
                ExpectSameAsRegistered(registry, typeof(ServiceA), ReadField(classB, "serviceA"), errors, "重建后 ClassB.serviceA");

                registry.TryGetValue(typeof(DisposableRootService), out var rootAfter);
                if (!ReferenceEquals(rootBefore, rootAfter))
                    errors.Add("Root 级服务应跨场景复用，重建后实例不应改变");

                // 重建后销毁顺序必须依旧正确
                injector.DisposeSceneServices();
                var n = DisposableLeafService.Events.Count;
                if (n < 2
                    || DisposableLeafService.Events[n - 2] != "Dispose:Host"
                    || DisposableLeafService.Events[n - 1] != "Dispose:Leaf")
                    errors.Add("重建后的销毁顺序应为 Host -> Leaf");

                Report(errors, "过场景重建（重复初始化）正确");
            }
            finally
            {
                foreach (var go in tempObjects)
                    UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void ExpectEventOrder(string before, string after, List<string> errors, string phase)
        {
            var i = DisposableLeafService.Events.IndexOf(before);
            var j = DisposableLeafService.Events.IndexOf(after);
            if (i < 0) { errors.Add($"{phase}：事件里没有 {before}"); return; }
            if (j < 0) { errors.Add($"{phase}：事件里没有 {after}"); return; }
            if (i > j) errors.Add($"{phase}：{before} 应早于 {after}，实际顺序相反");
        }

        /// <summary>测试专用类型：不属于运行时服务，全局断言（构造注入数量、入度表）要排除它们。</summary>
        private static bool IsTestOnlyType(Type type)
        {
            return type == typeof(DisposableLeafService)
                || type == typeof(DisposableHostService)
                || type == typeof(DisposableRootService)
                || type == typeof(HostDependencyService)
                || type == typeof(HostingProvider)
                || type == typeof(HostedServiceOne)
                || type == typeof(HostedServiceTwo)
                || type == typeof(ReadyService);
        }

        private static void RunSorterTests(Injector injector, List<string> errors)
        {
            // 场景 1：真实依赖图。依赖必须排在消费者之前，且节点一个都不能少
            var order = Sorter.PerformKahnAlgorithm(injector.InEdgeCounts, injector.OutEdges);
            Debug.Log("[拓扑排序] 真实依赖图顺序: " + string.Join(" -> ", order.Select(t => t.Name)));

            if (order.Count != injector.Nodes.Count())
                errors.Add($"拓扑排序节点数应为 {injector.Nodes.Count()}，实际为 {order.Count}");

            ExpectBefore(order, typeof(ServiceA), typeof(ClassA), errors);
            ExpectBefore(order, typeof(ServiceA), typeof(ClassB), errors);
            ExpectBefore(order, typeof(ServiceA), typeof(CtorService), errors);
            ExpectBefore(order, typeof(ServiceB), typeof(ClassA), errors);
            ExpectBefore(order, typeof(ServiceB), typeof(ClassB), errors);
            ExpectBefore(order, typeof(EnvironmentSystem), typeof(ClassB), errors);

            // 排序不应污染 Injector 的入度表（Sorter 内部应拷贝）
            foreach (var kv in injector.InEdgeCounts)
            {
                if (IsTestOnlyType(kv.Key)) continue;
                var expected = kv.Key == typeof(ClassA) ? 2
                    : kv.Key == typeof(ClassB) ? 3
                    : kv.Key == typeof(CtorService) ? 1 : 0;
                if (kv.Value != expected)
                    errors.Add($"拓扑排序后入度表被修改: {kv.Key.Name} 应为 {expected}，实际为 {kv.Value}");
            }

            // 场景 2：菱形依赖 A <- B,C <- D
            var a = typeof(DiamondA);
            var b = typeof(DiamondB);
            var c = typeof(DiamondC);
            var d = typeof(DiamondD);
            var diamondCounts = new Dictionary<Type, int> { { a, 0 }, { b, 1 }, { c, 1 }, { d, 2 } };
            var diamondEdges = new Dictionary<Type, HashSet<Type>>
            {
                { a, new HashSet<Type> { b, c } },
                { b, new HashSet<Type> { d } },
                { c, new HashSet<Type> { d } },
                { d, new HashSet<Type>() },
            };
            var diamondOrder = Sorter.PerformKahnAlgorithm(diamondCounts, diamondEdges);
            Debug.Log("[拓扑排序] 菱形顺序: " + string.Join(" -> ", diamondOrder.Select(t => t.Name)));
            ExpectBefore(diamondOrder, a, b, errors);
            ExpectBefore(diamondOrder, a, c, errors);
            ExpectBefore(diamondOrder, b, d, errors);
            ExpectBefore(diamondOrder, c, d, errors);
            if (diamondOrder.Count != 4)
                errors.Add($"菱形排序节点数应为 4，实际为 {diamondOrder.Count}");

            // 场景 3：多个孤立节点、完全没有边。必须全部返回，不能被提前 return 丢掉
            var isolatedCounts = new Dictionary<Type, int> { { typeof(IsolatedA), 0 }, { typeof(IsolatedB), 0 } };
            var isolatedOrder = Sorter.PerformKahnAlgorithm(isolatedCounts, new Dictionary<Type, HashSet<Type>>());
            if (isolatedOrder.Count != 2)
                errors.Add($"无依赖图排序节点数应为 2，实际为 {isolatedOrder.Count}");

            // 场景 4：循环依赖必须报错，不能静默返回不完整结果
            var x = typeof(CycleX);
            var y = typeof(CycleY);
            var cycleCounts = new Dictionary<Type, int> { { x, 1 }, { y, 1 } };
            var cycleEdges = new Dictionary<Type, HashSet<Type>>
            {
                { x, new HashSet<Type> { y } },
                { y, new HashSet<Type> { x } },
            };
            var cycleDetected = false;
            try
            {
                Sorter.PerformKahnAlgorithm(cycleCounts, cycleEdges);
            }
            catch (InvalidOperationException e)
            {
                cycleDetected = e.Message.Contains("依赖成环");
            }
            if (!cycleDetected)
                errors.Add("循环依赖应抛出「依赖成环」异常，但未检测到");
            else
                Debug.Log("[拓扑排序] 循环依赖检测生效");
        }

        private static void ExpectBefore(IList<Type> order, Type before, Type after, List<string> errors)
        {
            var i = order.IndexOf(before);
            var j = order.IndexOf(after);
            if (i < 0) { errors.Add($"拓扑排序结果里没有 {before.Name}"); return; }
            if (j < 0) { errors.Add($"拓扑排序结果里没有 {after.Name}"); return; }
            if (i > j) errors.Add($"{before.Name} 应排在 {after.Name} 之前，实际顺序相反");
        }

        private class DiamondA { }
        private class DiamondB { }
        private class DiamondC { }
        private class DiamondD { }
        private class IsolatedA { }
        private class IsolatedB { }
        private class CycleX { }
        private class CycleY { }

        private static void ExpectOutEdge(Injector injector, Type dependency, Type[] consumers, List<string> errors, string desc)
        {
            if (!injector.OutEdges.TryGetValue(dependency, out var set))
            {
                errors.Add($"{desc}，但出边里没有 {dependency.Name}");
                return;
            }
            var missing = consumers.Where(c => !set.Contains(c)).ToArray();
            var extra = set.Where(c => !consumers.Contains(c)).ToArray();
            if (missing.Length > 0) errors.Add($"{desc}，缺少消费者: {string.Join(", ", missing.Select(t => t.Name))}");
            if (extra.Length > 0) errors.Add($"{desc}，多出消费者: {string.Join(", ", extra.Select(t => t.Name))}");
        }

        private static void ExpectInEdge(Injector injector, Type consumer, int expected, List<string> errors, string desc)
        {
            if (!injector.InEdgeCounts.TryGetValue(consumer, out var count))
                errors.Add($"{desc}，但 {consumer.Name} 无入度记录");
            else if (count != expected)
                errors.Add($"{desc}，实际为 {count}");
        }
    }

    // ===== 以下为销毁测试专用服务 =====
    // 放在 Editor 程序集，只用于验证「反拓扑序销毁」；打包时不会进入运行时。
    // 用 [Inject] 构造函数让它们成为创建节点，从而进入初始化顺序。

    /// <summary>被依赖方：先创建、后销毁。</summary>
    [ServiceScope(E_ServiceType.Scene)]
    public class DisposableLeafService : IDisposableService
    {
        public static readonly List<string> Events = new List<string>();

        [Inject]
        public DisposableLeafService()
        {
            Events.Add("Create:Leaf");
        }

        public void Dispose()
        {
            Events.Add("Dispose:Leaf");
        }
    }

    /// <summary>依赖方：后创建、先销毁。</summary>
    [ServiceScope(E_ServiceType.Scene)]
    public class DisposableHostService : IDisposableService
    {
        // 依赖边由构造函数参数类型决定，不需要保存引用
        [Inject]
        public DisposableHostService(DisposableLeafService leaf)
        {
            DisposableLeafService.Events.Add("Create:Host");
        }

        public void Dispose()
        {
            DisposableLeafService.Events.Add("Dispose:Host");
        }
    }

    /// <summary>Root 级服务：跨场景保留，清理场景服务时不应被 Dispose。</summary>
    [ServiceScope(E_ServiceType.Root)]
    public class DisposableRootService : IDisposableService
    {
        public static int DisposeCount;

        [Inject]
        public DisposableRootService()
        {
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    // ===== 以下为「Unregistered + InjectExternal」测试专用类型 =====
    // 纯 C# 类：无 [Provide]、无 [Inject] 构造函数、不是 MonoBehaviour。
    // 预期：分类为 Unregistered，容器既不创建也不注入；由测试手动 new 后调用 InjectExternal。

    /// <summary>
    /// 注入成员特意用零参数方法，不产生任何依赖边，
    /// 因此不会改动其它测试的出边 / 入度断言。
    /// 也不标 [ServiceScope]：Unregistered 根本不进注册表，作用域无意义。
    /// </summary>
    public class DefaultCtorConsumer
    {
        public bool Injected;

        [Inject]
        public void OnInjected()
        {
            Injected = true;
        }
    }

    // ===== 以下为「普通类 Provider 宿主复用」测试专用类型 =====
    // 验证两件事：
    // 1. 宿主由容器按 [Inject] 构造函数创建，工厂方法拿到的依赖不为 null；
    // 2. 同一宿主类型只创建一次，多个 [Provide] 方法共享同一实例。

    [ServiceScope(E_ServiceType.Scene)]
    public class HostDependencyService
    {
        [Inject]
        public HostDependencyService() { }
    }

    [ServiceScope(E_ServiceType.Scene)]
    public class HostingProvider
    {
        public static int InstanceCount;
        public readonly HostDependencyService Dependency;

        [Inject]
        public HostingProvider(HostDependencyService dependency)
        {
            Dependency = dependency;
            InstanceCount++;
        }

        [Provide]
        public HostedServiceOne ProvideOne() => new HostedServiceOne(this, Dependency);

        [Provide]
        public HostedServiceTwo ProvideTwo() => new HostedServiceTwo(this, Dependency);
    }

    public class HostedServiceOne
    {
        public readonly HostingProvider Host;
        public readonly HostDependencyService Dependency;

        public HostedServiceOne(HostingProvider host, HostDependencyService dependency)
        {
            Host = host;
            Dependency = dependency;
        }
    }

    public class HostedServiceTwo
    {
        public readonly HostingProvider Host;
        public readonly HostDependencyService Dependency;

        public HostedServiceTwo(HostingProvider host, HostDependencyService dependency)
        {
            Host = host;
            Dependency = dependency;
        }
    }

    // ===== 以下为「IServiceReady」测试专用类型 =====
    // 验证就绪回调发生在所有服务创建与成员注入之后：构造函数依赖和字段注入都必须已就位。

    [ServiceScope(E_ServiceType.Scene)]
    public class ReadyService : IServiceReady
    {
        public static int ReadyCount;
        public static bool DependenciesAvailable;

        public readonly HostDependencyService CtorDependency;

        [Inject] HostDependencyService fieldDependency;

        [Inject]
        public ReadyService(HostDependencyService dependency)
        {
            CtorDependency = dependency;
        }

        public void OnServicesReady()
        {
            ReadyCount++;
            DependenciesAvailable = CtorDependency != null && fieldDependency != null;
        }
    }
}
