using System;
using System.Collections.Generic;
using System.Reflection;

namespace DependencyInjection
{
    /// <summary>
    /// 依赖图的数据来源。默认实现直接调用 Scanner（带缓存）。
    ///
    /// 单独抽这一层是为了让「构建期校验」可测试：校验要触发的都是非法配置，
    /// 如果把这些类型写成带 [Provide] / [Inject] 的真实类型，它们会被全局扫描，
    /// 导致每一次 Initialize 都抛异常，连正常流程也跑不了。
    /// 测试可以喂入不带特性的构造数据，只在被测的那张图上生效。
    ///
    /// 注意：实现方负责返回「已经过滤好的成员」，DependencyGraph 不会再检查特性。
    /// </summary>
    public interface IDependencyScanSource
    {
        MethodInfo[] ProvideMethods { get; }
        ConstructorInfo[] InjectableConstructors { get; }
        FieldInfo[] InjectableFields { get; }
        PropertyInfo[] InjectableProperties { get; }
        MethodInfo[] InjectableMethods { get; }
        Dictionary<Type, E_ServiceType> ServiceScopes { get; }
    }
}
