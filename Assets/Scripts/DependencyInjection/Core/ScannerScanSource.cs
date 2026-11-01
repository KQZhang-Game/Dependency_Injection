using System;
using System.Collections.Generic;
using System.Reflection;

namespace DependencyInjection
{
    /// <summary>默认扫描源：逐项转发给 Scanner，保留 Scanner 的静态缓存语义。</summary>
    public sealed class ScannerScanSource : IDependencyScanSource
    {
        public MethodInfo[] ProvideMethods => Scanner.ScanProvideMethods();
        public ConstructorInfo[] InjectableConstructors => Scanner.ScanInjectableCtor();
        public FieldInfo[] InjectableFields => Scanner.ScanInjectableField();
        public PropertyInfo[] InjectableProperties => Scanner.ScanInjectableProperties();
        public MethodInfo[] InjectableMethods => Scanner.ScanInjectableMethods();
        public Dictionary<Type, E_ServiceType> ServiceScopes => Scanner.ScanServiceScopes();
    }
}
