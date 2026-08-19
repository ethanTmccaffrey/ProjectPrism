using System.Collections.Generic;
using UnityEngine;

public class ConcentricRingsGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;

    [Header("Ripples")]
    [SerializeField] private float rippleRate = 0.5f;
    [SerializeField] private int maxRings = 150;
    [SerializeField] private float minFinalAngle = 0.25f;
    [SerializeField] private float maxFinalAngle = 1.6f;
    [SerializeField] private float minExpandSpeed = 0.15f;
    [SerializeField] private float maxExpandSpeed = 0.5f;

    [Header("Ring Shape")]
    [SerializeField] private int ringSegments = 64;
    [SerializeField] private float minThickness = 0.2f;
    [SerializeField] private float maxThickness = 1.2f;

    [Header("Placement & Coupling")]
    [SerializeField] private HeadPlacement placement;
    [SerializeField] private SkullRipple skullRipple;
    [SerializeField, Range(0f, 1f)] private float skullTintMute = 0.9f;
    [SerializeField] private float maxCentreOffset = 30f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _ringMaterial;

    private bool _active = false;
    private float _emitAccumulator = 0f;
    private int _ringCount = 0;

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

    private void EmitRing(TimbralProfile profile, Prominence pr)
    {
        float e = profile.RealtimeEnergy;

        float thickness = Mathf.Lerp(minThickness, maxThickness, e) * Mathf.Lerp(0.6f, 1f, pr.prominence);
        float energyBase = Mathf.Lerp(minFinalAngle, maxFinalAngle, e);
        float finalAngle = Mathf.Lerp(minFinalAngle, energyBase, Random.value) * Mathf.Lerp(0.7f, 1.2f, pr.prominence);
        finalAngle = Mathf.Clamp(finalAngle, minFinalAngle, maxFinalAngle);
        float expandSpeed = Mathf.Lerp(minExpandSpeed, maxExpandSpeed, e);

        Color colour = _prism != null ? _prism.RealtimeColour : Color.white;
        Vector3 earDir = PickEarDirection();

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

        var ripple = ringGO.AddComponent<EllipsoidRingRipple>();
        ripple.Begin(placement, earDir, ringSegments, finalAngle, expandSpeed, lr);

        if (skullRipple != null)
        {
            Color muted = colour * skullTintMute;
            muted.a = 1f;
            skullRipple.TriggerRipple(earDir, e, muted);
        }

        _ringCount++;
    }

    private Vector3 PickEarDirection()
    {
        Vector3 left = skullRipple != null ? skullRipple.LeftOrigin : new Vector3(-1f, 0.1f, 0.15f);
        Vector3 right = skullRipple != null ? skullRipple.RightOrigin : new Vector3(1f, 0.1f, 0.15f);
        Vector3 baseDir = (Random.value < 0.5f ? left : right).normalized;
        return (baseDir + Random.insideUnitSphere * 0.08f).normalized;
    }

    private void OnDestroy()
    {
        if (_ringMaterial != null) Destroy(_ringMaterial);
    }
}

public class EllipsoidRingRipple : MonoBehaviour
{
    private HeadPlacement _placement;
    private Vector3 _earDir;  
    private int _segments;
    private float _finalAngle;
    private float _speed;
    private LineRenderer _lr;

    private float _angle = 0.02f;   
    private bool _frozen = false;

    public void Begin(HeadPlacement placement, Vector3 earDir, int segments,
                      float finalAngle, float speed, LineRenderer lr)
    {
        _placement = placement;
        _earDir = earDir.normalized;
        _segments = segments;
        _finalAngle = finalAngle;
        _speed = speed;
        _lr = lr;

        Redraw();
    }

    private void Update()
    {
        if (_frozen || _lr == null) return;

        _angle += _speed * Time.deltaTime;
        if (_angle >= _finalAngle)
        {
            _angle = _finalAngle;
            Redraw();
            _frozen = true;
            enabled = false;
            return;
        }
        Redraw();
    }

    private void Redraw()
    {
        if (_placement == null || !_placement.Ready) return;

        Vector3 radii = _placement.EllipsoidRadii;
        Vector3 centre = _placement.EllipsoidCentre;
        Vector3 earU = new Vector3(_earDir.x / radii.x, _earDir.y / radii.y, _earDir.z / radii.z).normalized;

        Vector3 tA = Vector3.Cross(earU, Vector3.up);
        if (tA.sqrMagnitude < 1e-4f) tA = Vector3.Cross(earU, Vector3.right);
        tA.Normalize();
        Vector3 tB = Vector3.Cross(earU, tA).normalized;

        float sinA = Mathf.Sin(_angle);
        float cosA = Mathf.Cos(_angle);

        Vector3[] pts = new Vector3[_segments];
        for (int i = 0; i < _segments; i++)
        {
            float t = (i / (float)_segments) * Mathf.PI * 2f;
            Vector3 dU = earU * cosA + (tA * Mathf.Cos(t) + tB * Mathf.Sin(t)) * sinA;
            pts[i] = centre + Vector3.Scale(dU, radii);
        }

        _lr.positionCount = _segments;
        _lr.SetPositions(pts);
    }
}