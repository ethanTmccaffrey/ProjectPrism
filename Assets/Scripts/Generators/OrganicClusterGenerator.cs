using System.Collections.Generic;
using UnityEngine;

//OrganicClusterGenerator - Kluver Category 7 (Small Circular Figures), cluster subcategory//

//Grows merged blobby COLONIES - metaball masses that read as single living organisms
//rather than groups of separate balls. The counterpart to SpeckCluster: where speck
//SCATTERS lonely disconnected points through the void, organic cluster GATHERS - blobs
//bud outward from a seed and fuse into one seamless lumpy surface, a colony rather than a
//sprinkle. Same calm territory, opposite gesture: isolated vs. massed.
//
//Timbral home (weight = tF * tP * (1-E+0.1)): tonal (not noisy), sustained (not
//percussive), quiet-leaning. Specifically the TONAL calm - clean sustained passages, not
//just any quiet moment. Ambient, drone, held strings, soft synth pads.
//
//THE SURFACE IS GENUINELY MERGED. Each blob contributes a smooth density falloff around
//its centre; overlapping falloffs sum; the visible surface is the isosurface where the
//summed density crosses a threshold. Marching cubes walks a voxel grid bounding the
//colony and generates a real triangle mesh of that surface, so neighbouring blobs fuse
//into one skin with no seams - a true metaball mass, not interpenetrating spheres.
//
//COST IS CONTROLLED by the persistent-canvas model. A colony only regenerates its mesh
//WHILE IT IS ACTIVELY GROWING; once it reaches its target blob count it freezes forever
//and never recomputes. At any moment only one or two colonies are growing, so only one or
//two meshes are ever being rebuilt. Grid resolution is deliberately coarse - these are
//faint background masses, and softness plus bloom hide the low resolution.
//
//Acoustic -> visual:
//  Growth: continuous while the tonal-sustained passage holds, paced by energy//
//  Colony layout: prominence - minor presence gives several small colonies, dominant//
//    gives one large one//
//  Blob size: energy at the moment each blob buds//
//  Size variation: harmonic complexity - simple harmony gives even uniform cells, complex//
//    harmony gives a lumpier, more varied mass (the tonal character drives the organic//
//    irregularity)//
//  Colour: RealtimeColour, sampled as each colony is born//

public class OrganicClusterGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;
    [SerializeField] private float energyThreshold = 0.03f;

    [Header("Field")]
    [SerializeField] private float fieldRadius = 30f;
    [SerializeField] private float maxFieldOffset = 30f;
    //Hard ceiling on total colonies. Prominence scales how many actually appear.//
    [SerializeField] private int maxColonies = 8;
    //Only this many colonies grow at once; the rest wait their turn.//
    [SerializeField] private int maxConcurrent = 2;

    [Header("Colony")]
    //Blobs in a full colony. Prominence interpolates between these: minor colonies stay
    //small, a dominant colony grows large.
    [SerializeField] private int minBlobs = 5;
    [SerializeField] private int maxBlobs = 26;
    //World radius a blob can bud from its parent - how loosely the colony spreads.//
    [SerializeField] private float budDistance = 1.4f;
    //Blobs appended per second at full energy.//
    [SerializeField] private float growthRate = 3.5f;

    [Header("Blob Size")]
    [SerializeField] private float minBlobRadius = 0.8f;
    [SerializeField] private float maxBlobRadius = 2.2f;

    [Header("Metaball Surface")]
    //Voxel edge length. SMALLER = smoother surface but far more expensive (cost scales
    //with the cube of resolution). Kept coarse deliberately - these are soft faint masses.
    [SerializeField] private float voxelSize = 0.6f;
    //Density threshold defining the surface. Higher = tighter, smaller blobs; lower =
    //fatter, more eager merging.
    [SerializeField] private float isoLevel = 1.0f;
    //Padding around the colony bounds so the field has room to fall off to zero.//
    [SerializeField] private float gridPadding = 2.5f;
    [SerializeField, Range(0f, 1f)] private float surfaceOpacity = 0.5f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _blobMaterial;

    private bool _active = false;
    private float _debugTimer = 0f;
    private int _completedCount = 0;

    private readonly List<BlobColony> _growing = new List<BlobColony>();

    public void SetPrism(PRISMGenerator prism)
    {
        _prism = prism;
    }

    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("OrganicCluster_Root");
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.OrganicCluster);

        BuildMaterial();

        _active = false;
        _completedCount = 0;
        _growing.Clear();

        Debug.Log("PRISM OrganicClusterGenerator: Initialised");
    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightOrganicCluster;
        _active = weight >= activationThreshold;

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.OrganicCluster) : Prominence.Silent;

        if (!_active) return;
        if (profile.RealtimeEnergy < energyThreshold) return;

        int effectiveMax = Mathf.Max(1, Mathf.RoundToInt(maxColonies * pr.prominence * pr.prominence));

        //Grow live colonies. A colony rebuilds its mesh only here, while growing.//
        float grow = growthRate * profile.RealtimeEnergy * Time.deltaTime;
        for (int i = _growing.Count - 1; i >= 0; i--)
        {
            _growing[i].Grow(grow, profile);
            if (_growing[i].Complete)
            {
                //Frozen: this colony's mesh will never recompute again.//
                _completedCount++;
                _growing.RemoveAt(i);
            }
        }

        int total = _completedCount + _growing.Count;
        if (total < effectiveMax && _growing.Count < maxConcurrent)
            SeedColony(profile, pr);
    }

    public void Deactivate()
    {
        _active = false;
    }

    private void SeedColony(TimbralProfile profile, Prominence pr)
    {
        float spread = fieldRadius * Mathf.Lerp(0.4f, 1f, pr.prominence);
        Vector3 fieldCentre = transform.position + Random.onUnitSphere * (maxFieldOffset * (1f - pr.centrality));
        Vector3 seed = fieldCentre + Random.onUnitSphere * (spread * Mathf.Pow(Random.value, 0.5f));

        //Prominence sets colony size: minor -> small, dominant -> large.//
        int targetBlobs = Mathf.RoundToInt(Mathf.Lerp(minBlobs, maxBlobs, pr.prominence));
        float sizeScale = Mathf.Lerp(0.6f, 1f, pr.prominence);

        Color colour = _prism != null ? _prism.RealtimeColour : Color.cyan;

        var colony = new BlobColony();
        colony.Init(_root, _blobMaterial, seed, targetBlobs, sizeScale,
                    budDistance, minBlobRadius, maxBlobRadius,
                    voxelSize, isoLevel, gridPadding, surfaceOpacity, colour);
        _growing.Add(colony);
    }

    private void BuildMaterial()
    {
        //Soft transparent unlit so the mass reads as a faint gel, and overlapping colonies
        //build up gently. Double-sided so the surface shows from any angle.
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        _blobMaterial = new Material(unlit);
        _blobMaterial.SetFloat("_Surface", 1f);
        _blobMaterial.SetFloat("_Blend", 0f);
        _blobMaterial.SetFloat("_SrcBlend", 5f);
        _blobMaterial.SetFloat("_DstBlend", 10f);
        _blobMaterial.SetFloat("_ZWrite", 0f);
        _blobMaterial.SetFloat("_Cull", 0f);
        _blobMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _blobMaterial.renderQueue = 3000;
    }

    private void OnDestroy()
    {
        if (_blobMaterial != null) Destroy(_blobMaterial);
    }
}

//A single metaball colony: blobs bud outward from a seed, and the merged isosurface is
//rebuilt via marching cubes while growing, then frozen once the colony is complete.
public class BlobColony
{
    public bool Complete { get; private set; } = false;

    private struct Blob { public Vector3 centre; public float radius; }

    private GameObject _go;
    private MeshFilter _mf;
    private Mesh _mesh;

    private readonly List<Blob> _blobs = new List<Blob>();
    private int _targetBlobs;
    private float _sizeScale;
    private float _budDistance;
    private float _minRadius, _maxRadius;
    private float _voxelSize, _isoLevel, _gridPadding;
    private float _growthAccumulator = 0f;
    private bool _dirty = false;

    public void Init(GameObject parent, Material mat, Vector3 seed, int targetBlobs,
                     float sizeScale, float budDistance, float minRadius, float maxRadius,
                     float voxelSize, float isoLevel, float gridPadding,
                     float surfaceOpacity, Color colour)
    {
        _targetBlobs = Mathf.Max(2, targetBlobs);
        _sizeScale = sizeScale;
        _budDistance = budDistance;
        _minRadius = minRadius;
        _maxRadius = maxRadius;
        _voxelSize = voxelSize;
        _isoLevel = isoLevel;
        _gridPadding = gridPadding;

        _go = new GameObject("Colony");
        _go.transform.SetParent(parent.transform);
        _mf = _go.AddComponent<MeshFilter>();
        var mr = _go.AddComponent<MeshRenderer>();
        Material m = new Material(mat);
        Color c = colour; c.a = surfaceOpacity;
        m.color = c;
        mr.material = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        _mesh = new Mesh();
        _mf.mesh = _mesh;

        //First blob sits at the seed.//
        _blobs.Add(new Blob { centre = seed, radius = _maxRadius * _sizeScale * 0.8f });
        _dirty = true;
    }

    public void Grow(float amount, TimbralProfile profile)
    {
        if (Complete) return;

        _growthAccumulator += amount;
        int buds = Mathf.FloorToInt(_growthAccumulator);
        if (buds <= 0)
        {
            //Even with no new bud, rebuild once if the seed hasn't been meshed yet.//
            if (_dirty) Rebuild();
            return;
        }
        _growthAccumulator -= buds;

        for (int i = 0; i < buds && _blobs.Count < _targetBlobs; i++)
            BudBlob(profile);

        if (_blobs.Count >= _targetBlobs) Complete = true;

        Rebuild();
    }

    private void BudBlob(TimbralProfile profile)
    {
        //Attach to a random existing blob so the colony accretes irregularly.//
        Blob parent = _blobs[Random.Range(0, _blobs.Count)];

        //Blob size from energy at this moment; variation from harmonic complexity - simple
        //harmony gives uniform cells, complex harmony a lumpier, more varied mass.
        float baseR = Mathf.Lerp(_minRadius, _maxRadius, profile.RealtimeEnergy) * _sizeScale;
        float variation = Mathf.Lerp(0.05f, 0.6f, profile.RealtimeHarmonicComplexity);
        float radius = baseR * Random.Range(1f - variation, 1f + variation);
        radius = Mathf.Max(0.15f, radius);

        //Place it partially overlapping the parent so the field fuses them.//
        Vector3 dir = Random.onUnitSphere;
        float dist = (parent.radius + radius) * Random.Range(0.35f, 0.7f);
        Vector3 centre = parent.centre + dir * dist;

        _blobs.Add(new Blob { centre = centre, radius = radius });
        _dirty = true;
    }

    //Marching cubes over a voxel grid bounding the colony. Runs only while growing.//
    private void Rebuild()
    {
        _dirty = false;
        if (_blobs.Count == 0) return;

        //Tight bounds around the colony, padded so the density falls to zero at the edges.//
        Vector3 min = _blobs[0].centre, max = _blobs[0].centre;
        for (int i = 0; i < _blobs.Count; i++)
        {
            Vector3 c = _blobs[i].centre;
            float r = _blobs[i].radius + _gridPadding;
            min = Vector3.Min(min, c - Vector3.one * r);
            max = Vector3.Max(max, c + Vector3.one * r);
        }

        int nx = Mathf.Clamp(Mathf.CeilToInt((max.x - min.x) / _voxelSize), 1, 64);
        int ny = Mathf.Clamp(Mathf.CeilToInt((max.y - min.y) / _voxelSize), 1, 64);
        int nz = Mathf.Clamp(Mathf.CeilToInt((max.z - min.z) / _voxelSize), 1, 64);

        var verts = new List<Vector3>();
        var tris = new List<int>();

        //Sample the summed metaball field at each grid corner, then march each cell.//
        //Field: sum over blobs of (r^2 / d^2) - a classic soft metaball falloff.//
        for (int x = 0; x < nx; x++)
            for (int y = 0; y < ny; y++)
                for (int z = 0; z < nz; z++)
                {
                    Vector3 baseP = min + new Vector3(x, y, z) * _voxelSize;

                    //Eight corners of this voxel.//
                    float[] cube = new float[8];
                    Vector3[] corner = new Vector3[8];
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 p = baseP + CubeOffset(i) * _voxelSize;
                        corner[i] = p;
                        cube[i] = Field(p);
                    }

                    MarchCube(corner, cube, verts, tris);
                }

        _mesh.Clear();
        if (verts.Count > 0)
        {
            _mesh.indexFormat = verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.SetVertices(verts);
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }
    }

    private float Field(Vector3 p)
    {
        float sum = 0f;
        for (int i = 0; i < _blobs.Count; i++)
        {
            float d2 = (p - _blobs[i].centre).sqrMagnitude + 1e-4f;
            float r2 = _blobs[i].radius * _blobs[i].radius;
            sum += r2 / d2;
        }
        return sum;
    }

    //Marching cubes for one voxel. Interpolates surface crossings on each edge and emits
    //triangles from the lookup tables. Standard Lorensen & Cline (1987) implementation.
    private void MarchCube(Vector3[] corner, float[] val, List<Vector3> verts, List<int> tris)
    {
        int cubeIndex = 0;
        for (int i = 0; i < 8; i++)
            if (val[i] > _isoLevel) cubeIndex |= (1 << i);

        int edges = EdgeTable[cubeIndex];
        if (edges == 0) return;

        Vector3[] vertList = new Vector3[12];
        for (int i = 0; i < 12; i++)
        {
            if ((edges & (1 << i)) == 0) continue;
            int a = EdgeConnection[i, 0];
            int b = EdgeConnection[i, 1];
            vertList[i] = InterpEdge(corner[a], corner[b], val[a], val[b]);
        }

        for (int i = 0; TriTable[cubeIndex, i] != -1; i += 3)
        {
            int baseIdx = verts.Count;
            verts.Add(vertList[TriTable[cubeIndex, i]]);
            verts.Add(vertList[TriTable[cubeIndex, i + 1]]);
            verts.Add(vertList[TriTable[cubeIndex, i + 2]]);
            tris.Add(baseIdx);
            tris.Add(baseIdx + 1);
            tris.Add(baseIdx + 2);
        }
    }

    private Vector3 InterpEdge(Vector3 p1, Vector3 p2, float v1, float v2)
    {
        float t = Mathf.Abs(v2 - v1) < 1e-6f ? 0.5f : (_isoLevel - v1) / (v2 - v1);
        return Vector3.Lerp(p1, p2, Mathf.Clamp01(t));
    }

    private static Vector3 CubeOffset(int i)
    {
        switch (i)
        {
            case 0: return new Vector3(0, 0, 0);
            case 1: return new Vector3(1, 0, 0);
            case 2: return new Vector3(1, 1, 0);
            case 3: return new Vector3(0, 1, 0);
            case 4: return new Vector3(0, 0, 1);
            case 5: return new Vector3(1, 0, 1);
            case 6: return new Vector3(1, 1, 1);
            default: return new Vector3(0, 1, 1);
        }
    }

    //Edge i connects these two corners.//
    private static readonly int[,] EdgeConnection =
    {
        {0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7}
    };

    //Standard marching-cubes edge table: which of the 12 edges are crossed for each of
    //the 256 corner sign combinations.//
    private static readonly int[] EdgeTable = MarchingCubesTables.EdgeTable;
    private static readonly int[,] TriTable = MarchingCubesTables.TriTable;
}
