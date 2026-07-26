using System.Collections.Generic;
using UnityEngine;

//SkullRipple - feeds beat-driven ripples to the SkullRipple shader.
//
//On each tracked beat it spawns two ripples, at the left and right of the skull, their
//strength set by the stereo balance of the mix. Each ripple sweeps across the skull
//surface (by angle from its origin - see the shader) and fades over its lifetime, so the
//head resonates on the beat and is still between beats. Where the left and right waves
//meet across the crown they interfere, which reads as vibration through a solid object
//rather than two separate ripples.
//
//The displacement is done in the vertex shader, not by rebuilding the mesh, so the skull
//can ripple every frame without competing with the generators for CPU time. This component
//only maintains the small ripple table and pushes it to the material each frame.
//
//Applies to whichever renderers are assigned - typically both the outer (pale) and inner
//(void) skull shells, so they ripple together and the gap between them stays constant.

public class SkullRipple : MonoBehaviour
{
    private const int MAX_RIPPLES = 8;

    [Header("Source")]
    //The head, for its centre and containment. Optional - centre falls back to this
    //transform's position if not set.
    [SerializeField] private HeadField head;
    //PRISMGenerator owns the timbral profile. TimbralProfile is a plain data class, not a
    //MonoBehaviour, so it cannot be assigned in the inspector directly - Unity only shows
    //reference slots for components. Referencing the generator and reading its public
    //TimbralProfile property is how the beat, energy and stereo values are reached.
    [SerializeField] private PRISMGenerator prism;
    //Objects to ripple. Assign the outer and inner HeadField GameObjects; their runtime
    //mesh children are found automatically, so you can drag them in before play.
    [SerializeField] private GameObject[] targetObjects;

    [Header("Ripple Shape")]
    //Seconds for a wavefront to sweep from origin across the whole skull (pi radians).//
    [SerializeField] private float sweepSeconds = 0.4f;
    //How long a ripple takes to fade out after spawning.//
    [SerializeField] private float lifetime = 0.8f;
    //Overall displacement scale, in world units. The head renders at scale ~50-98, so this
    //wants to be several units to be visible - small values are a sub-percent nudge.
    [SerializeField] private float amplitude = 8f;
    //Angular width of the wave in radians - larger is a broader, softer swell.//
    [SerializeField] private float wavelength = 0.9f;

    [Header("Origins")]
    //Left and right ripple origins, as directions from the head centre. A beat fires both;
    //their relative strength follows the stereo balance.
    [SerializeField] private Vector3 leftOrigin = new Vector3(-1f, 0.1f, 0.15f);
    [SerializeField] private Vector3 rightOrigin = new Vector3(1f, 0.1f, 0.15f);

    [Header("Response")]
    //Beats quieter than this (in normalised energy) do not ripple.//
    [SerializeField] private float energyThreshold = 0.05f;
    //A perfectly centred mix still rings both sides this strongly; width pushes the
    //balance toward one side.
    [SerializeField, Range(0f, 1f)] private float centreBalance = 0.6f;

    private struct Ripple
    {
        public Vector3 dir;      //origin direction, unit, from head centre//
        public float strength;   //0 = dead//
        public float age;        //seconds since spawn//
    }

    private readonly Ripple[] _ripples = new Ripple[MAX_RIPPLES];
    private int _next = 0;

    //Scratch arrays pushed to the shader each frame.//
    private readonly Vector4[] _dir = new Vector4[MAX_RIPPLES];
    private readonly Vector4[] _data = new Vector4[MAX_RIPPLES];
    private MaterialPropertyBlock _block;

    private Vector3 HeadCentre => head != null ? head.transform.position : transform.position;

    //Spawns a pair of ripples from a beat.
    //  energy 0-1 - overall ripple strength.
    //  width  0-1 - stereo spread of the mix (0 = mono, 1 = wide). The analysis gives
    //               width, not left/right balance, so both sides ring together and width
    //               controls how strongly - a wide mix rings hard from both sides, a mono
    //               mix gives a gentler symmetric pulse. Where the two waves meet across
    //               the crown they interfere, which is what reads as vibration through the
    //               skull.
    public void OnBeat(float energy, float width)
    {
        if (energy < energyThreshold) return;

        //Both sides ring equally; width scales how strongly above the centre baseline.//
        float strength = Mathf.Lerp(centreBalance, 1f, Mathf.Clamp01(width)) * energy;

        SpawnRipple(leftOrigin.normalized, strength);
        SpawnRipple(rightOrigin.normalized, strength);
    }

    private void SpawnRipple(Vector3 dir, float strength)
    {
        if (strength <= 0.001f) return;

        _ripples[_next] = new Ripple { dir = dir, strength = strength, age = 0f };
        _next = (_next + 1) % MAX_RIPPLES;
    }

    private void Update()
    {
        //Read beats from the generator's profile, so nothing else has to call in.//
        TimbralProfile profile = prism != null ? prism.TimbralProfile : null;
        if (profile != null && profile.BeatThisFrame)
            OnBeat(profile.RealtimeEnergy, profile.RealtimeStereoWidth);

        float dt = Time.deltaTime;
        float speed = Mathf.PI / Mathf.Max(0.01f, sweepSeconds);   //radians per second//

        for (int i = 0; i < MAX_RIPPLES; i++)
        {
            if (_ripples[i].strength > 0f)
            {
                _ripples[i].age += dt;
                if (_ripples[i].age >= lifetime)
                    _ripples[i].strength = 0f;
            }

            _dir[i] = new Vector4(_ripples[i].dir.x, _ripples[i].dir.y, _ripples[i].dir.z,
                                  _ripples[i].strength);
            _data[i] = new Vector4(_ripples[i].age, speed, lifetime, 0f);
        }

        if (targetObjects == null) return;

        Vector3 centre = HeadCentre;
        bool live = _dir[0].w > 0f || _dir[1].w > 0f;

        foreach (var go in targetObjects)
        {
            if (go == null) continue;

            var rends = go.GetComponentsInChildren<Renderer>();
            foreach (var r in rends)
            {
                if (r == null) continue;

                //Skip renderers whose material is not a ripple shader. Checked by shader
                //name rather than HasProperty, because HasProperty does not reliably detect
                //vector-ARRAY properties and was silently rejecting the correct material.
                var sm = r.sharedMaterial;
                if (sm == null || sm.shader == null) continue;
                if (!sm.shader.name.Contains("SkullRipple")) continue;

                //Set the arrays DIRECTLY on the material, not through a MaterialPropertyBlock.
                //Property blocks do not reliably bind vector-ARRAY properties in URP - the
                //scalars and single vectors bound, but _RippleDir/_RippleData did not, so the
                //shader saw no ripples. Writing to the material itself binds them. sharedMaterial
                //is used so every renderer using this material updates together.
                sm.SetVectorArray("_RippleDir", _dir);
                sm.SetVectorArray("_RippleData", _data);
                sm.SetVector("_HeadCentre", centre);
                sm.SetFloat("_RippleAmplitude", amplitude);
                sm.SetFloat("_RippleWavelength", wavelength);

                if (live)
                    Debug.Log($"PUSH {r.name}: shader={sm.shader.name} r0={_dir[0].w:F2} amp={amplitude}");
            }
        }
    }
}
