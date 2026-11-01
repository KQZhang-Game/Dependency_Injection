using UnityEngine;

namespace DependencyInjection
{
    [ServiceScope(E_ServiceType.Root)]
    public class EnvironmentSystem : MonoBehaviour
    {
        public void Initialize()
        {
            Debug.Log("EnvironmentSystem.Initialize()");
        }
        [Provide]
        public EnvironmentSystem ProvideEnvironmentSystem()
        {
            return this;
        }
    }
}
