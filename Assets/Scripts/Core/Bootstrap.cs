using UnityEngine;
using UnityEngine.SceneManagement;

namespace DependencyInjection
{
    /// <summary>
    /// DI 引导器：全流程最早执行，持有容器的生命周期。
    ///
    /// - 自身过场景不移除（DontDestroyOnLoad），注册表与依赖图随之跨场景保留；
    /// - 首个场景在 Awake 里初始化（此时场景对象已实例化，可以直接扫描、查找）；
    /// - 之后每次新场景加载完成，先按反拓扑序销毁上一个场景的 Scene 级服务，再为新场景重建；
    /// - Root 级服务跨场景保留，不参与清理。
    ///
    /// 使用约定：Bootstrap 只放在第一个场景里，后续场景不要再放；
    /// 新场景中的脚本不要在 Awake 里访问 DI 服务（那时注入还没执行），用注入字段或 Start。
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public class Bootstrap : MonoBehaviour
    {
        public static Bootstrap Instance { get; private set; }

        /// <summary>容器持有的注册表，业务代码通过它解析服务。</summary>
        public IServiceRegistry Registry { get; private set; }

        private Injector injector;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            Registry = new ServiceRegistry();
            injector = new Injector(Registry);

            // 首个场景：场景对象此时都已实例化，就地完成一次初始化
            injector.Initialize();

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>
        /// 新场景加载完成后统一处理：
        /// 先销毁上一个场景的 Scene 级服务（反拓扑序），再为新场景重建。
        ///
        /// 两步放在同一个回调里，而不是分用 sceneUnloaded / sceneLoaded，
        /// 是为了不依赖「卸载与加载事件谁先触发」——否则可能出现清掉刚创建服务的时序。
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            injector.DisposeSceneServices();
            injector.Initialize();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (Instance != this) return;

            injector.DisposeSceneServices();
            Instance = null;
        }
    }
}
