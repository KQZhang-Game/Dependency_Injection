using UnityEngine;

namespace DependencyInjection
{
    public class ClassA : MonoBehaviour
    {
        ServiceA serviceA;
        [Inject] public ServiceB serviceB { get; set; }
        [Inject]
        public void Init(ServiceA serviceA)
        {
            this.serviceA = serviceA;
        }
        private void Start()
        {
            serviceA.Initialize();
        }
    }
}
