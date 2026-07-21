#pragma warning disable 0414
using UnityEngine;

public class ParticleLayer : MonoBehaviour
{
    [Header("Ambient System")]
    [SerializeField] private int ambientMaxParticles = 15000;
    [SerializeField] private float ambientRadius = 350f;

    [Header("Burst System")]
    [SerializeField] private int burstMaxParticles = 500;

    [Header("Bass Trigger")]
    [SerializeField] private float bassThreshold = 0.02f; //Minimum bass to trigger burst//
    [SerializeField] private float burstCooldown = 0.15f; //Seconds between bursts//

    //Particle Systems//
    private ParticleSystem _ambientSystem;
    private ParticleSystem.MainModule _ambientMain;
    private ParticleSystem.EmissionModule _ambientEmission;
    private ParticleSystem.ShapeModule _ambientShape;

    //Left burst system: fires from LeftSourcePoint, driven by left channel bass//
    private ParticleSystem _burstSystemLeft;
    private ParticleSystem.MainModule _burstMainLeft;

    //Rihgt burst system: fires from RightSourcePoint, driven by right channel bass//
    private ParticleSystem _burstSystemRight;
    private ParticleSystem.MainModule _burstMainRight;

    //Static qualitties cached at Init//
    private float _spaceQuality;
    private float _lightQuality;
    private float _colourQuality;
    private float _motionQuality;
    private float _scaleQuality;
    private float _formQuality;

    //Derived Colours//
    private Color _primaryColour;
    private Color _secondaryColour;

    //Burst timing//
    private float _burstCooldownLeft = 0f;
    private float _burstCooldownRight = 0f;
    private float _prevBassLeft = 0f;
    private float _prevBassRight = 0f;

    //Throttle caches//
    private float _lastAmbientSpeed = 0f;
    private float _lastEmissionRate = 0f;
    private float _lastBurstSpeedLeft = 0f;
    private float _lastBurstSpeedRight = 0f;
    private float _lastAmbientRadius = 0f;
    private float _lastMaxParticles = 0f;
    private float _smoothedRadius = 0f;

    public void Init(PRISMGenerator generator)
    {
        //Cache static qualities//
        _spaceQuality = generator.Space;
        _lightQuality = generator.Light;
        _colourQuality = generator.Colour;
        _motionQuality = generator.Motion;
        _scaleQuality = generator.Scale;
        _formQuality = generator.Form;

        //use the centerally derived colours//
        _primaryColour = generator.PrimaryColour;
        _secondaryColour = generator.SecondaryColour;

        //Build both systems//
        BuildAmbientSystem();

        Debug.Log($"PRISM ParticleLayer: Initialised. " + $"Primary: #{ColorUtility.ToHtmlStringRGB(_primaryColour)}  " + $"Secondary: #{ColorUtility.ToHtmlStringRGB(_secondaryColour)}");
    }

    private void BuildAmbientSystem()
    {
        //Create child GameObject so it doesn't interface with burst system//
        GameObject ambientGO = new GameObject("AmbientParticles");
        ambientGO.transform.SetParent(transform);
        ambientGO.transform.localPosition = Vector3.zero;

        _ambientSystem = ambientGO.AddComponent<ParticleSystem>();

        //Main module//
        _ambientMain = _ambientSystem.main;
        _ambientMain.loop = true;
        _ambientMain.simulationSpace = ParticleSystemSimulationSpace.World;

        //Particle count driven by Space Quality, vast tracks fill more space//
        _ambientMain.maxParticles = Mathf.RoundToInt(Mathf.Lerp(300, ambientMaxParticles, _scaleQuality));

        //Lifetime: slow tracks have long-lived particles, fast tracks shorter//
        float lifetime = Mathf.Lerp(8f, 3f, _motionQuality);
        _ambientMain.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.5f, lifetime * 1.2f);

        //Speed: Motion Quality drives how fast particles drift//
        float speed = Mathf.Lerp(0.3f, 2.5f, _motionQuality);
        _ambientMain.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);

        //Size: Scale quality drives variance: uniform tracks have similar sized particles//
        //High contrast tracks have wildly varying sizes//
        float baseSize = Mathf.Lerp(0.05f, 0.2f, _lightQuality);
        float sizeVariance = Mathf.Lerp(0.01f, 0.3f, _scaleQuality);
        _ambientMain.startSize = new ParticleSystem.MinMaxCurve(baseSize, baseSize + sizeVariance);

        //Colour gradient: primary -> secondary from PRSIMGenerator//
        _ambientMain.startColor = new ParticleSystem.MinMaxGradient(_primaryColour, _secondaryColour);

        //Emission rate driven by Space: vast tracks are denser//
        _ambientEmission = _ambientSystem.emission;
        _ambientEmission.enabled = true;
        float emissions = Mathf.Lerp(20f, 150f, _spaceQuality);
        _ambientEmission.rateOverTime = emissions;

        //Shape: sphere, radius driven by Space quality//
        var shape = _ambientSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = Mathf.Lerp(10f, ambientRadius, _spaceQuality);

        //Colour over lifetime: fade out at end//
        var colourOverLifetime = _ambientSystem.colorOverLifetime;
        colourOverLifetime.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(new GradientColorKey[] {
            new GradientColorKey(Color.white, 0f),
            new GradientColorKey(Color.white, 0.8f),
            new GradientColorKey(Color.white, 1f)
        },
        new GradientAlphaKey[] {
            new GradientAlphaKey(0f, 0f),
            new GradientAlphaKey(1f, 0.1f),
            new GradientAlphaKey(1f, 0.8f),
            new GradientAlphaKey(0f, 1f)
        }
        );
        colourOverLifetime.color = new ParticleSystem.MinMaxGradient(grad);

        //Noise: adds organic drift, form quality drives turbulance//
        //Smooth tracks (low Form) drift gently, angular tracks turbulate//
        var noise = _ambientSystem.noise;
        noise.enabled = true;
        noise.strength = Mathf.Lerp(0.1f, 0.8f, _formQuality);
        noise.frequency = Mathf.Lerp(0.05f, 0.4f, _motionQuality);
        noise.scrollSpeed = Mathf.Lerp(0.05f, 0.4f, _motionQuality);

        //Renderer: default additive-style particle material//
        var renderer = _ambientSystem.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material = GetParticleMaterial();

        _ambientShape = _ambientSystem.shape;
        _smoothedRadius = Mathf.Lerp(10f, ambientRadius, _spaceQuality);

        _ambientMain.prewarm = true;

        _ambientSystem.Play();
    }

    public void UpdateLayer(PRISMGenerator generator)
    {
        if (_ambientSystem == null || _burstSystemLeft == null || _burstSystemRight == null) return;

        float energy = generator.RealtimeEnergy;

        //Ambient: driven by mono energy//
        float dynamicEmission = Mathf.Lerp(10f, 200f, energy * 60f);
        if (Mathf.Abs(dynamicEmission - _lastEmissionRate) > 1f)
        {
            _ambientEmission.rateOverTime = dynamicEmission;
            _lastEmissionRate = dynamicEmission;
        }

        //Modulate ambient speed with energy//
        float dynamicSpeed = Mathf.Lerp(0.2f, 3f, energy * 80f);
        if (Mathf.Abs(dynamicSpeed - _lastAmbientSpeed) > 0.05f)
        {
            _ambientMain.startSpeed = new ParticleSystem.MinMaxCurve(dynamicSpeed * 0.5f, dynamicSpeed);
            _lastAmbientSpeed = dynamicSpeed;
        }

        //Raduis breathes with energy//
        float targetRaduis = Mathf.Lerp(Mathf.Lerp(50f, ambientRadius * 1f, _spaceQuality), Mathf.Lerp(120f, ambientRadius * 4.0f, _spaceQuality), energy * 50f);
        _smoothedRadius = Mathf.Lerp(_smoothedRadius, targetRaduis, Time.deltaTime * 2f);
        if (Mathf.Abs(_smoothedRadius - _lastAmbientRadius) > 0.3f)
        {
            _ambientShape.radius = _smoothedRadius;
            _lastAmbientRadius = _smoothedRadius;
        }

        //Particle Count scales with energy//
        int targetCount = Mathf.RoundToInt(Mathf.Lerp(Mathf.Lerp(100f, 400f, _spaceQuality), ambientMaxParticles, energy * 50f));
        if(Mathf.Abs(targetCount - _lastMaxParticles) > 20f)
        {
            _ambientMain.maxParticles = targetCount;
            _lastMaxParticles = targetCount;
        }
    }

    //Returns a default URP particle material//
    //Can be swapped for a custom additvie glow material later//
    private Material GetParticleMaterial()
    {
        //Use URP's default particles lit shader//
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Lit");
        
        if (shader == null)
        {
            //Fallback to legacy default//
            shader = Shader.Find("Particles/Standard Unlit");
        }
        if(shader == null)
        {
            Debug.LogWarning("PRSIM ParticleLayer: Could not find particle shader, using default.");
            return new Material(Shader.Find("Standard"));
        }

        Material mat = new Material(shader);
        mat.SetFloat("_Surface", 1f); //Transparent//
        mat.SetFloat("_Blend", 2f); //Additive blending//
        mat.SetFloat("_SoftParticlesEnabled", 1f);
        mat.SetFloat("_SoftParticlesFadeDistance", 2f);
        mat.enableInstancing = true;
        return mat;
    }
}
