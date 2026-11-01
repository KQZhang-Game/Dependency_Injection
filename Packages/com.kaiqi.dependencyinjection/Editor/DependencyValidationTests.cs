using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DependencyInjection
{
    /// <summary>
    /// 构建期校验测试：把「非法配置」直接喂给 DependencyGraph，验证建图阶段会拦下并给出可读信息。
    ///
    /// 为什么用假扫描源：这些非法类型如果带 [Provide] / [Inject] 特性，就会进入 Scanner 的全局扫描，
    /// 让每一次 Initialize 都抛异常，别的测试就全部跑不了了。
    /// 所以这里的测试类型一律不带特性，只通过 FakeScanSource 提供给被测的那一张图。
    /// </summary>
    public static class DependencyValidationTests
    {
        [MenuItem("Tools/DI/Run Validation Test")]
        public static void Run()
        {
            var errors = new List<string>();

            // 1. 普通类 Provider 宿主：有注入成员，却没有 [Inject] 构造函数
            //    工厂阶段宿主上的依赖必然是 null，属于确定的配置错误
            var invalidHost = typeof(InvalidProviderHost);
            var invalidSource = NewSource();
            invalidSource.ProvideMethods = new[] { invalidHost.GetMethod(nameof(InvalidProviderHost.ProvideThing)) };
            invalidSource.InjectableFields = new[] { invalidHost.GetField(nameof(InvalidProviderHost.dependency)) };
            ExpectThrows(
                () => new DependencyGraph(new UnityRuntimeAdapter(), invalidSource).Build(),
                "没有 [Inject] 构造函数",
                "Provider 宿主缺少构造注入",
                errors);

            // 2. 同样的宿主，补上 [Inject] 构造函数，就应当通过校验
            var validHost = typeof(ValidProviderHost);
            var validSource = NewSource();
            validSource.ProvideMethods = new[] { validHost.GetMethod(nameof(ValidProviderHost.ProvideOther)) };
            validSource.InjectableFields = new[] { validHost.GetField(nameof(ValidProviderHost.dependency)) };
            validSource.InjectableConstructors = new[] { validHost.GetConstructor(new[] { typeof(ServiceX) }) };
            ExpectNoThrow(
                () => new DependencyGraph(new UnityRuntimeAdapter(), validSource).Build(),
                "Provider 宿主有构造注入",
                errors);

            // 3. 同一类型既有 [Inject] 构造函数又有 [Provide] 工厂：两种创建方式互斥
            var conflictSource = NewSource();
            conflictSource.ProvideMethods = new[] { invalidHost.GetMethod(nameof(InvalidProviderHost.ProvideThing)) };  // 返回 ServiceX
            conflictSource.InjectableConstructors = new[] { typeof(ServiceX).GetConstructor(Type.EmptyTypes) };
            ExpectThrows(
                () => new DependencyGraph(new UnityRuntimeAdapter(), conflictSource).Build(),
                "同时声明了 [Inject] 构造函数和 [Provide] 工厂",
                "同一类型两种创建方式冲突",
                errors);

            // 4. Root 级服务依赖 Scene 级服务：Root 跨场景复用，Scene 依赖会被销毁重建
            var rootSource = NewSource();
            rootSource.ProvideMethods = new[] { typeof(RootHost).GetMethod(nameof(RootHost.ProvideRoot)) };
            rootSource.ServiceScopes = new Dictionary<Type, E_ServiceType>
            {
                { typeof(RootService), E_ServiceType.Root },
                { typeof(SceneDependency), E_ServiceType.Scene },
            };
            ExpectThrows(
                () => new DependencyGraph(new UnityRuntimeAdapter(), rootSource).Build(),
                "Root 级服务不能依赖 Scene 级服务",
                "Root 依赖 Scene",
                errors);

            // 5. 节点和声明类型都没有 [ServiceScope]：查作用域时必须报错，不能默认成某个作用域
            var noScopeGraph = new DependencyGraph(new UnityRuntimeAdapter(), NewSource());
            noScopeGraph.Build();
            ExpectThrows(
                () => noScopeGraph.GetScope(typeof(ServiceX), typeof(ServiceX)),
                "都没有 [ServiceScope]",
                "缺少 ServiceScope 时查作用域",
                errors);

            if (errors.Count == 0)
                Debug.Log("<color=green>PASS: 构建期校验分支正确</color>");
            else
                foreach (var e in errors)
                    Debug.LogError("FAIL: " + e);
        }

        private static FakeScanSource NewSource() => new FakeScanSource();

        /// <summary>期望抛出 InvalidOperationException，且信息里包含关键片段（避免只测「抛了」而测不到「讲清楚了」）。</summary>
        private static void ExpectThrows(Action action, string expectedFragment, string desc, List<string> errors)
        {
            try
            {
                action();
                errors.Add($"{desc}：应抛出异常，但没有抛");
            }
            catch (InvalidOperationException e)
            {
                if (!e.Message.Contains(expectedFragment))
                    errors.Add($"{desc}：异常信息缺少「{expectedFragment}」，实际为：{e.Message}");
                else
                    Debug.Log($"[校验] {desc} -> 已拦截");
            }
            catch (Exception e)
            {
                errors.Add($"{desc}：抛出的是 {e.GetType().Name}，应抛 InvalidOperationException：{e.Message}");
            }
        }

        private static void ExpectNoThrow(Action action, string desc, List<string> errors)
        {
            try
            {
                action();
                Debug.Log($"[校验] {desc} -> 通过");
            }
            catch (Exception e)
            {
                errors.Add($"{desc}：不应抛异常，实际抛出 {e.GetType().Name}：{e.Message}");
            }
        }

        /// <summary>
        /// 只提供被测数据，不调用 Scanner。
        /// 这样测试类型不带任何 DI 特性也能被当作合法 / 非法配置使用。
        /// </summary>
        private sealed class FakeScanSource : IDependencyScanSource
        {
            public MethodInfo[] ProvideMethods { get; set; } = Array.Empty<MethodInfo>();
            public ConstructorInfo[] InjectableConstructors { get; set; } = Array.Empty<ConstructorInfo>();
            public FieldInfo[] InjectableFields { get; set; } = Array.Empty<FieldInfo>();
            public PropertyInfo[] InjectableProperties { get; set; } = Array.Empty<PropertyInfo>();
            public MethodInfo[] InjectableMethods { get; set; } = Array.Empty<MethodInfo>();
            public Dictionary<Type, E_ServiceType> ServiceScopes { get; set; } = new Dictionary<Type, E_ServiceType>();
        }
    }

    // ===== 以下为校验测试专用类型 =====
    // 一律不带 [Provide] / [Inject] / [ServiceScope]：否则会被 Scanner 全局扫描，
    // 让正常的 Initialize 也抛异常。

    /// <summary>被提供的服务，同时用来构造「[Provide] 与 [Inject] 构造函数冲突」的场景。</summary>
    public class ServiceX
    {
        public ServiceX() { }
    }

    /// <summary>合法宿主提供的服务，用来区分「宿主本身」和「宿主提供的服务」。</summary>
    public class ServiceY
    {
    }

    /// <summary>非法宿主：非 MonoBehaviour，有注入成员但没有构造注入。</summary>
    public class InvalidProviderHost
    {
        public ServiceX dependency;

        public ServiceX ProvideThing() => new ServiceX();
    }

    /// <summary>合法宿主：同上有注入成员，但依赖通过构造函数拿到。</summary>
    public class ValidProviderHost
    {
        public ServiceX dependency;

        public ValidProviderHost(ServiceX dependency)
        {
            this.dependency = dependency;
        }

        public ServiceY ProvideOther() => new ServiceY();
    }

    /// <summary>Scene 级服务，被 Root 级服务依赖时属于非法配置。</summary>
    public class SceneDependency
    {
    }

    /// <summary>Root 级服务，构造参数是 Scene 级服务，用于触发 Root -> Scene 校验。</summary>
    public class RootService
    {
        public RootService(SceneDependency scene) { }
    }

    public class RootHost
    {
        public RootService ProvideRoot(SceneDependency scene) => new RootService(scene);
    }
}
