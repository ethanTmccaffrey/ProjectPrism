using System.Collections.Generic;
using UnityEngine;

public class SkullRipple : MonoBehaviour
{
    private const int MAX_RIPPLES = 8;

    [Header("Source")]

    [SerializeField] private HeadField head;
    [SerializeField] private PRISMGenerator prism;
    [SerializeField] private GameObject[] targetObjects;

    [Header("Ripple Shape")]
    [SerializeField] private float sweepSeconds = 0.4f;
    [SerializeField] private float lifetime = 0.8f;
    [SerializeField] private float amplitude = 8f;
    [SerializeField] private float wavelength = 0.9f;

    [Header("Origins")]
    [SerializeField] private Vector3 leftOrigin = new Vector3(-1f, 0.1f, 0.15f);
    [SerializeField] private Vector3 rightOrigin = new Vector3(1f, 0.1f, 0.15f);
    public Vector3 LeftOrigin => leftOrigin;
    public Vector3 RightOrigin => rightOrigin;

    [Header("Response")]
    [SerializeField] private float energyThreshold = 0.05f;
    [SerializeField, Range(0f, 1f)] private float centreBalance = 0.6f;

    private struct Ripple
    {
        public Vector3 dir;
        public float strength; 
        public float age; 
        public Color colour; 
    }

    private readonly Ripple[] _ripples = new Ripple[MAX_RIPPLES];
    private int _next = 0;

    private readonly Vector4[] _dir = new Vector4[MAX_RIPPLES];
    private readonly Vector4[] _data = new Vector4[MAX_RIPPLES];
    private readonly Vector4[] _colour = new Vector4[MAX_RIPPLES];
    private MaterialPropertyBlock _block;

    private Vector3 HeadCentre => head != null ? head.transform.position : transform.position;

    public void TriggerRipple(Vector3 dir, float energy, Color colour)
    {
        if (energy < energyThreshold) return;
        float strength = Mathf.Clamp01(energy);
        SpawnRipple(dir.normalized, strength, colour);
    }
    public void TriggerBothEars(float energy, Color colour)
    {
        TriggerRipple(leftOrigin, energy, colour);
        TriggerRipple(rightOrigin, energy, colour);
    }

    private void SpawnRipple(Vector3 dir, float strength, Color colour)
    {
        if (strength <= 0.001f) return;

        _ripples[_next] = new Ripple { dir = dir, strength = strength, age = 0f, colour = colour };
        _next = (_next + 1) % MAX_RIPPLES;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        float speed = Mathf.PI / Mathf.Max(0.01f, sweepSeconds);

        for (int i = 0; i < MAX_RIPPLES; i++)
        {
            if (_ripples[i].strength > 0f)
            {
                _ripples[i].age += dt;
                if (_ripples[i].age >= lifetime)
                {
                    _ripples[i].strength = 0f;
                }
            }

            _dir[i] = new Vector4(_ripples[i].dir.x, _ripples[i].dir.y, _ripples[i].dir.z,_ripples[i].strength);
            _data[i] = new Vector4(_ripples[i].age, speed, lifetime, 0f);
            Color c = _ripples[i].colour;
            _colour[i] = new Vector4(c.r, c.g, c.b, 1f);
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

                var sm = r.sharedMaterial;
                if (sm == null || sm.shader == null) continue;
                if (!sm.shader.name.Contains("SkullRipple")) continue;

                sm.SetVectorArray("_RippleDir", _dir);
                sm.SetVectorArray("_RippleData", _data);
                sm.SetVectorArray("_RippleColour", _colour);
                sm.SetVector("_HeadCentre", centre);
                sm.SetFloat("_RippleAmplitude", amplitude);
                sm.SetFloat("_RippleWavelength", wavelength);
            }
        }
    }
}
