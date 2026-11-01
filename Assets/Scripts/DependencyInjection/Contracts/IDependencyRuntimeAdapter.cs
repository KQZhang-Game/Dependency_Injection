using System;
using System.Collections.Generic;

namespace DependencyInjection
{
    /// <summary>
    /// 运行时环境适配接口：纯 C# 引擎需要的全部「Unity 能力」都收敛在这里。
    ///
    /// 需要适配的只有三件事：
    ///   1. 判断一个类型是不是场景对象（MonoBehaviour）；
    ///   2. 在场景里查找实例（单实例 / 多实例）；
    ///   3. 输出警告与错误日志。
    ///
    /// 引擎层（Scanner / Sorter / DependencyGraph / Injector / MemberInjector）因此不引用 UnityEngine，
    /// 后续按文件夹拆 asmdef 时，这一层可以直接编成不依赖 Unity 的程序集。
    ///
    /// 实现约定：查找不到、或对象已被销毁（Unity 的「假 null」）时，统一返回 null / 空集合，
    /// 不让引擎自己去判断 fake null。
    /// </summary>
    public interface IDependencyRuntimeAdapter
    {
        /// <summary>该类型是否为 Unity 场景对象（MonoBehaviour 及其子类）。</summary>
        bool IsSceneBehaviour(Type type);

        /// <summary>在场景中查找该类型的第一个实例；没有则返回 null。</summary>
        object FindSceneObject(Type type);

        /// <summary>在场景中查找该类型的全部实例；没有则返回空集合。</summary>
        IEnumerable<object> FindSceneObjects(Type type);

        void LogWarning(string message);
        void LogError(string message);
    }
}
