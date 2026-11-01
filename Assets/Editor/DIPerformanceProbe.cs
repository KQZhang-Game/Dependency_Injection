using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DependencyInjection
{
    /// <summary>
    /// 反射耗时探针：用 Stopwatch 分段测量 Scanner 的各扫描方法与 Injector.Initialize，
    /// 直观感受「扫全 AppDomain + 反射遍历成员」的真实成本。
    /// 菜单：Tools/DI/Measure Reflection Cost
    /// </summary>
    public static class DIPerformanceProbe
    {
        /// <summary>重复次数。首次调用含 JIT，所以每个方法先预热一次再计时。</summary>
        private const int Iterations = 10;

        private const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>本次测量的全部输出。控制台与桌面 txt 写的是同一份内容。</summary>
        private static readonly StringBuilder report = new StringBuilder();

        [MenuItem("Tools/DI/Measure Reflection Cost")]
        public static void MeasureReflectionCost()
        {
            report.Clear();

            try
            {
                // 阶段 1：冷扫描。Scanner 内部有静态缓存，这里自己走一遍真实流程，
                // 才能看到「第一次扫全 AppDomain」的成本。
                var classes = MeasureColdAssemblyScan();

                // 冷：每次测量前清空缓存，测的是真实反射成本
                Log("=== 各扫描方法（冷：每次清空缓存后重扫） ===");
                MeasureCold("ScanProvideMethods", () => Scanner.ScanProvideMethods().Length);
                MeasureCold("ScanInjectableCtor", () => Scanner.ScanInjectableCtor().Length);
                MeasureCold("ScanInjectableField", () => Scanner.ScanInjectableField().Length);
                MeasureCold("ScanInjectableProperties", () => Scanner.ScanInjectableProperties().Length);
                MeasureCold("ScanInjectableMethods", () => Scanner.ScanInjectableMethods().Length);
                MeasureCold("ScanServiceScopes", () => Scanner.ScanServiceScopes().Count);

                // 热：缓存命中，测的是取缓存本身的成本
                Log("=== 各扫描方法（热：缓存命中） ===");
                Measure("ScanProvideMethods", () => Scanner.ScanProvideMethods().Length);
                Measure("ScanInjectableCtor", () => Scanner.ScanInjectableCtor().Length);
                Measure("ScanInjectableField", () => Scanner.ScanInjectableField().Length);
                Measure("ScanInjectableProperties", () => Scanner.ScanInjectableProperties().Length);
                Measure("ScanInjectableMethods", () => Scanner.ScanInjectableMethods().Length);
                Measure("ScanServiceScopes", () => Scanner.ScanServiceScopes().Count);

                Log("=== 对照：只枚举、不判特性 ===");
                MeasureFullMethodEnumeration(classes);

                Log("=== 完整初始化 ===");
                MeasureInitialize();
            }
            finally
            {
                // 中途抛异常也要落盘，否则这次测量白跑
                WriteReport();
            }
        }

        /// <summary>
        /// 冷扫描：复刻 Scanner.EnsureAllTypesLoaded 的全流程（GetAssemblies -> GetTypes -> 过滤类）。
        /// 返回保留下来的类，供后续对照测量复用。
        /// </summary>
        private static Type[] MeasureColdAssemblyScan()
        {
            var sw = Stopwatch.StartNew();

            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(asm => !asm.IsDynamic)
                .ToArray();

            var allTypes = new List<Type>();
            foreach (var asm in assemblies)
            {
                try
                {
                    allTypes.AddRange(asm.GetTypes());
                }
                catch (ReflectionTypeLoadException e)
                {
                    // 与 Scanner 一致：加载失败的程序集只保留成功加载的类型
                    allTypes.AddRange(e.Types.Where(t => t != null));
                }
            }

            var classes = allTypes.Where(t => t.IsClass && !t.IsAbstract).ToArray();

            sw.Stop();
            Log($"[反射] 冷扫描：{assemblies.Length} 个程序集，{allTypes.Count} 个类型，" +
                $"其中类 {classes.Length} 个，耗时 {sw.Elapsed.TotalMilliseconds:F2} ms");

            return classes;
        }

        /// <summary>
        /// 对照项：只调用 GetMethods 枚举全部类的全部方法，不做任何特性判断。
        /// 这是 ScanProvideMethods 去掉接口过滤后必须付出的基础成本。
        /// </summary>
        private static void MeasureFullMethodEnumeration(Type[] classes)
        {
            // 预热
            foreach (var type in classes)
                type.GetMethods(Flags);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; i++)
                foreach (var type in classes)
                    type.GetMethods(Flags);
            sw.Stop();

            Log($"[反射] 仅 GetMethods 枚举 {classes.Length} 个类的全部方法：" +
                $"平均 {sw.Elapsed.TotalMilliseconds / Iterations:F3} ms / 次");
        }

        /// <summary>测量完整注入流程。需要场景里有 Provider / EnvironmentSystem 宿主，没有就临时造一个。</summary>
        private static void MeasureInitialize()
        {
            var tempObjects = new List<GameObject>();

            try
            {
                if (UnityEngine.Object.FindFirstObjectByType<Provider>() == null)
                    tempObjects.Add(NewHost<Provider>("[DI-Probe] Provider"));
                if (UnityEngine.Object.FindFirstObjectByType<EnvironmentSystem>() == null)
                    tempObjects.Add(NewHost<EnvironmentSystem>("[DI-Probe] EnvironmentSystem"));

                // 冷：每次重建 Injector 前清空扫描缓存，包含完整的反射扫描
                MeasureCold("Injector.Initialize（冷）", () =>
                {
                    var injector = new Injector(new ServiceRegistry());
                    injector.Initialize();
                    return injector.Nodes.Count();
                });

                // 热：扫描缓存命中，只测依赖图构建 + 实例化 + 注入
                Measure("Injector.Initialize（热）", () =>
                {
                    var injector = new Injector(new ServiceRegistry());
                    injector.Initialize();
                    return injector.Nodes.Count();
                });
            }
            catch (Exception e)
            {
                LogError($"[反射] Initialize 测量失败：{e.GetType().Name}: {e.Message}");
            }
            finally
            {
                foreach (var go in tempObjects)
                    UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>执行 action 一次作为预热，再重复计时，取平均值。action 返回值用于显示命中数量。</summary>
        private static void Measure(string name, Func<int> action)
        {
            action();

            var sw = Stopwatch.StartNew();
            int hits = 0;
            for (int i = 0; i < Iterations; i++)
                hits = action();
            sw.Stop();

            Log($"[反射] {name,-24} 平均 {sw.Elapsed.TotalMilliseconds / Iterations:F3} ms / 次，命中 {hits} 个");
        }

        /// <summary>
        /// 与 Measure 相同，但每次 action 前清空 Scanner 缓存。
        /// 不清缓存的话，第一次扫描之后所有测量都会命中缓存，测到的接近 0 ms。
        /// </summary>
        private static void MeasureCold(string name, Func<int> action)
        {
            // 预热 JIT，这一轮不计时
            Scanner.ResetCache();
            action();

            var sw = Stopwatch.StartNew();
            int hits = 0;
            for (int i = 0; i < Iterations; i++)
            {
                Scanner.ResetCache();
                hits = action();
            }
            sw.Stop();

            Log($"[反射] {name,-24} 平均 {sw.Elapsed.TotalMilliseconds / Iterations:F3} ms / 次，命中 {hits} 个");
        }

        /// <summary>同时写控制台和报告文件，保证 txt 与控制台内容一致。</summary>
        private static void Log(string line)
        {
            Debug.Log(line);
            report.AppendLine(line);
        }

        private static void LogError(string line)
        {
            Debug.LogError(line);
            report.AppendLine("[ERROR] " + line);
        }

        /// <summary>把本次测量结果写到桌面上的新 txt 文件。文件名带时间戳，不会覆盖旧报告。</summary>
        private static void WriteReport()
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var path = Path.Combine(desktop, $"DI_Performance_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            try
            {
                var content = $"DI 反射耗时测量\n生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n" + report.ToString();
                File.WriteAllText(path, content, Encoding.UTF8);
                Debug.Log($"[反射] 报告已写入：{path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[反射] 报告写入失败：{e.GetType().Name}: {e.Message}\n目标路径：{path}");
            }
        }

        private static GameObject NewHost<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.AddComponent<T>();
            return go;
        }
    }
}
