using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>Each test makes its objects in the scene the test runner opens for it (the runner puts the editor's own
/// scenes back afterwards), and they are destroyed after the test.</summary>
public abstract class TestScene
{
    readonly List<Object> _made = new List<Object>();

    [TearDown]
    public void DestroyMade()
    {
        foreach (var o in _made) if (o != null) Object.DestroyImmediate(o);
        _made.Clear();
    }

    /// <summary>Destroyed after the test.</summary>
    protected T Keep<T>(T o) where T : Object
    {
        _made.Add(o);
        return o;
    }

    protected T Make<T>(string name, Vector3 at) where T : Component
    {
        var go = Keep(new GameObject(name));
        go.transform.position = at;
        return go.AddComponent<T>();
    }
}
