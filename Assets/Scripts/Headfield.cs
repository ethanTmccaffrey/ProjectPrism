using System;
using System.Collections.Generic;
using UnityEngine;

//HeadField - the head as a signed distance field, meshed at runtime.
//
//PRISM's canvas is a head. Not decoration: Kluver's form constants are ENTOPTIC phenomena,
//seen inside the visual system with the eyes closed, and cross-modal correspondence is a
//property of neural processing. The head is the literal site of what is being visualised.
//It is also the FRAME - the equivalent of the rectangle a synaesthetic painting is painted
//on, the boundary that makes the work legible as composition rather than as scatter.
//
//WHAT THIS DOES, in order:
//    1. load the baked skull field
//    2. hollow it        -> wall + cranial cavity
//    3. cut it open      -> the interior becomes visible
//    4. mesh and render with a single material
//
//Step order matters and is not obvious. Hollowing wraps a wall around whatever form it
//receives, so it must happen BEFORE cutting: cut first and the hollow wraps the walls of
//the cut itself, producing a lined pocket - a sphere with its own shell - rather than an
//opening.
//
//WHY A DISTANCE FIELD AND NOT A MESH. Fields can be hollowed, cut, displaced and queried
//from one representation. The query matters most: the sign of a sample answers "is this
//point inside the skull", which is what the generator placement system is built on.
//
//WHY NOT A VOXEL FILL. Voxelising the source mesh and flood filling from outside does not
//work on a head. The model is open at the mouth, both ear canals, both eye sockets and the
//neck - correct anatomy, but far too open to seal. Every attempt leaked out through the
//eye sockets and reported an interior volume of exactly zero, at every resolution, with
//and without morphological closing. Signed distance via the generalized winding number
//ignores holes entirely and recovered the volume to within 0.03%.
//
//SCALE. The baked head measures roughly 2.9 x 4.0 x 4.8 field units, centred near the
//origin. Cut radii much above 1.0 swallow the whole side of the skull rather than opening
//a window in it - the defaults here were measured against the field, not guessed.
//
//Head mesh: "Planar head (Oleg Toropygin)" by BlueHorse (https://skfb.ly/6yCE7), CC-BY 4.0.

public class HeadField : MonoBehaviour
{
    [Header("Source")]
    [SerializeField] private string resourceName = "head_sdf";

    [Header("Transform")]
    [SerializeField] private float scale = 50f;
    [SerializeField] private Vector3 offset = Vector3.zero;

    [Header("Hollow")]
    //A distance field describes a FILLED volume, so an unmodified field cut open reveals a
    //solid block. Eroding inward and subtracting leaves a wall with a genuine cavity - and
    //that cavity is where the interior generators will live.
    [SerializeField] private bool hollow = true;
    [SerializeField] private float shellThickness = 0.18f;

    [Header("Cutaway - Plane")]
    //Slices with a half-space. The normal points toward the half REMOVED; offset slides
    //the plane, with 0 cutting through the midline.
    [SerializeField] private bool cutaway = false;
    [SerializeField] private Vector3 cutNormal = Vector3.right;
    [SerializeField] private float cutOffset = 0f;

    [Header("Cutaway - Sphere")]
    //Carves a rounded bite rather than slicing flat, so the interior reads as a bowl you
    //look into rather than a sawn face you look at.
    [SerializeField] private bool sphericalCut = true;
    [SerializeField] private Vector3 cutSphereCentre = new Vector3(0.4f, 0.3f, 0.6f);
    [SerializeField] private float cutSphereRadius = 1.2f;

    [Header("Rendering")]
    //Leave empty and a plain lit material is created automatically.//
    [SerializeField] private Material headMaterial;
    [SerializeField] private bool generateOnStart = true;

    private float[] _field;
    private int _nx, _ny, _nz;
    private Vector3 _origin;
    private float _pitch;
    private bool _loaded = false;

    private GameObject _meshObject;
    private Mesh _mesh;

    public bool Loaded => _loaded;
    public Bounds FieldBounds { get; private set; }
    public float ShellThickness => shellThickness;

    public float VoxelWorldSize => _pitch * scale;

    public void SetMaterial(Material m)
    {
        headMaterial = m;
        if (_meshObject != null)
        {
            var mr = _meshObject.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = m;
        }
    }

    private void Start()
    {
        if (generateOnStart && Load()) Rebuild();
    }

    //Loading//

    public bool Load()
    {
        TextAsset asset = Resources.Load<TextAsset>(resourceName);
        if (asset == null)
        {
            Debug.LogError($"PRISM HeadField: could not find '{resourceName}' in Resources. " +
                           "Run bake_head_sdf.py and place the .bytes file in Assets/Resources.");
            return false;
        }

        byte[] bytes = asset.bytes;
        if (bytes.Length < 32)
        {
            Debug.LogError("PRISM HeadField: field file too small to be valid");
            return false;
        }

        int p = 0;
        int magic = BitConverter.ToInt32(bytes, p); p += 4;
        int version = BitConverter.ToInt32(bytes, p); p += 4;

        if (magic != 0x50534446)
        {
            Debug.LogError($"PRISM HeadField: bad magic {magic:X} - not a PSDF file");
            return false;
        }

        _nx = BitConverter.ToInt32(bytes, p); p += 4;
        _ny = BitConverter.ToInt32(bytes, p); p += 4;
        _nz = BitConverter.ToInt32(bytes, p); p += 4;

        _origin = new Vector3(BitConverter.ToSingle(bytes, p),
                              BitConverter.ToSingle(bytes, p + 4),
                              BitConverter.ToSingle(bytes, p + 8));
        p += 12;

        _pitch = BitConverter.ToSingle(bytes, p); p += 4;

        int count = _nx * _ny * _nz;
        if (bytes.Length < p + count * 4)
        {
            Debug.LogError("PRISM HeadField: file truncated");
            return false;
        }

        _field = new float[count];
        Buffer.BlockCopy(bytes, p, _field, 0, count * 4);
        _loaded = true;

        Vector3 sizeLocal = new Vector3(_nx, _ny, _nz) * _pitch;
        FieldBounds = new Bounds(LocalToWorld(_origin + sizeLocal * 0.5f), sizeLocal * scale);

        Debug.Log($"PRISM HeadField: loaded v{version} grid {_nx}x{_ny}x{_nz} pitch {_pitch:F3}");
        return true;
    }

    //Sampling - the containment tests the placement system will use//

    //Signed distance at a world position. Positive = inside the head.//
    public float SampleWorld(Vector3 worldPos)
    {
        if (!_loaded) return -1f;

        Vector3 local = WorldToLocal(worldPos);
        Vector3 g = (local - _origin) / _pitch;

        int x = Mathf.Clamp(Mathf.RoundToInt(g.x), 0, _nx - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(g.y), 0, _ny - 1);
        int z = Mathf.Clamp(Mathf.RoundToInt(g.z), 0, _nz - 1);

        return _field[Index(x, y, z)];
    }

    public bool IsInside(Vector3 worldPos) => SampleWorld(worldPos) > 0f;
    public bool IsInCavity(Vector3 worldPos) => SampleWorld(worldPos) > shellThickness;

    public bool IsInShell(Vector3 worldPos)
    {
        float d = SampleWorld(worldPos);
        return d > 0f && d <= shellThickness;
    }

    private int Index(int x, int y, int z) => x * _ny * _nz + y * _nz + z;

    public Vector3 LocalToWorld(Vector3 local) => transform.position + offset + local * scale;
    public Vector3 WorldToLocal(Vector3 world) => (world - transform.position - offset) / scale;

    //Build//

    public void Rebuild()
    {
        if (!_loaded)
        {
            Debug.LogWarning("PRISM HeadField: Rebuild called before Load");
            return;
        }

        int count = _nx * _ny * _nz;
        float[] w = new float[count];
        Array.Copy(_field, w, count);

        for (int x = 0; x < _nx; x++)
            for (int y = 0; y < _ny; y++)
                for (int z = 0; z < _nz; z++)
                {
                    int i = Index(x, y, z);
                    Vector3 local = _origin + new Vector3(x, y, z) * _pitch;

                    //HOLLOW first, while the form is still closed.//
                    if (hollow) w[i] = Mathf.Min(w[i], shellThickness - w[i]);

                    //CUT second, removing material from the finished shell.//
                    if (cutaway)
                    {
                        Vector3 n = cutNormal.sqrMagnitude > 1e-6f ? cutNormal.normalized : Vector3.right;
                        w[i] = Mathf.Min(w[i], cutOffset - Vector3.Dot(local, n));
                    }

                    if (sphericalCut)
                    {
                        float d = Vector3.Distance(local, cutSphereCentre) - cutSphereRadius;
                        w[i] = Mathf.Min(w[i], d);
                    }
                }

        var verts = new List<Vector3>();
        var tris = new List<int>();
        March(w, verts, tris);
        KeepLargestComponent(verts, tris);

        //Weld coincident vertices. The march emits three unshared vertices per triangle, so
        //every facet edge is a seam of duplicate vertices at the same position. Under the
        //ripple shader those duplicates can displace by fractionally different amounts and
        //pull apart, opening thin slits that show the interior through the surface. Welding
        //shared corners into one vertex makes them move as a unit, closing the seams.
        //
        //The cost is smooth normals - a welded vertex has one normal averaged across its
        //faces, softening the planar facets. That is the trade for a surface that does not
        //tear when displaced.
        WeldVertices(verts, tris);

        if (verts.Count == 0)
        {
            Debug.LogWarning("PRISM HeadField: marching produced no geometry");
            return;
        }

        if (_meshObject == null)
        {
            _meshObject = new GameObject("Head");
            _meshObject.transform.SetParent(transform, false);
            _meshObject.AddComponent<MeshFilter>();
            _meshObject.AddComponent<MeshRenderer>();
        }

        if (headMaterial == null)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) lit = Shader.Find("Standard");
            headMaterial = new Material(lit);
            headMaterial.color = new Color(0.82f, 0.80f, 0.76f);
            //Double-sided, so the far wall is visible when looking in through the cut.//
            if (headMaterial.HasProperty("_Cull")) headMaterial.SetFloat("_Cull", 0f);
        }

        _meshObject.GetComponent<MeshRenderer>().sharedMaterial = headMaterial;

        if (_mesh == null) _mesh = new Mesh();
        _mesh.Clear();
        _mesh.indexFormat = verts.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        _mesh.SetVertices(verts);
        _mesh.SetTriangles(tris, 0);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
        _meshObject.GetComponent<MeshFilter>().mesh = _mesh;

        Debug.Log($"PRISM HeadField: rebuilt - {verts.Count} verts, {tris.Count / 3} tris");
    }

    //Merges vertices at the same position into one, so shared corners move together under
    //displacement. Quantises positions to weld, then rebuilds the triangle list to index
    //the merged set.
    private void WeldVertices(List<Vector3> verts, List<int> tris)
    {
        if (verts.Count == 0) return;

        var map = new Dictionary<Vector3Int, int>();
        var merged = new List<Vector3>();
        int[] remap = new int[verts.Count];
        float weldStep = _pitch * scale * 0.05f;   //5% of a scaled voxel//
        float q = 1f / weldStep;

        for (int i = 0; i < verts.Count; i++)
        {
            Vector3 v = verts[i];
            var key = new Vector3Int(Mathf.RoundToInt(v.x * q),
                                     Mathf.RoundToInt(v.y * q),
                                     Mathf.RoundToInt(v.z * q));
            if (!map.TryGetValue(key, out int id))
            {
                id = merged.Count;
                map[key] = id;
                merged.Add(v);
            }
            remap[i] = id;
        }

        for (int t = 0; t < tris.Count; t++)
            tris[t] = remap[tris[t]];

        verts.Clear();
        verts.AddRange(merged);
    }

    //Marching cubes over the field, using the shared lookup tables.//
    private void March(float[] field, List<Vector3> verts, List<int> tris)
    {
        float[] cube = new float[8];
        Vector3[] corner = new Vector3[8];
        Vector3[] edgeVert = new Vector3[12];

        for (int x = 0; x < _nx - 1; x++)
            for (int y = 0; y < _ny - 1; y++)
                for (int z = 0; z < _nz - 1; z++)
                {
                    int cubeIndex = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3Int o = CubeCorner(i);
                        int ix = x + o.x, iy = y + o.y, iz = z + o.z;
                        cube[i] = field[Index(ix, iy, iz)];
                        corner[i] = _origin + new Vector3(ix, iy, iz) * _pitch;
                        //Positive is INSIDE, so flag corners that sit within the solid.//
                        if (cube[i] > 0f) cubeIndex |= (1 << i);
                    }

                    int edges = MarchingCubesTables.EdgeTable[cubeIndex];
                    if (edges == 0) continue;

                    for (int i = 0; i < 12; i++)
                    {
                        if ((edges & (1 << i)) == 0) continue;
                        int a = EdgeConnection[i, 0];
                        int b = EdgeConnection[i, 1];
                        float va = cube[a], vb = cube[b];
                        float t = Mathf.Abs(vb - va) < 1e-6f ? 0.5f : (0f - va) / (vb - va);
                        edgeVert[i] = Vector3.Lerp(corner[a], corner[b], Mathf.Clamp01(t));
                    }

                    for (int i = 0; MarchingCubesTables.TriTable[cubeIndex, i] != -1; i += 3)
                    {
                        int b0 = verts.Count;
                        //Wound so faces point outward given "positive = inside".//
                        verts.Add(LocalToWorld(edgeVert[MarchingCubesTables.TriTable[cubeIndex, i + 2]]));
                        verts.Add(LocalToWorld(edgeVert[MarchingCubesTables.TriTable[cubeIndex, i + 1]]));
                        verts.Add(LocalToWorld(edgeVert[MarchingCubesTables.TriTable[cubeIndex, i]]));
                        tris.Add(b0); tris.Add(b0 + 1); tris.Add(b0 + 2);
                    }
                }
    }

    //Marching a sampled field always throws off small disconnected pockets where the sign
    //flips in isolation; they render as floating polyhedra around the head. Keep only the
    //largest connected body. Vertices are welded by position first, because the march
    //emits each triangle with its own unshared vertices.
    private void KeepLargestComponent(List<Vector3> verts, List<int> tris)
    {
        if (verts.Count == 0) return;

        var map = new Dictionary<Vector3Int, int>();
        int[] weld = new int[verts.Count];
        const float q = 1000f;

        for (int i = 0; i < verts.Count; i++)
        {
            Vector3 v = verts[i];
            var key = new Vector3Int(Mathf.RoundToInt(v.x * q),
                                     Mathf.RoundToInt(v.y * q),
                                     Mathf.RoundToInt(v.z * q));
            if (!map.TryGetValue(key, out int id)) { id = map.Count; map[key] = id; }
            weld[i] = id;
        }

        int[] parent = new int[map.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;

        int Find(int a)
        {
            while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; }
            return a;
        }
        void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) parent[ra] = rb;
        }

        for (int t = 0; t < tris.Count; t += 3)
        {
            Union(weld[tris[t]], weld[tris[t + 1]]);
            Union(weld[tris[t + 1]], weld[tris[t + 2]]);
        }

        var sizes = new Dictionary<int, int>();
        for (int t = 0; t < tris.Count; t += 3)
        {
            int r = Find(weld[tris[t]]);
            sizes.TryGetValue(r, out int c);
            sizes[r] = c + 1;
        }

        if (sizes.Count <= 1) return;

        int best = -1, bestCount = -1;
        foreach (var kv in sizes)
            if (kv.Value > bestCount) { bestCount = kv.Value; best = kv.Key; }

        var keep = new List<int>(bestCount * 3);
        for (int t = 0; t < tris.Count; t += 3)
        {
            if (Find(weld[tris[t]]) != best) continue;
            keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]);
        }

        tris.Clear();
        tris.AddRange(keep);
    }

    private static Vector3Int CubeCorner(int i)
    {
        switch (i)
        {
            case 0: return new Vector3Int(0, 0, 0);
            case 1: return new Vector3Int(1, 0, 0);
            case 2: return new Vector3Int(1, 1, 0);
            case 3: return new Vector3Int(0, 1, 0);
            case 4: return new Vector3Int(0, 0, 1);
            case 5: return new Vector3Int(1, 0, 1);
            case 6: return new Vector3Int(1, 1, 1);
            default: return new Vector3Int(0, 1, 1);
        }
    }

    private static readonly int[,] EdgeConnection =
    {
        {0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7}
    };

    //Editor convenience: regenerate from the inspector without entering play mode.//
    [ContextMenu("Rebuild Head")]
    private void RebuildFromMenu()
    {
        if (!_loaded) Load();
        Rebuild();
    }

    private void OnDestroy()
    {
        if (_mesh != null) Destroy(_mesh);
    }
}