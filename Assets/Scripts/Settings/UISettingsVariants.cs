using UnityEngine;
using UnityEngine.UI;

public class UISettingsVariants : MonoBehaviour
{
    public static UISettingsVariants Instance { get; private set; }

    static UmaViewerBuilder Builder => UmaViewerBuilder.Instance;

    [SerializeField] private Toggle _switchWet;

    private bool _enableSwitchWet = false;

    public bool EnableSwitchWet
    {
        get { return _enableSwitchWet; }
        set
        {
            _switchWet.SetIsOnWithoutNotify(value);
            SetSwitchWetEnable(value);
        }
    }

    private void Awake()
    {
        Instance = this;
    }

    public void SetSwitchWetEnable(bool isOn)
    {
        _enableSwitchWet = isOn;
        Builder.CurrentUMAContainer?.SetSwitchWetEnable(isOn);
    }
}