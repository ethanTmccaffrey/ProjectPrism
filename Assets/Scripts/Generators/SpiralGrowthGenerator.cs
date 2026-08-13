using System.Collections.Generic;
using UnityEngine;

public class SpiralGrowthGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;
    [SerializeField] private float energyThreshold = 0.02f;

    [Header("Spiral Shape")]
    [SerializeField] private float radialSpeed = 0.6f;
    [SerializeField] private float baseAngleStep = 6f;
    [SerializeField] private float tightAngleStep = 18f; 
    [SerializeField] private float growthRate = 30f;
    [SerializeField] private int maxPoints = 3000;

    [Header("Placement")]
    [SerializeField] private float maxCentreOffset = 40f;
    [SerializeField] private float scale = 1f;
    [SerializeField] private HeadPlacement placement;
    [SerializeField] private float wallMargin = 6f;
    [SerializeField] private float seedMinDepth = 0.9f;

    [Header("Appearance")]
    [SerializeField] private float lineWidth = 0.15f;
    [SerializeField] private float tipWidthMultiplier = 2.2f;

    [Header("Animation")]
    [SerializeField] private float waveAmplitude = 2.5f;
    [SerializeField] private float waveFrequency = 0.5f;
    [SerializeField] private float waveSpeed = 2f;
    [SerializeField] private float rotationSpeed = 8f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _lineMaterial;

    private bool _active = false;
    private bool _seeded = false;
    private bool _growthStopped = false;
    private float _growthAccumulator = 0f;

    private Vector3 _centre;
    private Quaternion _planeRotation; 
    private float _angle = 0f;
    private int _pointCount = 0;

    private struct SpiralPoint { public float angle, radius; }
    private readonly List<SpiralPoint> _points = new List<SpiralPoint>();

    private LineRenderer _line;
    private float _wavePhase = 0f;
    private float _spinAngle = 0f;
    private Vector3[] _worldScratch;

    public void SetPrism(PRISMGenerator prism)
    {
        _prism = prism;
    }

    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("Spiral_Root");
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.SpiralGrowth);

        BuildMaterial();

        _active = false;
        _seeded = false;
        _growthStopped = false;
        _growthAccumulator = 0f;
        _angle = 0f;
        _pointCount = 0;
        _points.Clear();
        _wavePhase = 0f;
        _spinAngle = 0f;

        Debug.Log("PRISM SpiralGrowthGenerator: Initialised");
    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightSpiralGrowth;
        _active = weight >= activationThreshold;

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.SpiralGrowth) : Prominence.Silent;

        if (_seeded)
        {
            _wavePhase += waveSpeed * Time.deltaTime;
            _spinAngle += rotationSpeed * Time.deltaTime;
            RebuildLine(profile);
        }

        if (!_active) return;
        if (profile.RealtimeEnergy < energyThreshold) return;
        if (_pointCount >= maxPoints || _growthStopped) return;

        if (!_seeded) SeedSpiral(pr);

        _growthAccumulator += growthRate * profile.RealtimeEnergy * Time.deltaTime;
        int ticks = Mathf.FloorToInt(_growthAccumulator);
        if (ticks <= 0) return;
        _growthAccumulator -= ticks;

        for (int i = 0; i < ticks && _pointCount < maxPoints && !_growthStopped; i++)
        {
            GrowPoint(profile, pr);
        }
    }

    public void Deactivate()
    {
        _active = false;
    }

    private void SeedSpiral(Prominence pr)
    {
        if (placement != null && placement.Ready)
        {
            _centre = placement.RandomInEllipsoid(Mathf.Lerp(0.3f, 0.7f, pr.prominence));
        }
        else
        {
            _centre = transform.position + Random.onUnitSphere * (maxCentreOffset * (1f - pr.centrality));
        }

        _planeRotation = Random.rotationUniform;

        GameObject lineGO = new GameObject("SpiralArm");
        lineGO.transform.SetParent(_root.transform);
        _line = lineGO.AddComponent<LineRenderer>();
        _line.useWorldSpace = true;
        _line.material = _lineMaterial;
        _line.numCapVertices = 2;
        _line.numCornerVertices = 2;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;

        float w = lineWidth * Mathf.Lerp(0.4f, 1.4f, pr.prominence);
        var widthCurve = new AnimationCurve( new Keyframe(0f, w), new Keyframe(1f, w * tipWidthMultiplier));
        _line.widthCurve = widthCurve;
        _line.widthMultiplier = 1f;

        Color seedCol = _prism != null ? _prism.RealtimeColour : Color.cyan;
        _line.startColor = seedCol;
        _line.endColor = seedCol;

        _angle = 0f;
        _seeded = true;

        if (placement != null && placement.Ready) placement.ClaimPoint(_centre, 30f * Mathf.Lerp(0.5f, 1.4f, pr.prominence));

    }

    //Growth//
    private void GrowPoint(TimbralProfile profile, Prominence pr)
    {
        float angleStepDeg = Mathf.Lerp(baseAngleStep, tightAngleStep, profile.RealtimeHarmonicComplexity);
        _angle += angleStepDeg * Mathf.Deg2Rad;

        float sizeScale = scale * Mathf.Lerp(0.5f, 1.4f, pr.prominence);
        float radius = radialSpeed * _angle * sizeScale;

        Vector3 flatWorld = _centre + _planeRotation * new Vector3(Mathf.Cos(_angle) * radius, Mathf.Sin(_angle) * radius, 0f);
        if (placement != null && placement.Ready)
        {
            Vector3 radialDir = (flatWorld - _centre).normalized;
            Vector3 probe = flatWorld + radialDir * wallMargin;
            if (!placement.InEllipsoid(probe))
            {
                _growthStopped = true;
                return;
            }
        }

        _points.Add(new SpiralPoint { angle = _angle, radius = radius });
        _pointCount++;

        if (_prism != null) _line.endColor = _prism.RealtimeColour;
    }

    private void RebuildLine(TimbralProfile profile)
    {
        int n = _points.Count;
        if (n == 0 || _line == null) return;

        if (_worldScratch == null || _worldScratch.Length != n) _worldScratch = new Vector3[n];

        float energy = profile != null ? profile.RealtimeEnergy : 0.5f;
        float amp = waveAmplitude * Mathf.Clamp01(energy);

        Quaternion spin = Quaternion.AngleAxis(_spinAngle, Vector3.forward);

        for (int i = 0; i < n; i++)
        {
            float a = _points[i].angle;
            float r = _points[i].radius;

            float z = Mathf.Sin(a * waveFrequency + _wavePhase) * amp;

            Vector3 local = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z);
            _worldScratch[i] = _centre + _planeRotation * (spin * local);
        }

        _line.positionCount = n;
        _line.SetPositions(_worldScratch);
    }

    private void BuildMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        _lineMaterial = new Material(shader);
    }

    private void OnDestroy()
    {
        if (_lineMaterial != null) Destroy(_lineMaterial);
    }
}
