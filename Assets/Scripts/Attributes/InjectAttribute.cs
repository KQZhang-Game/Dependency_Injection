using System;

namespace DependencyInjection
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor)]
    public class InjectAttribute : Attribute
    {
    }
}
