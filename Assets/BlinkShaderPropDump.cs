using UnityEngine;

public class BlinkShaderPropDump : MonoBehaviour
{
    public Renderer target;

    void Start()
    {
        if (!target) return;
        var s = target.sharedMaterial.shader;
        Debug.Log("Shader = " + s.name);
        for (int i = 0; i < s.GetPropertyCount(); i++)
        {
            Debug.Log($"{i}: {s.GetPropertyType(i)} {s.GetPropertyName(i)}");
        }
    }
}
