using System;

namespace DependencyInjection
{
    public interface IServiceRegistry
    {
        void ClearAllSceneService();
        void Register(Type type, object instance, E_ServiceType serviceType);
        bool TryGetValue(Type type, out object instance);
    }
}
