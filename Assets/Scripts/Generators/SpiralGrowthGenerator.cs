using System.Collections.Generic;
using UnityEngine;

//SpiralGrowthGenerator: Kluver Category 2 (Spirals)//
//Grows a single continuous spiral arm as a thin glowing filament, winding outward on a flat plane titlted in 3D space//
//Where fracture scatters and Honeycomb tesselates, the spiral Winds: rotation over time, mapping to music with harmonic movement and momentum//

//Acoustic > visual mapping://
//Coil tightness: harmonic complexity (rich harmony = tight winding, simple = loose)//
//Growth rate: Sustained energy (louder = the arm extends faster)//
//Colour: the track's RealTimeColour at the moment each segment is grown, so the filament records the songs colour journey//

//Grows continuosly (not beat-driven) while active, points are appended to the arm each frame of sustained energy//
//Tilt and centre lock at birth (persistent canvas)//
//Prominance sets where it sits (centrality) and how bold it is (prominence)//

public class SpiralGrowthGenerator : MonoBehaviour
{
    [Header("Activation")]
    [SerializeField] private float activationThreshold = 0.15f;
    [SerializeField] private float energyThreshold = 0.02f;

    [Header("Spiral Shape")]
    //base radial growth per unit angle (how fast the arm moves outward as it winds)//
    [SerializeField] private float radialSpeed = 0.6f;
    //Angular step per growth tick at simplest harmony. Harmonic complexity tightens this//
    [SerializeField] private float baseAngleStep = 6f; //Degress per point at loose coil//
    [SerializeField] private float tightAngleStep = 18f; //degress per point at tight coil//
    //How many growth ticks per second at full energy//
    [SerializeField] private float growthRate = 30f;
    //Hard cap on filament points so a long track can't grow unbounded geometry//
    [SerializeField] private int maxPoints = 3000;

    [Header("Placement")]
    //How far the spiral centre can sit from origin when minor//
    [SerializeField] private float maxCentreOffset = 40f;
    //Overall size scale of the spiral 9radius reached)//
    [SerializeField] private float scale = 1f;

    [Header("Appearence")]
    [SerializeField] private float lineWidth = 0.15f;

    //Internal state//
    private GameObject _root;
    private PRISMGenerator _prism;
    private Material _lineMaterial;

    private bool _active = false;
    private bool _seeded = false;
    private float _debugTimer = 0f;
    private float _growthAccumulator = 0f;

    //Spiral stats: locked at birth//
    private Vector3 _centre;
    private Quaternion _planeRotation; //The tilt of the spiral plane//
    private float _angle = 0f; //Current angular position//
    private int _pointCount = 0;

    //The growing filament, built as one LineRenderer//
    private LineRenderer _line;
    private readonly List<Vector3> _points = new List<Vector3>();

    public void SetPrism(PRISMGenerator prism)
    {
        _prism = prism;
    }
    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("Spiral_Root");
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.SpiralGrowth);

        BuildMaterial();

        _active = false;
        _seeded = false;
        _growthAccumulator = 0f;
        _angle = 0f;
        _pointCount = 0;
        _points.Clear();

        Debug.Log("PRISM SpiralGrowthGenerator: Initialised");
    }
    public void UpdateGenerator(TimbralProfile profile)
    {
        float weight = profile.WeightSpiralGrowth;
        _active = weight >= activationThreshold;

        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.SpiralGrowth) : Prominence.Silent;

        //_debugTimer += Time.deltaTime;
        //if(_debugTimer >= 1f)
        //{
        //    _debugTimer = 0f;
        //    Debug.Log($"[SPIRAL] weight={weight:F3} active={_active} points={_pointCount} " + $"H={profile.RealtimeHarmonicComplexity:F3} E={profile.RealtimeEnergy:F3} " + $"dominant={pr.isDominant} centrality={pr.centrality:F2} prominence={pr.prominence:F2}");
        //}

        if (!_active) return;
        if (profile.RealtimeEnergy < energyThreshold) return;
        if (_pointCount >= maxPoints) return;

        //Seed the spiral the firth time it grows: lockcentre + tilt by prominence//
        if (!_seeded) SeedSpiral(pr);

        //Continous growth: accumulate ticks scaled by energy, add that many points//
        _growthAccumulator += growthRate * profile.RealtimeEnergy * Time.deltaTime;
        int ticks = Mathf.FloorToInt(_growthAccumulator);
        if (ticks <= 0) return;
        _growthAccumulator -= ticks;

        for(int i = 0; i < ticks && _pointCount < maxPoints; i++)
        {
            GrowPoint(profile, pr);
        }

        //Push the updated point list to the LineRenderer//
        _line.positionCount = _points.Count;
        _line.SetPositions(_points.ToArray());
    }
    public void Deactivate()
    {
        _active = false;
    }

    //Seeding//
    private void SeedSpiral(Prominence pr)
    {
        //Centre offset by (1 - centrality): dominant centred, minor pushed outward//
        _centre = transform.position + Random.onUnitSphere * (maxCentreOffset * (1f - pr.centrality));

        //Random tilt so the flat spiral sits at an angle in 3D space, locked at birth//
        _planeRotation = Random.rotationUniform;

        //Create the LineRenderer for the filament//
        GameObject lineGO = new GameObject("SpiralArm");
        lineGO.transform.SetParent(_root.transform);
        _line = lineGO.AddComponent<LineRenderer>();
        _line.useWorldSpace = true;
        _line.material = _lineMaterial;
        _line.numCapVertices = 2;
        _line.numCornerVertices = 2;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;

        //Width scales with prominence so a minor sprial is a fine faint thread//
        float w = lineWidth * Mathf.Lerp(0.4f, 1.4f, pr.prominence);
        _line.startWidth = w;
        _line.endWidth = w;

        Color seedCol = _prism != null ? _prism.RealtimeColour : Color.cyan;
        _line.startColor = seedCol;
        _line.endColor = seedCol;

        _angle = 0f;
        _seeded = true;
    }

    //Growth//
    private void GrowPoint(TimbralProfile profile, Prominence pr)
    {
        //Harmonic complexity tightens the coil: more complex = larger angular step per point, so the arm winds faster (tighter spiral), Simple harmony = loose//
        float angleStepDeg = Mathf.Lerp(baseAngleStep, tightAngleStep, profile.RealtimeHarmonicComplexity);
        _angle += angleStepDeg * Mathf.Deg2Rad;

        //Archimedean sprial: radius grows with angle//
        float sizeScale = scale * Mathf.Lerp(0.5f, 1.4f, pr.prominence);
        float radius = radialSpeed * _angle * sizeScale;

        //Point on the flat spiral in the plane's local XY, then tilt + offset into world//
        Vector3 local = new Vector3(Mathf.Cos(_angle) * radius, Mathf.Sin(_angle) * radius, 0f);
        Vector3 world = _centre + _planeRotation * local;

        _points.Add(world);
        _pointCount++;

        //Colour: LineRenderer uses a single material; tint it toward the curretn RealtimeColour so the arm broadly tracks the song//
        if(_prism != null)
        {
            _line.endColor = _prism.RealtimeColour;
        }
    }

    private void BuildMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        _lineMaterial = new Material(shader);
    }

    private void OnDestroy()
    {
        if(_lineMaterial != null ) Destroy(_lineMaterial);
    }
}
