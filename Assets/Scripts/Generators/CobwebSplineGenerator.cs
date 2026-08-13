using System.Collections.Generic;
using UnityEngine;
public class CobwebSplineGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;

    [Header("Web")]
    [SerializeField] private int minAnchors = 20;
    [SerializeField] private int maxAnchors = 40;
    [SerializeField] private int maxThreads = 120;
    [SerializeField] private float fallbackTrackSeconds = 180f;

    [Header("Crossings")]
    [SerializeField] private float crossingDistance = 3f;
    [SerializeField] private int maxCrossingAnchors = 40;
    [SerializeField] private int crossingThreads = 40;

    [Header("Threads")]
    [SerializeField] private int threadSegments = 8;
    [SerializeField] private float minSag = 0.5f;
    [SerializeField] private float maxSag = 4f;
    [SerializeField] private float threadWidth = 0.05f;

    [Header("Placement")]
    [SerializeField] private float webRadius = 28f;
    [SerializeField] private float maxWebOffset = 30f;
    [SerializeField] private HeadPlacement placement;

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _threadMaterial;

    private bool _active = false;
    private bool _seeded = false;
    private float _elapsed = 0f;

    private Vector3 _webCentre;
    private List<Vector3> _anchors = new List<Vector3>();
    private struct ThreadPair { public int a, b; }
    private List<ThreadPair> _plan = new List<ThreadPair>();
    private int _planIndex = 0;
    private int _threadCount = 0;

    public void SetPrism(PRISMGenerator prism)
    {
        _prism = prism;
    }

    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("Cobweb_Root");
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.CobwebSpline);

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        _threadMaterial = new Material(shader);

        _anchors.Clear();
        _plan.Clear();
        _planIndex = 0;
        _threadCount = 0;
        _elapsed = 0f;
        _seeded = false;
        _active = false;

        Debug.Log("PRISM CobwebSplineGenerator: Initialised");
    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightCobwebSpline;
        _active = weight >= activationThreshold;

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.CobwebSpline) : Prominence.Silent;
        _elapsed += Time.deltaTime;

        if (!_active) return;

        if (!_seeded) SeedWeb(profile, pr);

        float trackLength = (_prism != null && _prism.TrackLength > 1f) ? _prism.TrackLength : fallbackTrackSeconds;
        float progress = Mathf.Clamp01(_elapsed / trackLength);
        int allowedByNow = Mathf.CeilToInt(progress * _plan.Count);

        while (_threadCount < allowedByNow && _threadCount < _plan.Count)
        {
            ThreadPair p = _plan[_planIndex++];
            BuildThread(_anchors[p.a], _anchors[p.b], profile, pr);
            _threadCount++;
        }
    }

    public void Deactivate()
    {
        _active = false;
    }

    private void SeedWeb(TimbralProfile profile, Prominence pr)
    {
        int baseAnchors = Mathf.RoundToInt(Mathf.Lerp(minAnchors, maxAnchors, profile.RealtimeHarmonicComplexity));
        int anchorCount = Mathf.Max(minAnchors, Mathf.RoundToInt(baseAnchors * Mathf.Lerp(0.7f, 1f, pr.prominence)));

        _anchors.Clear();
        if (placement != null && placement.Ready)
        {
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < anchorCount; i++)
            {
                float t = (i + 0.5f) / anchorCount;
                float y = 1f - 2f * t;
                float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                float phi = i * golden;
                Vector3 dir = new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);
                dir = (dir + Random.insideUnitSphere * 0.15f).normalized;
                _anchors.Add(placement.SurfacePointInDirection(dir));
            }
            _webCentre = placement.EllipsoidCentre;
        }
        else
        {
            _webCentre = transform.position + Random.onUnitSphere * (maxWebOffset * (1f - pr.centrality));
            float spread = webRadius * Mathf.Lerp(0.4f, 1f, pr.prominence);
            for (int i = 0; i < anchorCount; i++)
            {
                _anchors.Add(_webCentre + Random.onUnitSphere * (spread * Mathf.Pow(Random.value, 0.333f)));
            }
        }

        int wallAnchorCount = _anchors.Count;

        int threadTotal = Mathf.Max(8, Mathf.RoundToInt(maxThreads * Mathf.Lerp(0.5f, 1f, pr.prominence)));

        _plan.Clear();
        _planIndex = 0;
        _threadCount = 0;

        var seen = new HashSet<long>();
        var mainThreads = new List<ThreadPair>();
        int guard = 0;
        while (mainThreads.Count < threadTotal && guard < threadTotal * 20 && wallAnchorCount >= 2)
        {
            guard++;
            int i = Random.Range(0, wallAnchorCount);
            int j = Random.Range(0, wallAnchorCount);
            if (i == j) continue;
            long key = PairKey(i, j);
            if (!seen.Add(key)) continue;
            mainThreads.Add(new ThreadPair { a = i, b = j });
        }

        AddCrossingAnchors(mainThreads, wallAnchorCount);

        var crossingPairs = new List<ThreadPair>();
        if (_anchors.Count > wallAnchorCount)
        {
            int cGuard = 0;
            while (crossingPairs.Count < crossingThreads && cGuard < crossingThreads * 20)
            {
                cGuard++;
                int c = Random.Range(wallAnchorCount, _anchors.Count);
                int other = Random.Range(0, _anchors.Count);
                if (other == c) continue;
                long key = PairKey(c, other);
                if (!seen.Add(key)) continue;
                crossingPairs.Add(new ThreadPair { a = c, b = other });
            }
        }

        _plan.AddRange(mainThreads);
        _plan.AddRange(crossingPairs);
        for (int i = _plan.Count - 1; i > 0; i--)
        {
            int s = Random.Range(0, i + 1);
            ThreadPair tmp = _plan[i];
            _plan[i] = _plan[s];
            _plan[s] = tmp;
        }

        _seeded = true;
    }
    private void AddCrossingAnchors(List<ThreadPair> threads, int wallAnchorCount)
    {
        int added = 0;
        for (int i = 0; i < threads.Count && added < maxCrossingAnchors; i++)
        {
            Vector3 a1 = _anchors[threads[i].a];
            Vector3 a2 = _anchors[threads[i].b];

            for (int j = i + 1; j < threads.Count && added < maxCrossingAnchors; j++)
            {
                if (threads[i].a == threads[j].a || threads[i].a == threads[j].b || threads[i].b == threads[j].a || threads[i].b == threads[j].b) continue;

                Vector3 b1 = _anchors[threads[j].a];
                Vector3 b2 = _anchors[threads[j].b];

                if (ClosestApproach(a1, a2, b1, b2, out Vector3 mid, out float dist) && dist <= crossingDistance)
                {
                    _anchors.Add(mid);
                    added++;
                }
            }
        }
    }

    private bool ClosestApproach(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4, out Vector3 mid, out float dist)
    {
        mid = Vector3.zero; dist = float.MaxValue;

        Vector3 d1 = p2 - p1;
        Vector3 d2 = p4 - p3;
        Vector3 r = p1 - p3;

        float a = Vector3.Dot(d1, d1);
        float e = Vector3.Dot(d2, d2);
        float f = Vector3.Dot(d2, r);

        if (a < 1e-6f || e < 1e-6f) return false; 

        float c = Vector3.Dot(d1, r);
        float b = Vector3.Dot(d1, d2);
        float denom = a * e - b * b;

        float s = denom > 1e-6f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
        float t = (b * s + f) / e;

        if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
        else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }

        Vector3 closest1 = p1 + d1 * s;
        Vector3 closest2 = p3 + d2 * t;

        dist = Vector3.Distance(closest1, closest2);
        mid = (closest1 + closest2) * 0.5f;
        return true;
    }

    private long PairKey(int i, int j)
    {
        int lo = Mathf.Min(i, j), hi = Mathf.Max(i, j);
        return ((long)lo << 32) | (uint)hi;
    }

    private void BuildThread(Vector3 from, Vector3 to, TimbralProfile profile, Prominence pr)
    {
        float sag = Mathf.Lerp(minSag, maxSag, profile.RealtimeFlatness);

        Vector3[] pts = new Vector3[threadSegments + 1];
        for (int i = 0; i <= threadSegments; i++)
        {
            float t = i / (float)threadSegments;
            Vector3 straight = Vector3.Lerp(from, to, t);
            float droop = 4f * t * (1f - t) * sag; 
            Vector3 p = straight + Vector3.down * droop;

            if (placement != null && placement.Ready)
            {
                p = placement.ClampToEllipsoid(p);
            }

            pts[i] = p;
        }

        GameObject threadGO = new GameObject("Thread");
        threadGO.transform.SetParent(_root.transform);

        LineRenderer lr = threadGO.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.material = _threadMaterial;
        lr.positionCount = pts.Length;
        lr.SetPositions(pts);

        float w = threadWidth * Mathf.Lerp(0.5f, 1.4f, pr.prominence);
        lr.startWidth = w;
        lr.endWidth = w;
        lr.numCornerVertices = 1;

        Color colour = _prism != null ? _prism.RealtimeColour : Color.white;
        lr.startColor = colour;
        lr.endColor = colour;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
    }

    private void OnDestroy()
    {
        if (_threadMaterial != null) Destroy(_threadMaterial);
    }
}
