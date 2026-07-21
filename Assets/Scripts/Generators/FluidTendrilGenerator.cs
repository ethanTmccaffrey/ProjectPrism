using System.Collections.Generic;
using UnityEngine;

//FluidTendrilGenerator - Category 6: Wavy lines and Amorphous froms//
//Activates on low spectral organic curves that grow and branch across the scene, persisting permantley as geometry on the canvas//

//Timbral profile: Classical strings, ambient, slow soul, folk, acoustic instruments//
//Grounded in cross-modal correspondance: smooth tonal sounds consistently map to flowing curved visual forms. (Cytowic 2002, Marks 1974, Takete/Malum: Kohler 1929)//

//Mark generation: sustained energy above threshold triggers a new tendril every//
//spawnInterval seconds. Unlike percussion-triggered generators, FluidTendril responds to sustained musical passages rather than sharp//

public class FluidTendrilGenerator : MonoBehaviour
{
    [Header("Spawn Settings")]
    [SerializeField] private float spawnInterval = 2.5f; //Seconds between new tendrills//
    [SerializeField] private float spawnRadius = 80f; //Radius of spawn sphere//
    [SerializeField] private float energyThreshold = 0.001f; //Minimum energy to spawn//
    [SerializeField] private int maxTendrils = 400; //Hard cap on canvas marks//
    [SerializeField] private float weightThreshold = 0.15f; //Minimum timbral weight to be active//

    [Header("Tendril Shape")]
    [SerializeField] private int pointCount = 48; //Points per tendril curve//
    [SerializeField] private float tendrilLength = 60f; //Base length of each tendril//
    [SerializeField] private float tendrilWidth = 3f; //Max width at peak of brushstrokes//
    [SerializeField] private float branchChance = 0.3f; //Probability of spawning a branch//

    [Header("Fade Settings")]
    [SerializeField] private float fadeTime = 300f; //Seconds to fade to win opacity//
    [SerializeField] private float minOpacity = 0.15f; //Marks never fully dissapear//

    //Mark tracking//
    private List<MarkData> _marks = new List<MarkData>();
    private Material _material;

    //Timing//
    private float _spawnTimer = 0f;
    private bool _active = false;

    //Cached timbral values for colour use//
    private TimbralProfile _profile;
    private PRISMGenerator _prism;

    //Colours sourced from PRISMGenerator via TimbralProfile colour context//
    private Color _primaryColour = Color.cyan;
    private Color _secondaryColour = Color.red;

    private class MarkData
    {
        public GameObject go;
        public MeshRenderer renderer;
        public Material material;
        public float spawnTime;
        public Color colour;
        public float lastAlpha = -1f;
    }

    public void SetPrism(PRISMGenerator prism)
    {
        _prism = prism;
    }

    public void Init(TimbralProfile profile) 
    {
        _profile = profile;
        if (_prism != null) _prism.RegisterGenerator(GeneratorID.FluidTendril);

        //Unlit additvive material: tendrils glow with their own colour//
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        _material = new Material(shader);
        _material.SetFloat("_Surface", 1f);
        _material.SetFloat("_Blend", 0f);
        _material.SetFloat("_SrcBlend", 5f);
        _material.SetFloat("_DstBlend", 10f);
        _material.SetFloat("_ZWrite", 0f);
        _material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _material.renderQueue = 3000;

        Debug.Log("PRSIM FLuidTendrilGenerator: Initialised");
    }

    public void UpdateGenerator(TimbralProfile profile)
    {
        _profile = profile;

        //Check if this generator should be active right now//
        //Weight is driven by low flatness * low ZCR - tonal smooth music//
        float weight = profile.WeightFluidTendril;
        _active = weight > weightThreshold;

        //Debug.Log($"FluidTendril weight: {profile.WeightFluidTendril:F4}  active: {_active}  energy: {profile.RealtimeEnergy:F6}");

        //Spawn new tendrills on sustained energy, not sharp transients//
        if (_active && profile.RealtimeEnergy > energyThreshold)
        {
            _spawnTimer -= Time.deltaTime;
            if (_spawnTimer <= 0f)
            {
                //Prominence scales spawn rate: a dominant tendril grows fast and fills//
                //the canvas, a minor one accumulates slowly as a peripheral trace.//
                Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.FluidTendril) : Prominence.Silent;
                float rateScale = Mathf.Lerp(0.4f, 1f, pr.prominence);
                _spawnTimer = spawnInterval / Mathf.Max(weight * rateScale, 0.1f);
                SpawnTendril(profile, pr);
            }
        }
        else
        {
            //Slow timer decay when not active so it doesn't instantly fire on reactivation//
            _spawnTimer = Mathf.Max(_spawnTimer - Time.deltaTime * 0.1f, 0f);
        }

        
        UpdateFades();
    }
    public void Deactivate()
    {
        _active = false;
        //Marks remain on canvas - stops spawning//
    }

    private void SpawnTendril(TimbralProfile profile, Prominence pr)
    {
        if (_marks.Count >= maxTendrils)
        {
            //Cull oldest mark//
            if (_marks[0].go != null) Destroy(_marks[0].go);
            _marks.RemoveAt(0);
        }

        //Derive tendril character from current timbral state//
        //Low centroid = dark downward-drifting curves//
        //High centroid = bright upward-reaching curves//
        float centroid = profile.RealtimeCentroid;
        float flux = profile.RealtimeFlux;
        float harmonic = profile.RealtimeHarmonicComplexity;

        //Prominence scales the spawn field: dominant tendrils fill a wide sphere centred//
        //on the canvas, minor ones cluster in a small area pushed off-centre.//
        float area = spawnRadius * Mathf.Lerp(0.4f, 1f, pr.prominence);
        Vector3 fieldCentre = transform.position + Random.onUnitSphere * (spawnRadius * (1f - pr.centrality));

        //Spawn origin: loosely random within the (prominence-scaled) spawn field//
        Vector3 origin = fieldCentre + new Vector3(Random.Range(-area, area), Random.Range(-area * 0.4f, area * 0.4f), Random.Range(-area * 0.6f, area * 0.6f));

        //Direction: slow gentle drift, biased by centroid//
        //Bright tracks drift upward, dark tracks drift downward and sideways//
        Vector3 baseDir = new Vector3(Random.Range(-0.4f, 0.4f), Mathf.Lerp(-0.3f, 0.5f, centroid), Random.Range(-0.3f, 0.3f)).normalized; //Centroid drives vertical bias//

        //Prominence scales overall mark size so a minor tendril reads as a fine trace//
        float sizeScale = Mathf.Lerp(0.5f, 1f, pr.prominence);

        //Length vaires with harmonic complexity, complex music = longer tendrils//
        float length = tendrilLength * Mathf.Lerp(0.5f, 1.5f, harmonic) * Random.Range(0.6f, 1.4f) * sizeScale;

        //Width driven by flux, more percussive moments = slightly wider marks//
        float width = tendrilWidth * Mathf.Lerp(0.4f, 1f, 1f - flux) * sizeScale;

        //Turbulence driven by harmonic complexity//
        //Simple tonal music = smooth curves, complex harmonics = more wayward curves//
        float turbulence = Mathf.Lerp(0.2f, 1.2f, harmonic);

        //Generate the curve//
        Vector3[] points = GenerateCurvePoints(origin, baseDir, length, turbulence, centroid);

        //Random Ribbon orientation os tendrils catch light differently//
        Vector3 ribbonUp = new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0.6f, 1f), Random.Range(-0.2f, 0.2f)).normalized;

        Mesh mesh = GenerateRibbonMesh(points, width, ribbonUp);

        //Colour: blend between primary and secondary based on centroid//
        //Bright passages pull toward secondary, dark toward primary//
        Color markColour = DeriveColour();

        //Create mark GameObject//
        GameObject markGO = new GameObject("FluidTendril");
        markGO.transform.SetParent(transform);

        MeshFilter mf = markGO.AddComponent<MeshFilter>();
        MeshRenderer mr = markGO.AddComponent<MeshRenderer>();
        mf.mesh = mesh;

        Material instanceMat = new Material(_material);
        instanceMat.color = markColour;
        mr.material = instanceMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        _marks.Add(new MarkData
        {
            go = markGO,
            renderer = mr,
            material = instanceMat,
            spawnTime = Time.time,
            colour = markColour
        });

        //Optionally spawn a branch from a mid point on this tendril//
        if (Random.value < branchChance * profile.WeightFluidTendril)
        {
            SpawnBranch(points, profile);
        }
    }

    private void SpawnBranch(Vector3[] parentPoints, TimbralProfile profile)
    {
        if (_marks.Count >= maxTendrils) return;

        //Branch starts from a random point along the parent tendril//
        int branchStart = Random.Range(parentPoints.Length / 4, parentPoints.Length * 3/ 4);
        Vector3 origin = parentPoints[branchStart];

        //Branch direction deviates from parent//
        Vector3 branchDir = new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(-0.4f, 0.6f), Random.Range(-0.4f, 0.4f)).normalized;

        float branchLength = tendrilLength * Random.Range(0.2f, 0.6f);
        float turbulence = Mathf.Lerp(0.3f, 1f, profile.RealtimeHarmonicComplexity);

        Vector3[] points = GenerateCurvePoints(origin, branchDir, branchLength, turbulence, profile.RealtimeCentroid);

        Vector3 ribbonUp = new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0.5f, 1f), Random.Range(-0.3f, 0.3f)).normalized;

        Mesh mesh = GenerateRibbonMesh(points, tendrilWidth * 0.4f, ribbonUp);
        Color branchCol = DeriveColour();

        GameObject branchGo = new GameObject("FluidTendril_Branch");
        branchGo.transform.SetParent(transform);

        MeshFilter mf = branchGo.AddComponent<MeshFilter>();
        MeshRenderer mr = branchGo.AddComponent<MeshRenderer>();
        mf.mesh = mesh;

        Material instanceMat = new Material(_material);
        instanceMat.color = branchCol;
        mr.material = instanceMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        _marks.Add(new MarkData
        {
            go = branchGo,
            renderer = mr,
            material = instanceMat,
            spawnTime = Time.time,
            colour = branchCol
        });
    }

    private Vector3[] GenerateCurvePoints(Vector3 origin, Vector3 direction, float length, float turbulence, float centroid)
    {
        Vector3[] points = new Vector3[pointCount];

        float stepLength = length / pointCount;
        float noiseSeed = Random.Range(0f, 100f);
        float noiseScale = Mathf.Lerp(0.8f, 3f, turbulence);

        Vector3 currentPos = origin;
        Vector3 currentDir = direction;

        for (int i = 0; i < pointCount; i++)
        {
            points[i] = currentPos;

            float t = i / (float)(pointCount - 1);

            //Sample noise to rotate direction continuously//
            //This creates genuine spiralling and winding rather than a single arc//
            float noiseX = (Mathf.PerlinNoise(noiseSeed + t * noiseScale, 0.3f) - 0.5f) * 2f;
            float noiseY = (Mathf.PerlinNoise(0.7f, noiseSeed + t * noiseScale) - 0.5f) * 2f;
            float noiseZ = (Mathf.PerlinNoise(noiseSeed * 0.5f, t * noiseScale + 0.5f) - 0.5f) * 2f;

            //Rotate current direction by noise — grows more wayward as it travels//
            float curlStrength = turbulence * Mathf.Lerp(0.3f, 1.2f, t);
            currentDir += new Vector3(noiseX, noiseY, noiseZ) * curlStrength;

            //Vertical bias from centroid — bright = upward drift, dark = downward//
            currentDir.y += Mathf.Lerp(-0.05f, 0.1f, centroid);

            currentDir = currentDir.normalized;
            currentPos += currentDir * stepLength;
        }

        return points;

    }

    private Mesh GenerateRibbonMesh(Vector3[] curvePoints, float maxWidth, Vector3 ribbonUp)
    {
        int n = curvePoints.Length;

        //Validate all curve points before building mesh//
        for (int i = 0; i < n; i++)
        {
            if (float.IsNaN(curvePoints[i].x) || float.IsNaN(curvePoints[i].y) || float.IsNaN(curvePoints[i].z))
            {
                Debug.LogError($"NaN in curve point {i}: {curvePoints[i]}");
                return new Mesh(); //Return empty mesh rather than crash//
            }
            if (float.IsInfinity(curvePoints[i].x) || float.IsInfinity(curvePoints[i].y) || float.IsInfinity(curvePoints[i].z))
            {
                Debug.LogError($"Infinity in curve point {i}: {curvePoints[i]}");
                return new Mesh();
            }
        }

        Mesh mesh = new Mesh();
        Vector3[] vertices = new Vector3[n * 2];
        Vector2[] uvs = new Vector2[n * 2];
        int[] triangles = new int[(n - 1) * 6];

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)(n - 1);

            float widthProfile = Mathf.Sin(t * Mathf.PI);
            widthProfile = Mathf.Pow(widthProfile, 0.7f);
            widthProfile = Mathf.Clamp01(widthProfile);
            float width = maxWidth * widthProfile;

            Vector3 diff;
            if (i < n - 1) diff = curvePoints[i + 1] - curvePoints[i];
            else diff = curvePoints[i] - curvePoints[i - 1];

            Vector3 tangent = diff.magnitude > 0.0001f ? diff.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(tangent, ribbonUp);
            right = right.magnitude > 0.0001f ? right.normalized : Vector3.right;

            vertices[i * 2] = curvePoints[i] - right * width * 0.5f;
            vertices[i * 2 + 1] = curvePoints[i] + right * width * 0.5f;

            // Final NaN check on vertices
            if (float.IsNaN(vertices[i * 2].x))
            {
                vertices[i * 2] = curvePoints[i];
                vertices[i * 2 + 1] = curvePoints[i];
            }

            uvs[i * 2] = new Vector2(0f, t);
            uvs[i * 2 + 1] = new Vector2(1f, t);
        }

        int tri = 0;
        for (int i = 0; i < n - 1; i++)
        {
            int bl = i * 2, br = i * 2 + 1;
            int tl = (i + 1) * 2, tr = (i + 1) * 2 + 1;

            triangles[tri++] = bl; triangles[tri++] = tl; triangles[tri++] = br;
            triangles[tri++] = br; triangles[tri++] = tl; triangles[tri++] = tr;
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private Color DeriveColour()
    {
        if (_prism != null) return _prism.RealtimeColour;
        return Color.HSVToRGB(0.5f, 0.6f, 0.9f);
    }

    private void UpdateFades()
    {
        float now = Time.time;
        for(int i = 0; i < _marks.Count; i++)
        {
            if (_marks[i].material == null) continue;
            float age = now - _marks[i].spawnTime;
            float fadeT = Mathf.Clamp01(age / fadeTime);
            float alpha = Mathf.Lerp(1f, minOpacity, fadeT);

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
        foreach (var mark in _marks)
            if(mark.material != null) Destroy(mark.material);
    }
}
