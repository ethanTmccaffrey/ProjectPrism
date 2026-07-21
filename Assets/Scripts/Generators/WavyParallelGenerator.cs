using System.Collections.Generic;
using UnityEngine;

//WavyParallelGenerator - Kluver Category 5 (Parallel Figures), wavy subcategory//

//Lays down long bundles of parallel UNDULATING lines that flow through the volume - the
//smooth counterpart to ZigzagParallel. The parallel-ness is the category identity; the
//difference between the two generators is character, and that difference is deliberate
//and structural, not cosmetic:
//
//  ZigzagParallel STABS short angular bundles on the beat. Its home is percussive and
//  rough - it reacts to hits.
//  WavyParallel GROWS long flowing bundles continuously while the music sustains. Its
//  home is tonal and sustained - it responds to duration, not attack.
//
//One is jagged and event-driven; the other is smooth and time-driven. Placed in the same
//Kluver category, they are the angular and flowing poles of "parallel figures", which is
//exactly the kind of contrast the embodiment thesis rests on: the same abstract form
//constant rendered two ways by two different kinds of sound.
//
//Timbral home (weight = tF * tP * tD): tonal (not noisy), sustained (not percussive),
//and breathing (not relentless) - folk, strings, ambient, clean sustained passages.
//
//Acoustic -> visual:
//  Growth: continuous while active, paced by energy - flowing music keeps drawing//
//  Wave amplitude: harmonic complexity (richer harmony = deeper undulation)//
//  Wave wavelength: rhythmic regularity (steadier pulse = more even waves)//
//  Bundle thickness: prominence//
//  Colour: RealtimeColour at each bundle's birth//
//  Direction: a Perlin field, so nearby bundles flow in aligned currents//

public class WavyParallelGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;
    //Below this energy the generator idles rather than drawing dead-straight lines in
    //silence (a sustained generator would otherwise keep growing through quiet gaps).
    [SerializeField] private float energyThreshold = 0.04f;

    [Header("Field")]
    [SerializeField] private float fieldRadius = 30f;
    [SerializeField] private int maxBundles = 400;
    [SerializeField] private float fallbackTrackSeconds = 180f;

    [Header("Bundle Shape")]
    //Length of a bundle along its flow direction//
    [SerializeField] private float bundleLength = 12f;
    //Points sampled along each line (higher = smoother curve)//
    [SerializeField] private int lineResolution = 32;
    //Number of parallel lines in a bundle (scaled up by prominence)//
    [SerializeField] private int minLines = 2;
    [SerializeField] private int maxLines = 6;
    //Spacing between the parallel lines//
    [SerializeField] private float lineSpacing = 0.5f;
    //Undulation depth range, scaled by harmonic complexity//
    [SerializeField] private float minAmplitude = 0.4f;
    [SerializeField] private float maxAmplitude = 2.4f;
    //How many full waves fit along a bundle's length at minimum/maximum regularity//
    [SerializeField] private float minWaves = 1.2f;
    [SerializeField] private float maxWaves = 3.5f;
    [SerializeField] private float lineWidth = 0.07f;

    [Header("Growth")]
    //Bundles laid per second at full energy. Sustained music draws steadily.//
    [SerializeField] private float growthRate = 2.5f;

    [Header("Direction Field")]
    //Larger = broader aligned currents (smoother flow). Smaller = more local variation.//
    [SerializeField] private float directionFieldScale = 0.025f;

    [Header("Placement")]
    [SerializeField] private float maxFieldOffset = 30f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _lineMaterial;

    private bool _active = false;
    private bool _seeded = false;
    private float _debugTimer = 0f;
    private float _growthAccumulator = 0f;
    private float _elapsed = 0f;
    private float _fieldSeed;
    private Vector3 _fieldCentre;
    private int _bundleCount = 0;

    public void SetPrism(PRISMGenerator prism)
    {
        _prism = prism;
    }

    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("WavyParallel_Root");
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.WavyParallel);

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        _lineMaterial = new Material(shader);

        _active = false;
        _seeded = false;
        _growthAccumulator = 0f;
        _elapsed = 0f;
        _bundleCount = 0;
        _fieldSeed = Random.Range(0f, 1000f);

        Debug.Log("PRISM WavyParallelGenerator: Initialised");
    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightWavyParallel;
        _active = weight >= activationThreshold;

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.WavyParallel) : Prominence.Silent;
        _elapsed += Time.deltaTime;

        if (!_active) return;
        //Sustained music, but not silence: don't draw straight lines through quiet gaps.//
        if (profile.RealtimeEnergy < energyThreshold) return;
        if (_bundleCount >= maxBundles) return;

        if (!_seeded) SeedField(pr);

        //Pace the whole track so the field fills as the song plays, rather than saturating//
        //early. Mirrors the growth cap used by the other continuous generators.//
        float trackLength = (_prism != null && _prism.TrackLength > 1f) ? _prism.TrackLength : fallbackTrackSeconds;
        int effectiveMax = Mathf.Max(4, Mathf.RoundToInt(maxBundles * pr.prominence * pr.prominence));
        float progress = Mathf.Clamp01(_elapsed / trackLength);
        int allowedByNow = Mathf.CeilToInt(progress * effectiveMax);
        if (_bundleCount >= allowedByNow) return;
        if (_bundleCount >= effectiveMax) return;

        //Continuous growth: accumulate ticks scaled by energy, lay that many bundles.//
        _growthAccumulator += growthRate * profile.RealtimeEnergy * Time.deltaTime;
        int ticks = Mathf.FloorToInt(_growthAccumulator);
        if (ticks <= 0) return;
        _growthAccumulator -= ticks;

        for (int i = 0; i < ticks && _bundleCount < effectiveMax; i++)
            SpawnBundle(profile, pr);
    }

    public void Deactivate()
    {
        _active = false;
    }

    private void SeedField(Prominence pr)
    {
        _fieldCentre = transform.position + Random.onUnitSphere * (maxFieldOffset * (1f - pr.centrality));
        _seeded = true;
    }

    private void SpawnBundle(TimbralProfile profile, Prominence pr)
    {
        //Scattered position in a spherical field, scaled/positioned by prominence.//
        float spread = fieldRadius * Mathf.Lerp(0.35f, 1f, pr.prominence);
        Vector3 pos = _fieldCentre + Random.onUnitSphere * (spread * Mathf.Pow(Random.value, 0.5f));

        //Flow direction from the Perlin field: nearby bundles align into currents.//
        Vector3 dir = FieldDirection(pos);

        //An axis to lay the parallel lines along, and one to undulate along.//
        Vector3 across = Vector3.Cross(dir, Vector3.up);
        if (across.sqrMagnitude < 1e-4f) across = Vector3.Cross(dir, Vector3.right);
        across.Normalize();
        Vector3 wave = Vector3.Cross(dir, across).normalized; //undulation displacement axis//

        //Line count from prominence (dominant = thicker bundle).//
        int lineCount = Mathf.RoundToInt(Mathf.Lerp(minLines, maxLines, pr.prominence));
        lineCount = Mathf.Clamp(lineCount, minLines, maxLines);

        //Undulation depth from harmonic complexity: richer harmony undulates deeper.//
        float amplitude = Mathf.Lerp(minAmplitude, maxAmplitude, profile.RealtimeHarmonicComplexity);

        //Wavelength from rhythmic regularity: a steadier pulse gives more even, regular//
        //waves; a loose pulse gives fewer, longer undulations.//
        float waves = Mathf.Lerp(minWaves, maxWaves, profile.RealtimeRhythmicRegularity);

        //Overall size from prominence.//
        float sizeScale = Mathf.Lerp(0.5f, 1f, pr.prominence);
        float length = bundleLength * sizeScale;
        float amp = amplitude * sizeScale;

        //A random phase offset so bundles don't all undulate in lockstep.//
        float phase = Random.Range(0f, Mathf.PI * 2f);

        Color colour = _prism != null ? _prism.RealtimeColour : Color.cyan;

        GameObject bundle = new GameObject("WavyBundle");
        bundle.transform.SetParent(_root.transform);

        float bundleWidth = lineSpacing * (lineCount - 1);
        for (int l = 0; l < lineCount; l++)
        {
            float offset = (l * lineSpacing) - bundleWidth * 0.5f;
            Vector3 lineOrigin = pos + across * offset;
            BuildWaveLine(bundle, lineOrigin, dir, wave, length, amp, waves, phase, colour, sizeScale);
        }

        _bundleCount++;
    }

    //A single smooth undulating polyline as a LineRenderer.//
    private void BuildWaveLine(GameObject parent, Vector3 origin, Vector3 dir, Vector3 wave,
                               float length, float amplitude, float waves, float phase,
                               Color colour, float sizeScale)
    {
        int pts = lineResolution + 1;
        Vector3[] positions = new Vector3[pts];

        for (int i = 0; i < pts; i++)
        {
            float t = i / (float)lineResolution;         //0..1 along the bundle//
            //Smooth sine undulation - the defining contrast with Zigzag's hard kinks.//
            float displacement = Mathf.Sin(phase + t * waves * Mathf.PI * 2f) * amplitude;
            positions[i] = origin + dir * (length * t) + wave * displacement;
        }

        GameObject lineGO = new GameObject("WaveLine");
        lineGO.transform.SetParent(parent.transform);
        LineRenderer lr = lineGO.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.material = _lineMaterial;
        lr.positionCount = pts;
        lr.SetPositions(positions);

        float w = lineWidth * sizeScale;
        lr.startWidth = w;
        lr.endWidth = w;
        lr.numCornerVertices = 2; //rounded corners keep the curve reading as smooth//
        lr.numCapVertices = 2;
        lr.startColor = colour;
        lr.endColor = colour;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
    }

    //Perlin-based direction field: smoothly varying unit vectors across the volume, so
    //nearby bundles flow in aligned currents while distant ones diverge. Shared design
    //with ZigzagParallel so the two Category 5 generators sit in the same kind of space.
    private Vector3 FieldDirection(Vector3 p)
    {
        float nx = Mathf.PerlinNoise(_fieldSeed + p.x * directionFieldScale, p.z * directionFieldScale);
        float ny = Mathf.PerlinNoise(_fieldSeed + p.y * directionFieldScale, p.x * directionFieldScale);
        float nz = Mathf.PerlinNoise(_fieldSeed + p.z * directionFieldScale, p.y * directionFieldScale);

        Vector3 dir = new Vector3(nx * 2f - 1f, ny * 2f - 1f, nz * 2f - 1f);
        if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
        return dir.normalized;
    }

    private void OnDestroy()
    {
        if (_lineMaterial != null) Destroy(_lineMaterial);
    }
}
