using UnityEngine;

public class ShaderPropDump : MonoBehaviour
{
    public Material mat;

    void Start()
    {
        if (!mat || !mat.shader) return;
        var s = mat.shader;
        Debug.Log($"Shader = {s.name}, props = {s.GetPropertyCount()}");
        for (int i = 0; i < s.GetPropertyCount(); i++)
        {
            Debug.Log($"{i}: {s.GetPropertyName(i)}  ({s.GetPropertyType(i)})");
        }
    }
}