using System.Collections.Generic;
using UnityEngine;

//RotationFieldGenerator//
//Klüver Category 2 (Spirals)//
//Places rotating vortex fields, Triggered by harmonic complexity x rhythmic regularity x mid-flatness (peaks when flatness is neither fully tonal nor fully noisy)//

public class RotationFieldGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;
    [SerializeField] private float energyThreshold = 0.03f;

    [Header("Field Placement")]
    [SerializeField] private float scatterRadius = 30f;
    [SerializeField] private float maxFieldOffset = 30f;
    [SerializeField] private int maxFields = 6;
    [SerializeField] private int maxConcurrent = 2;
    [SerializeField] private HeadPlacement placement;
    [SerializeField] private float seedMinDepth = 0.9f;

    [Header("Vortex Shape")]
    [SerializeField] private float minFieldRadius = 80f;
    [SerializeField] private float maxFieldRadius = 180f;
    [SerializeField, Range(0.1f, 1.5f)] private float heightRatio = 0.55f;
    [SerializeField] private int minArcs = 300;
    [SerializeField] private int maxArcs = 2000;
    [SerializeField] private float expansionSeconds = 14f;

    [Header("Arc Shape")]
    [SerializeField] private float minArcSweep = 18f;
    [SerializeField] private float maxArcSweep = 55f;
    [SerializeField] private int arcSegments = 7;
    [SerializeField] private float lineWidth = 0.5f;

    [Header("Alignment")]
    [SerializeField] private float maxMisalignmentDeg = 45f;

    [Header("Growth")]
    [SerializeField] private float growthRate = 22f;

    [Header("Appearance")]
    [SerializeField, Range(0f, 1f)] private float opacity = 0.8f;

    [Header("Snapping Rotation")]
    [SerializeField] private int bandCount = 4;
    [SerializeField] private float snapDegrees = 90f;
    [SerializeField] private float outerSnapInterval = 1.4f;
    [SerializeField, Range(0.3f, 1f)] private float innerTickSpeedup = 0.6f;
    [SerializeField] private float snapEaseSpeed = 14f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _lineMaterial;

    private bool _active = false;
    private float _growthAccumulator = 0f;
    private int _completedFields = 0;

    private readonly List<Vortex> _growing = new List<Vortex>();
    private readonly List<Band> _bands = new List<Band>();

    private class Vortex
    {
        public Vector3 centre;
        public Vector3 axis;
        public Vector3 refA, refB;
        public float radius;
        public float halfHeight;
        public int targetArcs;
        public int drawn;
        public float age;
        public float spin;
        public float sizeScale;
        public Band[] bands;
    }

    private class Band
    {
        public Transform pivot;
        public float snapInterval; 
        public float snapTimer;
        public float targetAngle;
        public float currentAngle;
        public float spin; 
    }

    public void SetPrism(PRISMGenerator prism) { _prism = prism; }

    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("RotationField_Root");
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.RotationField);

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        _lineMaterial = new Material(shader);

        _active = false;
        _growthAccumulator = 0f;
        _completedFields = 0;
        _growing.Clear();
        _bands.Clear();

    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightRotationField;
        _active = weight >= activationThreshold;

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.RotationField) : Prominence.Silent;

        AnimateBands();

        if (!_active) return;
        if (profile.RealtimeEnergy < energyThreshold) return;

        int effectiveMax = Mathf.Max(1, Mathf.RoundToInt(maxFields * pr.prominence * pr.prominence));

        for (int i = 0; i < _growing.Count; i++)
        {
            _growing[i].age += Time.deltaTime;
        }

        if (_growing.Count > 0)
        {
            _growthAccumulator += growthRate * profile.RealtimeEnergy * Time.deltaTime;
            int steps = Mathf.FloorToInt(_growthAccumulator);
            if (steps > 0)
            {
                _growthAccumulator -= steps;
                for (int s = 0; s < steps && _growing.Count > 0; s++)
                {
                    int idx = s % _growing.Count;
                    Vortex v = _growing[idx];
                    DrawArc(v, profile, pr);
                    if (v.drawn >= v.targetArcs)
                    {
                        _completedFields++;
                        _growing.RemoveAt(idx);
                        if (_growing.Count == 0) break;
                    }
                }
            }
        }

        int total = _completedFields + _growing.Count;
        if (total < effectiveMax && _growing.Count < maxConcurrent) SeedVortex(profile, pr);
    }

    public void Deactivate() { _active = false; }

    private void AnimateBands()
    {
        float dt = Time.deltaTime;
        for (int i = 0; i < _bands.Count; i++)
        {
            Band b = _bands[i];
            if (b.pivot == null) continue;

            b.snapTimer += dt;
            if (b.snapTimer >= b.snapInterval)
            {
                b.snapTimer -= b.snapInterval;
                b.targetAngle += snapDegrees * b.spin;
            }

            float remaining = b.targetAngle - b.currentAngle;
            b.currentAngle += remaining * Mathf.Clamp01(snapEaseSpeed * dt);
            b.pivot.localRotation = Quaternion.AngleAxis(b.currentAngle, Vector3.forward);
        }
    }

    private void SeedVortex(TimbralProfile profile, Prominence pr)
    {
        var v = new Vortex();

        if (placement != null && placement.Ready)
        {
            v.centre = placement.RandomInEllipsoid(Mathf.Lerp(0.35f, 0.75f, pr.prominence));
        }
        else
        {
            float spread = scatterRadius * Mathf.Lerp(0.4f, 1f, pr.prominence);
            Vector3 fieldCentre = transform.position + Random.onUnitSphere * (maxFieldOffset * (1f - pr.centrality));
            v.centre = fieldCentre + Random.onUnitSphere * (spread * Mathf.Pow(Random.value, 0.5f));
        }

        v.axis = Random.onUnitSphere;
        v.sizeScale = Mathf.Lerp(0.55f, 2.5f, pr.prominence);
        v.radius = Mathf.Lerp(minFieldRadius, maxFieldRadius, Random.value) * v.sizeScale;

        if (placement != null && placement.Ready) v.radius = Mathf.Min(v.radius, MaxRadiusInCavity(v.centre, v.axis, v.radius));

        v.halfHeight = v.radius * heightRatio;
        v.targetArcs = Mathf.RoundToInt(Mathf.Lerp(minArcs, maxArcs, profile.RealtimeHarmonicComplexity) * pr.prominence);
        v.targetArcs = Mathf.Max(minArcs / 2, v.targetArcs);
        v.spin = Random.value < 0.5f ? 1f : -1f;
        v.drawn = 0;
        v.age = 0f;

        v.refA = Vector3.Cross(v.axis, Vector3.up);
        if (v.refA.sqrMagnitude < 1e-4f) v.refA = Vector3.Cross(v.axis, Vector3.right);
        v.refA.Normalize();
        v.refB = Vector3.Cross(v.axis, v.refA).normalized;

        Quaternion axisOrient = Quaternion.LookRotation(v.axis, v.refA);
        int bands = Mathf.Max(1, bandCount);
        v.bands = new Band[bands];
        for (int bi = 0; bi < bands; bi++)
        {
            var pivotGO = new GameObject($"Band_{bi}");
            pivotGO.transform.SetParent(_root.transform);
            pivotGO.transform.position = v.centre;
            pivotGO.transform.rotation = axisOrient;

            int fromOuter = (bands - 1) - bi;
            float interval = outerSnapInterval * Mathf.Pow(innerTickSpeedup, fromOuter);

            var band = new Band
            {
                pivot = pivotGO.transform,
                snapInterval = Mathf.Max(0.05f, interval),
                snapTimer = Random.Range(0f, interval),
                targetAngle = 0f,
                currentAngle = 0f,
                spin = v.spin
            };
            v.bands[bi] = band;
            _bands.Add(band);
        }

        _growing.Add(v);

        if (placement != null && placement.Ready) placement.ClaimPoint(v.centre, v.radius);

    }

    private float MaxRadiusInCavity(Vector3 centre, Vector3 axis, float want)
    {
        Vector3 a = Vector3.Cross(axis, Vector3.up);
        if (a.sqrMagnitude < 1e-4f) a = Vector3.Cross(axis, Vector3.right);
        a.Normalize();
        Vector3 b = Vector3.Cross(axis, a).normalized;

        float nearest = want;
        const int dirs = 8;
        for (int i = 0; i < dirs; i++)
        {
            float ang = (i / (float)dirs) * Mathf.PI * 2f;
            Vector3 dir = (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)).normalized;

            float step = 4f;
            float reached = want;
            for (float d = step; d <= want; d += step)
            {
                if (!placement.InEllipsoid(centre + dir * d)) { reached = d - step; break; }
            }
            nearest = Mathf.Min(nearest, reached);
        }
        return Mathf.Max(2f, nearest - 2f);
    }

    private void DrawArc(Vortex v, TimbralProfile profile, Prominence pr)
    {
        float growth = Mathf.Clamp01(v.age / expansionSeconds);
        float maxR = v.radius * Mathf.Lerp(0.6f, 1f, growth);

        float rNorm = Mathf.Pow(Random.value, 0.7f);
        float r = Mathf.Max(0.4f, rNorm * maxR);

        float theta = Random.Range(0f, Mathf.PI * 2f);
        float hh = Random.Range(-1f, 1f) * v.halfHeight * Mathf.Lerp(0.4f, 1f, growth);

        Vector3 radial = (v.refA * Mathf.Cos(theta) + v.refB * Mathf.Sin(theta)).normalized;
        Vector3 arcCentre = v.centre + radial * r + v.axis * hh;
        Vector3 tangent = Vector3.Cross(v.axis, radial).normalized * v.spin;

        float misalign = maxMisalignmentDeg * (1f - profile.RealtimeRhythmicRegularity);
        Quaternion wobble = Quaternion.AngleAxis(Random.Range(-misalign, misalign), tangent) * Quaternion.AngleAxis(Random.Range(-misalign, misalign), radial);

        float sweep = Mathf.Lerp(minArcSweep, maxArcSweep, profile.RealtimeEnergy) * Mathf.Deg2Rad;
        float arcRadius = Mathf.Max(0.6f, r * Random.Range(0.5f, 1.1f));

        Band band = v.bands[BandIndex(v, r)];
        Vector3[] pts = new Vector3[arcSegments + 1];
        for (int i = 0; i <= arcSegments; i++)
        {
            float t = (i / (float)arcSegments) - 0.5f;
            float aa = t * sweep * v.spin;
            Vector3 local = radial * (Mathf.Cos(aa) * arcRadius - arcRadius) + tangent * (Mathf.Sin(aa) * arcRadius);
            Vector3 world = arcCentre + wobble * local;
            pts[i] = band.pivot.InverseTransformPoint(world);
        }

        var go = new GameObject("Arc");
        go.transform.SetParent(band.pivot, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.material = _lineMaterial;
        lr.positionCount = pts.Length;
        lr.SetPositions(pts);

        float w = lineWidth * v.sizeScale;
        lr.startWidth = w;
        lr.endWidth = w;
        lr.numCornerVertices = 2;
        lr.numCapVertices = 1;

        Color c = _prism != null ? _prism.RealtimeColour : Color.cyan;
        float radialFade = 1f - Mathf.Clamp01(r / Mathf.Max(v.radius, 0.001f)) * 0.55f;
        c.a = opacity * radialFade;
        lr.startColor = c;
        lr.endColor = c;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        v.drawn++;
    }

    private int BandIndex(Vortex v, float r)
    {
        float frac = Mathf.Clamp01(r / Mathf.Max(v.radius, 0.001f));
        int idx = Mathf.FloorToInt(frac * v.bands.Length);
        return Mathf.Clamp(idx, 0, v.bands.Length - 1);
    }

    private void OnDestroy()
    {
        if (_lineMaterial != null) Destroy(_lineMaterial);
    }
}