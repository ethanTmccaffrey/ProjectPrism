using System.Collections.Generic;
using UnityEngine;

//FiligreeGenerator//
//Klüver Category 3 (Lattices / Honeycombs / Gratings) filigree subcategory//
//Grows recursive branching tracery with the line renderer, Triggered by harmonic complexity scaled by mid-range flatness//

public class FiligreeGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;
    [SerializeField] private float energyThreshold = 0.03f;

    [Header("Panel")]
    [SerializeField] private HeadPlacement placement;
    [SerializeField] private float surfaceOffset = 4f;
    [SerializeField] private float panelRadius = 22f;
    [SerializeField] private int minRootStems = 3;
    [SerializeField] private int maxRootStems = 7;

    [Header("Curl Shape")]
    [SerializeField] private float rootCurlLength = 7f;
    [SerializeField, Range(0.3f, 0.9f)] private float childScale = 0.62f;
    [SerializeField] private float minCurlRate = 25f;
    [SerializeField] private float maxCurlRate = 80f;
    [SerializeField] private float stepDistance = 0.22f;

    [Header("Branching")]
    [SerializeField] private int minDepth = 1;
    [SerializeField] private int maxDepth = 4;
    [SerializeField] private int minChildren = 1;
    [SerializeField] private int maxChildren = 3;
    [SerializeField] private float branchStart = 0.35f;
    [SerializeField] private float branchEnd = 0.9f;
    [SerializeField] private float childAngle = 55f;

    [Header("Growth")]
    [SerializeField] private float growthRate = 60f;
    [SerializeField] private int maxConcurrent = 6;
    [SerializeField] private int maxCurls = 600;

    [Header("Appearance")]
    [SerializeField] private float rootLineWidth = 0.075f;
    [SerializeField, Range(0.4f, 1f)] private float widthFalloff = 0.72f;
    [SerializeField, Range(0f, 1f)] private float opacity = 0.9f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _lineMaterial;

    private bool _active = false;
    private bool _seeded = false;
    private float _debugTimer = 0f;
    private float _growthAccumulator = 0f;
    private int _curlCount = 0;

    private Vector3 _panelCentre;
    private Quaternion _panelRotation;
    private float _sizeScale = 1f;

    private readonly List<Curl> _growing = new List<Curl>();
    private readonly Queue<Curl> _pending = new Queue<Curl>();

    public void SetPrism(PRISMGenerator prism)
    {
        _prism = prism;
    }

    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("Filigree_Root");
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.Filigree);

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        _lineMaterial = new Material(shader);

        _active = false;
        _seeded = false;
        _growthAccumulator = 0f;
        _curlCount = 0;
        _growing.Clear();
        _pending.Clear();

    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightFiligree;
        _active = weight >= activationThreshold;

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.Filigree) : Prominence.Silent;

        if (!_active) return;
        if (profile.RealtimeEnergy < energyThreshold) return;
        if (_curlCount >= maxCurls) return;

        if (!_seeded) SeedPanel(profile, pr);

        while (_growing.Count < maxConcurrent && _pending.Count > 0)
        {
            _growing.Add(_pending.Dequeue());
        }

        if (_growing.Count == 0) return;

        _growthAccumulator += growthRate * profile.RealtimeEnergy * Time.deltaTime;
        int steps = Mathf.FloorToInt(_growthAccumulator);
        if (steps <= 0) return;
        _growthAccumulator -= steps;

        for (int s = 0; s < steps && _growing.Count > 0; s++)
        {
            int idx = s % _growing.Count;
            Curl c = _growing[idx];
            c.Step(profile, this);

            if (c.Complete)
            {
                _growing.RemoveAt(idx);
                if (_growing.Count == 0) break;
            }
        }
    }

    public void Deactivate()
    {
        _active = false;
    }

    private void SeedPanel(TimbralProfile profile, Prominence pr)
    {
        Vector3 surf = Vector3.zero, normal = Vector3.up;
        bool onSkull = placement != null && placement.HasSkullMesh && placement.RandomSkullMeshPoint(out surf, out normal);

        if (onSkull)
        {
            Vector3 outward = (surf - placement.CavityCentre).normalized;
            if (Vector3.Dot(normal, outward) < 0f) normal = -normal;
            if (Vector3.Dot(normal, outward) < 0.3f) normal = outward;

            _panelCentre = surf + normal * surfaceOffset;

            Quaternion tangent = Quaternion.LookRotation(normal, Vector3.up);
            tangent *= Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.forward);
            _panelRotation = tangent;
        }
        else
        {
            Debug.LogWarning("PRISM Filigree: skull mesh unavailable, using fallback placement");
            _panelCentre = transform.position + Random.onUnitSphere * 45f;
            _panelRotation = Random.rotationUniform;
        }

        _sizeScale = Mathf.Lerp(0.5f, 1.2f, pr.prominence);
        _seeded = true;

        int stems = Mathf.RoundToInt(Mathf.Lerp(minRootStems, maxRootStems, pr.prominence));

        for (int i = 0; i < stems; i++)
        {
            float ang = (i / (float)stems) * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
            float r = panelRadius * _sizeScale * Random.Range(0.05f, 0.35f);
            Vector2 origin2D = new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r);

            float heading = ang * Mathf.Rad2Deg + Random.Range(-40f, 40f);

            var curl = new Curl();
            curl.Init(origin2D, heading, rootCurlLength * _sizeScale, 0, Random.value < 0.5f ? 1f : -1f);
            _pending.Enqueue(curl);
        }
    }

    public void SpawnChild(Vector2 origin2D, float headingDeg, float length, int depth, float spin)
    {
        if (_curlCount >= maxCurls) return;
        var curl = new Curl();
        curl.Init(origin2D, headingDeg, length, depth, spin);
        _pending.Enqueue(curl);
    }

    public Vector3 PanelToWorld(Vector2 p)
    {
        return _panelCentre + _panelRotation * new Vector3(p.x, p.y, 0f);
    }

    public int MaxDepthFor(TimbralProfile profile)
    {
        return Mathf.RoundToInt(Mathf.Lerp(minDepth, maxDepth, profile.RealtimeHarmonicComplexity));
    }

    public float CurlRateFor(TimbralProfile profile)
    {
        return Mathf.Lerp(minCurlRate, maxCurlRate, profile.RealtimeHarmonicComplexity);
    }

    public float StepDistance => stepDistance;
    public float ChildScale => childScale;
    public float BranchStart => branchStart;
    public float BranchEnd => branchEnd;
    public float ChildAngle => childAngle;
    public int MinChildren => minChildren;
    public int MaxChildren => maxChildren;
    public float PanelRadius => panelRadius * _sizeScale;

    public LineRenderer CreateLine(int depth)
    {
        var go = new GameObject("Curl");
        go.transform.SetParent(_root.transform);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.material = _lineMaterial;
        lr.numCornerVertices = 3;  
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        float w = rootLineWidth * _sizeScale * Mathf.Pow(widthFalloff, depth);
        lr.startWidth = w;
        lr.endWidth = w * 0.75f;   
        lr.positionCount = 0;

        Color c = _prism != null ? _prism.RealtimeColour : Color.cyan;
        c.a = opacity;
        lr.startColor = c;
        lr.endColor = c;

        _curlCount++;
        return lr;
    }

    private void OnDestroy()
    {
        if (_lineMaterial != null) Destroy(_lineMaterial);
    }
}


public class Curl
{
    public bool Complete { get; private set; } = false;

    private Vector2 _pos;
    private float _heading;       
    private float _spin;          
    private float _targetLength;
    private float _grown = 0f;
    private int _depth;

    private LineRenderer _lr;
    private readonly List<Vector3> _points = new List<Vector3>();
    private readonly List<float> _branchPoints = new List<float>();
    private int _nextBranch = 0;
    private bool _started = false;

    public void Init(Vector2 origin, float headingDeg, float length, int depth, float spin)
    {
        _pos = origin;
        _heading = headingDeg;
        _targetLength = length;
        _depth = depth;
        _spin = spin;
    }

    public void Step(TimbralProfile profile, FiligreeGenerator gen)
    {
        if (Complete) return;

        if (!_started)
        {
            _lr = gen.CreateLine(_depth);
            _points.Add(gen.PanelToWorld(_pos));

            int maxDepth = gen.MaxDepthFor(profile);
            if (_depth < maxDepth)
            {
                int children = Random.Range(gen.MinChildren, gen.MaxChildren + 1);
                for (int i = 0; i < children; i++)
                {
                    _branchPoints.Add(Random.Range(gen.BranchStart, gen.BranchEnd));
                }
                _branchPoints.Sort();
            }
            _started = true;
        }

        float curlRate = gen.CurlRateFor(profile);
        _heading += curlRate * gen.StepDistance * _spin;

        float rad = _heading * Mathf.Deg2Rad;
        _pos += new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * gen.StepDistance;
        _grown += gen.StepDistance;

        _points.Add(gen.PanelToWorld(_pos));
        _lr.positionCount = _points.Count;
        _lr.SetPositions(_points.ToArray());

        if (_pos.magnitude > gen.PanelRadius)
        {
            Complete = true;
            return;
        }

        float t = _grown / _targetLength;
        while (_nextBranch < _branchPoints.Count && t >= _branchPoints[_nextBranch])
        {
            float side = (_nextBranch % 2 == 0) ? 1f : -1f;
            float childHeading = _heading + gen.ChildAngle * side + Random.Range(-12f, 12f);
            float childLength = _targetLength * gen.ChildScale;

            gen.SpawnChild(_pos, childHeading, childLength, _depth + 1, -_spin * side);
            _nextBranch++;
        }

        if (_grown >= _targetLength) Complete = true;
    }
}
