using System.Collections.Generic;
using UnityEngine;

//ZigzagParallelGenerator//
//Klüver Category 5 (Parallel Figures)//
//Emits zigzag parallel strokes fanning from the flat mouth slot, Triggered by percussiveness x rhythmic regularity x sqrt(flatness)//
public class ZigzagParallelGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;

    [Header("Field")]
    [SerializeField] private float fieldRadius = 30f;
    [SerializeField] private int maxStrokes = 1500;

    [Header("Stroke Shape")]
    [SerializeField] private float strokeLength = 40f;
    [SerializeField] private int zigSegments = 5;
    [SerializeField] private float lineSpacing = 0.4f;
    [SerializeField] private float minAmplitude = 0.3f;
    [SerializeField] private float maxAmplitude = 1.6f;
    [SerializeField] private float lineWidth = 0.06f;

    [Header("Direction Field")]
    [SerializeField] private float directionFieldScale = 0.03f;

    [Header("Mouth Arc")]
    [SerializeField] private float arcUp = 55f; 
    [SerializeField] private float arcDown = 8f;   
    [SerializeField] private float arcWidth = 55f;  
    [SerializeField] private float mouthWidth = 22f;
    [SerializeField] private float mouthHeight = 6f;

    [Header("Onset Detection (Dixon 2001)")]
    [SerializeField] private int fluxHistorySize = 43;
    [SerializeField] private float onsetSensitivity = 1.5f;
    [SerializeField] private float refractorySeconds = 0.14f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _lineMaterial;
    private readonly List<float> _fluxHistory = new List<float>();
    private float _timeSinceLastStroke = 0f;
    private int _strokeCount = 0;
    private bool _active = false;
    private float _fieldSeed;

    public void SetPrism(PRISMGenerator prism)
    {
        _prism = prism;
    }
    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("Zigzag_Root");
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.ZigzagParallel);

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        _lineMaterial = new Material(shader);

        _fluxHistory.Clear();
        _timeSinceLastStroke = 0f;
        _strokeCount = 0;
        _active = false;
        _fieldSeed = Random.Range(0f, 1000f);

    }
    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightZigzagParallel;
        _active = weight >= activationThreshold;

        _timeSinceLastStroke += Time.deltaTime;

        float flux = profile.RealtimeFluxRaw;
        PushFlux(flux);

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.ZigzagParallel) : Prominence.Silent;

        if (!_active) return;
        if (_strokeCount >= maxStrokes) return;
        if (_timeSinceLastStroke < refractorySeconds) return;
        if (!IsOnset(flux)) return;

        SpawnStroke(profile, weight, pr);
        _timeSinceLastStroke = 0f;
    }
    public void Deactivate()
    {
        _active = false;
    }

    private void PushFlux(float flux)
    {
        _fluxHistory.Add(flux);
        if (_fluxHistory.Count > fluxHistorySize) _fluxHistory.RemoveAt(0);
    }

    private bool IsOnset(float flux)
    {
        if (_fluxHistory.Count < fluxHistorySize / 2) return false;
        float sum = 0f;
        for (int i = 0; i < _fluxHistory.Count; i++) sum += _fluxHistory[i];
        float avg = sum / _fluxHistory.Count;
        if (avg < 1e-6f) return false;
        return flux > avg * onsetSensitivity;
    }

    private void SpawnStroke(TimbralProfile profile, float weight, Prominence pr)
    {
        Vector3 pos, dir;
        MouthEmission(pr, out pos, out dir);

        Vector3 across = Vector3.Cross(dir, Vector3.up);
        if (across.sqrMagnitude < 1e-4f) across = Vector3.Cross(dir, Vector3.right);
        across.Normalize();
        Vector3 kick = Vector3.Cross(dir, across).normalized; 

        int lineCount = Mathf.RoundToInt(Mathf.Lerp(2f, 5f, weight));
        lineCount = Mathf.Clamp(lineCount, 2, 5);

        float amplitude = Mathf.Lerp(minAmplitude, maxAmplitude, profile.RealtimePercussiveRatio);

        float length = strokeLength;
        float amp = amplitude;
        float widthScale = Mathf.Lerp(0.6f, 1.4f, pr.prominence);

        Color colour = _prism != null ? _prism.RealtimeColour : Color.cyan;

        GameObject stroke = new GameObject("ZigStroke");
        stroke.transform.SetParent(_root.transform);

        float bundleWidth = lineSpacing * (lineCount - 1);
        for (int l = 0; l < lineCount; l++)
        {
            float offset = (l * lineSpacing) - bundleWidth * 0.5f;
            Vector3 lineOrigin = pos + across * offset;
            BuildZigLine(stroke, lineOrigin, dir, kick, length, amp, colour, widthScale);
        }

        _strokeCount++;
    }


    private void BuildZigLine(GameObject parent, Vector3 origin, Vector3 dir, Vector3 kick, float length, float amplitude, Color colour, float sizeScale)
    {
        int pts = zigSegments + 1;
        Vector3[] positions = new Vector3[pts];
        float step = length / zigSegments;

        for (int i = 0; i < pts; i++)
        {
            float side = (i % 2 == 0) ? -1f : 1f;
            positions[i] = origin + dir * (step * i) + kick * (amplitude * side);
        }

        GameObject lineGO = new GameObject("ZigLine");
        lineGO.transform.SetParent(parent.transform);
        LineRenderer lr = lineGO.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.material = _lineMaterial;
        lr.positionCount = pts;
        lr.SetPositions(positions);
        float w = lineWidth * sizeScale;
        lr.startWidth = w;
        lr.endWidth = w;
        lr.numCapVertices = 1;
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
            Vector3 fieldCentre = transform.position + Random.onUnitSphere * (fieldRadius * (1f - pr.centrality));
            origin = fieldCentre + Random.onUnitSphere * (spread * Mathf.Pow(Random.value, 0.5f));
            dir = FieldDirection(origin);
            return;
        }

        float n1 = Mathf.PerlinNoise(_fieldSeed + _strokeCount * 0.15f, 0f) * 2f - 1f;
        float n2 = Mathf.PerlinNoise(0f, _fieldSeed + _strokeCount * 0.15f) * 2f - 1f;

        float u = n2; 
        float v = (Mathf.PerlinNoise(_fieldSeed + 50f + _strokeCount * 0.15f, 0f) * 2f - 1f);
        origin = m.position + m.right * (u * mouthWidth * 0.5f) + m.up * (v * mouthHeight * 0.5f);

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