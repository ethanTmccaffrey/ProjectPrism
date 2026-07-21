using System.Collections.Generic;
using UnityEngine;

//SpeckClusterGenerator - Kluver Category 7 (Small Circular Figures)//
//Scatters small glowing dots sparsely through the volume during QUIET moments. Its weight is (1-E)^2 - driven purely by low energy - so it activates in the gaps of any song: intros, breakdowns, fade-outs, the spaces between the loud generators//
//It is the negative space of the whole system, populating silence Because it responds to absence rather than any genre//
//it should stay visually quiet: Specks appear continuously while energy is low (lower energy = faster appearance), paced slow and capped so they stay a delicate scattering rather than accumulating into a field//

//Appearance: continuous, rate scales with how quiet it is (1 - energy)//
//Colour: RealtimeColour at spawn//
//Prominence: field spread + speck size//

public class SpeckClusterGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;

    [Header("Field")]
    [SerializeField] private float fieldRadius = 30f;
    [SerializeField] private int maxSpecks = 400;

    [Header("Appearance")]
    [SerializeField] private float speckSize = 0.35f;
    //Specks per second at full quiet (energy near 0). Kept low for sparseness//
    [SerializeField] private float spawnRate = 6f;

    [Header("Placement")]
    [SerializeField] private float maxFieldOffset = 30f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _speckMaterial;
    private Mesh _quadMesh;

    private bool _active = false;
    private float _spawnAccumulator = 0f;
    private int _speckCount = 0;

    public void SetPrism(PRISMGenerator prism)
    {
        _prism = prism;
    }

    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("Speck_Root");
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.SpeckCluster);

        BuildMaterial();
        BuildQuadMesh();

        _active = false;
        _spawnAccumulator = 0f;
        _speckCount = 0;

        Debug.Log("PRISM SpeckClusterGenerator: Initialised");
    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightSpeckCluster;
        _active = weight >= activationThreshold;

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.SpeckCluster) : Prominence.Silent;

        if (!_active) return;
        if (_speckCount >= maxSpecks) return;

        //Rate scales with how QUIET it is: the lower the energy, the faster specks appear//
        float quiet = 1f - profile.RealtimeEnergy;
        _spawnAccumulator += spawnRate * quiet * Time.deltaTime;
        int ticks = Mathf.FloorToInt(_spawnAccumulator);
        if (ticks <= 0) return;
        _spawnAccumulator -= ticks;

        for (int i = 0; i < ticks && _speckCount < maxSpecks; i++)
            SpawnSpeck(profile, pr);
    }

    public void Deactivate()
    {
        _active = false;
    }

    private void SpawnSpeck(TimbralProfile profile, Prominence pr)
    {
        //Sparse scatter through a spherical field, positioned/scaled by prominence//
        float spread = fieldRadius * Mathf.Lerp(0.4f, 1f, pr.prominence);
        Vector3 fieldCentre = transform.position + Random.onUnitSphere * (maxFieldOffset * (1f - pr.centrality));
        //Cube-root for uniform volume distribution (no centre clumping)//
        Vector3 pos = fieldCentre + Random.onUnitSphere * (spread * Mathf.Pow(Random.value, 0.333f));

        float size = speckSize * Mathf.Lerp(0.6f, 1f, pr.prominence);

        GameObject speck = new GameObject("Speck");
        speck.transform.SetParent(_root.transform);
        speck.transform.position = pos;
        speck.transform.localScale = Vector3.one * size;

        var mf = speck.AddComponent<MeshFilter>();
        mf.mesh = _quadMesh;
        var mr = speck.AddComponent<MeshRenderer>();
        Material m = new Material(_speckMaterial);
        m.color = _prism != null ? _prism.RealtimeColour : Color.white;
        mr.material = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        //Billboard so the dot always faces the camera (reads as a glowing point)//
        speck.AddComponent<SpeckBillboard>();

        _speckCount++;
    }

    private void BuildQuadMesh()
    {
        _quadMesh = new Mesh();
        _quadMesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f,  0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f)
        };
        _quadMesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
        _quadMesh.RecalculateNormals();
        _quadMesh.RecalculateBounds();
    }

    private void BuildMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        _speckMaterial = new Material(shader);
    }

    private void OnDestroy()
    {
        if (_speckMaterial != null) Destroy(_speckMaterial);
        if (_quadMesh != null) Destroy(_quadMesh);
    }
}

//Tiny helper: makes a speck quad always face the main camera//
public class SpeckBillboard : MonoBehaviour
{
    private Transform _cam;

    private void Start()
    {
        if (Camera.main != null) _cam = Camera.main.transform;
    }

    private void LateUpdate()
    {
        if (_cam == null)
        {
            if (Camera.main != null) _cam = Camera.main.transform;
            else return;
        }
        transform.rotation = Quaternion.LookRotation(transform.position - _cam.position);
    }
}
