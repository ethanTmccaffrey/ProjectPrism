using System.Collections.Generic;
using UnityEngine;

//RotationFieldGenerator - Kluver Category 2 (Spirals), rotation field subcategory//

//Reveals VORTICES: regions of space that have spin, made visible by scattering short arc
//segments through them, each arc curving around the field's axis. You do not see a spiral
//line - you see a volume where everything is turning, the way iron filings reveal a
//magnetic field or debris reveals a whirlpool.
//
//The third expression of Category 2, and distinct from the other two by SCOPE:
//  SpiralGrowth  - ONE arm, a single continuous filament winding outward from a centre//
//  Drift         - ONE wandering path, rotation so loose it barely reads as rotation//
//  RotationField - MANY marks sharing one rotational logic across a whole region//
//The first two are individual paths. This is a property of space itself, expressed
//through everything caught in it.
//
//Timbral home (weight = H * R * |0.5 - F|): harmonically complex and regular. The
//flatness term peaks at the EXTREMES - very tonal or very noisy - and falls away in the
//middle, so the field wants music with a decided spectral character rather than an
//ambiguous one.
//
//Several separate vortices accumulate across a track rather than one large field or a
//single blended turbulence. Each is individually legible as a pocket of spin; together
//they make the canvas feel turbulent without dissolving into noise.
//
//STATIC ONCE DRAWN. Rotation is inherently about motion, but the arcs RECORD the turning
//rather than performing it - a curved mark is already evidence of spin, the same way
//Drift's wandering curve is evidence of wandering without anything actually moving. The
//persistent canvas is what makes PRISM a painting rather than a visualiser, so nothing
//here animates.
//
//Acoustic -> visual:
//  Arc alignment: rhythmic regularity - a locked pulse produces a clean orderly vortex,//
//    a loose one produces a turbulent, scattered field. THE key mapping here://
//    metronomic music spins tidily, irregular music churns//
//  Arc density: harmonic complexity - complex harmony packs the field more thickly//
//  Arc length: energy at the moment each arc is drawn//
//  Growth: outward from the axis over time, so each field expands as the music plays//
//  Density falloff: thickest near the axis, thinning toward the rim, like a real vortex//
//  Colour: RealtimeColour as each arc is drawn//

public class RotationFieldGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;
    [SerializeField] private float energyThreshold = 0.03f;

    [Header("Field Placement")]
    [SerializeField] private float scatterRadius = 30f;
    [SerializeField] private float maxFieldOffset = 30f;
    //Hard ceiling on separate vortices. Prominence scales how many actually appear.//
    [SerializeField] private int maxFields = 6;
    //How many vortices may be growing at once.//
    [SerializeField] private int maxConcurrent = 2;

    [Header("Vortex Shape")]
    //Radius the field grows out to.//
    [SerializeField] private float minFieldRadius = 8f;
    [SerializeField] private float maxFieldRadius = 18f;
    //Half-height of the cylindrical volume, as a fraction of its radius.//
    [SerializeField, Range(0.1f, 1.5f)] private float heightRatio = 0.55f;
    //Arcs in a completed field.//
    [SerializeField] private int minArcs = 40;
    [SerializeField] private int maxArcs = 200;
    //Seconds a field takes to grow from the axis out to full radius.//
    [SerializeField] private float expansionSeconds = 14f;

    [Header("Arc Shape")]
    //Angular sweep of a single arc, in degrees.//
    [SerializeField] private float minArcSweep = 18f;
    [SerializeField] private float maxArcSweep = 55f;
    //Points along each arc.//
    [SerializeField] private int arcSegments = 7;
    [SerializeField] private float lineWidth = 0.06f;

    [Header("Alignment")]
    //Maximum random tilt applied to an arc's plane when regularity is at its LOWEST.
    //At full regularity arcs sit perfectly in the rotation plane; as the pulse loosens
    //they scatter, so the field visibly churns instead of spinning cleanly.
    [SerializeField] private float maxMisalignmentDeg = 45f;

    [Header("Growth")]
    //Arcs drawn per second at full energy.//
    [SerializeField] private float growthRate = 22f;

    [Header("Appearance")]
    [SerializeField, Range(0f, 1f)] private float opacity = 0.8f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _lineMaterial;

    private bool _active = false;
    private float _debugTimer = 0f;
    private float _growthAccumulator = 0f;
    private int _completedFields = 0;

    private readonly List<Vortex> _growing = new List<Vortex>();

    //One vortex: an axis, a volume, and a target number of arcs to fill it with.//
    private class Vortex
    {
        public Vector3 centre;
        public Vector3 axis;          //everything rotates around this//
        public Vector3 refA, refB;    //orthonormal basis perpendicular to the axis//
        public float radius;
        public float halfHeight;
        public int targetArcs;
        public int drawn;
        public float age;             //seconds since seeded, drives outward growth//
        public float spin;            //+1 or -1: which way this vortex turns//
        public float sizeScale;
    }

    public void SetPrism(PRISMGenerator prism)
    {
        _prism = prism;
    }

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

        Debug.Log("PRISM RotationFieldGenerator: Initialised");
    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightRotationField;
        _active = weight >= activationThreshold;

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.RotationField) : Prominence.Silent;

        if (!_active) return;
        if (profile.RealtimeEnergy < energyThreshold) return;

        int effectiveMax = Mathf.Max(1, Mathf.RoundToInt(maxFields * pr.prominence * pr.prominence));

        //Age the live vortices so their growth radius expands.//
        for (int i = 0; i < _growing.Count; i++)
            _growing[i].age += Time.deltaTime;

        //Draw arcs into whichever vortices are still filling.//
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

        //Seed a new vortex when there's room.//
        int total = _completedFields + _growing.Count;
        if (total < effectiveMax && _growing.Count < maxConcurrent)
            SeedVortex(profile, pr);
    }

    public void Deactivate()
    {
        _active = false;
    }

    private void SeedVortex(TimbralProfile profile, Prominence pr)
    {
        float spread = scatterRadius * Mathf.Lerp(0.4f, 1f, pr.prominence);
        Vector3 fieldCentre = transform.position + Random.onUnitSphere * (maxFieldOffset * (1f - pr.centrality));

        var v = new Vortex();
        v.centre = fieldCentre + Random.onUnitSphere * (spread * Mathf.Pow(Random.value, 0.5f));
        v.axis = Random.onUnitSphere;
        v.sizeScale = Mathf.Lerp(0.55f, 2.5f, pr.prominence);
        v.radius = Mathf.Lerp(minFieldRadius, maxFieldRadius, Random.value) * v.sizeScale;
        v.halfHeight = v.radius * heightRatio;

        //Harmonic complexity packs the field more thickly.//
        v.targetArcs = Mathf.RoundToInt(
            Mathf.Lerp(minArcs, maxArcs, profile.RealtimeHarmonicComplexity) * pr.prominence);
        v.targetArcs = Mathf.Max(minArcs / 2, v.targetArcs);

        v.spin = Random.value < 0.5f ? 1f : -1f;
        v.drawn = 0;
        v.age = 0f;

        //Orthonormal basis perpendicular to the axis, so arcs can be placed in the
        //rotation plane at any angle around it.
        v.refA = Vector3.Cross(v.axis, Vector3.up);
        if (v.refA.sqrMagnitude < 1e-4f) v.refA = Vector3.Cross(v.axis, Vector3.right);
        v.refA.Normalize();
        v.refB = Vector3.Cross(v.axis, v.refA).normalized;

        _growing.Add(v);
    }

    private void DrawArc(Vortex v, TimbralProfile profile, Prominence pr)
    {
        //The field grows outward from the axis: early arcs sit near the centre, later ones
        //further out. Squaring the random sample biases toward the axis so density is
        //thickest at the centre and thins toward the rim, like a real vortex.
        float growth = Mathf.Clamp01(v.age / expansionSeconds);
        float maxR = v.radius * Mathf.Lerp(0.15f, 1f, growth);
        float rNorm = Random.value * Random.value;   //bias toward zero = toward the axis//
        float r = Mathf.Max(0.4f, rNorm * maxR);

        //Position around the axis and along it.//
        float theta = Random.Range(0f, Mathf.PI * 2f);
        float h = Random.Range(-1f, 1f) * v.halfHeight * Mathf.Lerp(0.4f, 1f, growth);

        Vector3 radial = (v.refA * Mathf.Cos(theta) + v.refB * Mathf.Sin(theta)).normalized;
        Vector3 arcCentre = v.centre + radial * r + v.axis * h;

        //Tangent to the rotation at this point - the direction the arc curves along.//
        Vector3 tangent = Vector3.Cross(v.axis, radial).normalized * v.spin;

        //Rhythmic regularity drives alignment. At full regularity the arc sits exactly in
        //the rotation plane; as the pulse loosens the plane is randomly tilted, so the
        //field churns instead of spinning cleanly. This is the mapping that makes a
        //metronomic track produce a tidy vortex and a loose one produce turbulence.
        float misalign = maxMisalignmentDeg * (1f - profile.RealtimeRhythmicRegularity);
        Quaternion wobble = Quaternion.AngleAxis(Random.Range(-misalign, misalign), tangent) *
                            Quaternion.AngleAxis(Random.Range(-misalign, misalign), radial);

        //Arc sweep and length from energy at this moment.//
        float sweep = Mathf.Lerp(minArcSweep, maxArcSweep, profile.RealtimeEnergy) * Mathf.Deg2Rad;
        float arcRadius = Mathf.Max(0.6f, r * Random.Range(0.5f, 1.1f));

        //Build the arc as a short circular segment about the vortex axis, centred on this
        //point, then apply the misalignment wobble.
        Vector3[] pts = new Vector3[arcSegments + 1];
        for (int i = 0; i <= arcSegments; i++)
        {
            float t = (i / (float)arcSegments) - 0.5f;     //-0.5 .. +0.5//
            float a = t * sweep * v.spin;

            //A point on a circle of arcRadius lying in the plane perpendicular to the axis.
            Vector3 local = radial * (Mathf.Cos(a) * arcRadius - arcRadius) +
                            tangent * (Mathf.Sin(a) * arcRadius);

            pts[i] = arcCentre + wobble * local;
        }

        var go = new GameObject("Arc");
        go.transform.SetParent(_root.transform);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.material = _lineMaterial;
        lr.positionCount = pts.Length;
        lr.SetPositions(pts);

        float w = lineWidth * v.sizeScale;
        lr.startWidth = w;
        lr.endWidth = w;
        lr.numCornerVertices = 2;
        lr.numCapVertices = 1;

        //Arcs near the axis run brighter, rim arcs fainter, reinforcing the falloff.//
        Color c = _prism != null ? _prism.RealtimeColour : Color.cyan;
        float radialFade = 1f - Mathf.Clamp01(r / Mathf.Max(v.radius, 0.001f)) * 0.55f;
        c.a = opacity * radialFade;
        lr.startColor = c;
        lr.endColor = c;

        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        v.drawn++;
    }

    private void OnDestroy()
    {
        if (_lineMaterial != null) Destroy(_lineMaterial);
    }
}
