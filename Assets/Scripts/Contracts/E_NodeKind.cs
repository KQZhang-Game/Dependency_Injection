namespace DependencyInjection
{
    /// <summary>
    /// 依赖图节点的实例来源。
    /// Provider / Constructor / SceneBehaviour 是三种明确的来源；
    /// Unregistered 表示没有显式来源：容器既不创建也不注入，
    /// 由业务方自己创建实例后调用 Injector.InjectExternal。
    /// </summary>
    public enum E_NodeKind
    {
        /// <summary>[Provide] 方法返回，由容器创建。</summary>
        Provider,
        /// <summary>[Inject] 构造函数，由容器创建。</summary>
        Constructor,
        /// <summary>场景中的 MonoBehaviour 实例，容器只注入、不创建。</summary>
        SceneBehaviour,
        /// <summary>无显式来源：容器不创建也不注入，需要外部创建后调用 InjectExternal。</summary>
        Unregistered
    }
}
