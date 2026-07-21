using System.Collections.Generic;
using UnityEngine;

//DriftGenerator - Kluver Category 2 (Spirals), drift subcategory//

//Grows slow, wandering BRUSHSTROKES that meander through the volume. The loose
//counterpart to SpiralGrowth: where the spiral WINDS - a tight coiling arm with real
//rotational momentum - drift WANDERS, aimless curves with no fixed centre and rotation so
//gentle it barely reads as rotation at all. Both belong to the spiral category; they are
//its tightest and its loosest expressions.
//
//Timbral home (weight = tP * tD * (1-E+0.3)): sustained (not percussive), sparse (not
//relentless), and quiet-leaning - ambient pads, slow passages, the calm spaces of a
//track. Like SpeckCluster it populates stillness, but with wandering line rather than
//scattered points.
//
//THE MARK IS A BRUSHSTROKE, NOT A LINE. The scene is already dense with thin
//LineRenderer marks (spiral, cobweb, zigzag, wavy, grid, tunnel edges), so drift is
//deliberately a different KIND of mark: a broad, soft, slightly transparent stroke with
//visible bristle texture. It is built as a tight bundle of many thin sub-lines sharing
//one wandering spine, each nudged sideways and given its own opacity, so where they
//overlap the stroke is solid and where they splay apart you get the streaks and gaps real
//bristles leave. Each bristle tapers at both ends, so the stroke swells in the middle and
//frays at the tips rather than closing to a clean point. The imperfection is the point:
//it reads as made, not computed.
//
//Acoustic -> visual:
//  Growth: progressive, paced by energy - the stroke is painted, not popped into being//
//  Wander rate: a slow Perlin walk nudging the spine's heading each step//
//  Stroke count: prominence-scaled, up to a safety ceiling - a track that lives in this//
//    timbral home approaches the ceiling; a minor presence gets only a couple//
//  Colour: RealtimeColour, sampled as each stroke is born//

public class DriftGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;
    //Below this energy the stroke pauses rather than painting through true silence.//
    [SerializeField] private float energyThreshold = 0.03f;

    [Header("Field")]
    [SerializeField] private float fieldRadius = 30f;
    //Hard safety ceiling. The number that ACTUALLY appears floats well below this,
    //scaled by prominence, so a track where drift is minor gets a couple of strokes and a
    //track that lives in its timbral home approaches the ceiling. Not a fixed target -
    //the music decides where between "a couple" and "the ceiling" it lands.
    [SerializeField] private int maxStrokes = 50;
    [SerializeField] private float maxFieldOffset = 30f;

    [Header("Stroke Size")]
    //Medium: longer than a zigzag/wavy bundle, shorter than a spiral's sweeping arm.//
    [SerializeField] private float minStrokeLength = 10f;
    [SerializeField] private float maxStrokeLength = 20f;
    //Points appended to the spine per second at full energy (progressive growth).//
    [SerializeField] private float growthRate = 14f;
    //World distance between successive spine points.//
    [SerializeField] private float stepDistance = 0.6f;
    //How many strokes may be growing at once before new seeds wait.//
    [SerializeField] private int maxConcurrent = 2;

    [Header("Wander")]
    //Larger = tighter, twistier wandering. Smaller = broad lazy curves.//
    [SerializeField] private float wanderScale = 0.12f;
    //Maximum heading change per step (radians). Keeps the walk gentle.//
    [SerializeField] private float wanderStrength = 0.25f;

    [Header("Brush")]
    //Bristles per stroke. 8-14 gives a coarse, splayed brush rather than a clean edge.//
    [SerializeField] private int minBristles = 8;
    [SerializeField] private int maxBristles = 14;
    //Half-width of the brush: how far bristles splay from the spine.//
    [SerializeField] private float brushWidth = 0.9f;
    //Thickness of a single bristle line.//
    [SerializeField] private float bristleWidth = 0.09f;
    //Overall stroke opacity (slightly transparent, soft).//
    [SerializeField, Range(0f, 1f)] private float strokeOpacity = 0.55f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _brushMaterial;

    private bool _active = false;
    private float _debugTimer = 0f;
    private float _elapsed = 0f;
    private float _fieldSeed;
    private int _completedCount = 0;

    //Strokes currently growing. Completed strokes are left in the scene and dropped from
    //this list (persistent canvas - nothing is destroyed).//
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

        Debug.Log("PRISM DriftGenerator: Initialised");
    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightDrift;
        _active = weight >= activationThreshold;

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.Drift) : Prominence.Silent;
        _elapsed += Time.deltaTime;

        if (!_active) return;
        //Sustained but not silent: don't paint through dead air.//
        if (profile.RealtimeEnergy < energyThreshold) return;

        //Effective ceiling for THIS track: prominence scales how many actually appear.//
        int effectiveMax = Mathf.Max(2, Mathf.RoundToInt(maxStrokes * pr.prominence * pr.prominence));

        //Grow whatever strokes are currently alive.//
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

        //Seed new strokes while there's room, capped by how many may grow at once.//
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
        //Scatter the origin through a spherical field, positioned by prominence.//
        float spread = fieldRadius * Mathf.Lerp(0.4f, 1f, pr.prominence);
        Vector3 fieldCentre = transform.position + Random.onUnitSphere * (maxFieldOffset * (1f - pr.centrality));
        Vector3 origin = fieldCentre + Random.onUnitSphere * (spread * Mathf.Pow(Random.value, 0.5f));

        float sizeScale = Mathf.Lerp(0.6f, 1f, pr.prominence);
        float length = Mathf.Lerp(minStrokeLength, maxStrokeLength, Random.value) * sizeScale;

        int bristles = Random.Range(minBristles, maxBristles + 1);

        Color colour = _prism != null ? _prism.RealtimeColour : Color.cyan;

        var stroke = new DriftStroke();
        stroke.Init(_root, _brushMaterial, origin, length, bristles, sizeScale,
                    _fieldSeed + _completedCount * 13.7f, //unique wander per stroke//
                    wanderScale, wanderStrength, stepDistance,
                    brushWidth * sizeScale, bristleWidth * sizeScale,
                    strokeOpacity, colour);

        _growing.Add(stroke);
    }

    private void BuildMaterial()
    {
        //Soft additive-ish material so overlapping bristles build up and the stroke reads
        //as painted rather than as a hard ribbon. Sprites/Default respects vertex alpha,
        //which is how the taper and per-bristle opacity come through.
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        _brushMaterial = new Material(shader);
    }

    private void OnDestroy()
    {
        if (_brushMaterial != null) Destroy(_brushMaterial);
    }
}

//A single wandering brushstroke: one Perlin-walked spine rendered as a bundle of tapered,
//offset bristle lines. Grows progressively; persists once complete.
public class DriftStroke
{
    public bool Complete { get; private set; } = false;

    private GameObject _go;
    private LineRenderer[] _bristles;
    private float[] _bristlePhase;      //per-bristle noise seed for its sideways splay//
    private float[] _bristleAlpha;      //per-bristle opacity (some darker, some faint)//
    private float[] _bristleSide;       //per-bristle base offset across the brush//
    private float[] _bristleUp;         //per-bristle base offset perpendicular to that//

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

    //Local frame for laying bristles across the spine's direction of travel.//
    private Vector3 _across;
    private Vector3 _up;

    public void Init(GameObject parent, Material mat, Vector3 origin, float length,
                     int bristleCount, float sizeScale, float noiseSeed,
                     float wanderScale, float wanderStrength, float stepDistance,
                     float brushWidth, float bristleWidth, float strokeOpacity, Color colour)
    {
        _pos = origin;
        _targetLength = length;
        _stepDistance = stepDistance;
        _wanderScale = wanderScale;
        _wanderStrength = wanderStrength;
        _brushWidth = brushWidth;
        _noiseSeed = noiseSeed;
        _colour = colour;
        _strokeOpacity = strokeOpacity;

        //Random initial heading.//
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
            //Spread bristles across the brush width, with a little vertical splay too.//
            float t = bristleCount > 1 ? (i / (float)(bristleCount - 1)) : 0.5f;
            _bristleSide[i] = Mathf.Lerp(-1f, 1f, t) * _brushWidth + Random.Range(-0.1f, 0.1f) * _brushWidth;
            _bristleUp[i] = Random.Range(-0.35f, 0.35f) * _brushWidth;
            _bristlePhase[i] = Random.Range(0f, 100f);
            //Bristles near the centre run fuller; edge bristles fainter. Plus jitter, so
            //some inner bristles are dry and some edge ones catch - the streaky look.//
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

        //Seed the first spine point.//
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
        //Nudge the heading by smooth 3D Perlin noise - a slow, non-repeating wander.//
        float n1 = Mathf.PerlinNoise(_noiseSeed + _pos.x * _wanderScale, _pos.z * _wanderScale) - 0.5f;
        float n2 = Mathf.PerlinNoise(_noiseSeed + 50f + _pos.y * _wanderScale, _pos.x * _wanderScale) - 0.5f;
        float n3 = Mathf.PerlinNoise(_noiseSeed + 99f + _pos.z * _wanderScale, _pos.y * _wanderScale) - 0.5f;

        Vector3 nudge = new Vector3(n1, n2, n3) * 2f * _wanderStrength;
        _heading = (_heading + nudge).normalized;
        RebuildFrame();

        _pos += _heading * _stepDistance;
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

    //Rebuild every bristle line from the current spine. Each bristle follows the spine
    //offset sideways, wavers slightly along its length (bristle drift), and tapers at both
    //ends so the stroke swells in the middle and frays at the tips.
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
                float t = i / (float)(n - 1); //0..1 along the grown spine//

                //Small along-length waver so bristles aren't perfectly parallel.//
                float waver = (Mathf.PerlinNoise(_bristlePhase[b] + t * 4f, _bristlePhase[b] * 0.3f) - 0.5f) * _brushWidth * 0.5f;

                Vector3 sideOffset = _across * (_bristleSide[b] + waver);
                Vector3 upOffset = _up * _bristleUp[b];

                lr.SetPosition(i, _spine[i] + sideOffset + upOffset);
            }

            //Taper at both ends: fat in the middle, thin at head and tail. A gradient
            //keyed on normalised position gives the swell; per-bristle base width varies
            //it slightly so the frayed ends aren't uniform.
            var widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.15f),
                new Keyframe(0.5f, 1f),
                new Keyframe(1f, 0.15f));
            lr.widthCurve = widthCurve;

            Color c = _colour;
            c.a = _strokeOpacity * _bristleAlpha[b];
            lr.startColor = c;
            lr.endColor = c;
        }
    }
}
