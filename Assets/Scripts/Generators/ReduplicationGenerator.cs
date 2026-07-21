using System.Collections.Generic;
using UnityEngine;

//ReduplicationGenerator - Kluver Category 3 (Lattices, Honeycombs, Gratings), reduplication subcategory//

//Stamps a single simple MOTIF over and over in a straight rank - one copy per beat, each
//slightly larger, rotated and dimmer than the last. The repetition IS the form: not a
//structure built from repeating parts, but one shape recurring, a visual echo.
//
//The fourth expression of Category 3, and distinct from the other three by WHAT REPEATS:
//  Honeycomb   - cells tessellate, sharing edges, building a continuous sheet//
//  GridGrating - frames nest, each stepping outward beyond the last//
//  Filigree    - tracery subdivides, each generation finer than the one before//
//  Reduplication - ONE motif recurs unchanged in series, stamped again and again//
//The first three build something from repetition. This one presents repetition itself.
//
//Timbral home (weight = R * R * tF): metronomic and clean. The regularity term is SQUARED,
//so it only fires when the pulse is genuinely locked rather than merely steady - tight
//electronic programming, precise sequencing. That is the honest mapping: a visual stutter
//for music that repeats itself exactly.
//
//BEAT-DRIVEN VIA THE ANALYSED TIMELINE. This generator stamps on profile.BeatThisFrame -
//the beat timestamps tracked offline by librosa (Ellis 2007) - rather than running its own
//adaptive flux threshold like the older generators do. One source of truth: every stamp
//lands on the same beat the analyser found, not on a per-generator guess about what a beat
//is.
//
//Acoustic -> visual:
//  A copy stamped per tracked beat, so the visual repetition matches the rhythmic one//
//  Motif shape: chosen per rank (triangle/square/pentagon/hexagon), then held for the//
//    whole series - repetition reads within a rank, variety across the canvas//
//  Rank direction and length: seeded per rank, scaled by prominence//
//  Progression: each copy larger, rotated and dimmer, so the series has direction//
//  Colour: RealtimeColour when the rank is born, fading along the series//

public class ReduplicationGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;

    [Header("Field")]
    [SerializeField] private float fieldRadius = 28f;
    [SerializeField] private float maxFieldOffset = 30f;
    //Hard ceiling on total ranks. Prominence scales how many actually appear.//
    [SerializeField] private int maxRanks = 14;

    [Header("Rank")]
    //Copies in a completed rank. Prominence interpolates between these.//
    [SerializeField] private int minCopies = 4;
    [SerializeField] private int maxCopies = 12;
    //World distance between successive stamps along the rank.//
    [SerializeField] private float baseSpacing = 3.2f;
    //Spacing grows slightly along the rank so the series opens out.//
    [SerializeField, Range(1f, 1.4f)] private float spacingGrowth = 1.06f;

    [Header("Motif")]
    //Simple outlined polygons - the shape is picked per rank then held.//
    [SerializeField] private float baseSize = 1.6f;
    //Each copy is this much larger than the last.//
    [SerializeField, Range(1f, 1.5f)] private float sizeGrowth = 1.12f;
    //Each copy rotates this much further, so the series twists.//
    [SerializeField] private float rotationStep = 14f;
    [SerializeField] private float lineWidth = 0.09f;

    [Header("Fade")]
    //Opacity of the first copy, and of the last. The series dims along its length.//
    [SerializeField, Range(0f, 1f)] private float startOpacity = 0.95f;
    [SerializeField, Range(0f, 1f)] private float endOpacity = 0.25f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _lineMaterial;

    private bool _active = false;
    private float _debugTimer = 0f;
    private int _rankCount = 0;

    //The rank currently being stamped. Completed ranks persist and are dropped.//
    private Rank _current = null;

    private class Rank
    {
        public Vector3 origin;
        public Vector3 direction;     //rank runs along this axis//
        public Quaternion motifPlane; //orientation the motif is drawn in//
        public int sides;             //3=triangle, 4=square, 5=pentagon, 6=hexagon//
        public int targetCopies;
        public int stamped;
        public float sizeScale;
        public float distance;        //accumulated distance along the rank//
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
        _current = null;

        Debug.Log("PRISM ReduplicationGenerator: Initialised");
    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightReduplication;
        _active = weight >= activationThreshold;

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.Reduplication) : Prominence.Silent;

        if (!_active) return;

        int effectiveMax = Mathf.Max(1, Mathf.RoundToInt(maxRanks * pr.prominence * pr.prominence));
        if (_rankCount >= effectiveMax && _current == null) return;

        //One stamp per tracked beat. Using the analysed beat timeline rather than a local
        //onset detector means every generator agrees on where the beats are.
        if (!profile.BeatThisFrame) return;

        if (_current == null)
        {
            if (_rankCount >= effectiveMax) return;
            _current = SeedRank(profile, pr);
        }

        StampCopy(_current);

        if (_current.stamped >= _current.targetCopies)
        {
            _rankCount++;
            _current = null;   //completed rank persists; next beat starts a new one//
        }
    }

    public void Deactivate()
    {
        _active = false;
    }

    private Rank SeedRank(TimbralProfile profile, Prominence pr)
    {
        float spread = fieldRadius * Mathf.Lerp(0.4f, 1f, pr.prominence);
        Vector3 fieldCentre = transform.position + Random.onUnitSphere * (maxFieldOffset * (1f - pr.centrality));

        var r = new Rank();
        r.origin = fieldCentre + Random.onUnitSphere * (spread * Mathf.Pow(Random.value, 0.5f));
        r.direction = Random.onUnitSphere;
        //The motif sits in a plane facing along the rank, so copies read as a receding
        //series rather than edge-on slivers.
        r.motifPlane = Quaternion.LookRotation(r.direction, Random.onUnitSphere);
        r.sides = Random.Range(3, 7);   //triangle through hexagon//
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
        //Progression along the series: each copy larger, further rotated, dimmer.//
        int i = r.stamped;
        float size = baseSize * r.sizeScale * Mathf.Pow(sizeGrowth, i);
        float roll = rotationStep * i;

        Vector3 centre = r.origin + r.direction * r.distance;

        //Advance for the next copy, with spacing opening out slightly.//
        r.distance += r.spacing;
        r.spacing *= spacingGrowth;

        //Fade along the series so it reads as a sequence with direction.//
        float t = r.targetCopies > 1 ? i / (float)(r.targetCopies - 1) : 0f;
        Color c = r.colour;
        c.a = Mathf.Lerp(startOpacity, endOpacity, t);

        BuildPolygon(centre, r.motifPlane, roll, r.sides, size, c, r.sizeScale);
        r.stamped++;
    }

    //A single outlined regular polygon, drawn as a closed LineRenderer.//
    private void BuildPolygon(Vector3 centre, Quaternion plane, float rollDeg,
                              int sides, float size, Color colour, float sizeScale)
    {
        var go = new GameObject("Motif");
        go.transform.SetParent(_root.transform);

        //Roll the motif within its own plane so successive copies twist.//
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
        lr.loop = true;              //closed outline//
        lr.material = _lineMaterial;
        lr.positionCount = sides;
        lr.SetPositions(pts);

        float w = lineWidth * sizeScale;
        lr.startWidth = w;
        lr.endWidth = w;
        lr.numCornerVertices = 0;    //crisp corners - these are hard geometric stamps//
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
