using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace DependencyInjection
{
    /// <summary>
    /// 类型扫描器，每个实例持有独立缓存。
    ///
    /// assemblyFilter 为 null 时扫描全部程序集（Scanner 用的就是这个全量实例）；
    /// 传入 ScannerScanSource.ReferencesDependencyInjection 则只扫描直接引用框架程序集的程序集。
    ///
    /// 缓存只取决于类型集合，与场景无关，过场景重建时可以直接复用。
    /// 静态缓存会在域重载时清空，实例缓存的生命周期跟随持有者。
    /// </summary>
    public sealed class TypeScanner
    {
        private const BindingFlags bindingFlags = BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private readonly Func<Assembly, bool> assemblyFilter;

        private Type[] types;
        private MethodInfo[] provideCache;
        private FieldInfo[] fieldCache;
        private MethodInfo[] methodCache;
        private PropertyInfo[] propertyCache;
        private ConstructorInfo[] ctorCache;
        private Dictionary<Type, E_ServiceType> scopeCache;

        public TypeScanner() : this(null) { }

        public TypeScanner(Func<Assembly, bool> assemblyFilter)
        {
            this.assemblyFilter = assemblyFilter;
        }

        /// <summary>上一次扫描覆盖的程序集数量（已应用过滤器）。</summary>
        public int ScannedAssemblyCount { get; private set; }

        /// <summary>上一次扫描得到的候选类型数量（已过滤掉非类和抽象类）。</summary>
        public int ScannedTypeCount { get; private set; }

        /// <summary>上一次扫描（枚举程序集 + GetTypes + 类型过滤）的耗时，毫秒。</summary>
        public long LastScanMilliseconds { get; private set; }

        /// <summary>
        /// 清空全部缓存，包括类型集合。仅用于性能测量，运行流程不需要调用。
        /// 类型集合也要一起清，否则测到的是缓存命中后的耗时，不是冷启动。
        /// </summary>
        public void ResetCache()
        {
            types = null;
            provideCache = null;
            fieldCache = null;
            methodCache = null;
            propertyCache = null;
            ctorCache = null;
            scopeCache = null;
            ScannedAssemblyCount = 0;
            ScannedTypeCount = 0;
            LastScanMilliseconds = 0;
        }

        private void EnsureAllTypesLoaded()
        {
            if (types != null && types.Length > 0) return;

            var stopwatch = Stopwatch.StartNew();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(asm => !asm.IsDynamic)
                .Where(asm => assemblyFilter == null || assemblyFilter(asm))
                .ToArray();
            ScannedAssemblyCount = assemblies.Length;

            types = assemblies
                .SelectMany(asm =>
                {
                    try
                    {
                        return asm.GetTypes();
                    }
                    catch (ReflectionTypeLoadException e)
                    {
                        return e.Types.Where(t => t != null);
                    }
                })
                .Where(type => type.IsClass && !type.IsAbstract)
                .ToArray();

            ScannedTypeCount = types.Length;
            LastScanMilliseconds = stopwatch.ElapsedMilliseconds;
        }

        public MethodInfo[] ScanProvideMethods()
        {
            EnsureAllTypesLoaded();
            if (provideCache != null) return provideCache;

            // 只认 [Provide] 特性，不要求实现任何接口：特性本身就是唯一的资格声明
            return provideCache = types.
                SelectMany(type => type.GetMethods(bindingFlags)).
                Where(method => Attribute.IsDefined(method, typeof(ProvideAttribute))).
                ToArray();
        }

        public FieldInfo[] ScanInjectableField()
        {
            EnsureAllTypesLoaded();
            if (fieldCache != null) return fieldCache;
            return fieldCache = types.
                SelectMany(type => type.GetFields(bindingFlags)).
                Where(field => Attribute.IsDefined(field, typeof(InjectAttribute))).
                ToArray();
        }

        public MethodInfo[] ScanInjectableMethods()
        {
            EnsureAllTypesLoaded();
            if (methodCache != null) return methodCache;
            return methodCache = types.
                SelectMany(type => type.GetMethods(bindingFlags)).
                Where(method => Attribute.IsDefined(method, typeof(InjectAttribute))).
                ToArray();
        }

        public PropertyInfo[] ScanInjectableProperties()
        {
            EnsureAllTypesLoaded();
            if (propertyCache != null) return propertyCache;
            return propertyCache = types.
                SelectMany(type => type.GetProperties(bindingFlags)).
                Where(property => Attribute.IsDefined(property, typeof(InjectAttribute))).
                ToArray();
        }

        public ConstructorInfo[] ScanInjectableCtor()
        {
            EnsureAllTypesLoaded();
            if (ctorCache != null) return ctorCache;
            return ctorCache = types.
                SelectMany(type => type.GetConstructors(bindingFlags)).
                Where(constructor => Attribute.IsDefined(constructor, typeof(InjectAttribute))).
                ToArray();
        }

        /// <summary>
        /// 扫描所有带 [ServiceScope] 的类型，返回「实现类型 -> 作用域」。
        /// 注册键可能是接口，但作用域仍按实现类型查询。
        /// </summary>
        public Dictionary<Type, E_ServiceType> ScanServiceScopes()
        {
            EnsureAllTypesLoaded();
            if (scopeCache != null) return scopeCache;
            var scopes = new Dictionary<Type, E_ServiceType>();
            foreach (var type in types)
            {
                var attribute = type.GetCustomAttribute<ServiceScopeAttribute>(false);
                if (attribute != null)
                    scopes[type] = attribute.Scope;
            }
            return scopeCache = scopes;
        }
    }
}
