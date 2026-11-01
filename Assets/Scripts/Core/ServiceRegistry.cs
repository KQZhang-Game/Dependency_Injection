using System;
using System.Collections.Generic;
using UnityEngine;

namespace DependencyInjection
{
    public class ServiceRegistry : IServiceRegistry
    {
        private readonly Dictionary<Type, object> sceneRegistry = new Dictionary<Type, object>();
        private readonly Dictionary<Type, object> rootRegistry = new Dictionary<Type, object>();
        public void Register(Type type, object instance, E_ServiceType serviceType)
        {
            switch (serviceType)
            {
                case E_ServiceType.Scene:
                    if (sceneRegistry.ContainsKey(type))
                    {
                        Debug.LogWarning($"{type.FullName}服务已存在，将覆盖旧实例");
                    }
                    sceneRegistry[type] = instance;
                    break;
                case E_ServiceType.Root:
                    if (rootRegistry.ContainsKey(type))
                    {
                        Debug.LogWarning($"{type.FullName}服务已存在，将覆盖旧实例");
                    }
                    rootRegistry[type] = instance;
                    break;
                default:
                    throw new Exception("不存在这种类型的服务");
            }
        }
        /// <summary>
        /// 过场景时提供注销方法
        /// </summary>
        public void ClearAllSceneService()
        {
            sceneRegistry.Clear();
        }
        public bool TryGetValue(Type type, out object instance)
        {
            if (sceneRegistry.TryGetValue(type, out instance))
            {
                return true;
            }
            if (rootRegistry.TryGetValue(type, out instance))
            {
                return true;
            }
            return false;
        }
    }
}
