using System.Collections.Generic;
using UnityEngine;

//GridGratingGenerator - Kluver Category 3 (Lattices, Honeycombs, Gratings), grating subcategory//

//Builds nested rectangular frames on a tilted plane: concentric square outlines, one stepping outward beyond the last, added ON THE BEAT//
//Rigid, geometric, architectural — the beat literally builds the structure, frame by frame//

//Distinct from the other generators it might be confused with://
//Honeycomb (same category) is ORGANIC tessellation, hexagons growing cell by cell, and it wants regular + FLAT (synthetic texture). GridGrating wants regular + PERCUSSIVE//
//ConcentricRings is the CIRCULAR answer to the same "concentric" idea, and it ripples outward organically like water//
//GridGrating steps outward in hard geometric increments on the beat. One is water, one is architecture//

//Timbral home (weight = R * X * C): regular (metronomic), percussive (punchy), and bright//
//Hard electronic — Levels, Sandstorm, industrial//

//Acoustic -> visual://
//Frame added: on each detected beat (the structure is built by the rhythm)//
//Frame proportion: brightness (bright = wide flat rectangles, dark = squarer)//
//Nesting precision: rhythmic regularity (metronomic = perfectly even steps, looser = slightly irregular spacing, so the grid itself reads the rhythm's tightness)//
//Colour: RealtimeColour at each frame's birth//
//Prominence: panel position + overall scale//

public class GridGratingGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;

    [Header("Grating")]
    [SerializeField] private float baseStep = 2.2f;
    [SerializeField] private int maxFrames = 60;
    [SerializeField] private float innerSize = 2f;
    [SerializeField] private float lineWidth = 0.08f;

    [Header("Proportion")]
    [SerializeField] private float minAspect = 1.0f;  
    [SerializeField] private float maxAspect = 2.4f;  

    [Header("Placement")]
    [SerializeField] private float maxPanelOffset = 30f;

    [Header("Onset Detection (Dixon 2001)")]
    [SerializeField] private int fluxHistorySize = 43;
    [SerializeField] private float onsetSensitivity = 1.5f;
    [SerializeField] private float refractorySeconds = 0.2f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _lineMaterial;
    private readonly List<float> _fluxHistory = new List<float>();

    private bool _active = false;
    private bool _seeded = false;
    private float _timeSinceLastFrame = 0f;

    private Vector3 _panelCentre;
    private Quaternion _planeRotation;
    private float _sizeScale = 1f;
    private float _currentExtent;
    private int _frameCount = 0;

    public void SetPrism(PRISMGenerator prism)
    {
        _prism = prism;
    }

    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("GridGrating_Root");
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.GridGrating);

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        _lineMaterial = new Material(shader);

        _fluxHistory.Clear();
        _active = false;
        _seeded = false;
        _frameCount = 0;
        _timeSinceLastFrame = 0f;

        Debug.Log("PRISM GridGratingGenerator: Initialised");
    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightGridGrating;
        _active = weight >= activationThreshold;

        _timeSinceLastFrame += Time.deltaTime;

        float flux = profile.RealtimeFluxRaw;
        PushFlux(flux);

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.GridGrating) : Prominence.Silent;

        if (!_active) return;

        if (!_seeded) SeedPanel(pr);

        int effectiveMax = Mathf.Max(3, Mathf.RoundToInt(maxFrames * pr.prominence * pr.prominence));
        if (_frameCount >= effectiveMax) return;

        if (_timeSinceLastFrame < refractorySeconds) return;
        if (!IsOnset(flux)) return;

        AddFrame(profile, pr);
        _timeSinceLastFrame = 0f;
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

    private void SeedPanel(Prominence pr)
    {
        _panelCentre = transform.position + Random.onUnitSphere * (maxPanelOffset * (1f - pr.centrality));
        _planeRotation = Random.rotationUniform;
        _sizeScale = Mathf.Lerp(0.5f, 1.2f, pr.prominence);
        _currentExtent = innerSize * _sizeScale;
        _frameCount = 0;
        _seeded = true;
    }

    private void AddFrame(TimbralProfile profile, Prominence pr)
    {
        float regularity = profile.RealtimeRhythmicRegularity;
        float jitter = Mathf.Lerp(0.45f, 0f, regularity); //0 = perfectly even//
        float step = baseStep * _sizeScale * (1f + Random.Range(-jitter, jitter));

        _currentExtent += step;

        float aspect = Mathf.Lerp(minAspect, maxAspect, profile.RealtimeCentroid);
        float halfW = _currentExtent * aspect * 0.5f;
        float halfH = _currentExtent * 0.5f;

        Vector3[] corners = new Vector3[4]
        {
            LocalToWorld(-halfW, -halfH),
            LocalToWorld( halfW, -halfH),
            LocalToWorld( halfW,  halfH),
            LocalToWorld(-halfW,  halfH)
        };

        Color colour = _prism != null ? _prism.RealtimeColour : Color.white;

        GameObject frame = new GameObject("Frame");
        frame.transform.SetParent(_root.transform);

        LineRenderer lr = frame.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = true; 
        lr.material = _lineMaterial;
        lr.positionCount = 4;
        lr.SetPositions(corners);

        float w = lineWidth * Mathf.Lerp(0.6f, 1.3f, pr.prominence);
        lr.startWidth = w;
        lr.endWidth = w;
        lr.numCornerVertices = 0;
        lr.numCapVertices = 0;
        lr.startColor = colour;
        lr.endColor = colour;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        _frameCount++;
    }

    private Vector3 LocalToWorld(float x, float y)
    {
        return _panelCentre + _planeRotation * new Vector3(x, y, 0f);
    }

    private void OnDestroy()
    {
        if (_lineMaterial != null) Destroy(_lineMaterial);
    }
}
