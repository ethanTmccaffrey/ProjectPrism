using System.Xml;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PostProcessingLayer : MonoBehaviour
{
    [Header("Bloom Settings")]
    [SerializeField] private float bloomBaseIntensity = 0.5f;
    [SerializeField] private float bloomBassMultiplier = 4f;
    [SerializeField] private float bloomSmoothSpeed = 8f;

    [Header("Vignette Settings")]
    [SerializeField] private float vignetteMin = 0.15f;
    [SerializeField] private float vignetteMax = 0.5f;

    [Header("Motion Blur Settings")]
    [SerializeField] private float motionBlurMax = 0.35f;

    [Header("Colour Settings")]
    [SerializeField] private float hueShiftRange = 40f; // +/- Degrees//
    [SerializeField] private float saturationRange = 60f; // +/- From Neutral//
    [SerializeField] private float exposureRange = 1.2f; // +/- EV//

    //Volume override references//
    private Volume _volume;
    private Bloom _bloom;
    private Vignette _vignette;
    private MotionBlur _motionBlur;
    private ColorAdjustments _colorAdjustments;

    //Smoothed values to avoid jitter//
    private float _smoothedBloomIntensity = 0f;
    private float _smoothedExposure = 0f;

    //Static qualities cached at Init//
    private float _lightQuality;
    private float _motionQuality;
    private float _colourQuality;
    private float _spaceQuality;

    private float _lastBloomIntesnity = -1f;
    private float _lastSaturation = -999f;
    private float _lastExposure = -999f;

    public void Init(PRISMGenerator generator)
    {
        //Cache static qualities//
        _lightQuality = generator.Light;
        _motionQuality = generator.Motion;
        _colourQuality = generator.Colour;
        _spaceQuality = generator.Space;

        //Get the volume component from this GameObject//
        _volume = FindFirstObjectByType<Volume>();
        if( _volume == null )
        {
            Debug.LogWarning("PRISM PostProcessingLayer: No Volume component found on this GameObject.");
            return;
        }

        //Try to grab each override from the profile//
        if (!_volume.profile.TryGet(out _bloom)) Debug.LogWarning("PRSIM PostProcessingLayer: No Bloom override found in Volume profile.");
        if (!_volume.profile.TryGet(out _vignette)) Debug.LogWarning("PRSIM PostProcessingLayer: No Vignette override found in Volume profile.");
        if (!_volume.profile.TryGet(out _motionBlur)) Debug.LogWarning("PRSIM PostProcessingLayer: No Motion Blur override found in Volume profile.");
        if (!_volume.profile.TryGet(out _colorAdjustments)) Debug.LogWarning("PRSIM PostProcessingLayer: No Colour Adjustments override found in Volume profile.");

        //Set static values that won't cahnge frame to frame//
        ApplyStaticQualities();

        Debug.Log("PRISM PostProcessingLayer: Initialised.");
    }

    private void ApplyStaticQualities()
    {
        //Vignette: dark/cool tracks (low light) get heavier vignette//
        //Light Quality: 0 = cool/dark, 1 = warm/bright//
        if(_vignette != null)
        {
            float vignetteIntensity = Mathf.Lerp(vignetteMax, vignetteMin, _lightQuality);
            _vignette.intensity.Override(vignetteIntensity);
        }

        //Motion Blur: fast tracks (high motion) get more blur//
        if(_motionBlur != null)
        {
            float blurIntensity = Mathf.Lerp(0f, motionBlurMax, _motionQuality);
            _motionBlur.intensity.Override(blurIntensity);
        }

        //Hue shift: cool tracks shift toward blue (-), warm tracks toward orange (+)//
        //Colour quality: 0 = cool hue, 1 = warm hue//
        if(_colorAdjustments != null)
        {
            float hueShift = Mathf.Lerp(-hueShiftRange, hueShiftRange, _colourQuality);
            _colorAdjustments.hueShift.Override(hueShift);
        }
    }

    public void UpdateLayer(PRISMGenerator generator)
    {
        if (_volume == null) return;

        float bass = generator.RealtimeBass;
        float mid = generator.RealtimeMid;
        float high = generator.RealtimeHigh;
        float energy = generator.RealtimeEnergy;

        UpdateBloom(bass, energy);
        UpdateColourAdjustments(energy, mid, high);
    }

    public void UpdateBloom(float bass, float energy)
    {
        if (_bloom == null) return;

        //Bass hits drive bloom bursts, smooth to avoid strobing//
        float targetInentsity = bloomBaseIntensity + (bass * bloomBassMultiplier);
        _smoothedBloomIntensity = Mathf.Lerp(_smoothedBloomIntensity, targetInentsity, Time.deltaTime * bloomSmoothSpeed);

        if (Mathf.Abs(_smoothedBloomIntensity - _lastBloomIntesnity) > 0.1f)
        {
            _bloom.intensity.Override(_smoothedBloomIntensity);
            _lastBloomIntesnity = _smoothedBloomIntensity;
        }
    }

    private void UpdateColourAdjustments(float energy, float mid, float high)
    {
        if(_colorAdjustments == null) return;

        //Saturation: rises with overall energy, quiet passages fell desaturated//
        //Loud moments feel vivid. Anchored around neutral (0 in URP = no change)//
        float targetSaturation = Mathf.Lerp(-saturationRange * 0.5f, saturationRange, energy * 80f);
        targetSaturation = Mathf.Clamp(targetSaturation, -100f, 100f);

        if(Mathf.Abs(targetSaturation - _lastSaturation) > 0.5f)
        {
            _colorAdjustments.saturation.Override(targetSaturation);
            _lastSaturation = targetSaturation;
        }
        
        //Post exposure: mid + high energy brightens the scene slightly//
        //Space quality (dynamic range) scales how dramatic this is//
        float targetExposure = (mid + high) * exposureRange * _spaceQuality;
        _smoothedExposure = Mathf.Lerp(_smoothedExposure, targetExposure, Time.deltaTime * 4f);

        if (Mathf.Abs(_smoothedExposure - _lastExposure) > 0.01f)
        {
            _colorAdjustments.postExposure.Override(Mathf.Clamp(_smoothedExposure, -exposureRange, exposureRange));
            _lastExposure = _smoothedExposure;
        }
    }
}
