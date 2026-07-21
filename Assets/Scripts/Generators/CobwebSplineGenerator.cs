using System.Collections.Generic;
using UnityEngine;

//CobwebSplineGenerator - Kluver Category 4 (Cobwebs and Radial Forms), cobweb subcategory//

//Weaves an irregular web: threads spanning between scattered anchor points, sagging under their own weight like real cobwebs//
//No radial centre — this is tangled webbing, not a spider's wheel//
//The web grows thread by thread across the song, so you watch it weave//

//Distinct from the other Cat 4 generators: Fracture SCATTERS (individual shards), RadiationBurst RADIATES (rays from a point), CobwebSpline CONNECTS (threads spanningbetween anchors)//
//Connection is the identity here//

//Timbral home (weight = H * sqrt(F) * tX): harmonically complex, somewhat noisy, and SUSTAINED rather than percussive — dense ambient, shoegaze, post-rock, orchestral with grit//

//Acoustic -> visual://
//Weave density: harmonic complexity (complex harmony = more anchors, denser web)//
//Thread sag: flatness (noisier = heavier, more drooping threads)//
//Growth rate: paced across the track so the web completes as the song ends//
//Colour: RealtimeColour at each thread's birth//
//Prominence: web spread + thread thickness//

public class CobwebSplineGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;

    [Header("Web")]
    [SerializeField] private float webRadius = 28f;
    [SerializeField] private int minAnchors = 12;
    [SerializeField] private int maxAnchors = 40;
    [SerializeField] private int maxThreads = 250;
    [SerializeField] private float fallbackTrackSeconds = 180f;

    [Header("Threads")]
    [SerializeField] private int threadSegments = 8;
    [SerializeField] private float minSag = 0.5f;
    [SerializeField] private float maxSag = 4f;
    [SerializeField] private float threadWidth = 0.05f;
    [SerializeField] private float maxThreadSpan = 16f;

    [Header("Placement")]
    [SerializeField] private float maxWebOffset = 30f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _threadMaterial;

    private bool _active = false;
    private bool _seeded = false;
    private float _elapsed = 0f;

    private Vector3 _webCentre;
    private List<Vector3> _anchors = new List<Vector3>();
    private HashSet<long> _threaded = new HashSet<long>();
    private int _threadCount = 0;
    private int _effectiveMaxThreads = 0;

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
        _threaded.Clear();
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

        _effectiveMaxThreads = Mathf.Max(4, Mathf.RoundToInt(maxThreads * pr.prominence * pr.prominence));
        if (_threadCount >= _effectiveMaxThreads) return;

        float trackLength = (_prism != null && _prism.TrackLength > 1f) ? _prism.TrackLength : fallbackTrackSeconds;
        float progress = Mathf.Clamp01(_elapsed / trackLength);
        int allowedByNow = Mathf.CeilToInt(progress * _effectiveMaxThreads);
        if (_threadCount >= allowedByNow) return;

        WeaveThread(profile, pr);
    }

    public void Deactivate()
    {
        _active = false;
    }

    private void SeedWeb(TimbralProfile profile, Prominence pr)
    {
        _webCentre = transform.position + Random.onUnitSphere * (maxWebOffset * (1f - pr.centrality));

        int anchorCount = Mathf.RoundToInt(Mathf.Lerp(minAnchors, maxAnchors, profile.RealtimeHarmonicComplexity));

        float spread = webRadius * Mathf.Lerp(0.4f, 1f, pr.prominence);

        _anchors.Clear();
        for (int i = 0; i < anchorCount; i++)
        {
            Vector3 p = _webCentre + Random.onUnitSphere * (spread * Mathf.Pow(Random.value, 0.333f));
            _anchors.Add(p);
        }

        _threaded.Clear();
        _threadCount = 0;
        _seeded = true;
    }

    private void WeaveThread(TimbralProfile profile, Prominence pr)
    {
        if (_anchors.Count < 2) return;

        int a = -1, b = -1;
        for (int attempt = 0; attempt < 24; attempt++)
        {
            int i = Random.Range(0, _anchors.Count);
            int j = Random.Range(0, _anchors.Count);
            if (i == j) continue;

            long key = PairKey(i, j);
            if (_threaded.Contains(key)) continue;

            if (Vector3.Distance(_anchors[i], _anchors[j]) > maxThreadSpan) continue;

            a = i; b = j;
            break;
        }

        if (a < 0) return; 

        _threaded.Add(PairKey(a, b));
        BuildThread(_anchors[a], _anchors[b], profile, pr);
        _threadCount++;
    }
    private long PairKey(int i, int j)
    {
        int lo = Mathf.Min(i, j), hi = Mathf.Max(i, j);
        return ((long)lo << 32) | (uint)hi;
    }

    private void BuildThread(Vector3 from, Vector3 to, TimbralProfile profile, Prominence pr)
    {
        float sag = Mathf.Lerp(minSag, maxSag, profile.RealtimeFlatness);
        sag *= Vector3.Distance(from, to) / Mathf.Max(maxThreadSpan, 0.001f);

        Vector3[] pts = new Vector3[threadSegments + 1];
        for (int i = 0; i <= threadSegments; i++)
        {
            float t = i / (float)threadSegments;
            Vector3 straight = Vector3.Lerp(from, to, t);

            float droop = 4f * t * (1f - t) * sag;
            pts[i] = straight + Vector3.down * droop;
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
