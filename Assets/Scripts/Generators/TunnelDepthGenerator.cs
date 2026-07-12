using System.Collections.Generic;
using UnityEngine;
 
//TunnelDepthGenerator - Kluver Category 1 (Tunnels and Funnels)//

//Builds a nested funnel receding down Z toward a vanishing point: a stack of concentric rings, each smaller and deeper than the last, connected by a translucent wall with brighter ring edges//
//Introduces DEPTH as a visual language - recession into the scene - which no other generator uses.//

//Timbral home (weight = tX*tF*tZ*(1-E+0.2)): sustained, tonal, smooth, dynamically restrained music - orchestral swells, ambient, drone, quiet classical//
//So the funnel grows slowly and calmly, drawing the eye inward, not a rushing vortex//

//Acoustic -> visual://
//Continuous slow growth: one ring deeper over time while active (fits sustained music)//
//Ring radius pulses with energy at birth: the wall breathes with the music's dynamics//
//Colour: RealtimeColour at each ring's birth (records the song's colour down the bore)//
//Prominence: mouth position (centrality) + overall scale (prominence)//
 
public class TunnelDepthGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;

    [Header("Funnel Shape")]
    //Radius of the tunnel mouth (nearest ring)//
    [SerializeField] private float mouthRadius = 12f;
    //How much each successive ring narrows (0.97 = gentle taper toward vanishing point)//
    [SerializeField, Range(0.8f, 0.999f)] private float taper = 0.965f;
    //Depth step between rings along Z//
    [SerializeField] private float depthStep = 2.5f;
    //Corners per ring (higher = rounder)//
    [SerializeField] private int ringSegments = 24;
    //Max rings before the funnel stops deepening//
    [SerializeField] private int maxRings = 120;
    //Rings added per second at full energy (slow - this is contemplative)//
    [SerializeField] private float growthRate = 4f;

    [Header("Breathing")]
    [SerializeField] private float breathAmount = 0.25f;

    [Header("Curve")]
    [SerializeField] private float baseCurve = 3f;
    [SerializeField] private float energyCurve = 8f;
    [SerializeField, Range(0f, 0.9f)] private float headingBias = 0.6f;

    [Header("Placement")]
    [SerializeField] private float maxMouthOffset = 30f;

    [Header("Appearance")]
    [SerializeField, Range(0f, 0.5f)] private float wallOpacity = 0.1f;
    [SerializeField] private float edgeWidth = 0.1f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _wallMaterial;
    private Material _edgeMaterial;

    private bool _active = false;
    private bool _seeded = false;
    private float _debugTimer = 0f;
    private float _growthAccumulator = 0f;

    private Vector3 _mouthCentre;     
    private Vector3 _axis;            
    private Vector3 _currentPos;       
    private Vector3 _heading;
    private float _steerPhase = 0f;
    private int _ringCount = 0;
    private float _currentRadius;
    private float _sizeScale = 1f;
    private Vector3[] _prevRing;

    public void SetPrism(PRISMGenerator prism)
    {
        _prism = prism;
    }

    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("Tunnel_Root");
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.TunnelDepth);

        BuildMaterials();

        _active = false;
        _seeded = false;
        _growthAccumulator = 0f;
        _ringCount = 0;
        _prevRing = null;

        Debug.Log("PRISM TunnelDepthGenerator: Initialised");
    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightTunnelDepth;
        _active = weight >= activationThreshold;

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.TunnelDepth) : Prominence.Silent;

        if (!_active) return;
        if (_ringCount >= maxRings) return;

        if (!_seeded) SeedTunnel(pr);

        //Continuous slow growth. Note the funnel deepens even in quiet passages (itstimbral home is quiet), so growth is time-based, only gently scaled by energy//
        float rate = growthRate * Mathf.Lerp(0.5f, 1.5f, profile.RealtimeEnergy);
        _growthAccumulator += rate * Time.deltaTime;
        int ticks = Mathf.FloorToInt(_growthAccumulator);
        if (ticks <= 0) return;
        _growthAccumulator -= ticks;

        for (int i = 0; i < ticks && _ringCount < maxRings; i++)
            AddRing(profile);
    }

    public void Deactivate()
    {
        _active = false;
    }

    private void SeedTunnel(Prominence pr)
    {
        _mouthCentre = transform.position + Random.onUnitSphere * (maxMouthOffset * (1f - pr.centrality));
        Vector3 tilt = new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f), 1f);
        _axis = tilt.normalized;
        _heading = _axis;
        _currentPos = _mouthCentre;

        _sizeScale = Mathf.Lerp(0.5f, 1.2f, pr.prominence);
        _currentRadius = mouthRadius * _sizeScale;
        _ringCount = 0;
        _prevRing = null;
        _seeded = true;
    }

    private void AddRing(TimbralProfile profile)
    {
        //Breathing: energy at birth nudges this ring's radius//
        float breath = 1f + (profile.RealtimeEnergy - 0.5f) * 2f * breathAmount;
        float radius = _currentRadius * Mathf.Max(0.05f, breath);
        float turnDeg = baseCurve + energyCurve * profile.RealtimeEnergy;
        Vector3 refUp = Mathf.Abs(Vector3.Dot(_heading, _axis)) > 0.99f ? Vector3.right : _axis;
        Vector3 right = Vector3.Cross(_heading, refUp).normalized;
        Vector3 up = Vector3.Cross(right, _heading).normalized;
        _steerPhase += Time.deltaTime * (0.3f + profile.RealtimeEnergy);
        float steerX = (Mathf.PerlinNoise(_steerPhase, profile.RealtimeCentroid) - 0.5f) * 2f;
        float steerY = (Mathf.PerlinNoise(profile.RealtimeFlatness, _steerPhase) - 0.5f) * 2f;
        Vector3 steer = right * steerX + up * steerY;
        if (steer.sqrMagnitude < 1e-4f) steer = right;

        _heading = Vector3.RotateTowards(_heading, (_heading + steer.normalized).normalized,
                                         turnDeg * Mathf.Deg2Rad, 0f).normalized;

        //Gently bias back toward the original axis so it keeps generally receding.//
        _heading = Vector3.Slerp(_heading, _axis, headingBias * 0.02f).normalized;

        _currentPos += _heading * depthStep;
        Vector3 centre = _currentPos;

        Vector3 ringRight = Vector3.Cross(_heading, Vector3.up);
        if (ringRight.sqrMagnitude < 1e-4f) ringRight = Vector3.Cross(_heading, Vector3.right);
        ringRight.Normalize();
        Vector3 ringUp = Vector3.Cross(ringRight, _heading).normalized;

        Vector3[] ring = new Vector3[ringSegments];
        for (int i = 0; i < ringSegments; i++)
        {
            float ang = (i / (float)ringSegments) * Mathf.PI * 2f;
            ring[i] = centre + ringRight * (Mathf.Cos(ang) * radius) + ringUp * (Mathf.Sin(ang) * radius);
        }

        Color colour = _prism != null ? _prism.RealtimeColour : Color.cyan;

        //Edge loop for this ring//
        BuildRingEdge(ring, colour);

        //Wall band connecting the previous ring to this one//
        if (_prevRing != null && wallOpacity > 0f)
            BuildWallBand(_prevRing, ring, colour);

        _prevRing = ring;
        _ringCount++;

        //Narrow toward the vanishing point for the next ring//
        _currentRadius *= taper;
    }

    //Thin ring outline as a closed LineRenderer//
    private void BuildRingEdge(Vector3[] ring, Color colour)
    {
        GameObject go = new GameObject("RingEdge");
        go.transform.SetParent(_root.transform);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = true;
        lr.material = _edgeMaterial;
        lr.positionCount = ring.Length;
        lr.SetPositions(ring);
        lr.startWidth = edgeWidth;
        lr.endWidth = edgeWidth;
        lr.numCornerVertices = 1;
        lr.startColor = colour;
        lr.endColor = colour;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
    }

    //Translucent quad band between two consecutive rings (the funnel wall segment)//
    private void BuildWallBand(Vector3[] a, Vector3[] b, Color colour)
    {
        int seg = a.Length;
        Vector3[] verts = new Vector3[seg * 2];
        for (int i = 0; i < seg; i++)
        {
            verts[i] = a[i];
            verts[i + seg] = b[i];
        }

        int[] tris = new int[seg * 6];
        int t = 0;
        for (int i = 0; i < seg; i++)
        {
            int next = (i + 1) % seg;
            int a0 = i, a1 = next, b0 = i + seg, b1 = next + seg;
            tris[t++] = a0; tris[t++] = b0; tris[t++] = a1;
            tris[t++] = a1; tris[t++] = b0; tris[t++] = b1;
        }

        Mesh mesh = new Mesh { vertices = verts, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GameObject go = new GameObject("WallBand");
        go.transform.SetParent(_root.transform);
        go.AddComponent<MeshFilter>().mesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        Material m = new Material(_wallMaterial);
        Color wc = colour; wc.a = wallOpacity;
        m.color = wc;
        mr.material = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    private void BuildMaterials()
    {
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");

        //Edges: sprites shader so LineRenderer vertex colours show//
        Shader lineShader = Shader.Find("Sprites/Default");
        if (lineShader == null) lineShader = unlit;
        _edgeMaterial = new Material(lineShader);

        //Wall: transparent unlit, double-sided so the tunnel shows from inside and out//
        _wallMaterial = new Material(unlit);
        _wallMaterial.SetFloat("_Surface", 1f);
        _wallMaterial.SetFloat("_Blend", 0f);
        _wallMaterial.SetFloat("_SrcBlend", 5f);
        _wallMaterial.SetFloat("_DstBlend", 10f);
        _wallMaterial.SetFloat("_ZWrite", 0f);
        _wallMaterial.SetFloat("_Cull", 0f);
        _wallMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _wallMaterial.renderQueue = 3000;
    }

    private void OnDestroy()
    {
        if (_wallMaterial != null) Destroy(_wallMaterial);
        if (_edgeMaterial != null) Destroy(_edgeMaterial);
    }
}
