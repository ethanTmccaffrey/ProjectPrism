using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class MeshVolumeLayer : MonoBehaviour
{
    [Header("Source Point Lights")]
    [SerializeField] private float lightBaseIntensity = 2f;
    [SerializeField] private float lightMaxIntensity = 8f;
    [SerializeField] private float lightRange = 200f;
    [SerializeField] private float lightSmoothSpeed = 8f;

    [Header("Mark Settings")]
    [SerializeField] private int markPointCount = 48; //Points per curve//
    [SerializeField] private int maxMarks = 1000; //Hard cap before oldest culled//
    [SerializeField] private float markFadeTime = 90f; //Seconds to fade to minimum opacity//
    [SerializeField] private float markMinOpacity = 0.12f; //Marks dont full dissappear//
    [SerializeField] private float markSpawnCooldown = 0.03f;

    [Header("Mark Shape")]
    [SerializeField] private float markWidthBass = 0.8f; //Max width for bass-triggered marks//
    [SerializeField] private float markWidthHigh = 0.15f; //Width for high-frequency marks//
    [SerializeField] private float markLength = 30f; //How far marks travel inward//

    //Left source point//
    private GameObject _leftSourceGO;
    private Light _leftLight;

    //Right source point//
    private GameObject _rightSourceGO;
    private Light _rightLight;

    //Mark tracking//
    private List<MarkData> _marks = new List<MarkData>();
    private Material _markMaterial;

    //Smoothed values, idependent per channel//
    private float _smoothedEnergyLeft = 0f;
    private float _smoothedEnergyRight = 0f;
    private float _smoothedBassLeft = 0f;
    private float _smoothedBassRight = 0f;

    //Cached positions//
    private Vector3 _leftPos;
    private Vector3 _rightPos;

    private float _lastLeftIntensity = -1f;
    private float _lastRightIntensity = -1f;
    private Color _lastLightColour = Color.black;

    //Burst cooldowns//
    private float _cooldownLeft = 0f;
    private float _cooldownRight = 0f;
    private float _prevBassLeft = 0f;
    private float _prevBassRight = 0f;

    //Static qualities//
    private float _motionQuality;
    private float _spaceQuality;
    private float _formQuality;

    //Mark data container//
    private class MarkData
    {
        public GameObject go;
        public MeshRenderer renderer;
        public float spawnTime;
        public Color colour;
        public Material material; //Instance material for individual fade//
        public float lastAlpha = -1f;
    }

    public void Init(PRISMGenerator generator)
    {
        Debug.Log("MeshVolumeLayer Init called");
        _leftPos = generator.LeftSourcePoint;
        _rightPos = generator.RightSourcePoint;

        BuildSourcePoint(ref _leftSourceGO, ref _leftLight, _leftPos, "LeftSourcePoint", generator.PrimaryColour);
        BuildSourcePoint(ref _rightSourceGO, ref _rightLight, _rightPos, "RightSourcePoint", generator.PrimaryColour);

        //Create the shared mark material: lit, transparent, no shadows cast//
        Shader litShader = Shader.Find("Universal Render Pipeline/Unlit");
        _markMaterial = new Material(litShader);
        _markMaterial.SetFloat("_Surface", 1f); //Transparent//
        _markMaterial.SetFloat("_Blend", 0f); //Alpha Blend//
        _markMaterial.SetFloat("_SrcBlend", 5f);
        _markMaterial.SetFloat("_DstBlend", 10f);
        _markMaterial.renderQueue = 3000;
        _markMaterial.enableInstancing = true;

        Debug.Log("PRISM MeshVolumeLayer: Initialised");
    }

    private void BuildSourcePoint(ref GameObject go, ref Light light, Vector3 position, string name, Color colour)
    {
        go = new GameObject(name);
        go.transform.SetParent(transform);
        go.transform.position = position;

        //Point light//
        light = go.AddComponent<Light>();
        go.AddComponent<UniversalAdditionalLightData>();
        go.AddComponent<LensFlareComponentSRP>();
        light.type = LightType.Point;
        light.range = lightRange;
        light.intensity = lightBaseIntensity;
        light.color = colour;
        light.shadows = LightShadows.None; 
        light.renderMode = LightRenderMode.ForcePixel;

        Debug.Log($"Light added: {light != null}, type: {light?.type}");
    }

    public void UpdateLayer(PRISMGenerator generator)
    {
        if (_leftLight == null || _rightLight == null) return;

        UpdateLights(generator);
        UpdateMarkFades();

        _cooldownLeft -= Time.deltaTime;
        _cooldownRight -= Time.deltaTime;

        float bassLeft = generator.RealtimeBassLeft;
        float bassRight = generator.RealtimeBassRight;

        // Left channel bass hit — spawn mark from left source point
        if (bassLeft > _prevBassLeft && bassLeft > 0.02f && _cooldownLeft <= 0f)
        {
            SpawnMark(_leftPos, bassLeft, generator, isLeft: true);
            _cooldownLeft = markSpawnCooldown;
        }
        _prevBassLeft = bassLeft;

        // Right channel bass hit — spawn mark from right source point
        if (bassRight > _prevBassRight && bassRight > 0.02f && _cooldownRight <= 0f)
        {
            SpawnMark(_rightPos, bassRight, generator, isLeft: false);
            _cooldownRight = markSpawnCooldown;
        }
        _prevBassRight = bassRight;
    }
    public void UpdateLights(PRISMGenerator generator)
    {
        if (_leftLight == null || _rightLight == null) return;

        //Smooth incoming channel values independently//
        _smoothedEnergyLeft = Mathf.Lerp(_smoothedEnergyLeft, generator.RealtimeEnergyLeft, Time.deltaTime * lightSmoothSpeed);
        _smoothedEnergyRight = Mathf.Lerp(_smoothedEnergyRight, generator.RealtimeEnergyRight, Time.deltaTime * lightSmoothSpeed);

        //Scale factor: energy values are small floats, scale up for visible response//
        float scaleL = _smoothedEnergyLeft * 80f;
        float scaleR = _smoothedEnergyRight * 80f;

        float leftIntensity = Mathf.Lerp(lightBaseIntensity, lightMaxIntensity, scaleL);
        float rightIntensity = Mathf.Lerp(lightBaseIntensity, lightMaxIntensity, scaleR);
        Color lightColour = generator.RealtimeColour;

        if (Mathf.Abs(leftIntensity - _lastLeftIntensity) > 0.05f)
        {
            _leftLight.intensity = leftIntensity;
            _lastLeftIntensity = leftIntensity;
        }
        if (Mathf.Abs(rightIntensity - _lastRightIntensity) > 0.05f)
        {
            _rightLight.intensity = rightIntensity;
            _lastRightIntensity = rightIntensity;
        }
        if (lightColour != _lastLightColour)
        {
            _leftLight.color = lightColour;
            _rightLight.color = lightColour;
            _lastLightColour = lightColour;
        }
    }

    private void SpawnMark(Vector3 sourcePoint, float bassIntensity, PRISMGenerator generator, bool isLeft)
    {
        //Cull oldest mark if over cap//
        if(_marks.Count >= maxMarks)
        {
            if (_marks[0].go != null) Destroy(_marks[0].go);
            _marks.RemoveAt(0);
        }

        //Determine mark character from current frequency balance//
        //Bass ratio drives width//
        //High ratio drives vertical arc amplitude//
        float bassRatio = Mathf.Clamp01(generator.RealtimeBass * 80f);
        float highRatio = Mathf.Clamp01(generator.RealtimeHigh * 300f);
        float midRatio = Mathf.Clamp01(generator.RealtimeMid * 150f);

        float markWidth = Mathf.Lerp(markWidthHigh, markWidthBass, bassRatio);

        //Vertical arc: high frequencies arc upward, bass arcs low//
        float verticalBias = Mathf.Lerp(-12f, 12f, highRatio) - (bassRatio * 6f);

        //Lengths scales with energy intesnity//
        float length = markLength * Mathf.Lerp(0.4f, 1.2f, bassIntensity * 50f) * Random.Range(0.3f, 1.8f);

        Vector3 baseDirection = Random.onUnitSphere;

        //Add variation so marks dont all travel the same path//
        //Form quality drives how much variation: angular tracks get wilder curves//
        float variation = Mathf.Lerp(0.5f, 2f, _formQuality);
        Vector3 randomOffset = new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(-variation * 1.5f, variation * 1.5f), Random.Range(-variation, variation));

        Vector3 direction = (baseDirection + randomOffset).normalized;

        //Generate the curve points//
        float originSpread = Random.Range(100f, 100f);
        Vector3 randomOrigin = sourcePoint + Random.onUnitSphere * originSpread;
        Vector3[] curvePoints = GenerateCurvePoints(randomOrigin, direction, length, verticalBias, bassRatio, highRatio);

        Vector3 ribbonUp = new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0.7f, 1f), Random.Range(-0.3f, 0.3f)).normalized;

        //Buld the ribbon mesh from curve points//
        Mesh mesh = GenerateRibbonMesh(curvePoints, markWidth, bassRatio, ribbonUp);

        //Create the mark GameObject//
        GameObject markGO = new GameObject("Mark");
        markGO.transform.SetParent(transform);

        MeshFilter mf = markGO.AddComponent<MeshFilter>();
        MeshRenderer mr = markGO.AddComponent<MeshRenderer>();
        mf.mesh = mesh;

        //Instance material so each mark can fade independently//
        Material instanceMat = new Material(_markMaterial);
        Color.RGBToHSV(generator.RealtimeColour, out float h, out float s, out float v);
        Color markColour = Color.HSVToRGB(h, Mathf.Max(s, 0.7f), Mathf.Max(v, 0.8f));
        instanceMat.color = markColour;
        mr.material = instanceMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        mr.receiveShadows = true;

        MarkData data = new MarkData
        {
            go = markGO,
            renderer = mr,
            spawnTime = Time.time,
            colour = markColour,
            material = instanceMat
        };
        _marks.Add(data);
    }

    private Vector3[] GenerateCurvePoints(Vector3 origin, Vector3 direction, float length, float verticalBias, float bassRatio, float highRatio)
    {
        Vector3[] points = new Vector3[markPointCount];

        //Use a smooth bezier-like curve rather than a straight line//
        //Control point pulls the curve in a direction based on frequency content//
        Vector3 controlOffset = new Vector3(direction.x * length * 0.3f, verticalBias * Mathf.Lerp(0.5f, 2f, highRatio), direction.z * length * 0.5f);
        Vector3 controlPoint = origin + controlOffset;
        Vector3 endPoint = origin + direction * length;

        //Add organic noise along the curve using Perlin noise//
        //Form quality drives how turbulent the curve is//
        float noiseSeed = Random.Range(0f, 100f);
        float noiseScale = Mathf.Lerp(0.5f, 3f, _formQuality);

        for (int i = 0; i < markPointCount; i++)
        {
            float t = i / (float)(markPointCount - 1);

            //Quadratic bezier: origin > control > end//
            Vector3 p = Mathf.Pow(1 - t, 2) * origin
                      + 2 * (1 - t) * t * controlPoint
                      + Mathf.Pow(t, 2) * endPoint;

            //Add Perlin noise displacement for organic feel//
            float noiseX = (Mathf.PerlinNoise(noiseSeed + t * noiseScale, 0f) - 0.5f) * 2f;
            float noiseY = (Mathf.PerlinNoise(0f, noiseSeed + t * noiseScale) - 0.5f) * 2f;
            p += new Vector3(noiseX, noiseY, 0f) * Mathf.Lerp(0.1f, 1.5f, _formQuality) * t;

            points[i] = p;
        }

        return points;
    }

    private Mesh GenerateRibbonMesh(Vector3[] curvePoints, float maxWidth, float bassRatio, Vector3 ribbonUp)
    {
        Mesh mesh = new Mesh();
        int n = curvePoints.Length;
        int vertCount = n * 2;

        Vector3[] vertices = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        Color[] colours = new Color[vertCount];
        int[] triangles = new int[(n - 1) * 6];

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)(n - 1);

            //Brushstroke profile: thin at start, wide in middle, tapers at end//
            //Uses a smooth bell curve peaking at t=0.4//
            float widthProfile = Mathf.Sin(t * Mathf.PI) * Mathf.Pow(1f - Mathf.Abs(t - 0.4f) * 1.5f, 0.5f);
            widthProfile = Mathf.Clamp01(widthProfile);
            float width = maxWidth * widthProfile;

            //Calculate tangent direction for ribbon orientation//
            Vector3 tangent;
            if (i < n - 1)
            {
                tangent = (curvePoints[i + 1] - curvePoints[i]).normalized;
            }
            else
            {
                tangent = (curvePoints[i] - curvePoints[i - 1]).normalized;
            }

            //Ribbon faces camera (billboard-like) using cross product with up//
            Vector3 up = ribbonUp;
            Vector3 right = Vector3.Cross(tangent, up).normalized;
            if (right == Vector3.zero) right = Vector3.right;

            vertices[i * 2] = curvePoints[i] - right * width * 0.5f;
            vertices[i * 2 + 1] = curvePoints[i] + right * width * 0.5f;

            uvs[i * 2] = new Vector2(0f, t);
            uvs[i * 2 + 1] = new Vector2(1f, t);

            //Vertex colour: full at peak, fades toward ends//
            float alpha = widthProfile;
            colours[i * 2] = new Color(1f, 1f, 1f, alpha);
            colours[i * 2 + 1] = new Color(1f, 1f, 1f, alpha);
        }

        //Build triangles//
        int tri = 0;
        for (int i = 0; i < n - 1; i++)
        {
            int bl = i * 2;
            int br = i * 2 + 1;
            int tl = (i + 1) * 2;
            int tr = (i + 1) * 2 + 1;

            triangles[tri++] = bl;
            triangles[tri++] = tl;
            triangles[tri++] = br;

            triangles[tri++] = br;
            triangles[tri++] = tl;
            triangles[tri++] = tr;
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.colors = colours;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private void UpdateMarkFades()
    {
        float currentTime = Time.time;

        for (int i = 0; i < _marks.Count; i++)
        {
            if (_marks[i].material == null) continue;

            float age = currentTime - _marks[i].spawnTime;
            float fadeT = Mathf.Clamp01(age / markFadeTime);

            //Fade from full opacity to minimum — never fully transparent//
            float alpha = Mathf.Lerp(1f, markMinOpacity, fadeT);

            if(Mathf.Abs(alpha - _marks[i].lastAlpha) > 0.005f)
            {
                Color c = _marks[i].colour;
                c.a = alpha;
                _marks[i].material.color = c;
                _marks[i].lastAlpha = alpha;
            }

            
        }
    }

    private void OnDestroy()
    {
        //Clean up instance materials//
        foreach (var mark in _marks)
        {
            if (mark.material != null) Destroy(mark.material);
        }
    }

}
