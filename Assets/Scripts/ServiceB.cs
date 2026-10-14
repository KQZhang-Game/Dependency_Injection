using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ServiceB
{
    public void Initialize(string message = null)
    {
        Debug.Log($"ServiceB Initialize({message})");
    }
}
