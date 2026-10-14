using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ClassA : MonoBehaviour
{
    ServiceA serviceA;
    [Inject]public ServiceB serviceB { get; set; }
    [Inject]
    public void Init(ServiceA serviceA)
    {
        this.serviceA = serviceA;
    }
    private void Start()
    {
        serviceA.Initialize();
    }
}
