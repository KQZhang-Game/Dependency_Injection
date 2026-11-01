using System;

namespace DependencyInjection
{
    /// <summary>
    /// 声明服务的作用域。作用域信息只认这个特性，provider 不再携带作用域。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ServiceScopeAttribute : Attribute
    {
        public E_ServiceType Scope { get; }

        public ServiceScopeAttribute(E_ServiceType scope)
        {
            Scope = scope;
        }
    }
}
