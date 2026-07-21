using System.Collections.Generic;
using UnityEngine;

//FiligreeGenerator - Kluver Category 3 (Lattices, Honeycombs, Gratings), filigree subcategory//

//Grows delicate ornamental TRACERY: curling stems that spiral outward and split into
//smaller curls, which split again, each generation finer than the last - the recursive
//scrollwork of real filigree metalwork and lace.
//
//The third expression of Category 3, and deliberately at a different SCALE OF DETAIL from
//the other two rather than a different shape:
//  Honeycomb   - organic tessellation, hexagon cells sharing edges, growing as a sheet//
//  GridGrating - architectural, hard rectangular frames stepping outward on the beat//
//  Filigree    - fine ornamental tracery, curling and subdividing//
//The first two are ARCHITECTURE, built from chunky repeating units. This is JEWELLERY.
//Same family - repeating patterned structure - at a completely different register.
//
//Timbral home (weight driven mainly by harmonic complexity): intricate harmony produces
//intricate ornament. Rich, layered, harmonically complex music grows elaborate tracery
//many generations deep; simple music grows sparse plain curls that barely branch. The
//mapping is direct and legible: complexity of sound becomes complexity of ornament.
//
//Acoustic -> visual:
//  Branching depth: harmonic complexity - the single most important mapping here//
//  Curl tightness: harmonic complexity too, so complex passages curl more sharply//
//  Growth: continuous, paced by energy - the tracery unfurls as the music plays//
//  Panel: a flat tilted plane, like real filigree is a flat ornamental plate//
//  Colour: RealtimeColour at each curl's birth//
//  Prominence: panel position, overall scale, and how many stems seed//

public class FiligreeGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;
    [SerializeField] private float energyThreshold = 0.03f;

    [Header("Panel")]
    //Filigree is a flat ornamental plate, tilted in 3D space - not a volume of scattered
    //marks. The plane is locked at birth (persistent canvas).
    [SerializeField] private float maxPanelOffset = 30f;
    [SerializeField] private float panelRadius = 22f;
    //How many root stems seed the ornament.//
    [SerializeField] private int minRootStems = 3;
    [SerializeField] private int maxRootStems = 7;

    [Header("Curl Shape")]
    //Length of a root curl. Children are shorter by childScale each generation.//
    [SerializeField] private float rootCurlLength = 7f;
    //How much each generation shrinks.//
    [SerializeField, Range(0.3f, 0.9f)] private float childScale = 0.62f;
    //Degrees of turn per unit length: how tightly a curl spirals.//
    [SerializeField] private float minCurlRate = 25f;
    [SerializeField] private float maxCurlRate = 80f;
    //World distance between points along a curl.//
    [SerializeField] private float stepDistance = 0.22f;

    [Header("Branching")]
    //Recursion depth range. Harmonic complexity interpolates between these: simple
    //harmony gives shallow plain curls, complex harmony elaborate multi-generation scroll.
    [SerializeField] private int minDepth = 1;
    [SerializeField] private int maxDepth = 4;
    //Children spawned per curl.//
    [SerializeField] private int minChildren = 1;
    [SerializeField] private int maxChildren = 3;
    //Where along a parent curl children branch (0-1 range along its length).//
    [SerializeField] private float branchStart = 0.35f;
    [SerializeField] private float branchEnd = 0.9f;
    //Angle a child departs from its parent.//
    [SerializeField] private float childAngle = 55f;

    [Header("Growth")]
    //Points appended per second at full energy, across all growing curls.//
    [SerializeField] private float growthRate = 60f;
    //How many curls may extend at once - keeps the unfurling legible.//
    [SerializeField] private int maxConcurrent = 6;
    //Hard safety ceiling on total curls.//
    [SerializeField] private int maxCurls = 600;

    [Header("Appearance")]
    [SerializeField] private float rootLineWidth = 0.075f;
    //Each generation thins, so fine detail reads as fine.//
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

    //Panel frame, locked at birth.//
    private Vector3 _panelCentre;
    private Quaternion _panelRotation;
    private float _sizeScale = 1f;

    //Curls currently extending. Completed curls persist in the scene but leave this list.//
    private readonly List<Curl> _growing = new List<Curl>();
    //Curls waiting to start, so children unfurl after their parent rather than at once.//
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

        Debug.Log("PRISM FiligreeGenerator: Initialised");

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

        //Promote pending curls into the growing set as slots free up, so the ornament
        //unfurls generation by generation rather than appearing fully formed.
        while (_growing.Count < maxConcurrent && _pending.Count > 0)
            _growing.Add(_pending.Dequeue());

        if (_growing.Count == 0) return;

        //Growth budget for this frame, shared across all extending curls.//
        _growthAccumulator += growthRate * profile.RealtimeEnergy * Time.deltaTime;
        int steps = Mathf.FloorToInt(_growthAccumulator);
        if (steps <= 0) return;
        _growthAccumulator -= steps;

        for (int s = 0; s < steps && _growing.Count > 0; s++)
        {
            //Round-robin across growing curls so they extend together.//
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
        _panelCentre = transform.position + Random.onUnitSphere * (maxPanelOffset * (1f - pr.centrality));
        _panelRotation = Random.rotationUniform;
        _sizeScale = Mathf.Lerp(0.5f, 1.2f, pr.prominence);
        _seeded = true;

        int stems = Mathf.RoundToInt(Mathf.Lerp(minRootStems, maxRootStems, pr.prominence));

        //Root stems radiate from around the panel centre, each starting at a random angle.//
        for (int i = 0; i < stems; i++)
        {
            float ang = (i / (float)stems) * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
            float r = panelRadius * _sizeScale * Random.Range(0.05f, 0.35f);
            Vector2 origin2D = new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r);

            //Heading roughly outward, so the ornament opens up from the middle.//
            float heading = ang * Mathf.Rad2Deg + Random.Range(-40f, 40f);

            var curl = new Curl();
            curl.Init(origin2D, heading, rootCurlLength * _sizeScale, 0,
                      Random.value < 0.5f ? 1f : -1f);
            _pending.Enqueue(curl);
        }
    }

    //Called by a Curl when it reaches a branch point.//
    public void SpawnChild(Vector2 origin2D, float headingDeg, float length, int depth, float spin)
    {
        if (_curlCount >= maxCurls) return;
        var curl = new Curl();
        curl.Init(origin2D, headingDeg, length, depth, spin);
        _pending.Enqueue(curl);
    }

    //Panel-local 2D -> world. The ornament lives on a flat tilted plane.//
    public Vector3 PanelToWorld(Vector2 p)
    {
        return _panelCentre + _panelRotation * new Vector3(p.x, p.y, 0f);
    }

    public int MaxDepthFor(TimbralProfile profile)
    {
        //THE core mapping: harmonic complexity drives how elaborate the tracery becomes.//
        return Mathf.RoundToInt(Mathf.Lerp(minDepth, maxDepth, profile.RealtimeHarmonicComplexity));
    }

    public float CurlRateFor(TimbralProfile profile)
    {
        //Complex harmony also curls more tightly, so intricate passages scroll harder.//
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
        lr.numCornerVertices = 3;   //smooth scrollwork, not faceted//
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        //Each generation is thinner, so fine detail actually reads as fine.//
        float w = rootLineWidth * _sizeScale * Mathf.Pow(widthFalloff, depth);
        lr.startWidth = w;
        lr.endWidth = w * 0.75f;   //slight taper along each curl//
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

//A single curling stem. Grows point by point along a spiral arc in panel space, spawning
//child curls at intervals until the recursion depth is exhausted.
public class Curl
{
    public bool Complete { get; private set; } = false;

    private Vector2 _pos;
    private float _heading;       //degrees, in panel space//
    private float _spin;          //+1 or -1: which way this curl scrolls//
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

            //Decide branch positions once, when the curl starts.//
            int maxDepth = gen.MaxDepthFor(profile);
            if (_depth < maxDepth)
            {
                int children = Random.Range(gen.MinChildren, gen.MaxChildren + 1);
                for (int i = 0; i < children; i++)
                    _branchPoints.Add(Random.Range(gen.BranchStart, gen.BranchEnd));
                _branchPoints.Sort();
            }
            _started = true;
        }

        //Advance along a spiral arc: the heading turns steadily, so the stem curls.//
        float curlRate = gen.CurlRateFor(profile);
        _heading += curlRate * gen.StepDistance * _spin;

        float rad = _heading * Mathf.Deg2Rad;
        _pos += new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * gen.StepDistance;
        _grown += gen.StepDistance;

        _points.Add(gen.PanelToWorld(_pos));
        _lr.positionCount = _points.Count;
        _lr.SetPositions(_points.ToArray());

        //Keep the ornament inside the panel.//
        if (_pos.magnitude > gen.PanelRadius)
        {
            Complete = true;
            return;
        }

        //Branch when we pass a branch point.//
        float t = _grown / _targetLength;
        while (_nextBranch < _branchPoints.Count && t >= _branchPoints[_nextBranch])
        {
            //Children depart at an angle, alternate their spin, and are shorter.//
            float side = (_nextBranch % 2 == 0) ? 1f : -1f;
            float childHeading = _heading + gen.ChildAngle * side + Random.Range(-12f, 12f);
            float childLength = _targetLength * gen.ChildScale;

            gen.SpawnChild(_pos, childHeading, childLength, _depth + 1, -_spin * side);
            _nextBranch++;
        }

        if (_grown >= _targetLength) Complete = true;
    }
}
