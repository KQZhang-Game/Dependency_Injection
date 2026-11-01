using System;

namespace DependencyInjection
{
    [AttributeUsage(AttributeTargets.Method)]
    public class ProvideAttribute : Attribute
    {
    }
}
