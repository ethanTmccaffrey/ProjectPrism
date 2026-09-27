using System.Collections.Generic;
using UnityEngine;

//ReduplicationGenerator//
//Klüver Category 3 (Lattices / Honeycombs / Gratings)//
//Fires repeating concurrent ranks outward from the outer skull surface along the normal driven per beat, Triggered by rhythmic regularity squared x tonality//
public class ReduplicationGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;

    [Header("Field")]
    [SerializeField] private float fieldRadius = 28f;
    [SerializeField] private float maxFieldOffset = 30f;
    [SerializeField] private HeadPlacement placement;
    [SerializeField] private float launchOffset = 4f;
    [SerializeField] private int maxRanks = 40;

    [Header("Rank")]
    [SerializeField] private int minCopies = 4;
    [SerializeField] private int maxCopies = 12;
    [SerializeField] private float baseSpacing = 3.2f;
    [SerializeField, Range(1f, 1.4f)] private float spacingGrowth = 1.06f;

    [Header("Motif")]
    [SerializeField] private float baseSize = 1.6f;
    [SerializeField, Range(1f, 1.5f)] private float sizeGrowth = 1.12f;
    [SerializeField] private float rotationStep = 14f;
    [SerializeField] private float lineWidth = 0.18f;

    [Header("Fade")]
    [SerializeField, Range(0f, 1f)] private float startOpacity = 0.95f;
    [SerializeField, Range(0f, 1f)] private float endOpacity = 0.25f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _lineMaterial;

    private bool _active = false;
    private int _rankCount = 0;

    private readonly List<Rank> _growing = new List<Rank>();
    [SerializeField] private int maxConcurrentRanks = 3;

    private class Rank
    {
        public Vector3 origin;
        public Vector3 direction;   
        public Quaternion motifPlane; 
        public int sides;             
        public int targetCopies;
        public int stamped;
        public float sizeScale;
        public float distance;       
        public float spacing;
        public Color colour;
    }

    public void SetPrism(PRISMGenerator prism)
    {
        _prism = prism;
    }

    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("Reduplication_Root");
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.Reduplication);

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        _lineMaterial = new Material(shader);

        _active = false;
        _rankCount = 0;
        _growing.Clear();

    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightReduplication;
        _active = weight >= activationThreshold;

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.Reduplication) : Prominence.Silent;

        if (!_active) return;

        int effectiveMax = Mathf.Max(1, Mathf.RoundToInt(maxRanks * Mathf.Lerp(0.3f, 1f, pr.prominence)));

        if (!profile.BeatThisFrame) return;

        for (int i = _growing.Count - 1; i >= 0; i--)
        {
            StampCopy(_growing[i]);
            if (_growing[i].stamped >= _growing[i].targetCopies)
            {
                _rankCount++;
                _growing.RemoveAt(i);   
            }
        }
        int started = _rankCount + _growing.Count;
        while (_growing.Count < maxConcurrentRanks && started < effectiveMax)
        {
            _growing.Add(SeedRank(profile, pr));
            started++;
        }
    }

    public void Deactivate()
    {
        _active = false;
    }

    private Rank SeedRank(TimbralProfile profile, Prominence pr)
    {
        var r = new Rank();
        bool placed = false;
        if (placement != null && placement.HasSkullMesh)
        {
            Vector3 bestSp = Vector3.zero, bestSn = Vector3.up;
            float bestDist = -1f;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                if (!placement.RandomSkullMeshPoint(out Vector3 s, out Vector3 n)) break;
                float dist = (s - placement.CavityCentre).magnitude;
                if (dist > bestDist) { bestDist = dist; bestSp = s; bestSn = n; }
            }
            if (bestDist > 0f)
            {
                Vector3 outFromCentre = (bestSp - placement.CavityCentre).normalized;
                Vector3 dir = bestSn.normalized;
                if (Vector3.Dot(dir, outFromCentre) < 0f) dir = -dir;
                if (Vector3.Dot(dir, outFromCentre) < 0.3f) dir = outFromCentre;
                r.direction = dir;
                r.origin = bestSp + r.direction * launchOffset;
                placed = true;
            }
        }
        if (!placed)
        {
            r.direction = Random.onUnitSphere;
            r.origin = transform.position + r.direction * launchOffset;
        }
        r.motifPlane = Quaternion.LookRotation(r.direction, Random.onUnitSphere);
        r.sides = Random.Range(3, 7); 
        r.targetCopies = Mathf.RoundToInt(Mathf.Lerp(minCopies, maxCopies, pr.prominence));
        r.sizeScale = Mathf.Lerp(0.6f, 1.2f, pr.prominence);
        r.stamped = 0;
        r.distance = 0f;
        r.spacing = baseSpacing * r.sizeScale;
        r.colour = _prism != null ? _prism.RealtimeColour : Color.cyan;
        return r;
    }

    private void StampCopy(Rank r)
    {
        int i = r.stamped;
        float size = baseSize * r.sizeScale * Mathf.Pow(sizeGrowth, i);
        float roll = rotationStep * i;

        Vector3 centre = r.origin + r.direction * r.distance;


        r.distance += r.spacing;
        r.spacing *= spacingGrowth;

        float t = r.targetCopies > 1 ? i / (float)(r.targetCopies - 1) : 0f;
        Color c = r.colour;
        c.a = Mathf.Lerp(startOpacity, endOpacity, t);

        BuildPolygon(centre, r.motifPlane, roll, r.sides, size, c, r.sizeScale);
        r.stamped++;
    }

    private void BuildPolygon(Vector3 centre, Quaternion plane, float rollDeg, int sides, float size, Color colour, float sizeScale)
    {
        var go = new GameObject("Motif");
        go.transform.SetParent(_root.transform);

        Quaternion rot = plane * Quaternion.AngleAxis(rollDeg, Vector3.forward);

        Vector3[] pts = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float ang = (i / (float)sides) * Mathf.PI * 2f;
            Vector3 local = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * size;
            pts[i] = centre + rot * local;
        }

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = true;              
        lr.material = _lineMaterial;
        lr.positionCount = sides;
        lr.SetPositions(pts);

        float w = lineWidth * sizeScale;
        lr.startWidth = w;
        lr.endWidth = w;
        lr.numCornerVertices = 0;    
        lr.numCapVertices = 0;
        lr.startColor = colour;
        lr.endColor = colour;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
    }

    private void OnDestroy()
    {
        if (_lineMaterial != null) Destroy(_lineMaterial);
    }
}