using UnityEngine;

namespace DependencyInjection
{
    /// <summary>
    /// 构造注入示例：纯 C# 服务，由容器通过 [Inject] 构造函数创建。
    /// 不依赖 MonoBehaviour，也不通过 [Provide] 工厂注册。
    /// </summary>
    [ServiceScope(E_ServiceType.Scene)]
    public class CtorService
    {
        private readonly ServiceA serviceA;

        /// <summary>
        /// 无参构造仅用于占位，实际注入走下面的 [Inject] 构造。
        /// </summary>
        public CtorService()
        {
        }

        [Inject]
        public CtorService(ServiceA serviceA)
        {
            this.serviceA = serviceA;
            Debug.Log("CtorService 通过构造注入创建");
        }

        public void Initialize()
        {
            serviceA.Initialize("from CtorService");
        }
    }
}
