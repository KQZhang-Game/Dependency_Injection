using UnityEngine;

namespace DependencyInjection
{
    public class Provider : MonoBehaviour
    {
        [Provide]
        public ServiceA ProvideServiceA()
        {
            return new ServiceA();
        }
        [Provide]
        public ServiceB ProvideServiceB()
        {
            return new ServiceB();
        }
    }
}
