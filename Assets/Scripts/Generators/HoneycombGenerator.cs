using System.Collections.Generic;
using UnityEngine;

//HoneycombGenerator//
//Klüver Category 3 (Lattices / Honeycombs / Gratings)//
//Paints hexagonal honeycomb edge bands onto the outer skull, Triggered by rhythmic regularity x flatness x onset density//

public class HoneycombGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;

    [Header("Lattice")]
    [SerializeField] private float cellSize = 4f;
    [SerializeField] private int maxCells = 200;
    [SerializeField] private float fallbackTrackSeconds = 180f;

    [Header("Placement")]
    [SerializeField] private HeadPlacement placement;

    [Header("Painting")]
    [SerializeField] private float paintRadius = 0.4f;
    [SerializeField] private float paintSpacing = 0.3f;
    [SerializeField, Range(0f, 1f)] private float paintStrength = 1f;
    [SerializeField, Range(0f, 1f)] private float paintBrightness = 0.7f;

    [Header("Onset Detection (Dixon 2001)")]
    [SerializeField] private int fluxHistorySize = 43;
    [SerializeField] private float onsetSensitivity = 1.5f;
    [SerializeField] private float refractorySeconds = 0.18f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private readonly List<float> _fluxHistory = new List<float>();
    private float _timeSinceLastCell = 0f;
    private float _elapsed = 0f;
    private bool _active = false;


    private readonly HashSet<Vector2Int> _occupied = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> _frontier = new List<Vector2Int>();

    private Vector3 _seedPoint;
    private Vector3 _seedNormal;
    private Vector3 _tangentX, _tangentY;
    private bool _seeded = false;
    private int _cellCount = 0;

    private static readonly Vector2Int[] HexDirs =
    {
        new Vector2Int(1, 0), new Vector2Int(1, -1), new Vector2Int(0, -1),
        new Vector2Int(-1, 0), new Vector2Int(-1, 1), new Vector2Int(0, 1)
    };

    public void SetPrism(PRISMGenerator prism) { _prism = prism; }

    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("Honeycomb_Root");
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.Honeycomb);

        _fluxHistory.Clear();
        _occupied.Clear();
        _frontier.Clear();
        _timeSinceLastCell = 0f;
        _elapsed = 0f;
        _cellCount = 0;
        _seeded = false;
        _active = false;

    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightHoneycomb;
        _active = weight >= activationThreshold;

        _timeSinceLastCell += Time.deltaTime;

        float flux = profile.RealtimeFluxRaw;
        PushFlux(flux);

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.Honeycomb) : Prominence.Silent;

        if (!_active) return;

        int effectiveMax = Mathf.Max(3, Mathf.RoundToInt(maxCells * pr.prominence * pr.prominence));
        if (_cellCount >= effectiveMax) return;

        float trackLength = (_prism != null && _prism.TrackLength > 1f) ? _prism.TrackLength : fallbackTrackSeconds;
        _elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(_elapsed / trackLength);
        int allowedByNow = Mathf.CeilToInt(progress * effectiveMax);
        if (_cellCount >= allowedByNow) return;

        if (_timeSinceLastCell < refractorySeconds) return;
        if (!IsOnset(flux)) return;

        AddCell(profile, pr);
        _timeSinceLastCell = 0f;
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

    private void AddCell(TimbralProfile profile, Prominence pr)
    {
        if (!_seeded)
        {
            if (placement != null && placement.HasSkullMesh &&
                placement.RandomSkullMeshPoint(out _seedPoint, out _seedNormal))
            {
                _tangentX = Vector3.Cross(_seedNormal, Vector3.up);
                if (_tangentX.sqrMagnitude < 1e-4f) _tangentX = Vector3.Cross(_seedNormal, Vector3.right);
                _tangentX.Normalize();
                _tangentY = Vector3.Cross(_seedNormal, _tangentX).normalized;
            }
            else
            {
                _seedPoint = transform.position;
                _seedNormal = Vector3.forward;
                _tangentX = Vector3.right;
                _tangentY = Vector3.up;
            }
            _seeded = true;
            Occupy(new Vector2Int(0, 0), profile);
            return;
        }

        if (_frontier.Count == 0) return;

        int bestIdx = 0;
        float bestScore = float.MaxValue;
        for (int i = 0; i < _frontier.Count; i++)
        {
            float d = AxialToLocal(_frontier[i]).sqrMagnitude + Random.Range(0f, cellSize * cellSize);
            if (d < bestScore) { bestScore = d; bestIdx = i; }
        }

        Occupy(_frontier[bestIdx], profile);
    }

    private void Occupy(Vector2Int coord, TimbralProfile profile)
    {
        if (_occupied.Contains(coord)) return;
        _occupied.Add(coord);
        _frontier.RemoveAll(c => c == coord);

        SpawnHex(coord, profile);
        _cellCount++;

        foreach (var d in HexDirs)
        {
            Vector2Int n = coord + d;
            if (!_occupied.Contains(n) && !_frontier.Contains(n))
            {
                _frontier.Add(n);
            }
        }
    }

    private Vector3 AxialToLocal(Vector2Int c)
    {
        float x = cellSize * 1.5f * c.x;
        float y = cellSize * Mathf.Sqrt(3f) * (c.y + c.x * 0.5f);
        return new Vector3(x, y, 0f);
    }

    private void SurfaceAt(Vector3 local, out Vector3 point, out Vector3 normal)
    {
        Vector3 approx = _seedPoint + _tangentX * local.x + _tangentY * local.y;
        if (placement != null && placement.HasSkullMesh && placement.NearestSkullMeshPoint(approx, out point, out normal)) return;
        point = approx;
        normal = _seedNormal;
    }

    private void SpawnHex(Vector2Int coord, TimbralProfile profile)
    {
        Vector3 centreLocal = AxialToLocal(coord);
        Color colour = ResolveColour(profile);

        Vector3[] corner = new Vector3[6];
        for (int i = 0; i < 6; i++)
        {
            float ang = Mathf.Deg2Rad * (60f * i);
            Vector3 cLocal = centreLocal + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * (cellSize / Mathf.Sqrt(3f));
            SurfaceAt(cLocal, out Vector3 sp, out _);
            corner[i] = sp;
        }

        for (int i = 0; i < 6; i++)
        {
            Vector3 a = corner[i];
            Vector3 b = corner[(i + 1) % 6];
            float len = Vector3.Distance(a, b);
            int steps = Mathf.Max(2, Mathf.CeilToInt(len / paintSpacing));
            for (int s2 = 0; s2 <= steps; s2++)
            {
                Vector3 p = Vector3.Lerp(a, b, s2 / (float)steps);
                if (placement != null) placement.PaintSkull(p, paintRadius, colour, paintStrength);
            }
        }

        if (placement != null) placement.ApplySkullPaint();
    }

    private Color ResolveColour(TimbralProfile profile)
    {
        Color c = _prism != null ? _prism.RealtimeColour : Color.HSVToRGB(0.6f, 0.5f, 0.9f);
        return c * paintBrightness;
    }

    private void OnDestroy()
    {
    }
}