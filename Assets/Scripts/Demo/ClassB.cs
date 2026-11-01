using UnityEngine;

namespace DependencyInjection
{
    public class ClassB : MonoBehaviour
    {
        [Inject] ServiceA serviceA;
        [Inject] ServiceB serviceB;
        [Inject] EnvironmentSystem environmentSystem;
        private void Start()
        {
            environmentSystem.Initialize();
            serviceB.Initialize();
        }
    }
}
