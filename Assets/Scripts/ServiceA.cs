using System.Collections;
using System.Collections.Generic;
using UnityEditor.VersionControl;
using UnityEngine;

public class ServiceA
{
    public void Initialize(string message = null)
    {
        Debug.Log($"ServiceA Initialize({message})");
    }
}
