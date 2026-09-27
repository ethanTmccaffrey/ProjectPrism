using System.Collections.Generic;
using UnityEngine;

//TunnelDepthGenerator//
//Klüver Category 1 (Tunnels / Funnels)//
//Bores two mirrored tunnels inward through the cut eye sockets receding into the head, Triggered by sustain x tonality x sparseness at low energy//
public class TunnelDepthGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;

    [Header("Head Reference")]
    [SerializeField] private HeadField head;

    [Header("Funnel Shape")]
    [SerializeField, Range(0.4f, 1.2f)] private float mouthRadiusFraction = 0.9f;
    [SerializeField] private float fallbackMouthRadius = 6f;
    [SerializeField, Range(0.8f, 0.999f)] private float taper = 0.965f;
    [SerializeField] private float depthStep = 2.5f;
    [SerializeField] private int ringSegments = 24;
    [SerializeField] private int maxRings = 120;
    [SerializeField] private float growthRate = 4f;

    [Header("Breathing")]
    [SerializeField] private float breathAmount = 0.25f;

    [Header("Bore Direction")]
    [SerializeField] private float convergeDepth = 1.5f;
    [SerializeField] private float baseCurve = 2f;
    [SerializeField] private float energyCurve = 5f;
    [SerializeField, Range(0f, 0.3f)] private float axisBias = 0.12f;

    [Header("Appearance")]
    [SerializeField, Range(0f, 0.5f)] private float wallOpacity = 0.1f;
    [SerializeField] private float edgeWidth = 0.1f;

    private class Tunnel
    {
        public Vector3 axis;       
        public Vector3 currentPos; 
        public Vector3 heading;    
        public float currentRadius;
        public float steerPhase;
        public int ringCount;
        public Vector3[] prevRing;
    }

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _wallMaterial;
    private Material _edgeMaterial;

    private bool _active = false;
    private bool _seeded = false;
    private float _growthAccumulator = 0f;
    private Tunnel[] _tunnels;

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
        _tunnels = null;

    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightTunnelDepth;
        _active = weight >= activationThreshold;

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.TunnelDepth) : Prominence.Silent;

        if (!_active) return;

        if (!_seeded)
        {
            bool headReady = head != null && head.Loaded && head.HasMesh
                             && head.FieldBounds.size.sqrMagnitude > 1e-3f;
            if (!headReady) return;
            SeedTunnels(pr);
        }
        if (_tunnels == null) return;

        bool anyGrowing = false;
        for (int t = 0; t < _tunnels.Length; t++)
        {
            if (_tunnels[t].ringCount < maxRings) { anyGrowing = true; break; }
        }
        if (!anyGrowing) return;

        float rate = growthRate * Mathf.Lerp(0.5f, 1.5f, profile.RealtimeEnergy);
        _growthAccumulator += rate * Time.deltaTime;
        int ticks = Mathf.FloorToInt(_growthAccumulator);
        if (ticks <= 0) return;
        _growthAccumulator -= ticks;

        for (int i = 0; i < ticks; i++)
        {
            for (int t = 0; t < _tunnels.Length; t++)
            {
                if (_tunnels[t].ringCount < maxRings) AddRing(_tunnels[t], profile);
            }
        }
    }

    public void Deactivate()
    {
        _active = false;
    }

    private void SeedTunnels(Prominence pr)
    {
        float sizeScale = Mathf.Lerp(0.5f, 1.2f, pr.prominence);

        bool haveEyes = head != null && head.Loaded && head.EyeCutEnabled;

        Vector3 leftMouth, rightMouth, centre;
        float mouthRadius;

        if (haveEyes)
        {
            leftMouth = head.LeftEyeWorld;
            rightMouth = head.RightEyeWorld;
            mouthRadius = head.EyeRadiusWorld * mouthRadiusFraction;

            Vector3 mid = (leftMouth + rightMouth) * 0.5f;
            Vector3 intoHead = (head.transform.position - mid);
            if (intoHead.sqrMagnitude < 1e-3f)
            {
                Vector3 across = (rightMouth - leftMouth).normalized;
                intoHead = Vector3.Cross(across, Vector3.up);
            }
            intoHead.Normalize();
            float eyeGap = Vector3.Distance(leftMouth, rightMouth);
            centre = mid + intoHead * (eyeGap * convergeDepth);
        }
        else
        {
            centre = transform.position;
            leftMouth = centre + Vector3.left * 10f + Vector3.up * 4f + Vector3.forward * 12f;
            rightMouth = centre + Vector3.right * 10f + Vector3.up * 4f + Vector3.forward * 12f;
            mouthRadius = fallbackMouthRadius;
        }

        _tunnels = new Tunnel[2];
        _tunnels[0] = MakeTunnel(leftMouth, centre, mouthRadius * sizeScale);
        _tunnels[1] = MakeTunnel(rightMouth, centre, mouthRadius * sizeScale);
        _seeded = true;
    }

    private Tunnel MakeTunnel(Vector3 mouth, Vector3 centre, float radius)
    {
        Vector3 inward = (centre - mouth);
        if (inward.sqrMagnitude < 1e-4f) inward = Vector3.forward;
        inward.Normalize();

        return new Tunnel
        {
            axis = inward,
            heading = inward,
            currentPos = mouth,
            currentRadius = radius,
            steerPhase = Random.Range(0f, 100f),
            ringCount = 0,
            prevRing = null
        };
    }

    private void AddRing(Tunnel tun, TimbralProfile profile)
    {
        float breath = 1f + (profile.RealtimeEnergy - 0.5f) * 2f * breathAmount;
        float radius = tun.currentRadius * Mathf.Max(0.05f, breath);
        float turnDeg = baseCurve + energyCurve * profile.RealtimeEnergy;

        Vector3 refUp = Mathf.Abs(Vector3.Dot(tun.heading, tun.axis)) > 0.99f ? Vector3.right : tun.axis;
        Vector3 right = Vector3.Cross(tun.heading, refUp).normalized;
        Vector3 up = Vector3.Cross(right, tun.heading).normalized;

        tun.steerPhase += Time.deltaTime * (0.3f + profile.RealtimeEnergy);
        float steerX = (Mathf.PerlinNoise(tun.steerPhase, profile.RealtimeCentroid) - 0.5f) * 2f;
        float steerY = (Mathf.PerlinNoise(profile.RealtimeFlatness, tun.steerPhase) - 0.5f) * 2f;
        Vector3 steer = right * steerX + up * steerY;
        if (steer.sqrMagnitude < 1e-4f) steer = right;

        tun.heading = Vector3.RotateTowards(tun.heading, (tun.heading + steer.normalized).normalized, turnDeg * Mathf.Deg2Rad, 0f).normalized;

        tun.heading = Vector3.Slerp(tun.heading, tun.axis, axisBias).normalized;

        tun.currentPos += tun.heading * depthStep;
        Vector3 centre = tun.currentPos;

        Vector3 ringRight = Vector3.Cross(tun.heading, Vector3.up);
        if (ringRight.sqrMagnitude < 1e-4f) ringRight = Vector3.Cross(tun.heading, Vector3.right);
        ringRight.Normalize();
        Vector3 ringUp = Vector3.Cross(ringRight, tun.heading).normalized;

        Vector3[] ring = new Vector3[ringSegments];
        for (int i = 0; i < ringSegments; i++)
        {
            float ang = (i / (float)ringSegments) * Mathf.PI * 2f;
            ring[i] = centre + ringRight * (Mathf.Cos(ang) * radius) + ringUp * (Mathf.Sin(ang) * radius);
        }

        Color colour = _prism != null ? _prism.RealtimeColour : Color.cyan;

        BuildRingEdge(ring, colour);

        if (tun.prevRing != null && wallOpacity > 0f) BuildWallBand(tun.prevRing, ring, colour);

        tun.prevRing = ring;
        tun.ringCount++;

        tun.currentRadius *= taper;
    }

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

        Shader lineShader = Shader.Find("Sprites/Default");
        if (lineShader == null) lineShader = unlit;
        _edgeMaterial = new Material(lineShader);

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

    private void OnDrawGizmos()
    {
        if (head == null) return;
        if (!head.Loaded || !head.EyeCutEnabled) return;

        Vector3 L = head.LeftEyeWorld;
        Vector3 R = head.RightEyeWorld;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(L, head.EyeRadiusWorld);
        Gizmos.DrawWireSphere(R, head.EyeRadiusWorld);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(L, R);
    }
}
