using System.Collections.Generic;
using UnityEngine;

//HoneycombGenerator - Kluver Category 3 (Lattices, Honeycombs, Gratings)//
//
//Grows a connected hexagonal tessellation one cell per beat. Each new hex snaps onto
//the open edge of the existing lattice, so the structure reads as a genuine honeycomb
//sheet of shared edges rather than separate floating cells. Cells are drawn as a bright
//outline plus a very faint transparent fill, so the lattice stays airy and never drowns
//other generators. The sheet sits on a gently curved plane (slight dome) so it has depth
//without losing hexagon readability.
//
//Maps to electronic / EDM: regularity AND flatness (synthetic, gridded texture).
//
//Growth: one hex per detected beat (Dixon 2001 onset detector on raw flux). Prominence
//caps how large the sheet can get, so a minor honeycomb stays a small patch.

public class HoneycombGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;

    [Header("Lattice")]
    [SerializeField] private float cellSize = 4f;
    [SerializeField] private int maxCells = 200;
    [SerializeField] private float maxSeedOffset = 35f;
    [SerializeField] private float fallbackTrackSeconds = 180f;

    [Header("Curve")]
    [SerializeField] private float domeHeight = 8f;
    [SerializeField] private float domeRadius = 60f;

    [Header("Appearance")]
    [SerializeField] private float edgeWidth = 0.12f;
    [SerializeField, Range(0f, 0.5f)] private float fillOpacity = 0.12f;

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

    private Material _edgeMaterial;
    private Material _fillMaterial;

    private readonly HashSet<Vector2Int> _occupied = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> _frontier = new List<Vector2Int>();
    private Vector3 _seedCentre;
    private bool _seeded = false;
    private int _cellCount = 0;

    private static readonly Vector2Int[] HexDirs =
    {
        new Vector2Int(1, 0), new Vector2Int(1, -1), new Vector2Int(0, -1),
        new Vector2Int(-1, 0), new Vector2Int(-1, 1), new Vector2Int(0, 1)
    };

    public void SetPrism(PRISMGenerator prism)
    {
        _prism = prism;
    }

    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("Honeycomb_Root");
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.Honeycomb);

        BuildMaterials();

        _fluxHistory.Clear();
        _occupied.Clear();
        _frontier.Clear();
        _timeSinceLastCell = 0f;
        _elapsed = 0f;
        _cellCount = 0;
        _seeded = false;
        _active = false;

        Debug.Log("PRISM HoneycombGenerator: Initialised");
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

    private void AddCell(TimbralProfile profile, Prominence pr)
    {
        if (!_seeded)
        {
            _seedCentre = transform.position + Random.onUnitSphere * (maxSeedOffset * (1f - pr.centrality));
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
                _frontier.Add(n);
        }
    }

    private Vector3 AxialToLocal(Vector2Int c)
    {
        float x = cellSize * 1.5f * c.x;
        float y = cellSize * Mathf.Sqrt(3f) * (c.y + c.x * 0.5f);
        return new Vector3(x, y, 0f); 
    }

    private Vector3 DomePosition(Vector3 local)
    {
        float distSq = local.x * local.x + local.y * local.y;
        float t = Mathf.Clamp01(distSq / (domeRadius * domeRadius));
        float z = domeHeight * (1f - t); //Curve toward camera (−Z) at the centre//
        return _seedCentre + new Vector3(local.x, local.y, z);
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
            corner[i] = DomePosition(cLocal);
        }

        GameObject cell = new GameObject("Hex");
        cell.transform.SetParent(_root.transform);

        if (fillOpacity > 0f)
            BuildFill(cell, corner, colour);

        BuildOutline(cell, corner, colour);
    }

    private void BuildFill(GameObject parent, Vector3[] corner, Color colour)
    {
        Vector3 centre = Vector3.zero;
        for (int i = 0; i < 6; i++) centre += corner[i];
        centre /= 6f;

        Vector3[] verts = new Vector3[7];
        verts[0] = centre;
        for (int i = 0; i < 6; i++) verts[i + 1] = corner[i];

        int[] tris = new int[18];
        for (int i = 0; i < 6; i++)
        {
            tris[i * 3] = 0;
            tris[i * 3 + 1] = 1 + (i + 1) % 6; //Reversed winding so the face points at the camera//
            tris[i * 3 + 2] = 1 + i;
        }
        Mesh mesh = new Mesh { vertices = verts, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(parent.transform);
        fill.AddComponent<MeshFilter>().mesh = mesh;
        var mr = fill.AddComponent<MeshRenderer>();
        Material m = new Material(_fillMaterial);
        Color fc = colour; fc.a = fillOpacity;
        m.color = fc;
        mr.material = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void BuildOutline(GameObject parent, Vector3[] corner, Color colour)
    {
        Vector3[] verts = new Vector3[24];
        int[] tris = new int[36];

        for (int i = 0; i < 6; i++)
        {
            Vector3 a = corner[i];
            Vector3 b = corner[(i + 1) % 6];
            Vector3 dir = (b - a).normalized;
            Vector3 perp = Vector3.Cross(dir, Vector3.forward).normalized * (edgeWidth * 0.5f);

            int v = i * 4;
            verts[v] = a - perp; verts[v + 1] = a + perp;
            verts[v + 2] = b + perp; verts[v + 3] = b - perp;

            int t = i * 6;
            tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
            tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
        }

        Mesh mesh = new Mesh { vertices = verts, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GameObject edges = new GameObject("Edges");
        edges.transform.SetParent(parent.transform);
        edges.AddComponent<MeshFilter>().mesh = mesh;
        var mr = edges.AddComponent<MeshRenderer>();
        Material m = new Material(_edgeMaterial);
        m.color = colour;
        mr.material = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private Color ResolveColour(TimbralProfile profile)
    {
        //Use the song's real frequency-derived colour so honeycomb matches the track's//
        //identity instead of forcing its own palette. Falls back to a neutral tone if//
        //the coordinator reference is unavailable so cells never render invisible.//
        if (_prism != null) return _prism.RealtimeColour;
        return Color.HSVToRGB(0.6f, 0.5f, 0.9f);
    }

    private void BuildMaterials()
    {
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");

        _edgeMaterial = new Material(unlit);

        _fillMaterial = new Material(unlit);
        _fillMaterial.SetFloat("_Surface", 1f);
        _fillMaterial.SetFloat("_Blend", 0f);
        _fillMaterial.SetFloat("_SrcBlend", 5f);
        _fillMaterial.SetFloat("_DstBlend", 10f);
        _fillMaterial.SetFloat("_ZWrite", 0f);
        _fillMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _fillMaterial.renderQueue = 3000;
    }

    private void OnDestroy()
    {
        if (_edgeMaterial != null) Destroy(_edgeMaterial);
        if (_fillMaterial != null) Destroy(_fillMaterial);
    }
}
