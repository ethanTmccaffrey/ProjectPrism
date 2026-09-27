using System.Collections.Generic;
using UnityEngine;

//RadiationBurstGenerator//
//Klüver Category 4 (Cobwebs / Radial Forms)//
//Fires radial bursts outward on transients, Triggered by percussiveness x rhythmic irregularity x energy//

public class RadiationBurstGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;

    [Header("Field")]
    [SerializeField] private float fieldRadius = 30f;
    [SerializeField] private int maxBursts = 120;

    [Header("Burst Shape")]
    [SerializeField] private int minRays = 8;
    [SerializeField] private int maxRays = 28;
    [SerializeField] private float minRayLength = 3f;
    [SerializeField] private float maxRayLength = 12f;
    [SerializeField] private int raySegments = 5;
    [SerializeField] private float raggedness = 0.35f;
    [SerializeField] private float lineWidth = 0.08f;

    [Header("Placement")]
    [SerializeField] private float maxFieldOffset = 30f;
    [SerializeField] private HeadPlacement placement;

    [Header("Onset Detection (Dixon 2001)")]
    [SerializeField] private int fluxHistorySize = 43;
    [SerializeField] private float onsetSensitivity = 2.2f;
    [SerializeField] private float refractorySeconds = 0.35f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _lineMaterial;
    private readonly List<float> _fluxHistory = new List<float>();
    private float _timeSinceLastBurst = 0f;
    private int _burstCount = 0;
    private bool _active = false;


    public void SetPrism(PRISMGenerator prism)
    {
        _prism = prism;
    }

    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("RadiationBurst_Root");
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.RadiationBurst);

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        _lineMaterial = new Material(shader);

        _fluxHistory.Clear();
        _timeSinceLastBurst = 0f;
        _burstCount = 0;
        _active = false;

    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightRadiationBurst;
        _active = weight >= activationThreshold;

        _timeSinceLastBurst += Time.deltaTime;

        float flux = profile.RealtimeFluxRaw;
        PushFlux(flux);

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.RadiationBurst) : Prominence.Silent;

        if (!_active) return;
        if (_burstCount >= maxBursts) return;
        if (_timeSinceLastBurst < refractorySeconds) return;
        if (!IsOnset(flux, out float strength)) return;

        SpawnBurst(profile, weight, pr, strength);
        _timeSinceLastBurst = 0f;
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

    private bool IsOnset(float flux, out float strength)
    {
        strength = 0f;
        if (_fluxHistory.Count < fluxHistorySize / 2) return false;

        float sum = 0f;
        for (int i = 0; i < _fluxHistory.Count; i++) sum += _fluxHistory[i];
        float avg = sum / _fluxHistory.Count;
        if (avg < 1e-6f) return false;

        float ratio = flux / avg;
        if (ratio < onsetSensitivity) return false;

        strength = Mathf.Clamp01((ratio - onsetSensitivity) / onsetSensitivity);
        return true;
    }

    private void SpawnBurst(TimbralProfile profile, float weight, Prominence pr, float strength)
    {
        Vector3 origin;
        if (placement != null && placement.Ready)
        {
            origin = placement.RandomInEllipsoid(Mathf.Lerp(0.4f, 0.85f, pr.prominence));
        }
        else
        {
            float spread = fieldRadius * Mathf.Lerp(0.35f, 1f, pr.prominence);
            Vector3 fieldCentre = transform.position + Random.onUnitSphere * (maxFieldOffset * (1f - pr.centrality));
            origin = fieldCentre + Random.onUnitSphere * (spread * Mathf.Pow(Random.value, 0.333f));
        }

        int rayCount = Mathf.RoundToInt(Mathf.Lerp(minRays, maxRays, weight));

        float sizeScale = Mathf.Lerp(0.5f, 1f, pr.prominence);
        float length = Mathf.Lerp(minRayLength, maxRayLength, strength) * sizeScale;

        float jag = raggedness * profile.RealtimePercussiveRatio;

        Color colour = _prism != null ? _prism.RealtimeColour : Color.white;

        GameObject burst = new GameObject("Burst");
        burst.transform.SetParent(_root.transform);
        burst.transform.position = origin;

        for (int i = 0; i < rayCount; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            float rayLen = length * Random.Range(0.6f, 1.3f);
            BuildRay(burst, origin, dir, rayLen, jag, colour, sizeScale, placement);
        }

        _burstCount++;
    }

    private void BuildRay(GameObject parent, Vector3 origin, Vector3 dir, float length, float jag, Color colour, float sizeScale, HeadPlacement placement)
    {
        float step = length / raySegments;

        Vector3 perpA = Vector3.Cross(dir, Vector3.up);
        if (perpA.sqrMagnitude < 1e-4f) perpA = Vector3.Cross(dir, Vector3.right);
        perpA.Normalize();
        Vector3 perpB = Vector3.Cross(dir, perpA).normalized;

        List<Vector3> built = new List<Vector3>(raySegments + 1);
        for (int i = 0; i <= raySegments; i++)
        {
            float t = i / (float)raySegments;
            float dev = jag * step * t;
            Vector3 wobble = perpA * Random.Range(-dev, dev) + perpB * Random.Range(-dev, dev);
            Vector3 p = origin + dir * (step * i) + wobble;

            if (i > 0 && placement != null && placement.Ready && !placement.InEllipsoid(p)) break;

            built.Add(p);
        }

        if (built.Count < 2) return;

        GameObject rayGO = new GameObject("Ray");
        rayGO.transform.SetParent(parent.transform);
        LineRenderer lr = rayGO.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.material = _lineMaterial;
        lr.positionCount = built.Count;
        lr.SetPositions(built.ToArray());


        float w = lineWidth * sizeScale;
        lr.startWidth = w;
        lr.endWidth = w * 0.2f;
        lr.numCapVertices = 1;
        lr.startColor = colour;
        Color endCol = colour; endCol.a = 0.3f;
        lr.endColor = endCol;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
    }

    private void OnDestroy()
    {
        if (_lineMaterial != null) Destroy(_lineMaterial);
    }
}