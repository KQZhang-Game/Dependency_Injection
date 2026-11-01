using System;
using System.Collections.Generic;
using System.Reflection;

namespace DependencyInjection
{
    /// <summary>
    /// 全量扫描的静态门面，内部委托给一个不带过滤器的 TypeScanner。
    ///
    /// 运行时的默认扫描源也是全量（ScannerScanSource 无参构造），
    /// 只有组合根显式传入过滤器时才会按程序集引用收敛（见 Bootstrap）。
    /// 保留这层静态门面，是为了让性能探针始终能量到「全量」基线。
    /// </summary>
    public static class Scanner
    {
        private static readonly TypeScanner Default = new TypeScanner();

        /// <summary>清空扫描结果缓存。仅用于性能测量，运行流程不需要调用。</summary>
        public static void ResetCache() => Default.ResetCache();

        /// <summary>上一次全量扫描覆盖的程序集数量。</summary>
        public static int ScannedAssemblyCount => Default.ScannedAssemblyCount;

        /// <summary>上一次全量扫描得到的候选类型数量。</summary>
        public static int ScannedTypeCount => Default.ScannedTypeCount;

        /// <summary>上一次全量扫描（枚举程序集 + GetTypes + 类型过滤）的耗时，毫秒。</summary>
        public static long LastScanMilliseconds => Default.LastScanMilliseconds;

        public static MethodInfo[] ScanProvideMethods() => Default.ScanProvideMethods();

        public static FieldInfo[] ScanInjectableField() => Default.ScanInjectableField();

        public static MethodInfo[] ScanInjectableMethods() => Default.ScanInjectableMethods();

        public static PropertyInfo[] ScanInjectableProperties() => Default.ScanInjectableProperties();

        public static ConstructorInfo[] ScanInjectableCtor() => Default.ScanInjectableCtor();

        public static Dictionary<Type, E_ServiceType> ScanServiceScopes() => Default.ScanServiceScopes();
    }
}
