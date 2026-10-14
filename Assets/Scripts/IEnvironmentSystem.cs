using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IEnvironmentSystem
{
    IEnvironmentSystem ProvideEnvironmentSystem();
    void Initialize();
}
