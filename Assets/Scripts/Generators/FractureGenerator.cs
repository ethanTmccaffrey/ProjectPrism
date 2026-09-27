using System.Collections.Generic;
using UnityEngine;

//FractureGenerator//
//Klüver Category 4 (Cobwebs / Radial Forms)//
//Grows a jagged emissive crack painted across the outer skull surface, Triggered by percussiveness x rhythmic irregularity//
public class FractureGenerator : MonoBehaviour
{
    private PRISMGenerator _prism;

    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;

    [Header("Crack Growth")]
    [SerializeField] private int maxCracks = 12;
    [SerializeField] private int minSegments = 20;
    [SerializeField] private int maxSegments = 40;
    [SerializeField] private float segmentLength = 8f;
    [SerializeField] private float maxKinkDegrees = 28f;
    [SerializeField, Range(0f, 0.5f)] private float forkChance = 0.1f;
    [SerializeField] private int segmentsPerFrame = 2;
    [SerializeField] private int maxConcurrentGrowing = 8;

    [Header("Emissive Seam")]
    [SerializeField, Range(0f, 1f)] private float paintBrightness = 1f;
    [SerializeField] private float seamRadius = 0.8f;
    [SerializeField] private float seamSpacing = 0.6f;

    [Header("Placement")]
    [SerializeField] private HeadPlacement placement;

    [Header("Onset Detection (Dixon 2001)")]
    [SerializeField] private int fluxHistorySize = 43;
    [SerializeField] private float onsetSensitivity = 1.6f;
    [SerializeField] private float refactorSeconds = 0.5f;

    private GameObject _root;
    private readonly List<float> _fluxHistory = new List<float>();
    private float _timeSinceLastCrack = 0f;
    private int _crackCount = 0;
    private bool _active = false;


    public void SetPrism(PRISMGenerator prism) { _prism = prism; }

    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("Fracture_Root");
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.Fracture);
        _fluxHistory.Clear();
        _timeSinceLastCrack = 0f;
        _crackCount = 0;
        _growing.Clear();
        _active = false;

    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightFracture;
        _active = weight >= activationThreshold;

        _timeSinceLastCrack += Time.deltaTime;

        float flux = profile.RealtimeFluxRaw;
        PushFlux(flux);

        StepGrowingCracks();

        if (!_active) return;
        if (_crackCount >= maxCracks) return;
        if (_timeSinceLastCrack < refactorSeconds) return;
        if (!IsOnset(flux)) return;

        GrowCrack(profile);
        _timeSinceLastCrack = 0f;
    }

    public void Deactivate() { _active = false; }

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

    private void GrowCrack(TimbralProfile profile)
    {
        if (placement == null || !placement.HasSkullMesh) return;

        Vector3 start = Vector3.zero, startNormal = Vector3.up;
        float bestDist = -1f;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (!placement.RandomSkullMeshPoint(out Vector3 s, out Vector3 n)) break;
            float dist = (s - placement.CavityCentre).magnitude;
            if (dist > bestDist) { bestDist = dist; start = s; startNormal = n; }
        }
        if (bestDist < 0f) return;

        Vector3 outward = (start - placement.CavityCentre).normalized;
        if (Vector3.Dot(startNormal, outward) < 0f) startNormal = -startNormal;
        if (Vector3.Dot(startNormal, outward) < 0.3f) startNormal = outward;

        Color colour = _prism != null ? _prism.RealtimeColour : new Color(1f, 0.6f, 0.2f);
        Color hot = colour * paintBrightness;
        hot.a = 1f;

        int segs = Random.Range(minSegments, maxSegments + 1);

        Vector3 tangent = Vector3.Cross(startNormal, Random.onUnitSphere).normalized;
        if (tangent.sqrMagnitude < 1e-4f) tangent = Vector3.Cross(startNormal, Vector3.up).normalized;

        _growing.Add(new GrowingCrack { pos = start, travel = tangent, remaining = segs, hot = hot });
        _crackCount++;
    }

    private class GrowingCrack
    {
        public Vector3 pos;
        public Vector3 travel;
        public int remaining;
        public Color hot;
    }
    private readonly List<GrowingCrack> _growing = new List<GrowingCrack>();

    private void StepGrowingCracks()
    {
        if (_growing.Count == 0) return;

        for (int c = _growing.Count - 1; c >= 0; c--)
        {
            GrowingCrack g = _growing[c];
            int thisFrame = Mathf.Min(segmentsPerFrame, g.remaining);

            for (int i = 0; i < thisFrame; i++)
            {
                placement.NearestSkullMeshPoint(g.pos, out Vector3 surf, out Vector3 normal);

                float kink = Random.Range(-maxKinkDegrees, maxKinkDegrees);
                g.travel = Quaternion.AngleAxis(kink, normal) * g.travel;
                g.travel = Vector3.ProjectOnPlane(g.travel, normal).normalized;
                if (g.travel.sqrMagnitude < 1e-4f) { g.remaining = 0; break; }

                Vector3 next = surf + g.travel * segmentLength;
                if (placement.NearestSkullMeshPoint(next, out Vector3 nextSurf, out _))
                {
                    next = nextSurf;
                }

                if ((next - surf).sqrMagnitude < (segmentLength * 0.25f) * (segmentLength * 0.25f))
                {
                    next = surf + g.travel * segmentLength;
                }

                PaintSeam(surf, next, g.hot);

                Vector3 moved = next - surf;
                if (moved.sqrMagnitude > 1e-4f) g.travel = moved.normalized;
                g.pos = next;

                if (Random.value < forkChance && g.remaining > 6 && _growing.Count < maxConcurrentGrowing)
                {
                    Vector3 branchDir = Quaternion.AngleAxis(Random.value < 0.5f ? 55f : -55f, normal) * g.travel;
                    _growing.Add(new GrowingCrack { pos = g.pos, travel = branchDir, remaining = Mathf.Max(4, g.remaining / 3), hot = g.hot });
                }
            }

            g.remaining -= thisFrame;
            if (g.remaining <= 0) _growing.RemoveAt(c);
        }
    }

    private void PaintSeam(Vector3 a, Vector3 b, Color hot)
    {
        float len = Vector3.Distance(a, b);
        int steps = Mathf.Max(2, Mathf.CeilToInt(len / seamSpacing));
        for (int s = 0; s <= steps; s++)
        {
            Vector3 p = Vector3.Lerp(a, b, s / (float)steps);
            placement.PaintSkull(p, seamRadius, hot, 1f);
        }
        placement.ApplySkullPaint();
    }

    private void OnDestroy() { }
}