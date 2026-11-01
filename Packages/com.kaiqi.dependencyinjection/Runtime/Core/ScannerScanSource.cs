using System;
using System.Collections.Generic;
using System.Reflection;

namespace DependencyInjection
{
    /// <summary>
    /// 默认扫描源。
    ///
    /// - 无参构造：复用 Scanner 的静态缓存（全量扫描），探针的「热」测量依赖这一点；
    /// - 传入过滤器：持有独立的 TypeScanner，缓存与全量基线互不影响。
    ///
    /// 收敛的合法性依据：使用 [Provide] / [Inject] / [ServiceScope] 的程序集，
    /// 必然直接引用 DependencyInjection 程序集（否则特性类型无法解析）。
    /// </summary>
    public sealed class ScannerScanSource : IDependencyScanSource
    {
        private const string FrameworkAssemblyName = "DependencyInjection";

        // null 表示转发给 Scanner 的静态缓存；非 null 表示本实例独立缓存
        private readonly TypeScanner scanner;

        public ScannerScanSource() { }

        public ScannerScanSource(Func<Assembly, bool> assemblyFilter)
        {
            scanner = new TypeScanner(assemblyFilter);
        }

        /// <summary>上一次扫描覆盖的程序集数量（已应用过滤器）。</summary>
        public int ScannedAssemblyCount =>
            scanner == null ? Scanner.ScannedAssemblyCount : scanner.ScannedAssemblyCount;

        /// <summary>上一次扫描得到的候选类型数量。</summary>
        public int ScannedTypeCount =>
            scanner == null ? Scanner.ScannedTypeCount : scanner.ScannedTypeCount;

        /// <summary>上一次扫描（枚举程序集 + GetTypes + 类型过滤）的耗时，毫秒。</summary>
        public long LastScanMilliseconds =>
            scanner == null ? Scanner.LastScanMilliseconds : scanner.LastScanMilliseconds;

        /// <summary>
        /// 只保留直接引用 DependencyInjection 程序集的程序集。
        /// 只需要直接引用判断：写特性就必须引用特性所在的程序集，间接引用不会带来任何 [Inject] 成员。
        /// </summary>
        public static bool ReferencesDependencyInjection(Assembly assembly)
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (string.Equals(reference.Name, FrameworkAssemblyName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public MethodInfo[] ProvideMethods =>
            scanner == null ? Scanner.ScanProvideMethods() : scanner.ScanProvideMethods();

        public ConstructorInfo[] InjectableConstructors =>
            scanner == null ? Scanner.ScanInjectableCtor() : scanner.ScanInjectableCtor();

        public FieldInfo[] InjectableFields =>
            scanner == null ? Scanner.ScanInjectableField() : scanner.ScanInjectableField();

        public PropertyInfo[] InjectableProperties =>
            scanner == null ? Scanner.ScanInjectableProperties() : scanner.ScanInjectableProperties();

        public MethodInfo[] InjectableMethods =>
            scanner == null ? Scanner.ScanInjectableMethods() : scanner.ScanInjectableMethods();

        public Dictionary<Type, E_ServiceType> ServiceScopes =>
            scanner == null ? Scanner.ScanServiceScopes() : scanner.ScanServiceScopes();
    }
}
