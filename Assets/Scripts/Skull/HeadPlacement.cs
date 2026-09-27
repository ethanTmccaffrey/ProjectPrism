using System.Collections.Generic;
using UnityEngine;

//HeadPlacement//
//Placement service between generators and the head geometry//
//Probes the hollow interior into a containment ellipsoid and provides cavity/ellipsoid sampling (RandomPointInCavity, WallAnchors, claim-based spacing) for interior generators//
//plus skull-surface projection and paint forwarding (PaintSkull / PaintPlane / NearestSkullMeshPoint) for shell generators//

[RequireComponent(typeof(HeadField))]
public class HeadPlacement : MonoBehaviour
{
    [Header("Field")]
    [SerializeField] private HeadField field;
    [SerializeField] private HeadField outerField;

    [Header("Probe")]
    [SerializeField] private int probeResolution = 24;

    [Header("Containment Ellipsoid")]
    [SerializeField, Range(0.3f, 1f)] private float fitFactor = 0.72f;
    [SerializeField] private Vector3 axisFit = new Vector3(1f, 0.7f, 1f);
    [SerializeField] private Vector3 centreOffset = new Vector3(0f, 40f, 0f);
    [SerializeField] private bool showEllipsoid = false;
    [SerializeField] private Color ellipsoidColour = new Color(1f, 0f, 1f, 0.5f);   

    public bool Ready { get; private set; }
    public Vector3 CavityCentre { get; private set; }
    public Vector3 CavityExtent { get; private set; }

    public Vector3 EllipsoidCentre { get; private set; }
    public Vector3 EllipsoidRadii { get; private set; }
    private GameObject _ellipsoidViz;

    private struct Claim { public Vector3 centre; public float radius; }
    private readonly System.Collections.Generic.List<Claim> _claims = new System.Collections.Generic.List<Claim>();

    public void ClaimPoint(Vector3 centre, float radius)
    {
        _claims.Add(new Claim { centre = centre, radius = Mathf.Max(1f, radius) });
    }

    public void ClearClaims() => _claims.Clear();

    private void Awake()
    {
        if (field == null) field = GetComponent<HeadField>();
    }

    private void Start()
    {
        if (field != null && !field.Loaded) field.Load();
        Probe();
    }
    private void Probe()
    {
        if (field == null || !field.Loaded)
        {
            Debug.LogError("PRISM HeadPlacement: no loaded HeadField to probe.");
            return;
        }

        Bounds b = field.FieldBounds;
        Vector3 min = b.min, size = b.size;
        int n = Mathf.Max(2, probeResolution);

        Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        int hits = 0;

        for (int ix = 0; ix < n; ix++)
            for (int iy = 0; iy < n; iy++)
                for (int iz = 0; iz < n; iz++)
                {
                    Vector3 p = min + new Vector3(size.x * ix / (n - 1), size.y * iy / (n - 1), size.z * iz / (n - 1));

                    if (!field.IsInCavity(p)) continue;

                    hits++;
                    lo = Vector3.Min(lo, p);
                    hi = Vector3.Max(hi, p);
                }

        if (hits == 0)
        {
            Debug.LogError("PRISM HeadPlacement: probe found no cavity. Is the inner HeadField " + "assigned and its shell thickness sane?");
            return;
        }

        CavityCentre = (lo + hi) * 0.5f;
        CavityExtent = (hi - lo) * 0.5f;

        EllipsoidCentre = CavityCentre + centreOffset;
        EllipsoidRadii = Vector3.Scale(CavityExtent * fitFactor, axisFit);

        Ready = true;

        Debug.Log($"PRISM HeadPlacement: cavity probed - centre {CavityCentre}, " + $"extent {CavityExtent}, {hits} hits at res {n}.");
        Debug.Log($"PRISM HeadPlacement: containment ellipsoid centre {EllipsoidCentre}, " + $"radii {EllipsoidRadii} (fitFactor {fitFactor}).");

        if (showEllipsoid) BuildEllipsoidViz();
    }

    public bool InEllipsoid(Vector3 world)
    {
        Vector3 u = ToUnit(world);
        return u.sqrMagnitude <= 1f;
    }

    public Vector3 RandomInEllipsoid()
    {
        Vector3 u = Random.insideUnitSphere;
        return EllipsoidCentre + Vector3.Scale(u, EllipsoidRadii);
    }


    public Vector3 RandomInEllipsoid(float fill)
    {
        Vector3 u = Random.insideUnitSphere * Mathf.Clamp01(fill);
        return EllipsoidCentre + Vector3.Scale(u, EllipsoidRadii);
    }

    public Vector3 ClampToEllipsoid(Vector3 world)
    {
        Vector3 u = ToUnit(world);
        float m = u.magnitude;
        if (m <= 1f) return world;    
        u /= m;  
        return EllipsoidCentre + Vector3.Scale(u, EllipsoidRadii);
    }
    public Vector3 SurfacePointInDirection(Vector3 dir)
    {
        Vector3 dU = new Vector3(dir.x / EllipsoidRadii.x, dir.y / EllipsoidRadii.y, dir.z / EllipsoidRadii.z);
        float len = dU.magnitude;
        if (len < 1e-6f) return EllipsoidCentre;
        Vector3 uHit = dU / len; 
        return EllipsoidCentre + Vector3.Scale(uHit, EllipsoidRadii);
    }

    public Vector3 EllipsoidNormal(Vector3 world)
    {
        Vector3 d = world - EllipsoidCentre;
        Vector3 g = new Vector3(
            d.x / (EllipsoidRadii.x * EllipsoidRadii.x),
            d.y / (EllipsoidRadii.y * EllipsoidRadii.y),
            d.z / (EllipsoidRadii.z * EllipsoidRadii.z));
        return g.sqrMagnitude < 1e-12f ? Vector3.up : g.normalized;
    }

    public float EllipsoidDepth(Vector3 world)
    {
        return 1f - ToUnit(world).magnitude;
    }

    public bool HasOuterField => outerField != null && outerField.Loaded;
    public bool HasSkullMesh => outerField != null && outerField.HasMesh;

    public bool RandomSkullMeshPoint(out Vector3 point, out Vector3 normal)
    {
        point = CavityCentre; normal = Vector3.up;
        if (outerField == null || !outerField.HasMesh) return false;
        return outerField.RandomSurfacePoint(out point, out normal);
    }

    public bool NearestSkullMeshPoint(Vector3 world, out Vector3 point, out Vector3 normal)
    {
        point = world; normal = Vector3.up;
        if (outerField == null || !outerField.HasMesh) return false;
        return outerField.NearestSurfacePoint(world, out point, out normal);
    }

    public void PaintSkull(Vector3 centre, float radius, Color colour, float strength)
    {
        if (outerField != null) outerField.PaintSphere(centre, radius, colour, strength);
    }

    public void PaintPlane(Vector3 planePoint, Vector3 planeNormal, float halfThickness, Color colour, float strength)
    {
        if (outerField != null) outerField.PaintPlane(planePoint, planeNormal, halfThickness, colour, strength);
    }

    public void ApplySkullPaint()
    {
        if (outerField != null) outerField.ApplyPaint();
    }

    public void ClearSkullPaint()
    {
        if (outerField != null) outerField.ClearPaint();
    }

    public Vector3 ProjectToSkullSurface(Vector3 world, float proudDistance = 1.5f)
    {
        if (!HasOuterField) return world;

        Vector3 centre = outerField.FieldBounds.center;
        Vector3 dir = (world - centre).normalized;
        if (dir.sqrMagnitude < 1e-8f) return world;

        float maxReach = outerField.FieldBounds.extents.magnitude * 1.2f;
        float step = outerField.VoxelWorldSize * 0.5f;

        Vector3 surface = centre;
        bool found = false;
        bool prevInside = outerField.IsInside(centre);
        for (float t = step; t <= maxReach; t += step)
        {
            Vector3 p = centre + dir * t;
            bool inside = outerField.IsInside(p);
            if (prevInside && !inside)
            {
                surface = centre + dir * (t - step * 0.5f);  
                found = true;
            }
            prevInside = inside;
        }

        if (!found) return world;

        Vector3 outward = -SkullNormalInward(surface);
        if (Vector3.Dot(outward, dir) < 0f) outward = dir;   
        return surface + outward * proudDistance;
    }

    public Vector3 SkullSurfaceNormal(Vector3 world)
    {
        return -SkullNormalInward(world);
    }

    private Vector3 SkullNormalInward(Vector3 world)
    {
        Vector3 g = SkullGradient(world);
        return g.sqrMagnitude < 1e-12f ? Vector3.up : g.normalized;
    }

    private Vector3 SkullGradient(Vector3 world)
    {
        float h = outerField.VoxelWorldSize;
        float dx = outerField.SampleWorld(world + Vector3.right * h) - outerField.SampleWorld(world - Vector3.right * h);
        float dy = outerField.SampleWorld(world + Vector3.up * h) - outerField.SampleWorld(world - Vector3.up * h);
        float dz = outerField.SampleWorld(world + Vector3.forward * h) - outerField.SampleWorld(world - Vector3.forward * h);
        return new Vector3(dx, dy, dz);
    }

    public Vector3 RandomSkullSurfacePoint(Vector3 dir, float proudDistance = 1.5f)
    {
        if (!HasOuterField) return CavityCentre;
        Vector3 centre = outerField.FieldBounds.center;
        Vector3 start = centre + dir.normalized * (outerField.FieldBounds.extents.magnitude * 0.4f);
        return ProjectToSkullSurface(start, proudDistance);
    }

    private Vector3 ToUnit(Vector3 world)
    {
        Vector3 d = world - EllipsoidCentre;
        return new Vector3(d.x / EllipsoidRadii.x, d.y / EllipsoidRadii.y, d.z / EllipsoidRadii.z);
    }

    private void BuildEllipsoidViz()
    {
        if (_ellipsoidViz != null) Destroy(_ellipsoidViz);
        _ellipsoidViz = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _ellipsoidViz.name = "ContainmentEllipsoid_VIZ";
        var col = _ellipsoidViz.GetComponent<Collider>();
        if (col != null) Destroy(col);

        _ellipsoidViz.transform.SetParent(transform, false);
        _ellipsoidViz.transform.position = EllipsoidCentre;
        _ellipsoidViz.transform.localScale = EllipsoidRadii * 2f;

        var r = _ellipsoidViz.GetComponent<Renderer>();
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        var mat = new Material(sh);
        mat.color = ellipsoidColour;
        r.material = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    public void SetEllipsoidVisible(bool visible)
    {
        showEllipsoid = visible;
        if (visible && _ellipsoidViz == null && Ready) BuildEllipsoidViz();
        else if (_ellipsoidViz != null) _ellipsoidViz.SetActive(visible);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying || !Ready) return;
        EllipsoidCentre = CavityCentre + centreOffset;
        EllipsoidRadii = Vector3.Scale(CavityExtent * fitFactor, axisFit);
        if (showEllipsoid) BuildEllipsoidViz();
        else if (_ellipsoidViz != null) { Destroy(_ellipsoidViz); _ellipsoidViz = null; }
    }
#endif

    public Vector3 RandomPointInCavity(float prominence, float centrality, float minDepth = 0f)
    {
        if (!Ready) return CavityCentre;

        float offMag = Mathf.Clamp(1f - Mathf.Clamp01(centrality), 0.2f, 0.55f);

        Vector3 dir = Random.onUnitSphere;
        dir.y = Random.Range(-1f, 1f);  
        dir = dir.normalized;
        Vector3 anchor = CavityCentre + Vector3.Scale(dir * offMag, CavityExtent);

        float spread = Mathf.Lerp(0.15f, 0.9f, Mathf.Clamp01(prominence));

        Vector3 best = CavityCentre;
        float bestScore = float.NegativeInfinity;
        bool haveBest = false;

        for (int attempt = 0; attempt < 32; attempt++)
        {
            float s = spread * (1f - attempt / 48f);
            Vector3 candidate = anchor + Vector3.Scale(Random.insideUnitSphere * s, CavityExtent);

            bool inside = minDepth <= 0f ? field.IsInCavity(candidate) : field.SampleWorld(candidate) >= minDepth;
            if (!inside) continue;

            float score = NearestClaimClearance(candidate);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
                haveBest = true;
                if (score >= 0f) return candidate;
            }
        }

        if (haveBest) return best;

        for (int j = 0; j < 16; j++)
        {
            Vector3 jittered = CavityCentre + Vector3.Scale(Random.insideUnitSphere * 0.35f, CavityExtent);
            bool inside = minDepth <= 0f ? field.IsInCavity(jittered) : field.SampleWorld(jittered) >= minDepth;
            if (inside && NearestClaimClearance(jittered) >= 0f) return jittered;
        }
        return CavityCentre;
    }

    private float NearestClaimClearance(Vector3 p)
    {
        if (_claims.Count == 0) return 1f;   
        float worst = float.PositiveInfinity;
        for (int i = 0; i < _claims.Count; i++)
        {
            float d = Vector3.Distance(p, _claims[i].centre) - _claims[i].radius;
            if (d < worst) worst = d;
        }
        return worst;
    }

    public bool IsInCavity(Vector3 world) => field != null && field.IsInCavity(world);

    public Vector3 SampleNormal(Vector3 world)
    {
        if (field == null || !field.Loaded) return Vector3.zero;

        float h = field.VoxelWorldSize;
        float dx = field.SampleWorld(world + Vector3.right * h) - field.SampleWorld(world - Vector3.right * h);
        float dy = field.SampleWorld(world + Vector3.up * h) - field.SampleWorld(world - Vector3.up * h);
        float dz = field.SampleWorld(world + Vector3.forward * h) - field.SampleWorld(world - Vector3.forward * h);

        Vector3 grad = new Vector3(dx, dy, dz);
        if (grad.sqrMagnitude < 1e-10f) return Vector3.zero; 
        return (-grad).normalized;   
    }

    public float DistanceInside(Vector3 world) => field != null ? field.SampleWorld(world) : -1f;

    private Vector3 InteriorPoint()
    {
        if (field.IsInCavity(CavityCentre)) return CavityCentre;

        Bounds b = field.FieldBounds;
        Vector3 min = b.min, size = b.size;
        int n = 20;
        Vector3 best = CavityCentre;
        float bestDist = float.MaxValue;

        for (int ix = 0; ix < n; ix++)
            for (int iy = 0; iy < n; iy++)
                for (int iz = 0; iz < n; iz++)
                {
                    Vector3 p = min + new Vector3(size.x * ix / (n - 1), size.y * iy / (n - 1), size.z * iz / (n - 1));
                    if (!field.IsInCavity(p)) continue;
                    float dd = (p - CavityCentre).sqrMagnitude;
                    if (dd < bestDist) { bestDist = dd; best = p; }
                }

        return best;
    }

    public List<Vector3> WallAnchors(int count, float jitter = 0.15f, float inwardPull = 14f, float verticalStretch = 0.5f)
    {
        var anchors = new List<Vector3>(count);
        if (!Ready) return anchors;

        float step = Mathf.Min(CavityExtent.x, Mathf.Min(CavityExtent.y, CavityExtent.z)) / 40f;
        step = Mathf.Max(step, field.VoxelWorldSize);
        float maxReach = CavityExtent.magnitude * 1.2f; 
        float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));

        Vector3 start = InteriorPoint();

        for (int i = 0; i < count; i++)
        {
            float t = (i + 0.5f) / count;
            float y = 1f - 2f * t;                    
            if (verticalStretch > 0f)
            {
                float p = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(verticalStretch));  
                y = Mathf.Sign(y) * Mathf.Pow(Mathf.Abs(y), p);
            }
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float phi = i * golden;
            Vector3 dir = new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);

            if (jitter > 0f)
            {
                dir = (dir + Random.insideUnitSphere * jitter).normalized;
            }

            Vector3 wall = start;
            bool haveWall = false;
            bool prevInside = field.IsInCavity(start);

            for (float d = step; d <= maxReach; d += step)
            {
                Vector3 p = start + dir * d;
                bool inside = field.IsInCavity(p);

                if (prevInside && !inside)
                {
                    wall = start + dir * (d - step);
                    haveWall = true;
                }
                prevInside = inside;
            }

            if (haveWall)
            {
                wall = wall - dir * inwardPull;
                if (field.IsInCavity(wall) && field.SampleWorld(wall) > field.ShellThickness * 1.5f)
                {
                    anchors.Add(wall);
                }
            }
        }

        return anchors;
    }

}