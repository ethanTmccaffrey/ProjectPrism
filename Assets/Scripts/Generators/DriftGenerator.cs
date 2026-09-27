using System.Collections.Generic;
using UnityEngine;

//DriftGenerator//
//Klüver Category 2 (Spirals)//
//Slow drifting spiral motion through space, Triggered by sustain x sparseness (inverse onset density) x quietness//

public class DriftGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;
    [SerializeField] private float energyThreshold = 0.03f;

    [Header("Field")]
    [SerializeField] private float fieldRadius = 30f;
    [SerializeField] private int maxStrokes = 50;
    [SerializeField] private float maxFieldOffset = 30f;
    [SerializeField] private HeadPlacement placement;

    [Header("Stroke Size")]
    [SerializeField] private float minStrokeLength = 10f;
    [SerializeField] private float maxStrokeLength = 20f;
    [SerializeField] private float growthRate = 14f;
    [SerializeField] private float stepDistance = 0.6f;
    [SerializeField] private int maxConcurrent = 2;

    [Header("Wander")]
    [SerializeField] private float wanderScale = 0.12f;
    [SerializeField] private float wanderStrength = 0.25f;

    [Header("Brush")]
    [SerializeField] private int minBristles = 8;
    [SerializeField] private int maxBristles = 14;
    [SerializeField] private float brushWidth = 0.9f;
    [SerializeField] private float bristleWidth = 0.09f;
    [SerializeField, Range(0f, 1f)] private float strokeOpacity = 0.55f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _brushMaterial;

    private bool _active = false;
    private float _debugTimer = 0f;
    private float _elapsed = 0f;
    private float _fieldSeed;
    private int _completedCount = 0;

    private readonly List<DriftStroke> _growing = new List<DriftStroke>();

    public void SetPrism(PRISMGenerator prism)
    {
        _prism = prism;
    }

    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("Drift_Root");
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.Drift);

        BuildMaterial();

        _active = false;
        _elapsed = 0f;
        _completedCount = 0;
        _growing.Clear();
        _fieldSeed = Random.Range(0f, 1000f);

    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightDrift;
        _active = weight >= activationThreshold;

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.Drift) : Prominence.Silent;
        _elapsed += Time.deltaTime;

        if (!_active) return;
        if (profile.RealtimeEnergy < energyThreshold) return;

        int effectiveMax = Mathf.Max(2, Mathf.RoundToInt(maxStrokes * pr.prominence * pr.prominence));

        float grow = growthRate * profile.RealtimeEnergy * Time.deltaTime;
        for (int i = _growing.Count - 1; i >= 0; i--)
        {
            _growing[i].Grow(grow, profile, _prism);
            if (_growing[i].Complete)
            {
                _completedCount++;
                _growing.RemoveAt(i);
            }
        }

        int total = _completedCount + _growing.Count;
        if (total < effectiveMax && _growing.Count < maxConcurrent)
        {
            SeedStroke(profile, pr);
        }
    }

    public void Deactivate()
    {
        _active = false;
    }

    private void SeedStroke(TimbralProfile profile, Prominence pr)
    {
        Vector3 origin;
        if (placement != null && placement.Ready)
        {
            origin = placement.RandomInEllipsoid(Mathf.Lerp(0.5f, 0.9f, pr.prominence));
        }
        else
        {
            float spread = fieldRadius * Mathf.Lerp(0.4f, 1f, pr.prominence);
            Vector3 fieldCentre = transform.position + Random.onUnitSphere * (maxFieldOffset * (1f - pr.centrality));
            origin = fieldCentre + Random.onUnitSphere * (spread * Mathf.Pow(Random.value, 0.5f));
        }

        float sizeScale = Mathf.Lerp(0.6f, 1f, pr.prominence);
        float length = Mathf.Lerp(minStrokeLength, maxStrokeLength, Random.value) * sizeScale;

        int bristles = Random.Range(minBristles, maxBristles + 1);

        Color colour = _prism != null ? _prism.RealtimeColour : Color.cyan;

        var stroke = new DriftStroke();
        stroke.Init(_root, _brushMaterial, origin, length, bristles, sizeScale, _fieldSeed + _completedCount * 13.7f, wanderScale, wanderStrength, stepDistance, brushWidth * sizeScale, bristleWidth * sizeScale, strokeOpacity, colour, placement);

        _growing.Add(stroke);
    }

    private void BuildMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        _brushMaterial = new Material(shader);
    }

    private void OnDestroy()
    {
        if (_brushMaterial != null) Destroy(_brushMaterial);
    }
}
public class DriftStroke
{
    public bool Complete { get; private set; } = false;

    private GameObject _go;
    private LineRenderer[] _bristles;
    private float[] _bristlePhase;
    private float[] _bristleAlpha;
    private float[] _bristleSide;
    private float[] _bristleUp;         

    private readonly List<Vector3> _spine = new List<Vector3>();
    private Vector3 _pos;
    private Vector3 _heading;
    private float _targetLength;
    private float _grown = 0f;
    private float _stepDistance;
    private float _wanderScale;
    private float _wanderStrength;
    private float _brushWidth;
    private float _noiseSeed;
    private float _growthAccumulator = 0f;
    private Color _colour;
    private float _strokeOpacity;
    private HeadPlacement _placement;

    private Vector3 _across;
    private Vector3 _up;

    public void Init(GameObject parent, Material mat, Vector3 origin, float length, int bristleCount, float sizeScale, float noiseSeed, float wanderScale, float wanderStrength, float stepDistance, float brushWidth, float bristleWidth, float strokeOpacity, Color colour,HeadPlacement placement)
    {
        _placement = placement;
        _pos = origin;
        _targetLength = length;
        _stepDistance = stepDistance;
        _wanderScale = wanderScale;
        _wanderStrength = wanderStrength;
        _brushWidth = brushWidth;
        _noiseSeed = noiseSeed;
        _colour = colour;
        _strokeOpacity = strokeOpacity;

        _heading = Random.onUnitSphere;
        RebuildFrame();

        _go = new GameObject("DriftStroke");
        _go.transform.SetParent(parent.transform);

        _bristles = new LineRenderer[bristleCount];
        _bristlePhase = new float[bristleCount];
        _bristleAlpha = new float[bristleCount];
        _bristleSide = new float[bristleCount];
        _bristleUp = new float[bristleCount];

        for (int i = 0; i < bristleCount; i++)
        {
            float t = bristleCount > 1 ? (i / (float)(bristleCount - 1)) : 0.5f;
            _bristleSide[i] = Mathf.Lerp(-1f, 1f, t) * _brushWidth + Random.Range(-0.1f, 0.1f) * _brushWidth;
            _bristleUp[i] = Random.Range(-0.35f, 0.35f) * _brushWidth;
            _bristlePhase[i] = Random.Range(0f, 100f);
            float centreBias = 1f - Mathf.Abs(Mathf.Lerp(-1f, 1f, t));
            _bristleAlpha[i] = Mathf.Clamp01(Mathf.Lerp(0.25f, 1f, centreBias) * Random.Range(0.5f, 1.15f));

            var go = new GameObject("Bristle");
            go.transform.SetParent(_go.transform);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.material = mat;
            lr.numCornerVertices = 2;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.startWidth = bristleWidth;
            lr.endWidth = bristleWidth;
            lr.positionCount = 0;
            _bristles[i] = lr;
        }

        _spine.Add(_pos);
    }

    public void Grow(float amount, TimbralProfile profile, PRISMGenerator prism)
    {
        if (Complete) return;

        _growthAccumulator += amount;
        int steps = Mathf.FloorToInt(_growthAccumulator);
        if (steps <= 0) return;
        _growthAccumulator -= steps;

        for (int s = 0; s < steps && !Complete; s++)
            Step();

        Redraw();
    }

    private void Step()
    {
        float n1 = Mathf.PerlinNoise(_noiseSeed + _pos.x * _wanderScale, _pos.z * _wanderScale) - 0.5f;
        float n2 = Mathf.PerlinNoise(_noiseSeed + 50f + _pos.y * _wanderScale, _pos.x * _wanderScale) - 0.5f;
        float n3 = Mathf.PerlinNoise(_noiseSeed + 99f + _pos.z * _wanderScale, _pos.y * _wanderScale) - 0.5f;

        Vector3 nudge = new Vector3(n1, n2, n3) * 2f * _wanderStrength;
        _heading = (_heading + nudge).normalized;

        if (_placement != null && _placement.Ready)
        {
            float depth = _placement.EllipsoidDepth(_pos);  
            if (depth < 0.15f)
            {
                Vector3 inward = -_placement.EllipsoidNormal(_pos);
                float strength = Mathf.Clamp01(1f - depth / 0.15f) * 0.5f;
                _heading = Vector3.Slerp(_heading, inward, strength).normalized;
            }
        }
        RebuildFrame();

        _pos += _heading * _stepDistance;
        if (_placement != null && _placement.Ready)
            _pos = _placement.ClampToEllipsoid(_pos);
        _spine.Add(_pos);
        _grown += _stepDistance;

        if (_grown >= _targetLength) Complete = true;
    }

    private void RebuildFrame()
    {
        _across = Vector3.Cross(_heading, Vector3.up);
        if (_across.sqrMagnitude < 1e-4f) _across = Vector3.Cross(_heading, Vector3.right);
        _across.Normalize();
        _up = Vector3.Cross(_across, _heading).normalized;
    }

    private void Redraw()
    {
        int n = _spine.Count;
        if (n < 2) return;

        for (int b = 0; b < _bristles.Length; b++)
        {
            var lr = _bristles[b];
            lr.positionCount = n;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);

                float waver = (Mathf.PerlinNoise(_bristlePhase[b] + t * 4f, _bristlePhase[b] * 0.3f) - 0.5f) * _brushWidth * 0.5f;

                Vector3 sideOffset = _across * (_bristleSide[b] + waver);
                Vector3 upOffset = _up * _bristleUp[b];

                lr.SetPosition(i, _spine[i] + sideOffset + upOffset);
            }

            var widthCurve = new AnimationCurve(new Keyframe(0f, 0.15f), new Keyframe(0.5f, 1f),new Keyframe(1f, 0.15f));
            lr.widthCurve = widthCurve;

            Color c = _colour;
            c.a = _strokeOpacity * _bristleAlpha[b];
            lr.startColor = c;
            lr.endColor = c;
        }
    }
}
