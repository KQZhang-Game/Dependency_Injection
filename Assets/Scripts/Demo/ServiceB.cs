using UnityEngine;

namespace DependencyInjection
{
    [ServiceScope(E_ServiceType.Scene)]
    public class ServiceB
    {
        public void Initialize(string message = null)
        {
            Debug.Log($"ServiceB Initialize({message})");
        }
    }
}
