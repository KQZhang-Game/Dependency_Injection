using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnvironmentSystem : MonoBehaviour,IDependencyProvider,IEnvironmentSystem
{
    public void Initialize()
    {
        Debug.Log("EnvironmentSystem.Initialize()");
    }
    [Provide]
    public IEnvironmentSystem ProvideEnvironmentSystem()
    {
        return this;
    }
}
