namespace DependencyInjection
{
    /// <summary>
    /// 服务就绪回调：所有服务完成创建与成员注入后，由容器按拓扑序调用。
    /// 适合做事件订阅、跨服务接线等「依赖全部就位之后」才能做的事。
    ///
    /// 时序边界：它发生在 Bootstrap.Awake 内部，早于其他 MonoBehaviour 的 Awake。
    /// 所以这里只应操作 DI 服务，不要假设场景组件的 Awake 已经执行过。
    /// 只有服务或注入宿主会被回调；没有任何 [Inject] 成员的 MonoBehaviour 不在依赖图里，
    /// 需要它参与的话，给它加一个 [Inject] 方法或让它成为服务。
    /// </summary>
    public interface IServiceReady
    {
        void OnServicesReady();
    }
}
