using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Provider : MonoBehaviour, IDependencyProvider
{
    [Provide]
    public ServiceA ProvideServiceA()
    {
        return new ServiceA();
    }
    [Provide]
    public ServiceB ProvideServiceB()
    {
        return new ServiceB();
    }
}
