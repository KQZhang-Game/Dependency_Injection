namespace DependencyInjection
{
    /// <summary>
    /// 场景级服务的销毁约定。
    /// 容器在过场景清理场景级服务时，会按「反拓扑序」依次调用 Dispose()，
    /// 即依赖方先销毁、被依赖方后销毁，避免 Dispose 过程中访问到已经销毁的服务。
    ///
    /// 注意：这里刻意不继承 System.IDisposable，避免容器误判普通类的 IDisposable 实现
    /// （例如自己管理资源的对象）为需要由容器销毁的服务。要实现容器生命周期，显式实现本接口。
    /// </summary>
    public interface IDisposableService
    {
        void Dispose();
    }
}
