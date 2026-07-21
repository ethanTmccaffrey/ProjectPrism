using System.Collections.Generic;
using UnityEngine;

//ConcentricRingsGenerator - Kluver Category 1 (Tunnels and Funnels), ripple subcategory//

//Sends rings rippling outward from a point on a tilted plane, like water//
//Each ring is born small, expands outward, and then FREEZES permanently at its final radius — so you watch the ripple travel, and what it leaves behind is a fixed set of concentric circles recording the song's swells//

//Distinct from TunnelDepth, which shares this category: TunnelDepth RECEDES (bores away into depth toward a vanishing point)//
//ConcentricRings EXPANDS (ripples outward across a plane)//
//Depth versus spread — the same form constant expressed oppositely//

//Timbral home (weight = tX * tF * E): sustained (not percussive), tonal, and ENERGETIC//
//Big powerful flowing music — orchestral swells, post-rock crescendos, sustained walls of tonal sound//
//Nothing else in the catalogue owns this corner//

//Acoustic -> visual://
//Ring thickness: energy at birth (powerful moments leave bolder rings)//
//Ripple rate: energy (bigger swells send rings more often)//
//Expansion speed: energy at birth (a powerful swell ripples out faster)//
//Colour: RealtimeColour at birth//
//Prominence: ripple centre position + overall scale//

public class ConcentricRingsGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;

    [Header("Ripples")]
    [SerializeField] private float rippleRate = 1.2f;
    [SerializeField] private int maxRings = 150;
    [SerializeField] private float minFinalRadius = 8f;
    [SerializeField] private float maxFinalRadius = 30f;
    [SerializeField] private float minExpandSpeed = 3f;
    [SerializeField] private float maxExpandSpeed = 12f;

    [Header("Ring Shape")]
    [SerializeField] private int ringSegments = 48;
    [SerializeField] private float minThickness = 0.06f;
    [SerializeField] private float maxThickness = 0.5f;

    [Header("Placement")]
    [SerializeField] private float maxCentreOffset = 30f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _ringMaterial;

    private bool _active = false;
    private bool _seeded = false;
    private float _emitAccumulator = 0f;
    private int _ringCount = 0;

    private Vector3 _centre;
    private Quaternion _planeRotation;
    private float _sizeScale = 1f;

    public void SetPrism(PRISMGenerator prism)
    {
        _prism = prism;
    }

    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("ConcentricRings_Root");
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.ConcentricRings);

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        _ringMaterial = new Material(shader);

        _active = false;
        _seeded = false;
        _emitAccumulator = 0f;
        _ringCount = 0;

        Debug.Log("PRISM ConcentricRingsGenerator: Initialised");
    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightConcentricRings;
        _active = weight >= activationThreshold;

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.ConcentricRings) : Prominence.Silent;

        if (!_active) return;
        if (_ringCount >= maxRings) return;

        if (!_seeded) SeedRipples(pr);

        _emitAccumulator += rippleRate * profile.RealtimeEnergy * Time.deltaTime;
        int ticks = Mathf.FloorToInt(_emitAccumulator);
        if (ticks <= 0) return;
        _emitAccumulator -= ticks;

        for (int i = 0; i < ticks && _ringCount < maxRings; i++)
            EmitRing(profile, pr);
    }

    public void Deactivate()
    {
        _active = false;
    }

    private void SeedRipples(Prominence pr)
    {
        _centre = transform.position + Random.onUnitSphere * (maxCentreOffset * (1f - pr.centrality));
        _planeRotation = Random.rotationUniform;
        _sizeScale = Mathf.Lerp(0.5f, 1.2f, pr.prominence);
        _seeded = true;
    }

    private void EmitRing(TimbralProfile profile, Prominence pr)
    {
        float e = profile.RealtimeEnergy;

        float thickness = Mathf.Lerp(minThickness, maxThickness, e) * Mathf.Lerp(0.6f, 1f, pr.prominence);

        float finalRadius = Mathf.Lerp(minFinalRadius, maxFinalRadius, e) * _sizeScale;
        float expandSpeed = Mathf.Lerp(minExpandSpeed, maxExpandSpeed, e);

        Color colour = _prism != null ? _prism.RealtimeColour : Color.white;

        GameObject ringGO = new GameObject("Ring");
        ringGO.transform.SetParent(_root.transform);

        LineRenderer lr = ringGO.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = true;
        lr.material = _ringMaterial;
        lr.positionCount = ringSegments;
        lr.startWidth = thickness;
        lr.endWidth = thickness;
        lr.numCornerVertices = 1;
        lr.startColor = colour;
        lr.endColor = colour;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        var ripple = ringGO.AddComponent<RingRipple>();
        ripple.Begin(_centre, _planeRotation, ringSegments, finalRadius, expandSpeed, lr);

        _ringCount++;
    }

    private void OnDestroy()
    {
        if (_ringMaterial != null) Destroy(_ringMaterial);
    }
}

public class RingRipple : MonoBehaviour
{
    private Vector3 _centre;
    private Quaternion _plane;
    private int _segments;
    private float _finalRadius;
    private float _speed;
    private LineRenderer _lr;

    private float _radius = 0.2f;
    private bool _frozen = false;

    public void Begin(Vector3 centre, Quaternion plane, int segments, float finalRadius, float speed, LineRenderer lr)
    {
        _centre = centre;
        _plane = plane;
        _segments = segments;
        _finalRadius = finalRadius;
        _speed = speed;
        _lr = lr;
        Redraw();
    }

    private void Update()
    {
        if (_frozen || _lr == null) return;

        _radius += _speed * Time.deltaTime;

        if (_radius >= _finalRadius)
        {
            _radius = _finalRadius;
            Redraw();
            _frozen = true;
            enabled = false;
            return;
        }

        Redraw();
    }

    private void Redraw()
    {
        Vector3[] pts = new Vector3[_segments];
        for (int i = 0; i < _segments; i++)
        {
            float ang = (i / (float)_segments) * Mathf.PI * 2f;
            Vector3 local = new Vector3(Mathf.Cos(ang) * _radius, Mathf.Sin(ang) * _radius, 0f);
            pts[i] = _centre + _plane * local;
        }
        _lr.positionCount = _segments;
        _lr.SetPositions(pts);
    }
}
