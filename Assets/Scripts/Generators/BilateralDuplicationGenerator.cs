using System.Collections.Generic;
using UnityEngine;

//BilateralDuplicationGenerator//
//Klüver Category 6 (Wavy Lines / Amorphous)//
//Fires mirrored marks outward from the two stereo ear source points, Triggered by stereo width x energy//


public class BilateralDuplicationGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;

    [Header("Flock")]
    [SerializeField] private int minPerBurst = 2;
    [SerializeField] private int maxPerBurst = 14;
    [SerializeField] private int maxButterflies = 500;
    [SerializeField] private float fallbackTrackSeconds = 180f;

    [Header("Butterfly")]
    [SerializeField] private float minSize = 6f;
    [SerializeField] private float maxSize = 16f;

    [Header("Flight")]
    [SerializeField] private float minLaunchSpeed = 8f;
    [SerializeField] private float maxLaunchSpeed = 22f;
    [SerializeField] private float drag = 1.4f;
    [SerializeField] private float settleSpeed = 0.4f;

    [SerializeField, Range(0f, 2f)] private float launchSpread = 1.6f;
    [SerializeField, Range(0f, 1f)] private float forwardBias = 0.35f;

    [Header("Stereo Event Detection")]
    [SerializeField] private int widthHistorySize = 43;
    [SerializeField] private float widthSensitivity = 1.5f;
    [SerializeField] private float refractorySeconds = 0.3f;

    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _wingMaterial;
    private Mesh _butterflyMesh;

    private readonly List<float> _widthHistory = new List<float>();
    private float _timeSinceLastBurst = 0f;
    private int _butterflyCount = 0;
    private bool _active = false;
    private bool _nextIsLeft = true;
    private float _elapsed = 0f;


    public void SetPrism(PRISMGenerator prism)
    {
        _prism = prism;
    }

    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("Bilateral_Root");
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.BilateralDuplication);

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        _wingMaterial = new Material(shader);
        _wingMaterial.SetFloat("_Cull", 0f);

        _butterflyMesh = BuildButterflyMesh();

        _widthHistory.Clear();
        _timeSinceLastBurst = 0f;
        _butterflyCount = 0;
        _active = false;
        _nextIsLeft = true;

    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightBilateralDuplication;
        _active = weight >= activationThreshold;

        _timeSinceLastBurst += Time.deltaTime;

        float width = profile.RealtimeStereoWidth;
        float flux = profile.RealtimeFluxRaw;
        PushFlux(flux);

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.BilateralDuplication) : Prominence.Silent;

        if (!_active) return;
        if (_butterflyCount >= maxButterflies) return;
        float trackLength = (_prism != null && _prism.TrackLength > 1f) ? _prism.TrackLength : fallbackTrackSeconds;
        _elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(_elapsed / trackLength);
        int allowedByNow = Mathf.CeilToInt(progress * maxButterflies);
        if (_butterflyCount >= allowedByNow) return;

        if (_timeSinceLastBurst < refractorySeconds) return;
        if (!IsOnset(flux)) return;

        FireBurst(profile, pr, width);
        _timeSinceLastBurst = 0f;
    }

    public void Deactivate()
    {
        _active = false;
    }

    private void PushFlux(float flux)
    {
        _widthHistory.Add(flux);
        if (_widthHistory.Count > widthHistorySize) _widthHistory.RemoveAt(0);
    }

    private bool IsOnset(float flux)
    {
        if (_widthHistory.Count < widthHistorySize / 2) return false;
        float sum = 0f;
        for (int i = 0; i < _widthHistory.Count; i++) sum += _widthHistory[i];
        float avg = sum / _widthHistory.Count;
        if (avg < 1e-6f) return false;
        return flux > avg * widthSensitivity;
    }

    private void FireBurst(TimbralProfile profile, Prominence pr, float width)
    {
        Vector3 source = _nextIsLeft ? (_prism != null ? _prism.LeftSourcePoint : new Vector3(-60f, 0f, 0f)) : (_prism != null ? _prism.RightSourcePoint : new Vector3(60f, 0f, 0f)); _nextIsLeft = !_nextIsLeft;

        int count = Mathf.RoundToInt(Mathf.Lerp(minPerBurst, maxPerBurst, Mathf.Clamp01(width)));
        count = Mathf.RoundToInt(count * Mathf.Lerp(0.5f, 1f, pr.prominence));
        count = Mathf.Max(1, count);

        Vector3 outward = (source - transform.position).normalized;
        if (outward.sqrMagnitude < 1e-4f) outward = Vector3.forward;

        Color colour = _prism != null ? _prism.RealtimeColour : Color.white;
        float sizeScale = Mathf.Lerp(0.6f, 1f, pr.prominence);

        for (int i = 0; i < count && _butterflyCount < maxButterflies; i++)
        {
            SpawnButterfly(source, outward, profile, colour, sizeScale);
        }
    }

    private void SpawnButterfly(Vector3 source, Vector3 outward, TimbralProfile profile, Color colour, float sizeScale)
    {
        GameObject b = new GameObject("Butterfly");
        b.transform.SetParent(_root.transform);
        b.transform.position = source;
        b.transform.rotation = Random.rotationUniform;

        float size = Mathf.Lerp(minSize, maxSize, profile.RealtimeEnergy) * sizeScale * Random.Range(0.75f, 1.25f);
        b.transform.localScale = Vector3.one * size;

        b.AddComponent<MeshFilter>().mesh = _butterflyMesh;
        var mr = b.AddComponent<MeshRenderer>();
        Material m = new Material(_wingMaterial);
        m.color = colour;
        mr.material = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        Vector3 biased = (outward + Vector3.forward * forwardBias).normalized;
        Vector3 dir = (biased + Random.onUnitSphere * launchSpread).normalized;
        float speed = Random.Range(minLaunchSpeed, maxLaunchSpeed);

        var glide = b.AddComponent<ButterflyGlide>();
        glide.Launch(dir * speed, drag, settleSpeed);

        _butterflyCount++;
    }

    //Butterfly mesh //
    private Mesh BuildButterflyMesh()
    {
        Mesh m = new Mesh();

        Vector3[] verts = new Vector3[]
        {
            //0: body centre//
            new Vector3(0f, 0f, 0f),
            //Right wing: upper lobe (1,2,3), lower lobe (4,5)//
            new Vector3(0.15f,  0.10f, 0f),  //1 body top-right//
            new Vector3(0.75f,  0.55f, 0f),  //2 upper outer//
            new Vector3(0.50f, -0.05f, 0f),  //3 upper inner-lower//
            new Vector3(0.60f, -0.55f, 0f),  //4 lower outer//
            new Vector3(0.15f, -0.15f, 0f),  //5 body bottom-right//
            //Left wing: mirrored X//
            new Vector3(-0.15f,  0.10f, 0f), //6//
            new Vector3(-0.75f,  0.55f, 0f), //7//
            new Vector3(-0.50f, -0.05f, 0f), //8//
            new Vector3(-0.60f, -0.55f, 0f), //9//
            new Vector3(-0.15f, -0.15f, 0f), //10//
        };

        int[] tris = new int[]
        {
            //Right upper wing//
            0, 1, 2,
            0, 2, 3,
            //Right lower wing//
            0, 3, 4,
            0, 4, 5,
            //Left upper wing (reverse winding so it faces the same way)//
            0, 7, 6,
            0, 8, 7,
            //Left lower wing//
            0, 9, 8,
            0, 10, 9,
        };

        m.vertices = verts;
        m.triangles = tris;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    private void OnDestroy()
    {
        if (_wingMaterial != null) Destroy(_wingMaterial);
        if (_butterflyMesh != null) Destroy(_butterflyMesh);
    }
}

public class ButterflyGlide : MonoBehaviour
{
    private Vector3 _velocity;
    private float _drag;
    private float _settleSpeed;
    private bool _settled = false;

    private float _flutterPhase;
    private float _flutterAmount;

    public void Launch(Vector3 velocity, float drag, float settleSpeed)
    {
        _velocity = velocity;
        _drag = drag;
        _settleSpeed = settleSpeed;
        _flutterPhase = Random.Range(0f, 100f);
        _flutterAmount = Random.Range(0.5f, 2.0f);
    }

    private void Update()
    {
        if (_settled) return;

        float dt = Time.deltaTime;

        _velocity *= Mathf.Exp(-_drag * dt);

        _flutterPhase += dt * 3f;
        Vector3 flutter = new Vector3(Mathf.Sin(_flutterPhase * 1.3f), Mathf.Cos(_flutterPhase * 0.9f), Mathf.Sin(_flutterPhase * 0.7f)) * _flutterAmount;

        transform.position += (_velocity + flutter) * dt;

        if (_velocity.sqrMagnitude > 1e-4f)
        {
            Quaternion target = Quaternion.LookRotation(_velocity.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, dt * 2f);
        }


        if (_velocity.magnitude < _settleSpeed)
        {
            _settled = true;
            enabled = false;
        }
    }
}