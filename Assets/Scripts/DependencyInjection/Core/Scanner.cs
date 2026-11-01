using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DependencyInjection
{
    public static class Scanner
    {
        private static Type[] types = new Type[] { };
        private const BindingFlags bindingFlags = BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        // 扫描结果缓存：结果只取决于类型集合，与场景无关，过场景重建时可以直接复用。
        // 静态字段在域重载时自动清空，所以改了代码不会读到旧结果。
        private static MethodInfo[] provideCache;
        private static FieldInfo[] fieldCache;
        private static MethodInfo[] methodCache;
        private static PropertyInfo[] propertyCache;
        private static ConstructorInfo[] ctorCache;
        private static Dictionary<Type, E_ServiceType> scopeCache;

        /// <summary>清空扫描结果缓存。仅用于性能测量，运行流程不需要调用。</summary>
        public static void ResetCache()
        {
            provideCache = null;
            fieldCache = null;
            methodCache = null;
            propertyCache = null;
            ctorCache = null;
            scopeCache = null;
        }

        private static void EnsureAllTypesLoaded()
        {
            if (types == null || types.Length == 0)
            {
                types = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(asm => !asm.IsDynamic)
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
            }
        }
        public static MethodInfo[] ScanProvideMethods()
        {
            EnsureAllTypesLoaded();
            if (provideCache != null) return provideCache;

            // 只认 [Provide] 特性，不要求实现任何接口：特性本身就是唯一的资格声明
            return provideCache = types.
                SelectMany(type => type.GetMethods(bindingFlags)).
                Where(method => Attribute.IsDefined(method, typeof(ProvideAttribute))).
                ToArray();
        }
        public static FieldInfo[] ScanInjectableField()
        {
            EnsureAllTypesLoaded();
            if (fieldCache != null) return fieldCache;
            return fieldCache = types.
                SelectMany(type => type.GetFields(bindingFlags)).
                Where(field => Attribute.IsDefined(field, typeof(InjectAttribute))).
                ToArray();
        }
        public static MethodInfo[] ScanInjectableMethods()
        {
            EnsureAllTypesLoaded();
            if (methodCache != null) return methodCache;
            return methodCache = types.
                SelectMany(type => type.GetMethods(bindingFlags)).
                Where(method => Attribute.IsDefined(method, typeof(InjectAttribute))).
                ToArray();
        }
        public static PropertyInfo[] ScanInjectableProperties()
        {
            EnsureAllTypesLoaded();
            if (propertyCache != null) return propertyCache;
            return propertyCache = types.
                SelectMany(type => type.GetProperties(bindingFlags)).
                Where(property => Attribute.IsDefined(property, typeof(InjectAttribute))).
                ToArray();
        }
        public static ConstructorInfo[] ScanInjectableCtor()
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
        public static Dictionary<Type, E_ServiceType> ScanServiceScopes()
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
