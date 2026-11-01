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

        /// <summary>
        /// 容器实例。手工创建的对象需要注入时调用 InjectExternal；
        /// 不要直接调用 Initialize / DisposeSceneServices，它们的时机由 Bootstrap 管理。
        /// </summary>
        public Injector Injector { get; private set; }

        private static int initializeCount;

        // 首场景的 sceneLoaded 在 Awake 之后派发，会与 Awake 里的初始化重复；
        // 记录 Bootstrap 所在场景，跳过它自己的那一次回调。
        private Scene initialScene;
        private bool initialSceneEventHandled;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            initialScene = gameObject.scene;   // 必须在 DontDestroyOnLoad 之前取，之后对象会被移到常驻场景
            DontDestroyOnLoad(gameObject);

            var runtime = new UnityRuntimeAdapter();

            // 只扫描直接引用 DependencyInjection 的程序集：写特性就必须引用它，
            // 引用不了的程序集不可能声明 [Provide] / [Inject]，扫了也是白扫。
            var scanSource = new ScannerScanSource(ScannerScanSource.ReferencesDependencyInjection);

            Registry = new ServiceRegistry(runtime);
            Injector = new Injector(Registry, runtime, scanSource);

            // 首个场景：场景对象此时都已实例化，就地完成一次初始化
            Debug.Log($"[DI] Initialize #{++initializeCount}（来源：{nameof(Awake)}）");
            Injector.Initialize();
            Debug.Log($"[DI] 扫描 {scanSource.ScannedAssemblyCount} 个程序集 / {scanSource.ScannedTypeCount} 个候选类型（{scanSource.LastScanMilliseconds} ms）");

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
            // 首场景已经在 Awake 里初始化过，这次回调要跳过，否则会重建一遍刚建好的 Scene 级服务
            if (!initialSceneEventHandled && scene.handle == initialScene.handle)
            {
                initialSceneEventHandled = true;
                Debug.Log("[DI] 跳过首场景的 sceneLoaded 回调（该场景已在 Awake 中初始化）");
                return;
            }

            Injector.DisposeSceneServices();
            Debug.Log($"[DI] Initialize #{++initializeCount}（来源：{nameof(OnSceneLoaded)}）");
            Injector.Initialize();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (Instance != this) return;

            Injector.DisposeSceneServices();
            Instance = null;
        }
    }
}
