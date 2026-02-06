using System;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class NeuralNexusController : MonoBehaviour
{
    [Serializable]
    private class ModeSettings
    {
        public string label = "MODE";
        [ColorUsage(false, true)] public Color baseColor = Color.cyan;
        [Range(0f, 0.25f)] public float hueSpread = 0.03f;
        [Range(-0.3f, 0.3f)] public float saturationOffset = 0.02f;
        [Range(-0.3f, 0.3f)] public float lightnessOffset = 0.03f;
        [Min(0.01f)] public float baseSize = 1.3f;
        [Min(0f)] public float sizeJitter = 1.6f;
        public Vector2 brightnessRange = new Vector2(0.75f, 1.35f);
        public Color[] palette = Array.Empty<Color>();

        public void EnsurePalette(int width)
        {
            if (palette == null || palette.Length != width)
            {
                palette = new Color[width];
            }
        }

        public void ClampRanges()
        {
            if (brightnessRange.y < brightnessRange.x)
            {
                brightnessRange.y = brightnessRange.x;
            }

            brightnessRange.x = Mathf.Max(0f, brightnessRange.x);
            brightnessRange.y = Mathf.Max(brightnessRange.x + 0.01f, brightnessRange.y);
        }
    }

    private const int PaletteWidth = 10;
    private const int MaxModes = 4;
    private const int ThreadGroupSize = 256;

    [Header("Compute Resources")]
    [SerializeField] private ComputeShader updateShader;
    [SerializeField] private string nodeKernelName = "UpdateNodes";
    [SerializeField] private string trailKernelName = "UpdateTrails";
    [SerializeField] private string backgroundKernelName = "UpdateBackground";

    [Header("Materials")]
    [SerializeField] private Material nodeMaterial;
    [SerializeField] private Material trailMaterial;
    [SerializeField] private Material backgroundMaterial;

    [Header("Counts")]
    [SerializeField, Range(512, 65536)] private int nodeCount = 28000;
    [SerializeField, Range(0, 32768)] private int trailCount = 10000;
    [SerializeField, Range(0, 8192)] private int backgroundCount = 3000;

    [Header("Animation")]
    [SerializeField, Min(0.1f)] private float transitionDuration = 1.3f;
    [SerializeField] private bool autoRotate = true;
    [SerializeField] private float autoRotateSpeed = 8f;
    [SerializeField] private bool dataFlow = true;
    [SerializeField] private bool trailsVisible = true;
    [SerializeField] private bool backgroundVisible = true;

    [Header("Rendering")]
    [SerializeField, Range(0.1f, 12f)] private float nodeSizeScale = 1.5f;
    [SerializeField, Range(0f, 1f)] private float nodeGlobalOpacity = 1f;
    [SerializeField, Range(0.1f, 12f)] private float trailSizeScale = 1.2f;
    [SerializeField, Range(0f, 1f)] private float trailGlobalOpacity = 0.65f;
    [SerializeField, Range(0.1f, 5f)] private float glowEnabledIntensity = 1f;
    [SerializeField, Range(0.1f, 5f)] private float glowDisabledIntensity = 0.5f;
    [SerializeField, Range(50f, 1200f)] private float boundsRadius = 360f;

    [Header("Trail Settings")]
    [SerializeField, Min(0.01f)] private float trailBaseSize = 1.0f;
    [SerializeField, Min(0f)] private float trailSizeJitter = 1.1f;
    [SerializeField] private Vector2 trailOpacityRange = new Vector2(0.25f, 0.7f);

    [Header("Background Settings")]
    [SerializeField] private float backgroundRadiusMin = 220f;
    [SerializeField] private float backgroundRadiusMax = 520f;
    [SerializeField] private float backgroundTwinkleSpeed = 0.75f;
    [SerializeField] private float backgroundTwinkleIntensity = 0.85f;
    [SerializeField] private Color backgroundColorA = new Color(0.04f, 0.5f, 0.65f, 1f);
    [SerializeField] private Color backgroundColorB = new Color(0.02f, 0.2f, 0.45f, 1f);

    [Header("Visualization Modes")]
    [SerializeField] private ModeSettings[] modes = Array.Empty<ModeSettings>();

    private GraphicsBuffer nodePositionSizeBuffer;
    private GraphicsBuffer nodeColorBuffer;
    private GraphicsBuffer trailPositionSizeBuffer;
    private GraphicsBuffer trailColorBuffer;
    private GraphicsBuffer backgroundPositionSizeBuffer;
    private GraphicsBuffer backgroundColorBuffer;

    private MaterialPropertyBlock nodePropertyBlock;
    private MaterialPropertyBlock trailPropertyBlock;
    private MaterialPropertyBlock backgroundPropertyBlock;

    private Vector4[] paletteUpload;
    private Vector4[] modeParamsUpload;

    private bool modeUploadDirty = true;
    private bool buffersDirty = true;
    private bool glowEnabled = true;

    private int nodeKernel = -1;
    private int trailKernel = -1;
    private int backgroundKernel = -1;

    private int activeModeIndex;
    private int fromModeIndex;
    private int toModeIndex;
    private float transitionProgress;
    private bool isTransitioning;

    private static readonly int LocalToWorldId = Shader.PropertyToID("_LocalToWorld");
    private static readonly int NodePositionSizeId = Shader.PropertyToID("_NodePositionSize");
    private static readonly int NodeColorId = Shader.PropertyToID("_NodeColor");
    private static readonly int NodeSizeScaleId = Shader.PropertyToID("_NodeSizeScale");
    private static readonly int NodeOpacityId = Shader.PropertyToID("_NodeOpacity");
    private static readonly int GlowIntensityId = Shader.PropertyToID("_GlowIntensity");
    private static readonly int TrailPositionSizeId = Shader.PropertyToID("_TrailPositionSize");
    private static readonly int TrailColorId = Shader.PropertyToID("_TrailColor");
    private static readonly int TrailSizeScaleId = Shader.PropertyToID("_TrailSizeScale");
    private static readonly int TrailOpacityId = Shader.PropertyToID("_TrailOpacity");
    private static readonly int BackgroundPositionSizeId = Shader.PropertyToID("_BackgroundPositionSize");
    private static readonly int BackgroundColorId = Shader.PropertyToID("_BackgroundColor");
    private static readonly int BackgroundOpacityId = Shader.PropertyToID("_BackgroundOpacity");

    private static readonly int NodeCountId = Shader.PropertyToID("_NodeCount");
    private static readonly int TrailCountId = Shader.PropertyToID("_TrailCount");
    private static readonly int BackgroundCountId = Shader.PropertyToID("_BackgroundCount");
    private static readonly int TimeId = Shader.PropertyToID("_Time");
    private static readonly int DeltaTimeId = Shader.PropertyToID("_DeltaTime");
    private static readonly int ModeFromId = Shader.PropertyToID("_ModeFrom");
    private static readonly int ModeToId = Shader.PropertyToID("_ModeTo");
    private static readonly int TransitionId = Shader.PropertyToID("_Transition");
    private static readonly int FlowToggleId = Shader.PropertyToID("_FlowToggle");
    private static readonly int TrailVisibilityId = Shader.PropertyToID("_TrailVisibility");
    private static readonly int ModeCountId = Shader.PropertyToID("_ModeCount");
    private static readonly int PaletteSliceId = Shader.PropertyToID("_PaletteSlice");
    private static readonly int ModeParamsId = Shader.PropertyToID("_ModeParams");
    private static readonly int PaletteColorsId = Shader.PropertyToID("_PaletteColors");
    private static readonly int TrailParamsId = Shader.PropertyToID("_TrailParams");
    private static readonly int BackgroundParamsId = Shader.PropertyToID("_BackgroundParams");
    private static readonly int BackgroundColorAId = Shader.PropertyToID("_BackgroundColorA");
    private static readonly int BackgroundColorBId = Shader.PropertyToID("_BackgroundColorB");

    private void Reset()
    {
        nodeCount = 28000;
        trailCount = 10000;
        backgroundCount = 3000;
        transitionDuration = 1.3f;
        autoRotate = true;
        autoRotateSpeed = 8f;
        dataFlow = true;
        trailsVisible = true;
        backgroundVisible = true;
        nodeSizeScale = 1.5f;
        nodeGlobalOpacity = 1f;
        trailSizeScale = 1.2f;
        trailGlobalOpacity = 0.65f;
        glowEnabledIntensity = 1f;
        glowDisabledIntensity = 0.5f;
        boundsRadius = 360f;
        trailBaseSize = 1.0f;
        trailSizeJitter = 1.1f;
        trailOpacityRange = new Vector2(0.25f, 0.7f);
        backgroundRadiusMin = 220f;
        backgroundRadiusMax = 520f;
        backgroundTwinkleSpeed = 0.75f;
        backgroundTwinkleIntensity = 0.85f;
        backgroundColorA = new Color32(0x00, 0xff, 0xee, 0xff);
        backgroundColorB = new Color32(0x00, 0x55, 0xff, 0xff);

        CreateDefaultModes();
        modeUploadDirty = true;
        buffersDirty = true;
        glowEnabled = true;
    }

    private void OnEnable()
    {
        EnsureModeDefaults();
        AcquireKernels();
        RenderPipelineManager.beginCameraRendering += HandleCameraRendering;
        buffersDirty = true;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= HandleCameraRendering;
        ReleaseBuffers();
    }

    private void OnValidate()
    {
        if (trailOpacityRange.y < trailOpacityRange.x)
        {
            trailOpacityRange.y = trailOpacityRange.x;
        }

        if (backgroundRadiusMax < backgroundRadiusMin)
        {
            backgroundRadiusMax = backgroundRadiusMin;
        }

        boundsRadius = Mathf.Max(10f, boundsRadius);
        trailBaseSize = Mathf.Max(0.01f, trailBaseSize);
        trailSizeJitter = Mathf.Max(0f, trailSizeJitter);
        transitionDuration = Mathf.Max(0.1f, transitionDuration);
        glowEnabledIntensity = Mathf.Max(0.05f, glowEnabledIntensity);
        glowDisabledIntensity = Mathf.Clamp(glowDisabledIntensity, 0.05f, glowEnabledIntensity);

        EnsureModeDefaults();
        modeUploadDirty = true;
        buffersDirty = true;
    }

    private void Update()
    {
        if (!updateShader || modes == null || modes.Length == 0)
        {
            return;
        }

        if (buffersDirty)
        {
            EnsureBuffers();
            buffersDirty = false;
        }

        float deltaTime = Application.isPlaying ? Time.deltaTime : 0f;
        float time = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;

        if (autoRotate && Application.isPlaying)
        {
            transform.Rotate(Vector3.up, autoRotateSpeed * deltaTime, Space.World);
        }

        AdvanceTransition(deltaTime);
        UploadModeData();
        DispatchCompute(time, Mathf.Max(deltaTime, 0.0001f));
    }

    private void EnsureModeDefaults()
    {
        if (modes == null || modes.Length == 0)
        {
            CreateDefaultModes();
        }

        int count = Mathf.Clamp(modes.Length, 1, MaxModes);
        for (int i = 0; i < count; i++)
        {
            modes[i] ??= new ModeSettings();
            modes[i].EnsurePalette(PaletteWidth);
            modes[i].ClampRanges();
            if (string.IsNullOrWhiteSpace(modes[i].label))
            {
                modes[i].label = $"MODE {i + 1}";
            }
            GeneratePalette(modes[i]);
        }

        modeUploadDirty = true;
    }

    private void CreateDefaultModes()
    {
        modes = new ModeSettings[3];

        modes[0] = new ModeSettings
        {
            label = "HARMONIC SPHERE",
            baseColor = new Color32(0x00, 0xff, 0xff, 0xff),
            hueSpread = 0.028f,
            saturationOffset = 0.015f,
            lightnessOffset = 0.02f,
            baseSize = 1.25f,
            sizeJitter = 1.5f,
            brightnessRange = new Vector2(0.8f, 1.4f)
        };

        modes[1] = new ModeSettings
        {
            label = "TOROIDAL VORTEX",
            baseColor = new Color32(0x00, 0x88, 0xff, 0xff),
            hueSpread = 0.032f,
            saturationOffset = 0.02f,
            lightnessOffset = 0.03f,
            baseSize = 1.3f,
            sizeJitter = 1.6f,
            brightnessRange = new Vector2(0.85f, 1.35f)
        };

        modes[2] = new ModeSettings
        {
            label = "NEBULA CORE",
            baseColor = new Color32(0xff, 0x55, 0xff, 0xff),
            hueSpread = 0.04f,
            saturationOffset = 0.03f,
            lightnessOffset = 0.04f,
            baseSize = 1.45f,
            sizeJitter = 1.8f,
            brightnessRange = new Vector2(0.75f, 1.45f)
        };
    }

    private void AcquireKernels()
    {
        nodeKernel = updateShader && !string.IsNullOrWhiteSpace(nodeKernelName) ? updateShader.FindKernel(nodeKernelName) : -1;
        trailKernel = updateShader && !string.IsNullOrWhiteSpace(trailKernelName) ? updateShader.FindKernel(trailKernelName) : -1;
        backgroundKernel = updateShader && !string.IsNullOrWhiteSpace(backgroundKernelName) ? updateShader.FindKernel(backgroundKernelName) : -1;
    }

    private void EnsureBuffers()
    {
        ReleaseIfCountMismatch(ref nodePositionSizeBuffer, nodeCount);
        ReleaseIfCountMismatch(ref nodeColorBuffer, nodeCount);
        ReleaseIfCountMismatch(ref trailPositionSizeBuffer, trailCount);
        ReleaseIfCountMismatch(ref trailColorBuffer, trailCount);
        ReleaseIfCountMismatch(ref backgroundPositionSizeBuffer, backgroundCount);
        ReleaseIfCountMismatch(ref backgroundColorBuffer, backgroundCount);

        AllocateIfNeeded(ref nodePositionSizeBuffer, nodeCount, sizeof(float) * 4);
        AllocateIfNeeded(ref nodeColorBuffer, nodeCount, sizeof(float) * 4);
        if (trailCount > 0)
        {
            AllocateIfNeeded(ref trailPositionSizeBuffer, trailCount, sizeof(float) * 4);
            AllocateIfNeeded(ref trailColorBuffer, trailCount, sizeof(float) * 4);
        }

        if (backgroundCount > 0)
        {
            AllocateIfNeeded(ref backgroundPositionSizeBuffer, backgroundCount, sizeof(float) * 4);
            AllocateIfNeeded(ref backgroundColorBuffer, backgroundCount, sizeof(float) * 4);
        }

        BindBuffersToKernels();
    }

    private static void ReleaseIfCountMismatch(ref GraphicsBuffer buffer, int desiredCount)
    {
        if (buffer == null)
        {
            return;
        }

        if (desiredCount <= 0 || buffer.count != desiredCount)
        {
            buffer.Release();
            buffer = null;
        }
    }

    private static void AllocateIfNeeded(ref GraphicsBuffer buffer, int count, int stride)
    {
        if (count <= 0)
        {
            return;
        }

        buffer ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, stride);
    }

    private void BindBuffersToKernels()
    {
        if (!updateShader)
        {
            return;
        }

        if (nodeKernel >= 0 && nodePositionSizeBuffer != null && nodeColorBuffer != null)
        {
            updateShader.SetBuffer(nodeKernel, NodePositionSizeId, nodePositionSizeBuffer);
            updateShader.SetBuffer(nodeKernel, NodeColorId, nodeColorBuffer);
        }

        if (trailKernel >= 0 && trailPositionSizeBuffer != null && trailColorBuffer != null)
        {
            updateShader.SetBuffer(trailKernel, TrailPositionSizeId, trailPositionSizeBuffer);
            updateShader.SetBuffer(trailKernel, TrailColorId, trailColorBuffer);
        }

        if (backgroundKernel >= 0 && backgroundPositionSizeBuffer != null && backgroundColorBuffer != null)
        {
            updateShader.SetBuffer(backgroundKernel, BackgroundPositionSizeId, backgroundPositionSizeBuffer);
            updateShader.SetBuffer(backgroundKernel, BackgroundColorId, backgroundColorBuffer);
        }
    }

    private void ReleaseBuffers()
    {
        ReleaseBuffer(ref nodePositionSizeBuffer);
        ReleaseBuffer(ref nodeColorBuffer);
        ReleaseBuffer(ref trailPositionSizeBuffer);
        ReleaseBuffer(ref trailColorBuffer);
        ReleaseBuffer(ref backgroundPositionSizeBuffer);
        ReleaseBuffer(ref backgroundColorBuffer);
    }

    private static void ReleaseBuffer(ref GraphicsBuffer buffer)
    {
        if (buffer != null)
        {
            buffer.Release();
            buffer = null;
        }
    }

    private void AdvanceTransition(float deltaTime)
    {
        if (!isTransitioning)
        {
            return;
        }

        if (transitionDuration <= 0.001f)
        {
            transitionProgress = 1f;
        }
        else
        {
            float step = deltaTime / Mathf.Max(transitionDuration, 0.001f);
            transitionProgress = Mathf.Clamp01(transitionProgress + step);
        }

        if (transitionProgress >= 0.999f)
        {
            activeModeIndex = toModeIndex;
            fromModeIndex = activeModeIndex;
            transitionProgress = 0f;
            isTransitioning = false;
        }
    }

    private void UploadModeData()
    {
        if (!modeUploadDirty || !updateShader)
        {
            return;
        }

        int modeCount = Mathf.Clamp(modes.Length, 1, MaxModes);
        paletteUpload ??= Array.Empty<Vector4>();
        modeParamsUpload ??= Array.Empty<Vector4>();

        if (paletteUpload.Length != modeCount * PaletteWidth)
        {
            paletteUpload = new Vector4[modeCount * PaletteWidth];
        }

        if (modeParamsUpload.Length != modeCount)
        {
            modeParamsUpload = new Vector4[modeCount];
        }

        for (int m = 0; m < modeCount; m++)
        {
            ModeSettings settings = modes[m];
            settings.EnsurePalette(PaletteWidth);
            settings.ClampRanges();
            GeneratePalette(settings);

            Vector2 brightness = settings.brightnessRange;
            for (int i = 0; i < PaletteWidth; i++)
            {
                Color color = settings.palette[i].linear;
                paletteUpload[m * PaletteWidth + i] = new Vector4(color.r, color.g, color.b, 1f);
            }

            modeParamsUpload[m] = new Vector4(settings.baseSize, settings.sizeJitter, brightness.x, brightness.y);
        }

        updateShader.SetInt(ModeCountId, modeCount);
        updateShader.SetInt(PaletteSliceId, PaletteWidth);
        updateShader.SetVectorArray(ModeParamsId, modeParamsUpload);
        updateShader.SetVectorArray(PaletteColorsId, paletteUpload);

        modeUploadDirty = false;
    }

    private void DispatchCompute(float time, float deltaTime)
    {
        int modeCount = Mathf.Clamp(modes.Length, 1, MaxModes);
        int active = Mathf.Clamp(activeModeIndex, 0, modeCount - 1);
        int from = isTransitioning ? Mathf.Clamp(fromModeIndex, 0, modeCount - 1) : active;
        int to = isTransitioning ? Mathf.Clamp(toModeIndex, 0, modeCount - 1) : active;
        float blend = isTransitioning ? transitionProgress : 0f;

        updateShader.SetInt(NodeCountId, nodeCount);
        updateShader.SetInt(TrailCountId, trailCount);
        updateShader.SetInt(BackgroundCountId, backgroundCount);
        updateShader.SetFloat(TimeId, time);
        updateShader.SetFloat(DeltaTimeId, deltaTime);
        updateShader.SetInt(ModeFromId, from);
        updateShader.SetInt(ModeToId, to);
        updateShader.SetFloat(TransitionId, blend);
        updateShader.SetFloat(FlowToggleId, dataFlow ? 1f : 0f);
        updateShader.SetFloat(TrailVisibilityId, trailsVisible ? 1f : 0f);
        updateShader.SetVector(TrailParamsId, new Vector4(trailBaseSize, trailSizeJitter, trailOpacityRange.x, trailOpacityRange.y));
        updateShader.SetVector(BackgroundParamsId, new Vector4(backgroundRadiusMin, backgroundRadiusMax, backgroundTwinkleSpeed, backgroundTwinkleIntensity));
        updateShader.SetVector(BackgroundColorAId, backgroundColorA.linear);
        updateShader.SetVector(BackgroundColorBId, backgroundColorB.linear);

        if (nodeKernel >= 0 && nodeCount > 0 && nodePositionSizeBuffer != null && nodeColorBuffer != null)
        {
            updateShader.SetBuffer(nodeKernel, NodePositionSizeId, nodePositionSizeBuffer);
            updateShader.SetBuffer(nodeKernel, NodeColorId, nodeColorBuffer);
            int groups = Mathf.CeilToInt(nodeCount / (float)ThreadGroupSize);
            updateShader.Dispatch(nodeKernel, groups, 1, 1);
        }

        if (trailKernel >= 0 && trailCount > 0 && trailPositionSizeBuffer != null && trailColorBuffer != null)
        {
            updateShader.SetBuffer(trailKernel, TrailPositionSizeId, trailPositionSizeBuffer);
            updateShader.SetBuffer(trailKernel, TrailColorId, trailColorBuffer);
            int groups = Mathf.CeilToInt(trailCount / (float)ThreadGroupSize);
            updateShader.Dispatch(trailKernel, groups, 1, 1);
        }

        if (backgroundVisible && backgroundKernel >= 0 && backgroundCount > 0 && backgroundPositionSizeBuffer != null && backgroundColorBuffer != null)
        {
            updateShader.SetBuffer(backgroundKernel, BackgroundPositionSizeId, backgroundPositionSizeBuffer);
            updateShader.SetBuffer(backgroundKernel, BackgroundColorId, backgroundColorBuffer);
            int groups = Mathf.CeilToInt(backgroundCount / (float)ThreadGroupSize);
            updateShader.Dispatch(backgroundKernel, groups, 1, 1);
        }
    }

    private void HandleCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (!enabled || !gameObject.activeInHierarchy || camera.cameraType == CameraType.Preview)
        {
            return;
        }

        float radius = Mathf.Max(boundsRadius, 10f);
        Bounds bounds = new Bounds(transform.position, Vector3.one * radius * 2f);
        Matrix4x4 localToWorld = transform.localToWorldMatrix;

        if (nodeMaterial && nodePositionSizeBuffer != null && nodeColorBuffer != null)
        {
            nodePropertyBlock ??= new MaterialPropertyBlock();
            nodePropertyBlock.SetMatrix(LocalToWorldId, localToWorld);
            nodePropertyBlock.SetFloat(NodeSizeScaleId, nodeSizeScale);
            nodePropertyBlock.SetFloat(NodeOpacityId, nodeGlobalOpacity);
            nodePropertyBlock.SetFloat(GlowIntensityId, glowEnabled ? glowEnabledIntensity : glowDisabledIntensity);

            nodeMaterial.SetBuffer(NodePositionSizeId, nodePositionSizeBuffer);
            nodeMaterial.SetBuffer(NodeColorId, nodeColorBuffer);

            Graphics.DrawProcedural(nodeMaterial, bounds, MeshTopology.Points, nodeCount, 1, camera, nodePropertyBlock, ShadowCastingMode.Off, false, gameObject.layer);
        }

        if (trailsVisible && trailMaterial && trailPositionSizeBuffer != null && trailColorBuffer != null && trailCount > 0)
        {
            trailPropertyBlock ??= new MaterialPropertyBlock();
            trailPropertyBlock.SetMatrix(LocalToWorldId, localToWorld);
            trailPropertyBlock.SetFloat(TrailSizeScaleId, trailSizeScale);
            trailPropertyBlock.SetFloat(TrailOpacityId, trailGlobalOpacity);

            trailMaterial.SetBuffer(TrailPositionSizeId, trailPositionSizeBuffer);
            trailMaterial.SetBuffer(TrailColorId, trailColorBuffer);

            Graphics.DrawProcedural(trailMaterial, bounds, MeshTopology.Points, trailCount, 1, camera, trailPropertyBlock, ShadowCastingMode.Off, false, gameObject.layer);
        }

        if (backgroundVisible && backgroundMaterial && backgroundPositionSizeBuffer != null && backgroundColorBuffer != null && backgroundCount > 0)
        {
            backgroundPropertyBlock ??= new MaterialPropertyBlock();
            backgroundPropertyBlock.SetMatrix(LocalToWorldId, localToWorld);
            backgroundPropertyBlock.SetFloat(BackgroundOpacityId, 1f);

            backgroundMaterial.SetBuffer(BackgroundPositionSizeId, backgroundPositionSizeBuffer);
            backgroundMaterial.SetBuffer(BackgroundColorId, backgroundColorBuffer);

            Graphics.DrawProcedural(backgroundMaterial, bounds, MeshTopology.Points, backgroundCount, 1, camera, backgroundPropertyBlock, ShadowCastingMode.Off, false, gameObject.layer);
        }
    }

    public void CycleMode()
    {
        if (modes == null || modes.Length == 0)
        {
            return;
        }

        int next = (activeModeIndex + 1) % modes.Length;
        SetMode(next);
    }

    public void SetMode(int modeIndex)
    {
        if (modes == null || modes.Length == 0)
        {
            return;
        }

        modeIndex = Mathf.Clamp(modeIndex, 0, modes.Length - 1);
        if (modeIndex == activeModeIndex)
        {
            return;
        }

        fromModeIndex = activeModeIndex;
        toModeIndex = modeIndex;
        transitionProgress = 0f;
        isTransitioning = true;
    }

    public string GetActiveModeLabel()
    {
        if (modes == null || modes.Length == 0)
        {
            return string.Empty;
        }

        return modes[activeModeIndex].label;
    }

    public bool AutoRotate
    {
        get => autoRotate;
        set => autoRotate = value;
    }

    public bool DataFlow
    {
        get => dataFlow;
        set => dataFlow = value;
    }

    public bool TrailsVisible
    {
        get => trailsVisible;
        set => trailsVisible = value;
    }

    public bool BackgroundVisible
    {
        get => backgroundVisible;
        set => backgroundVisible = value;
    }

    public bool GlowEnabled
    {
        get => glowEnabled;
        set => glowEnabled = value;
    }

    public void SetAutoRotate(bool enabled)
    {
        autoRotate = enabled;
    }

    public void SetDataFlow(bool enabled)
    {
        dataFlow = enabled;
    }

    public void SetTrailsVisible(bool visible)
    {
        trailsVisible = visible;
    }

    public void SetBackgroundVisible(bool visible)
    {
        backgroundVisible = visible;
    }

    public void SetGlowEnabled(bool enabled)
    {
        glowEnabled = enabled;
    }

    public void SetGlowLevels(float enabledIntensity, float disabledIntensity)
    {
        glowEnabledIntensity = Mathf.Max(0.05f, enabledIntensity);
        glowDisabledIntensity = Mathf.Clamp(disabledIntensity, 0.05f, glowEnabledIntensity);
    }

    public void SetModeBaseColor(int modeIndex, Color color)
    {
        if (modes == null || modes.Length == 0)
        {
            return;
        }

        modeIndex = Mathf.Clamp(modeIndex, 0, modes.Length - 1);
        modes[modeIndex].baseColor = color;
        GeneratePalette(modes[modeIndex]);
        modeUploadDirty = true;
    }

    private void GeneratePalette(ModeSettings settings)
    {
        settings.EnsurePalette(PaletteWidth);
        Color baseColor = settings.baseColor;
        Color.RGBToHSV(baseColor, out float baseH, out float baseS, out float baseV);

        float baseL = Mathf.Clamp01(ColorToLightness(baseColor));
        float baseSat = Mathf.Clamp01(baseS);

        float centre = (PaletteWidth - 1) * 0.5f;
        for (int i = 0; i < PaletteWidth; i++)
        {
            float offset = (i - centre);
            float h = Mathf.Repeat(baseH + offset * settings.hueSpread, 1f);
            float s = Mathf.Clamp01(baseSat + offset * settings.saturationOffset);
            float l = Mathf.Clamp01(baseL + offset * settings.lightnessOffset);
            Color color = ColorFromHSL(h, s, l);
            settings.palette[i] = color;
        }
    }

    private static float ColorToLightness(Color color)
    {
        float max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        float min = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
        return (max + min) * 0.5f;
    }

    private static Color ColorFromHSL(float h, float s, float l)
    {
        float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
        float p = 2f * l - q;
        float r = HueToRGB(p, q, h + 1f / 3f);
        float g = HueToRGB(p, q, h);
        float b = HueToRGB(p, q, h - 1f / 3f);
        return new Color(r, g, b, 1f);
    }

    private static float HueToRGB(float p, float q, float t)
    {
        t = Mathf.Repeat(t, 1f);
        if (t < 1f / 6f)
        {
            return p + (q - p) * 6f * t;
        }
        if (t < 1f / 2f)
        {
            return q;
        }
        if (t < 2f / 3f)
        {
            return p + (q - p) * (2f / 3f - t) * 6f;
        }
        return p;
    }
}
