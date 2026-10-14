using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
[DefaultExecutionOrder(-1000)]
public class Injector : MonoBehaviour
{
    private static Injector instance;
    public static Injector Instance => instance;

    const BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    readonly Dictionary<Type, object> registry = new Dictionary<Type, object>();
    private void Awake()
    {
        if (!Application.isPlaying) return;
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        Initialize();
    }
    private void Initialize()
    {
        var providers = FindMonoBehaviours().OfType<IDependencyProvider>();
        foreach (var provider in providers)
        {
            RegisterProvider(provider);
        }
        var injectables = FindMonoBehaviours().Where(IsInjectable);
        foreach (var i in injectables)
        {
            Inject(i);
        }
    }
    private void Inject(object injectable)
    {
        var type = injectable.GetType();
        var injectFields = type
            .GetFields(bindingFlags)
            .Where(field => Attribute.IsDefined(field, typeof(InjectAttribute)));
        foreach (var field in injectFields)
        {
            Type fieldType = field.FieldType;
            var resolveInstance = Resolve(fieldType);
            if (resolveInstance == null)
            {
                throw new Exception($"Failed to inject {fieldType.Name} into {type.Name}");
            }
            field.SetValue(injectable, resolveInstance);
            Debug.Log($"Field injected {fieldType.Name} into {type.Name}");
        }
        var injectMethods = type.GetMethods().Where(method => Attribute.IsDefined(method, typeof(InjectAttribute)));
        foreach (var method in injectMethods)
        {
            var parameters = method.GetParameters().Select(para => para.ParameterType).ToArray();
            var resolveInstances = parameters.Select(Resolve).ToArray();
            if (resolveInstances.Any(resolveInstance => resolveInstance == null))
            {
                throw new Exception($"Failed to inject {type.Name}.{method.Name}");
            }
            method.Invoke(injectable, resolveInstances);
            Debug.Log($"Method injected {type.Name}.{method.Name}");
        }
        var injectProperties = type.GetProperties().Where(property => Attribute.IsDefined(property, typeof(InjectAttribute)));
        foreach(var property in injectProperties)
        {
            Type propertyType = property.PropertyType;
            var resolveInstance = Resolve(propertyType);
            if (resolveInstance == null)
            {
                throw new Exception($"Failed to inject {propertyType.Name} into {type.Name}");
            }
            property.SetValue(injectable, resolveInstance);
            Debug.Log($"Property injected {propertyType.Name} into {type.Name}");
        }
    }
    private object Resolve(Type type)
    {
        registry.TryGetValue(type, out object instance);
        return instance;
    }

    private bool IsInjectable(MonoBehaviour mono)
    {
        var members = mono.GetType().GetMembers(bindingFlags);
        return members.Any(m => Attribute.IsDefined(m, typeof(InjectAttribute)));
    }
    private void RegisterProvider(IDependencyProvider provider)
    {
        var mathods = provider.GetType().GetMethods(bindingFlags);
        foreach (var m in mathods)
        {
            if (!Attribute.IsDefined(m, typeof(ProvideAttribute))) continue;
            var returnType = m.ReturnType;
            object instance = m.Invoke(provider, null);
            if (instance != null)
            {
                registry[returnType] = instance;
                Debug.Log($"Registed {returnType.Name} from {provider.GetType().Name}");
            }
            else
            {
                throw new Exception($" Provider {provider.GetType().Name} retruned null for{returnType.Name}");
            }
        }
    }
    static MonoBehaviour[] FindMonoBehaviours()
    {
        return FindObjectsOfType<MonoBehaviour>();
    }
}
