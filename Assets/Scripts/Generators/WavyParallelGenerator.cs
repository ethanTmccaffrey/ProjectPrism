using System.Collections.Generic;
using UnityEngine;

//WavyParallelGenerator//
//Klüver Category 5 (Parallel Figures)//
//Emits wavy parallel strokes fanning from the flat mouth slot, Triggered by tonality x sustain x sparseness//

public class WavyParallelGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;
    [SerializeField] private float energyThreshold = 0.04f;

    [Header("Field")]
    [SerializeField] private float fieldRadius = 30f;
    [SerializeField] private int maxBundles = 400;
    [SerializeField] private float fallbackTrackSeconds = 180f;

    [Header("Bundle Shape")]
    [SerializeField] private float bundleLength = 50f;
    [SerializeField] private int lineResolution = 32;
    [SerializeField] private int minLines = 2;
    [SerializeField] private int maxLines = 6;
    [SerializeField] private float lineSpacing = 0.5f;
    [SerializeField] private float minAmplitude = 0.4f;
    [SerializeField] private float maxAmplitude = 2.4f;
    [SerializeField] private float minWaves = 1.2f;
    [SerializeField] private float maxWaves = 3.5f;
    [SerializeField] private float lineWidth = 0.07f;

    [Header("Growth")]
    [SerializeField] private float growthRate = 2.5f;

    [Header("Direction Field")]
    [SerializeField] private float directionFieldScale = 0.025f;

    [Header("Mouth Arc")]
    [SerializeField] private float arcUp = 55f;     
    [SerializeField] private float arcDown = 8f;   
    [SerializeField] private float arcWidth = 55f;   
    [SerializeField] private float mouthWidth = 22f;
    [SerializeField] private float mouthHeight = 6f;

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

    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightWavyParallel;
        _active = weight >= activationThreshold;

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.WavyParallel) : Prominence.Silent;
        _elapsed += Time.deltaTime;

        if (!_active) return;
        if (profile.RealtimeEnergy < energyThreshold) return;
        if (_bundleCount >= maxBundles) return;

        if (!_seeded) SeedField(pr);

        float trackLength = (_prism != null && _prism.TrackLength > 1f) ? _prism.TrackLength : fallbackTrackSeconds;
        int effectiveMax = Mathf.Max(4, Mathf.RoundToInt(maxBundles * pr.prominence * pr.prominence));
        float progress = Mathf.Clamp01(_elapsed / trackLength);
        int allowedByNow = Mathf.CeilToInt(progress * effectiveMax);
        if (_bundleCount >= allowedByNow) return;
        if (_bundleCount >= effectiveMax) return;

        _growthAccumulator += growthRate * profile.RealtimeEnergy * Time.deltaTime;
        int ticks = Mathf.FloorToInt(_growthAccumulator);
        if (ticks <= 0) return;
        _growthAccumulator -= ticks;

        for (int i = 0; i < ticks && _bundleCount < effectiveMax; i++)
        {
            SpawnBundle(profile, pr);
        }
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
        Vector3 pos, dir;
        MouthEmission(pr, out pos, out dir);

        Vector3 across = Vector3.Cross(dir, Vector3.up);
        if (across.sqrMagnitude < 1e-4f) across = Vector3.Cross(dir, Vector3.right);
        across.Normalize();
        Vector3 wave = Vector3.Cross(dir, across).normalized; 

        int lineCount = Mathf.RoundToInt(Mathf.Lerp(minLines, maxLines, pr.prominence));
        lineCount = Mathf.Clamp(lineCount, minLines, maxLines);

        float amplitude = Mathf.Lerp(minAmplitude, maxAmplitude, profile.RealtimeHarmonicComplexity);

        float waves = Mathf.Lerp(minWaves, maxWaves, profile.RealtimeRhythmicRegularity);

        float length = bundleLength;
        float amp = amplitude;
        float widthScale = Mathf.Lerp(0.6f, 1.4f, pr.prominence);

        float phase = Random.Range(0f, Mathf.PI * 2f);

        Color colour = _prism != null ? _prism.RealtimeColour : Color.cyan;

        GameObject bundle = new GameObject("WavyBundle");
        bundle.transform.SetParent(_root.transform);

        float bundleWidth = lineSpacing * (lineCount - 1);
        for (int l = 0; l < lineCount; l++)
        {
            float offset = (l * lineSpacing) - bundleWidth * 0.5f;
            Vector3 lineOrigin = pos + across * offset;
            BuildWaveLine(bundle, lineOrigin, dir, wave, length, amp, waves, phase, colour, widthScale);
        }

        _bundleCount++;
    }

    private void BuildWaveLine(GameObject parent, Vector3 origin, Vector3 dir, Vector3 wave, float length, float amplitude, float waves, float phase, Color colour, float sizeScale)
    {
        int pts = lineResolution + 1;
        Vector3[] positions = new Vector3[pts];

        for (int i = 0; i < pts; i++)
        {
            float t = i / (float)lineResolution; 
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
        lr.numCornerVertices = 2; 
        lr.numCapVertices = 2;
        lr.startColor = colour;
        lr.endColor = colour;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
    }
    private void MouthEmission(Prominence pr, out Vector3 origin, out Vector3 dir)
    {
        Transform m = _prism != null ? _prism.MouthAnchor : null;

        if (m == null)
        {
            float spread = fieldRadius * Mathf.Lerp(0.35f, 1f, pr.prominence);
            origin = _fieldCentre + Random.onUnitSphere * (spread * Mathf.Pow(Random.value, 0.5f));
            dir = FieldDirection(origin);
            return;
        }

        float n1 = Mathf.PerlinNoise(_fieldSeed + _bundleCount * 0.12f, 0f) * 2f - 1f;
        float n2 = Mathf.PerlinNoise(0f, _fieldSeed + _bundleCount * 0.12f) * 2f - 1f;

        float u = n2;
        float v = (Mathf.PerlinNoise(_fieldSeed + 50f + _bundleCount * 0.12f, 0f) * 2f - 1f);
        origin = m.position + m.right * (u * mouthWidth * 0.5f)  + m.up * (v * mouthHeight * 0.5f);

        float up = Mathf.Lerp(-arcDown, arcUp, (n1 * 0.5f + 0.5f));
        float side = u * arcWidth;

        Quaternion rot = Quaternion.AngleAxis(side, m.up) * Quaternion.AngleAxis(-up, m.right);
        dir = (rot * m.forward).normalized;
    }

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
