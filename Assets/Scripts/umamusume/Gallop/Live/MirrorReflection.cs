using System;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(Renderer))]
public class MirrorReflection : MonoBehaviour
{
    [Header("Base Camera (a1+48 / a1+136)")]
    [SerializeField] private Camera _baseCamera;
    [SerializeField] private Transform _baseCameraTransform;

    [Header("Self (a1+144 / a1+200 / a1+228 / a1+252 / a1+256)")]
    [SerializeField] private Transform _selfTransform;
    [SerializeField] private Renderer _renderer;
    [SerializeField] private int _selfLayer;
    [SerializeField] private int _mirrorIndex;
    [SerializeField] private bool _initFlag;

    [Header("State (a1+168 / a1+232)")]
    [SerializeField] private bool _cameraReady;
    [SerializeField] private bool _initialized;

    [Header("Mirror Camera (a1+152 / a1+176 / a1+184 / a1+224)")]
    [SerializeField] private Camera _mirrorCamera;
    [SerializeField] private Transform _mirrorCameraTransform;
    [SerializeField] private RenderTexture _renderTexture;
    [SerializeField] private int _defaultCullingMask = -1;
    [SerializeField] private int _currentCullingMask = -1;

    [Header("Materials (a1+240)")]
    [SerializeField] private Material[] _materials;

    [Header("Material Params (a1+60 / a1+76 / a1+96 / a1+112 / a1+128 / a1+280 / a1+296)")]
    [SerializeField] private Vector4 _currentVec447;
    [SerializeField] private Vector4 _currentVec448;
    [SerializeField] private Color _currentColor = Color.white;

    [SerializeField] private Color _customBackgroundColor = Color.black;
    [SerializeField] private bool _useCustomBackgroundColor = false;

    [SerializeField] private Vector4 _defaultVec448;
    [SerializeField] private Vector4 _defaultVec447;

    [Header("RenderTexture")]
    [SerializeField] private bool _matchScreenSize = true;
    [SerializeField] private int _textureWidth = 1024;
    [SerializeField] private int _textureHeight = 1024;
    [SerializeField] private int _depthBuffer = 16;
    [SerializeField] private RenderTextureFormat _renderTextureFormat = RenderTextureFormat.ARGB32;

    [Header("Shader Property Names")]
    [SerializeField] private string _reflectionTexProperty = "_ReflectionTex";
    [SerializeField] private string _color74Property = "_Color";
    [SerializeField] private string _vector447Property = "_MirrorParam447";
    [SerializeField] private string _vector448Property = "_MirrorParam448";
    [SerializeField] private string _float89Property = "_MirrorFloat89";
    [SerializeField] private float _float89Value = 1f;

    [Header("Debug / Temporary")]
    [SerializeField] private bool _disableGameObjectAfterInit = false;
    [SerializeField] private bool _logDebug = false;

    private int _reflectionTexID;
    private int _color74ID;
    private int _vector447ID;
    private int _vector448ID;
    private int _float89ID;
    private bool _propertyIDsCached;

    // 对应你前面 helper 里传进来的 DOGetter<float>(director.GetMainCameraFovFactor)
    private Func<float> _fovFactorGetter;

    // 对应 AddBeginCameraRenderingCallback / AddEndCameraRenderingCallback
    private event Action<ScriptableRenderContext, Camera> _beginCameraRenderingCallbacks;
    private event Action<ScriptableRenderContext, Camera> _endCameraRenderingCallbacks;

    public Camera BaseCamera => _baseCamera;
    public Camera MirrorCamera => _mirrorCamera;
    public RenderTexture MirrorRT => _renderTexture;
    public bool Initialized => _initialized;
    public bool CameraReady => _cameraReady;

    // =========================
    // Gallop_MirrorReflection__Initialize
    // =========================
    public void Initialize(Camera baseCamera, int index, bool flag = false)
    {
        _initialized = false;

        if (baseCamera != null)
        {
            _baseCamera = baseCamera;
            _baseCameraTransform = baseCamera.transform;
        }

        if (_baseCamera == null)
        {
            _baseCamera = Camera.main;
            if (_baseCamera != null)
                _baseCameraTransform = _baseCamera.transform;
        }

        if (_baseCamera == null)
        {
            Log("Initialize failed: base camera is null.");
            return;
        }

        if (_renderer == null)
            _renderer = GetComponent<Renderer>();

        if (_renderer == null)
        {
            Log("Initialize failed: Renderer missing.");
            return;
        }

        if (_renderer.sharedMaterial == null &&
            (_renderer.sharedMaterials == null || _renderer.sharedMaterials.Length == 0))
        {
            Log("Initialize failed: no shared material.");
            return;
        }

        _mirrorIndex = index;
        _selfTransform = transform;
        _selfLayer = gameObject.layer;
        _initFlag = flag;

        CachePropertyIDs();
        InitMaterials();
        CreateMirrorCamera();
        UpdateRenderTexture();
        UpdateMirrorParams();

        _initialized = true;

        // 反编译里官方最后会 SetActive(false)
        // 但你现在是简化版调度，先默认 false，避免把子相机一起关掉。
        if (_disableGameObjectAfterInit && gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }

        Log($"Initialize success. index={_mirrorIndex}");
    }

    // =========================
    // Gallop_MirrorReflection__SetupBaseCamera
    // =========================
    public void SetupBaseCamera(Camera baseCamera, Func<float> fovFactorGetter)
    {
        if (baseCamera != null)
        {
            _baseCamera = baseCamera;
            _baseCameraTransform = baseCamera.transform;
        }

        _fovFactorGetter = fovFactorGetter;
        UpdateMirrorParams();
    }

    public void SetBaseCamera(Camera cam)
    {
        _baseCamera = cam;
        _baseCameraTransform = cam != null ? cam.transform : null;
    }

    public void SetFovFactorGetter(Func<float> getter)
    {
        _fovFactorGetter = getter;
    }

    // =========================
    // Gallop_MirrorReflection__InitMaterials
    // =========================
    private void InitMaterials()
    {
        if (_renderer == null)
            return;

        _materials = _renderer.sharedMaterials;
        if (_materials == null || _materials.Length == 0)
            return;

        bool found448 = false;
        bool found447 = false;

        for (int i = 0; i < _materials.Length; i++)
        {
            var mat = _materials[i];
            if (mat == null)
                continue;

            if (!found448 && mat.HasProperty(_vector448ID))
            {
                _defaultVec448 = mat.GetVector(_vector448ID);
                found448 = true;
            }

            if (!found447 && mat.HasProperty(_vector447ID))
            {
                _defaultVec447 = mat.GetVector(_vector447ID);
                found447 = true;
            }

            if (found447 && found448)
                break;
        }

        // 对应反编译最后：
        // +76 = +280
        // +60 = +296
        _currentVec448 = _defaultVec448;
        _currentVec447 = _defaultVec447;

        Log($"InitMaterials done. found447={found447}, found448={found448}");
    }

    // =========================
    // Gallop_MirrorReflection__CreateMirrorCamera
    // =========================
    private void CreateMirrorCamera()
    {
        _cameraReady = false;

        if (_mirrorCamera != null)
        {
            _mirrorCameraTransform = _mirrorCamera.transform;
            _cameraReady = true;
            return;
        }

        if (_selfTransform == null)
            _selfTransform = transform;

        var go = new GameObject("MirrorCamera", typeof(Camera));
        go.hideFlags = HideFlags.DontSave;

        var t = go.transform;
        t.SetParent(_selfTransform, true);
        t.position = _selfTransform.position;
        t.rotation = _selfTransform.rotation;
        t.localScale = Vector3.one;

        _mirrorCamera = go.GetComponent<Camera>();
        if (_mirrorCamera == null)
        {
            Log("CreateMirrorCamera failed: camera missing.");
            return;
        }

        _mirrorCameraTransform = _mirrorCamera.transform;

        // 对应反编译：
        // allowHDR = false
        // allowMSAA = false
        _mirrorCamera.allowHDR = false;
        _mirrorCamera.allowMSAA = false;
        _mirrorCamera.enabled = false;
        _mirrorCamera.depth = 0f;

        // 反编译里更像是把某个默认 mask 拷到 current mask
        if (_defaultCullingMask != -1)
        {
            _currentCullingMask = _defaultCullingMask;
        }
        else if (_baseCamera != null)
        {
            _currentCullingMask = _baseCamera.cullingMask;
        }
        else
        {
            _currentCullingMask = _mirrorCamera.cullingMask;
        }

        _cameraReady = true;
        Log("CreateMirrorCamera done.");
    }

    // =========================
    // Gallop_MirrorReflection__UpdateRenderTexture
    // =========================
    public void UpdateRenderTexture()
    {
        if (_mirrorCamera == null)
            return;

        int width = _matchScreenSize ? Mathf.Max(64, Screen.width) : Mathf.Max(64, _textureWidth);
        int height = _matchScreenSize ? Mathf.Max(64, Screen.height) : Mathf.Max(64, _textureHeight);

        bool needRebuild =
            _renderTexture == null ||
            !_renderTexture.IsCreated() ||
            _renderTexture.width != width ||
            _renderTexture.height != height ||
            _renderTexture.format != _renderTextureFormat;

        if (!needRebuild)
            return;

        if (_renderTexture != null)
        {
            if (_mirrorCamera.targetTexture == _renderTexture)
                _mirrorCamera.targetTexture = null;

            _renderTexture.Release();

#if UNITY_EDITOR
            DestroyImmediate(_renderTexture);
#else
            Destroy(_renderTexture);
#endif
        }

        _renderTexture = new RenderTexture(width, height, _depthBuffer, _renderTextureFormat)
        {
            name = $"MirrorRT_{name}",
            useMipMap = false,
            autoGenerateMips = false
        };

        _renderTexture.Create();
        _mirrorCamera.targetTexture = _renderTexture;

        ApplyTextureToMaterials();

        Log($"UpdateRenderTexture done. {width}x{height}");
    }

    // =========================
    // Gallop_MirrorReflection__UpdateMirrorParams
    // =========================
    public void UpdateMirrorParams()
    {
        if (_baseCamera == null || !_cameraReady || _mirrorCamera == null)
            return;

        // clearFlags = 2
        _mirrorCamera.clearFlags = CameraClearFlags.Color;

        // backgroundColor
        _mirrorCamera.backgroundColor =
            _useCustomBackgroundColor ? _customBackgroundColor : _baseCamera.backgroundColor;

        // fieldOfView
        float fov = _baseCamera.fieldOfView;
        if (_fovFactorGetter != null)
        {
            try
            {
                fov *= _fovFactorGetter();
            }
            catch
            {
                // 保持当前 fov，不让 getter 异常炸掉 live
            }
        }

        _mirrorCamera.fieldOfView = fov;

        // near / far / ortho / orthoSize
        _mirrorCamera.nearClipPlane = _baseCamera.nearClipPlane;
        _mirrorCamera.farClipPlane = _baseCamera.farClipPlane;
        _mirrorCamera.orthographic = _baseCamera.orthographic;
        _mirrorCamera.orthographicSize = _baseCamera.orthographicSize;
        _mirrorCamera.aspect = _baseCamera.aspect;

        // targetTexture / cullingMask
        _mirrorCamera.targetTexture = _renderTexture;
        _mirrorCamera.cullingMask = _currentCullingMask;

        // 反编译里 CreateMirrorCamera 时把 mirror camera 位置/旋转同步到 self transform
        if (_mirrorCameraTransform != null && _selfTransform != null)
        {
            _mirrorCameraTransform.position = _selfTransform.position;
            _mirrorCameraTransform.rotation = _selfTransform.rotation;
        }

        ApplyMaterialParams();
    }

    private void ApplyMaterialParams()
    {
        if (_materials == null)
            return;

        for (int i = 0; i < _materials.Length; i++)
        {
            var mat = _materials[i];
            if (mat == null)
                continue;

            // property 89 -> SetFloat
            if (mat.HasProperty(_float89ID))
                mat.SetFloat(_float89ID, _float89Value);

            // property 74 -> SetColor
            if (mat.HasProperty(_color74ID))
                mat.SetColor(_color74ID, _currentColor);

            // property 447 -> SetVector
            if (mat.HasProperty(_vector447ID))
                mat.SetVector(_vector447ID, _currentVec447);

            // property 448 -> SetVector
            if (mat.HasProperty(_vector448ID))
                mat.SetVector(_vector448ID, _currentVec448);

            if (_renderTexture != null && mat.HasProperty(_reflectionTexID))
                mat.SetTexture(_reflectionTexID, _renderTexture);
        }
    }

    private void ApplyTextureToMaterials()
    {
        if (_materials == null || _renderTexture == null)
            return;

        for (int i = 0; i < _materials.Length; i++)
        {
            var mat = _materials[i];
            if (mat == null)
                continue;

            if (mat.HasProperty(_reflectionTexID))
                mat.SetTexture(_reflectionTexID, _renderTexture);
        }
    }

    // =========================
    // 对应 AddBeginCameraRenderingCallback / AddEndCameraRenderingCallback
    // =========================
    public void AddBeginCameraRenderingCallback(Action<ScriptableRenderContext, Camera> callback)
    {
        _beginCameraRenderingCallbacks += callback;
    }

    public void AddEndCameraRenderingCallback(Action<ScriptableRenderContext, Camera> callback)
    {
        _endCameraRenderingCallbacks += callback;
    }

    public void RemoveBeginCameraRenderingCallback(Action<ScriptableRenderContext, Camera> callback)
    {
        _beginCameraRenderingCallbacks -= callback;
    }

    public void RemoveEndCameraRenderingCallback(Action<ScriptableRenderContext, Camera> callback)
    {
        _endCameraRenderingCallbacks -= callback;
    }

    // 现在你的 Director 会统一调这个
    public void ForceRenderOnce()
    {
        if (!_initialized || !_cameraReady || _mirrorCamera == null)
            return;

        UpdateRenderTexture();
        UpdateMirrorParams();

        // 你前面逆出来的是 SRP callback/custom pass，
        // 这里先用一个简化版，方便你先恢复画面。
        var context = default(ScriptableRenderContext);

        _beginCameraRenderingCallbacks?.Invoke(context, _mirrorCamera);

        try
        {
            _mirrorCamera.Render();
        }
        catch (Exception e)
        {
            Log($"ForceRenderOnce failed: {e.Message}");
        }

        _endCameraRenderingCallbacks?.Invoke(context, _mirrorCamera);
    }

    public void SetCurrentColor(Color color)
    {
        _currentColor = color;
        ApplyMaterialParams();
    }

    public void SetCurrentVectors(Vector4 vec447, Vector4 vec448)
    {
        _currentVec447 = vec447;
        _currentVec448 = vec448;
        ApplyMaterialParams();
    }

    public void ResetVectorsToDefault()
    {
        _currentVec447 = _defaultVec447;
        _currentVec448 = _defaultVec448;
        ApplyMaterialParams();
    }

    public void SetCullingMask(int mask)
    {
        _currentCullingMask = mask;
        if (_mirrorCamera != null)
            _mirrorCamera.cullingMask = mask;
    }

    private void CachePropertyIDs()
    {
        if (_propertyIDsCached)
            return;

        _reflectionTexID = Shader.PropertyToID(_reflectionTexProperty);
        _color74ID = Shader.PropertyToID(_color74Property);
        _vector447ID = Shader.PropertyToID(_vector447Property);
        _vector448ID = Shader.PropertyToID(_vector448Property);
        _float89ID = Shader.PropertyToID(_float89Property);

        _propertyIDsCached = true;
    }

    private void OnDestroy()
    {
        if (_renderTexture != null)
        {
            _renderTexture.Release();
#if UNITY_EDITOR
            DestroyImmediate(_renderTexture);
#else
            Destroy(_renderTexture);
#endif
        }

        if (_mirrorCamera != null)
        {
#if UNITY_EDITOR
            DestroyImmediate(_mirrorCamera.gameObject);
#else
            Destroy(_mirrorCamera.gameObject);
#endif
        }
    }

    private void Log(string msg)
    {
        if (_logDebug)
            Debug.Log($"[MirrorReflection] {name}: {msg}", this);
    }
}