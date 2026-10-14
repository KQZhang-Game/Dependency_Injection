using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ClassB : MonoBehaviour
{
    [Inject] ServiceA serviceA;
    [Inject] ServiceB serviceB;
    [Inject] IEnvironmentSystem environmentSystem;
    private void Start()
    {
        environmentSystem.Initialize();
        serviceB.Initialize();
    }
}
