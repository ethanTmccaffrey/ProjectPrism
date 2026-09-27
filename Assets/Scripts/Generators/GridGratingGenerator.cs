using System.Collections.Generic;
using UnityEngine;

//GridGratingGenerator//
//Klüver Category 3 (Lattices / Honeycombs / Gratings)//
//Paints parallel banded gratings across the skull one per beat, Triggered by rhythmic regularity x percussiveness, brightened by centroid//

public class GridGratingGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;

    [Header("Grating")]
    [SerializeField] private float bandStep = 6f;
    [SerializeField] private int maxBands = 40;

    [Header("Placement & Painting")]
    [SerializeField] private HeadPlacement placement;
    [SerializeField] private float bandRadius = 1.0f;
    [SerializeField, Range(0f, 1f)] private float paintBrightness = 0.5f;
    [SerializeField] private float paintStrength = 1f;

    [Header("Onset Detection (Dixon 2001)")]
    [SerializeField] private int fluxHistorySize = 43;
    [SerializeField] private float onsetSensitivity = 1.5f;
    [SerializeField] private float refractorySeconds = 0.2f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private readonly List<float> _fluxHistory = new List<float>();

    private bool _active = false;
    private bool _seeded = false;
    private float _timeSinceLastBand = 0f;

    private Vector3 _gratingCentre;
    private Vector3 _stepAxis;     
    private int _bandCount = 0;
    private int _bandSign = 1;       
    private int _bandIndex = 0;

    public void SetPrism(PRISMGenerator prism) { _prism = prism; }

    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("GridGrating_Root");
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.GridGrating);

        _fluxHistory.Clear();
        _active = false;
        _seeded = false;
        _timeSinceLastBand = 0f;
        _bandCount = 0;
        _bandSign = 1;
        _bandIndex = 0;

    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightGridGrating;
        _active = weight >= activationThreshold;

        _timeSinceLastBand += Time.deltaTime;

        float flux = profile.RealtimeFluxRaw;
        PushFlux(flux);

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.GridGrating) : Prominence.Silent;

        if (!_active) return;

        int effectiveMax = Mathf.Max(3, Mathf.RoundToInt(maxBands * Mathf.Lerp(0.4f, 1f, pr.prominence)));
        if (_bandCount >= effectiveMax) return;

        if (!_seeded) { SeedGrating(pr); return; }

        if (_timeSinceLastBand < refractorySeconds) return;
        if (!IsOnset(flux)) return;

        AddBand(profile);
        _timeSinceLastBand = 0f;
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

    private void SeedGrating(Prominence pr)
    {
        if (placement != null && placement.HasSkullMesh && placement.RandomSkullMeshPoint(out Vector3 sp, out _)) _gratingCentre = sp;
        else if (placement != null && placement.Ready) _gratingCentre = placement.CavityCentre;
        else  _gratingCentre = transform.position;

        _stepAxis = Random.onUnitSphere;
        _bandCount = 0;
        _bandSign = 1;
        _bandIndex = 0;
        _seeded = true;
    }

    private void AddBand(TimbralProfile profile)
    {
        if (placement == null || !placement.HasSkullMesh) return;

        float regularity = profile.RealtimeRhythmicRegularity;
        float jitter = Mathf.Lerp(0.4f, 0f, regularity);  
        int step = (_bandIndex + 1) / 2;
        float offset = _bandSign * step * bandStep * (1f + Random.Range(-jitter, jitter));

        Vector3 planePoint = _gratingCentre + _stepAxis * offset;
        Color colour = (_prism != null ? _prism.RealtimeColour : Color.white) * paintBrightness;

        PaintBand(planePoint, _stepAxis, colour);

        _bandSign = -_bandSign;
        _bandIndex++;
        _bandCount++;
    }

    private void PaintBand(Vector3 planePoint, Vector3 planeNormal, Color colour)
    {
        placement.PaintPlane(planePoint, planeNormal, bandRadius, colour, paintStrength);
        placement.ApplySkullPaint();
    }

    private void OnDestroy() { }
}
