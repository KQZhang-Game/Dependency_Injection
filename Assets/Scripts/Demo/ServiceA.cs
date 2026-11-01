using UnityEngine;

namespace DependencyInjection
{
    [ServiceScope(E_ServiceType.Scene)]
    public class ServiceA
    {
        public void Initialize(string message = null)
        {
            Debug.Log($"ServiceA Initialize({message})");
        }
    }
}
