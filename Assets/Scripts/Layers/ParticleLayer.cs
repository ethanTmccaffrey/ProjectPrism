using UnityEngine;

public class ParticleLayer : MonoBehaviour
{
    [Header("Ambient System")]
    [SerializeField] private int ambientMaxParticles = 15000;
    [SerializeField] private float ambientRadius = 40f;

    [Header("Burst System")]
    [SerializeField] private int burstMaxParticles = 500;

    [Header("Bass Trigger")]
    [SerializeField] private float bassThreshold = 0.02f; //Minimum bass to trigger burst//
    [SerializeField] private float burstCooldown = 0.15f; //Seconds between bursts//

    //Particle Systems//
    private ParticleSystem _ambientSystem;
    private ParticleSystem _burstSystem;
    private ParticleSystem.MainModule _ambientMain;
    private ParticleSystem.MainModule _burstMain;
    private ParticleSystem.EmissionModule _ambientEmission;

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
    private float _burstCooldownTimer = 0f;
    private float _prevBass = 0f;

    private float _lastAmbientSpeed = 0f;
    private float _lastEmissionRate = 0f;
    private float _lastBurstSpeed = 0f;

    private ParticleSystem.ShapeModule _ambientShape;
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
        BuildBurstSystem();

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

    private void BuildBurstSystem()
    {
        GameObject burstGO = new GameObject("BurstParticles");
        burstGO.transform.SetParent(transform);
        burstGO.transform.localPosition = Vector3.zero;

        _burstSystem = burstGO.AddComponent<ParticleSystem>();

        _burstMain = _burstSystem.main;
        _burstMain.loop = false;
        _burstMain.playOnAwake = false;
        _burstMain.simulationSpace = ParticleSystemSimulationSpace.World;
        _burstMain.maxParticles = burstMaxParticles;

        //Short lifetime, burst particles appear and vanish//
        _burstMain.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);

        //Fast outward speed//
        float burstSpeed = Mathf.Lerp(20f, 60f, _motionQuality);
        _burstMain.startSpeed = new ParticleSystem.MinMaxCurve(burstSpeed * 0.6f, burstSpeed);

        //Burst particles are brighter and larger than ambient//
        float burstSize = Mathf.Lerp(0.1f, 0.5f, _lightQuality);
        _burstMain.startSize = new ParticleSystem.MinMaxCurve(burstSize * 0.5f, burstSize * 1.5f);

        _burstMain.startColor = _primaryColour;

        //Emission: controlled manually via Emit() calls, not auto rate//
        var emission = _burstSystem.emission;
        emission.enabled = false;

        //Shape: emit from a small sphere at centre//
        var shape = _burstSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 8f;

        //Colour over lifetime: bright flash then fade through secondary colour//
        var colourOverLifetime = _burstSystem.colorOverLifetime;
        colourOverLifetime.enabled = true;
        Gradient burstGrad = new Gradient();
        burstGrad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(_primaryColour, 0.3f),
                new GradientColorKey(_secondaryColour, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.8f, 0.3f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        colourOverLifetime.color = new ParticleSystem.MinMaxGradient(burstGrad);

        //Size over lifetime: shrink as they travel//
        var sizeOverLifetime = _burstSystem.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 1f);
        sizeCurve.AddKey(1f, 0f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        //Renderer//
        var renderer = _burstSystem.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material = GetParticleMaterial();
    }

    public void UpdateLayer(PRISMGenerator generator)
    {
        if (_ambientSystem == null || _burstSystem == null) return;

        float bass = generator.RealtimeBass;
        float energy = generator.RealtimeEnergy;

        //Modulate ambinet emission rate with overall energy//
        //Quiet passages thin out, loud passages fill with particles//
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

        //Bass burst trigger//
        _burstCooldownTimer -= Time.deltaTime;

        bool bassRising = bass > _prevBass;
        bool bassAboveThreshold = bass > bassThreshold;
        bool cooledDown = _burstCooldownTimer <= 0f;

        if(bassRising && bassAboveThreshold && cooledDown)
        {
            TriggerBurst(bass, generator.RealtimeColour);
            _burstCooldownTimer = burstCooldown;
        }

        _prevBass = bass;
    }

    private void TriggerBurst(float bassIntensity, Color realtimeColour)
    {
        //Scale particle count to bass intensity//
        int count = Mathf.RoundToInt(Mathf.Lerp(20, 120, bassIntensity * 50f));
        count = Mathf.Clamp(count, 5, burstMaxParticles);

        //Scale burst speed to intensity//
        float minSpeed = Mathf.Lerp(5f, 20f, _motionQuality);
        float maxSpeed = Mathf.Lerp(25f, 80f, _motionQuality);
        float speed = Mathf.Lerp(minSpeed, maxSpeed, bassIntensity * 50f);
        if (Mathf.Abs(speed - _lastBurstSpeed) > 0.5f)
        {
            _burstMain.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
            _lastBurstSpeed = speed;
        }

        //Set burst colour to the current realtime frequency colour//
        //This means bass hits during a high frequency moment flash cool/violet and bass drops during a low frequency moment flash warm/orange//
        _burstMain.startColor = realtimeColour;

        var burstShape = _burstSystem.shape;
        //burstShape.radius = Mathf.Lerp(10f, 60f, bassIntensity * 50f);
        burstShape.radius = Mathf.Lerp(5f, 30f, bassIntensity * 50f);

        _burstSystem.Emit(count);
    }

    //Returns a default URP particle material//
    //Can be swapped for a custom additvie glow material later//
    private Material GetParticleMaterial()
    {
        //Use URP's default particles lit shader//
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if(shader == null)
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
        mat.enableInstancing = true;
        return mat;
    }
}
