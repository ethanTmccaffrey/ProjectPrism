#pragma warning disable 0414
using System.Collections.Generic;
using UnityEngine;

public class SpatialGenerator : MonoBehaviour
{
    //Mode//
    public enum GenerationMode { Landscape, Organic, Abstract }
    public enum LandscapeShape { Square, Circle, Diamond, Cross}
    public enum GridSize { Small = 64, Medium = 128, Large = 256, Ultra = 512 }

    [Header("Generation Settings")]
    [SerializeField] private GenerationMode mode = GenerationMode.Landscape;
    [SerializeField] private GridSize gridSize = GridSize.Small;
    [SerializeField] private LandscapeShape landscapeShape = LandscapeShape.Square;

    //Landscape Mesh State//
    private Mesh _landscapeMesh;
    private GameObject _meshObject;
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Vector3[] _meshVertices;
    private Color[] _meshColours;
    private int _meshResolution;
    private bool _meshAnimating = false;
    private bool _meshPaused = false;
    private float[] _baseHeightMap;
    private float[] _currentHeightMap;
    private AudioSource _audioSource;

    //Frequency character per column for smoothing biome//
    private float[] _columnLowRatio;
    private float[] _columnMidRatio;
    private float[] _columnHighRatio;

    //Internal//
    private AudioAnalyser _analyser;
    private GameObject _generatedEnvironment;
    private OrganicGenerator _organicGenerator;

    //Primitive prefab references//
    private const float BASE_UNIT = 1f;

    private void Awake()
    {
        _organicGenerator = GetComponent<OrganicGenerator>();
        if (_organicGenerator == null) _organicGenerator = gameObject.AddComponent<OrganicGenerator>();
    }

    private void Update()
    {
        //if (_meshAnimating && !_meshPaused && mode == GenerationMode.Landscape) UpdateLandscapeMesh();
    }
    public void Generate(AudioAnalyser analyser)
    {
        _analyser = analyser;

        //Clear any previously generated environment//
        if (_generatedEnvironment != null) Destroy(_generatedEnvironment);

        _generatedEnvironment = new GameObject("PRISM_Environment");

        switch (mode)
        {
            case GenerationMode.Landscape:
                GenerateLandscape();
                break;
            case GenerationMode.Organic:
                _organicGenerator.Generate(analyser, _generatedEnvironment);
                break;
            case GenerationMode.Abstract:
                //Phase 3//
                break;

        }
    }

    private void GenerateLandscape()
    {
        GenerateLandscapeMesh();
    }
    private void GenerateLandscapeMesh()
    {
        int size = (int)gridSize;
        _meshResolution = size;

        float[] energyMap = _analyser.EnergyOverTime;
        float peakEnergy = _analyser.PeakEnergy;
        float avgEnergy = _analyser.AverageEnergy;
        int segmentCount = energyMap.Length;

        //Initialise frequency character arrays//
        _columnLowRatio = new float[size];
        _columnMidRatio = new float[size];
        _columnHighRatio = new float[size];

        //Step 1: Force-directed placement in normalised 0-1 space, Places musically similar segments near each other spatially//
        Vector2[] segmentPositions = new Vector2[segmentCount];

        for (int i = 0; i < segmentCount; i++)
        {
            float angle = (i / (float)segmentCount) * Mathf.PI * 2f;
            segmentPositions[i] = new Vector2(0.5f + Mathf.Cos(angle) * 0.35f, 0.5f + Mathf.Sin(angle) * 0.35f);
        }

        for (int iter = 0; iter < 120; iter++)
        {
            Vector2[] forces = new Vector2[segmentCount];
            float dampen = 1f - (iter / 120f);

            for (int a = 0; a < segmentCount; a++)
            {
                for (int b = a + 1; b < segmentCount; b++)
                {
                    Vector2 delta = segmentPositions[b] - segmentPositions[a];
                    float dist = Mathf.Max(delta.magnitude, 0.01f);
                    Vector2 dir = delta.normalized;

                    float rawDist = GetMusicalDistance(a, b);
                    float musicalSim = 1f - Mathf.Clamp01(rawDist / 3f);

                    float attraction = musicalSim * 0.8f;
                    float repulsion = 0.02f / (dist * dist);
                    float net = attraction - repulsion;

                    forces[a] += dir * net;
                    forces[b] -= dir * net;
                }
            }

            for (int i = 0; i < segmentCount; i++)
            {
                segmentPositions[i] += forces[i] * 0.01f * dampen;
                segmentPositions[i].x = Mathf.Clamp(segmentPositions[i].x, 0.05f, 0.95f);
                segmentPositions[i].y = Mathf.Clamp(segmentPositions[i].y, 0.05f, 0.95f);
            }
        }

        //Step 2: Build heightmap using layered Perlin noise, Each grid point finds its nearest segment That segment's musical data drives the noise parameters //
        _baseHeightMap = new float[size * size];

        int[] nearestSegMap = new int[size * size];
        int[] secondSegMap = new int[size * size];
        float[] blendTMap = new float[size * size];

        for (int x = 0; x < size; x++)
        {
            for (int z = 0; z < size; z++)
            {
                int idx = x * size + z;

                if (!IsInsideShape(x, z, size))
                {
                    _baseHeightMap[idx] = -1f;
                    continue;
                }

                float nx = x / (float)(size - 1);
                float nz = z / (float)(size - 1);

                //Find nearest AND second nearest segment//
                float bestDist = float.MaxValue;
                float secondDist = float.MaxValue;
                int nearestSeg = 0;
                int secondSeg = 0;

                for (int s = 0; s < segmentCount; s++)
                {
                    float dx = nx - segmentPositions[s].x;
                    float dz = nz - segmentPositions[s].y;
                    float dist = dx * dx + dz * dz;
                    if (dist < bestDist)
                    {
                        secondDist = bestDist;
                        secondSeg = nearestSeg;
                        bestDist = dist;
                        nearestSeg = s;
                    }
                    else if (dist < secondDist)
                    {
                        secondDist = dist;
                        secondSeg = s;
                    }
                }

                //Blend weight how close are we to the boundary//
                float blendT = Mathf.Clamp01(bestDist / (bestDist + secondDist));

                //Get musical data for nearest segment//
                float normalizedEnergyA = energyMap[nearestSeg] / peakEnergy;
                float lowA = _analyser.LowEnergyOverTime[nearestSeg];
                float midA = _analyser.MidEnergyOverTime[nearestSeg];
                float highA = _analyser.HighEnergyOverTime[nearestSeg];
                float totalA = Mathf.Max(lowA + midA + highA, 0.001f);
                float lowRatioA = lowA / totalA;
                float midRatioA = midA / totalA;
                float highRatioA = highA / totalA;

                //Store frequency character for biome//
                _columnLowRatio[x] = lowRatioA;
                _columnMidRatio[x] = midRatioA;
                _columnHighRatio[x] = highRatioA;

                //Height A: nearest segment//
                float amplitudeA = Mathf.Lerp(0.5f, 22f, Mathf.Pow(normalizedEnergyA, 1.5f));
                float noiseFreqA = Mathf.Lerp(0.04f, 0.2f, highRatioA);
                float seedXA = nearestSeg * 127.3f;
                float seedZA = nearestSeg * 311.7f;

                float heightA = 0f;
                heightA += Mathf.PerlinNoise((x + seedXA) * noiseFreqA, (z + seedZA) * noiseFreqA) * amplitudeA;
                heightA += Mathf.PerlinNoise((x + seedXA) * noiseFreqA * 2.1f, (z + seedZA) * noiseFreqA * 2.1f) * amplitudeA * 0.5f;
                heightA += Mathf.PerlinNoise((x + seedXA) * noiseFreqA * 4.3f, (z + seedZA) * noiseFreqA * 4.3f) * amplitudeA * 0.25f;
                heightA += Mathf.PerlinNoise((x + seedXA) * noiseFreqA * 8.7f, (z + seedZA) * noiseFreqA * 8.7f) * amplitudeA * 0.125f;
                heightA += Mathf.PerlinNoise((x + seedXA) * noiseFreqA * 17.3f, (z + seedZA) * noiseFreqA * 17.3f) * amplitudeA * 0.0625f * (1f + highRatioA * 2f);

                //Domain warping//
                float warpX = Mathf.PerlinNoise((x + seedXA) * noiseFreqA * 0.5f + 100f, (z + seedZA) * noiseFreqA * 0.5f) * 15f;
                float warpZ = Mathf.PerlinNoise((x + seedXA) * noiseFreqA * 0.5f, (z + seedZA) * noiseFreqA * 0.5f + 100f) * 15f;
                heightA += Mathf.PerlinNoise((x + seedXA + warpX) * noiseFreqA * 2f, (z + seedZA + warpZ) * noiseFreqA * 2f) * amplitudeA * 0.3f;

                heightA *= normalizedEnergyA;

                //Height B: second nearest segment//
                float normalizedEnergyB = energyMap[secondSeg] / peakEnergy;
                float lowB = _analyser.LowEnergyOverTime[secondSeg];
                float midB = _analyser.MidEnergyOverTime[secondSeg];
                float highB = _analyser.HighEnergyOverTime[secondSeg];
                float totalB = Mathf.Max(lowB + midB + highB, 0.001f);
                float lowRatioB = lowB / totalB;
                float highRatioB = highB / totalB;

                float amplitudeB = Mathf.Lerp(0.5f, 22f, Mathf.Pow(normalizedEnergyB, 1.5f));
                float noiseFreqB = Mathf.Lerp(0.04f, 0.2f, highRatioB);
                float seedXB = secondSeg * 127.3f;
                float seedZB = secondSeg * 311.7f;

                float heightB = 0f;
                heightB += Mathf.PerlinNoise((x + seedXB) * noiseFreqB,(z + seedZB) * noiseFreqB) * amplitudeB;
                heightB += Mathf.PerlinNoise((x + seedXB) * noiseFreqB * 2.1f,(z + seedZB) * noiseFreqB * 2.1f) * amplitudeB * 0.5f;
                heightB += Mathf.PerlinNoise((x + seedXB) * noiseFreqB * 4.3f,(z + seedZB) * noiseFreqB * 4.3f) * amplitudeB * 0.25f;
                heightB += Mathf.PerlinNoise((x + seedXB) * noiseFreqB * 8.7f,(z + seedZB) * noiseFreqB * 8.7f) * amplitudeB * 0.125f;
                heightB += Mathf.PerlinNoise((x + seedXB) * noiseFreqB * 17.3f,(z + seedZB) * noiseFreqB * 17.3f) * amplitudeB * 0.0625f * (1f + highRatioB * 2f);
                heightB *= normalizedEnergyB;

                //Blend at boundaries//
                _baseHeightMap[idx] = Mathf.Lerp(heightA, heightB, blendT * 0.5f);
            }
        }

        //Step 3: Smoothing pass at boundaries between segments, Low frequency dominant tracks get more smoothing for softer terrain//
        float avgLow = 0f;
        for (int i = 0; i < segmentCount; i++) avgLow += _analyser.LowEnergyOverTime[i];
        avgLow /= segmentCount;
        float avgTotal = Mathf.Max(avgLow + avgEnergy, 0.001f);
        float smoothAmt = Mathf.Clamp01(avgLow / avgTotal);
        int smoothPasses = Mathf.RoundToInt(Mathf.Lerp(3f, 6f, smoothAmt));

        for (int pass = 0; pass < smoothPasses; pass++)
            _baseHeightMap = SmoothHeightMap(_baseHeightMap, size);

        //Initialise current heightmap for animation//
        _currentHeightMap = new float[size * size];
        for (int i = 0; i < _baseHeightMap.Length; i++)
            _currentHeightMap[i] = _baseHeightMap[i];

        //Step 4: Build mesh//
        _meshObject = new GameObject("PRISM_LandscapeMesh");
        _meshObject.transform.SetParent(_generatedEnvironment.transform);
        _meshFilter = _meshObject.AddComponent<MeshFilter>();
        _meshRenderer = _meshObject.AddComponent<MeshRenderer>();
        _meshRenderer.material = new Material(Shader.Find("Custom/VertexColour"));

        _landscapeMesh = new Mesh();
        _landscapeMesh.name = "LandscapeMesh";
        _landscapeMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        _meshVertices = new Vector3[size * size];
        _meshColours = new Color[size * size];
        Vector2[] uvs = new Vector2[size * size];
        float worldScale = 2f;

        for (int x = 0; x < size; x++)
        {
            for (int z = 0; z < size; z++)
            {
                int idx = x * size + z;
                float height = _baseHeightMap[idx];
                _meshVertices[idx] = new Vector3(x * worldScale, height, z * worldScale);
                uvs[idx] = new Vector2(x / (float)size, z / (float)size);
                _meshColours[idx] = GetBiomeColour(x, z, height, size);
            }
        }

        int[] triangles = new int[(size - 1) * (size - 1) * 6];
        int t = 0;
        for (int x = 0; x < size - 1; x++)
        {
            for (int z = 0; z < size - 1; z++)
            {
                int idx = x * size + z;
                triangles[t++] = idx;
                triangles[t++] = idx + 1;
                triangles[t++] = idx + size;
                triangles[t++] = idx + 1;
                triangles[t++] = idx + size + 1;
                triangles[t++] = idx + size;
            }
        }

        _landscapeMesh.vertices = _meshVertices;
        _landscapeMesh.triangles = triangles;
        _landscapeMesh.uv = uvs;
        _landscapeMesh.colors = _meshColours;
        _landscapeMesh.RecalculateNormals();
        _meshFilter.mesh = _landscapeMesh;

        //Tessellation//
        SubdivideMesh(2);

        _meshAnimating = true;
        _meshPaused = false;
    }

    private float[] SmoothHeightMap(float[] heightmap, int size)
    {
        float[] smoothed = new float[heightmap.Length];

        for(int x = 0; x < size; x++)
        {
            for(int z = 0; z < size; z++)
            {
                float sum = 0f;
                int count = 0;

                for(int nx = x - 1; nx <= x + 1; nx++)
                {
                    for(int nz = z - 1; nz <= z + 1; nz++)
                    {
                        if(nx >= 0 && nx < size && nz >= 0 && nz < size)
                        {
                            sum += heightmap[nx * size + nz];
                            count++;
                        }
                    }
                }

                smoothed[x * size + z] = sum / count;
            }
        }

        return smoothed;
    }

    private Color GetBiomeColour(int x, int z, float height, int size)
    {
        float lowRatio = _columnLowRatio[x];
        float midRatio = _columnMidRatio[x];
        float highRatio = _columnHighRatio[x];

        float waterLevel = 0.5f;
        float sandLevel = 1.5f;
        float grassLevel = 5f;
        float rockLevel = 11f;
        float snowLevel = 14f;

        //Transition zone width — smooth blending between biomes//
        float transitionWidth = 0.8f;

        Color deepWater = new Color(0.05f, 0.15f, 0.5f);
        Color shallowWater = new Color(0.1f, 0.3f, 0.7f);
        Color sand = new Color(0.76f, 0.7f, 0.5f);
        Color darkSand = new Color(0.6f, 0.55f, 0.35f);
        Color baseGrass = new Color(0.2f, 0.5f, 0.15f);
        Color richGrass = new Color(0.1f, 0.4f, 0.05f);
        Color baseRock = new Color(0.4f, 0.35f, 0.3f);
        Color highRock = new Color(0.55f, 0.45f, 0.35f);
        Color snowBase = new Color(0.85f, 0.9f, 0.95f);

        //Grass varies by mid frequency, more mid = richer greener//
        Color grassColour = Color.Lerp(baseGrass, richGrass, midRatio);
        //Rock varies by high frequency, more high = warmer rockier//
        Color rockColour = Color.Lerp(baseRock, highRock, highRatio);

        if (height < waterLevel)
            return Color.Lerp(deepWater, shallowWater, height / waterLevel);

        if (height < sandLevel)
        {
            float t = (height - waterLevel) / (sandLevel - waterLevel);
            //Blend water to sand near waterline//
            if (t < transitionWidth / (sandLevel - waterLevel))
                return Color.Lerp(shallowWater, sand, t * (sandLevel - waterLevel) / transitionWidth);
            return Color.Lerp(sand, darkSand, t);
        }

        if (height < grassLevel)
        {
            float t = (height - sandLevel) / (grassLevel - sandLevel);
            //Smooth transition sand to grass//
            if (t < transitionWidth / (grassLevel - sandLevel))
                return Color.Lerp(darkSand, grassColour, t * (grassLevel - sandLevel) / transitionWidth);
            return grassColour;
        }

        if (height < rockLevel)
        {
            float t = (height - grassLevel) / (rockLevel - grassLevel);
            //Smooth transition grass to rock//
            if (t < transitionWidth / (rockLevel - grassLevel))
                return Color.Lerp(grassColour, rockColour, t * (rockLevel - grassLevel) / transitionWidth);
            return rockColour;
        }

        if (height < snowLevel)
        {
            float t = (height - rockLevel) / (snowLevel - rockLevel);
            return Color.Lerp(rockColour, snowBase, t);
        }

        return Color.Lerp(snowBase, Color.white, (height - snowLevel) / 4f);
    }

    private void UpdateLandscapeMesh()
    {
        if (_audioSource == null || !_audioSource.isPlaying) return;
        if (_landscapeMesh == null) return;

        int size = _meshResolution;

        float[] spectrum = new float[256];
        _audioSource.GetSpectrumData(spectrum, 0, FFTWindow.BlackmanHarris);

        //Overall energy for global breathing//
        float overallEnergy = 0f;
        for (int i = 0; i < spectrum.Length; i++) overallEnergy += spectrum[i];
        overallEnergy /= spectrum.Length;
        float normalizedOverall = Mathf.Clamp01(overallEnergy * 200f);

        // Per column spectrum energy
        for (int x = 0; x < size; x++)
        {
            //Logarithmic mapping so bass and treble both contribute//
            float logT = Mathf.Pow(x / (float)size, 1.5f);
            int bin = Mathf.Clamp(Mathf.RoundToInt(logT * (spectrum.Length - 1)), 0, spectrum.Length - 1);
            float columnEnergy = spectrum[bin] * 12f;

            for (int z = 0; z < size; z++)
            {
                int idx = x * size + z;
                float baseHeight = _baseHeightMap[idx];

                //Breathing — peaks swell on loud moments, proportional to base height//
                //High ground reacts more than low ground//
                float heightInfluence = Mathf.Clamp01(baseHeight / 11f);
                float breatheAmount = columnEnergy * heightInfluence;
                float globalBreathe = normalizedOverall * 1.5f * heightInfluence;

                float targetHeight = baseHeight + breatheAmount + globalBreathe;

                //Smooth lerp organic growth feeling//
                float lerpSpeed = Mathf.Lerp(1f, 4f, normalizedOverall);
                _currentHeightMap[idx] = Mathf.Lerp( _currentHeightMap[idx], targetHeight, lerpSpeed * Time.deltaTime);

                _meshVertices[idx].y = _currentHeightMap[idx];
                _meshColours[idx] = GetBiomeColour(x, z, _currentHeightMap[idx], size);
            }
        }

        _landscapeMesh.vertices = _meshVertices;
        _landscapeMesh.colors = _meshColours;
        _landscapeMesh.RecalculateNormals();
        _landscapeMesh.RecalculateBounds();
    }

    public void PauseLandscape()
    {
        _meshPaused = !_meshPaused;
    }

    public void SetAudioSource(AudioSource source)
    {
        _audioSource = source;
    }

    private void SubdivideMesh(int passes)
    {
        for (int pass = 0; pass < passes; pass++)
        {
            Vector3[] oldVerts = _landscapeMesh.vertices;
            Color[] oldColours = _landscapeMesh.colors;
            int[] oldTris = _landscapeMesh.triangles;

            int newTriCount = oldTris.Length;
            Dictionary<long, int> midpointCache = new Dictionary<long, int>();

            List<Vector3> newVerts = new List<Vector3>(oldVerts);
            List<Color> newColours = new List<Color>(oldColours);
            List<int> newTris = new List<int>();

            for (int i = 0; i < oldTris.Length; i += 3)
            {
                int i0 = oldTris[i];
                int i1 = oldTris[i + 1];
                int i2 = oldTris[i + 2];

                int m01 = GetMidpoint(i0, i1, newVerts, newColours, oldVerts, oldColours, midpointCache);
                int m12 = GetMidpoint(i1, i2, newVerts, newColours, oldVerts, oldColours, midpointCache);
                int m20 = GetMidpoint(i2, i0, newVerts, newColours, oldVerts, oldColours, midpointCache);

                //Four triangles from one//
                newTris.Add(i0); newTris.Add(m01); newTris.Add(m20);
                newTris.Add(i1); newTris.Add(m12); newTris.Add(m01);
                newTris.Add(i2); newTris.Add(m20); newTris.Add(m12);
                newTris.Add(m01); newTris.Add(m12); newTris.Add(m20);
            }

            _landscapeMesh.vertices = newVerts.ToArray();
            _landscapeMesh.colors = newColours.ToArray();
            _landscapeMesh.triangles = newTris.ToArray();
            _landscapeMesh.RecalculateNormals();

            //Update vertex arrays to match//
            _meshVertices = _landscapeMesh.vertices;
            _meshColours = _landscapeMesh.colors;
        }
    }

    private int GetMidpoint(int a, int b, List<Vector3> verts, List<Color> colours, Vector3[] oldVerts, Color[] oldColours, Dictionary<long, int> cache)
    {
        long key = ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
        if (cache.TryGetValue(key, out int existing)) return existing;

        Vector3 mid = (oldVerts[a] + oldVerts[b]) * 0.5f;
        Color midCol = Color.Lerp(oldColours[a], oldColours[b], 0.5f);

        int idx = verts.Count;
        verts.Add(mid);
        colours.Add(midCol);
        cache[key] = idx;
        return idx;
    }

    private void GeneratePrimitveLandscape()
    {
        int size = (int)gridSize;
        float[] energyMap = _analyser.EnergyOverTime; //64 Segments//
        float pitchRegister = _analyser.PitchRegister; //0-1//
        float avgEnergy = _analyser.AverageEnergy;
        float peakEnergy = _analyser.PeakEnergy;
        float bpm = _analyser.EstimatedTempo;

        //How many grid columns map to each energy segment//
        int columnsPerSegment = size / energyMap.Length;

        //BPM drives objects spacing - Faster = Denser//
        float spacingMultiplier = Mathf.Lerp(2f, 0.8f, Mathf.InverseLerp(60f, 180f, bpm));


        for (int segment = 0; segment < energyMap.Length; segment++)
        {
            float normalizedEnergy = energyMap[segment] / peakEnergy;

            //Height driven by energy//
            float terrainHeight = Mathf.Lerp(2f, 12f, normalizedEnergy);

            //Each segment now gets its own colour//
            Color shapeColour = GetSegmentColour(segment);

            //Shape type driven by energy level//
            //Low energy = cube, mid = sphere, high = pyramid//
            PrimitiveType shapeType = GetShapeFromEnergy(normalizedEnergy);

            for (int col = 0; col < columnsPerSegment; col++)
            {
                int x = segment * columnsPerSegment + col;

                for (int z = 0; z < size; z += 2)
                {
                    //Skip positions outside the choen shape//
                    if (!IsInsideShape(x, z, size)) continue;

                    //Add variation so it doesnt look perfectly uniform//
                    float heightVariation = Random.Range(0.8f, 1.2f);
                    float finalHeight = terrainHeight * heightVariation;

                    Vector3 position = shapeType == PrimitiveType.Sphere ? new Vector3(x * 2f, finalHeight, z * 2f) : new Vector3(x * 2f, finalHeight / 2f, z * 2f);

                    PlaceShape(shapeType, position, shapeType == PrimitiveType.Sphere ? new Vector3(BASE_UNIT * 1.8f, BASE_UNIT * 1.8f, BASE_UNIT * 1.8f) : new Vector3(BASE_UNIT * 1.8f, finalHeight, BASE_UNIT * 1.8f), shapeColour);
                }
            }
        }
    }

    
    public static GameObject CreatePrimitiveChild(PrimitiveType type, GameObject parent)
    {
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.transform.SetParent(parent.transform);
        Collider col = obj.GetComponent<Collider>();
        if (col != null) Destroy(col);
        return obj;
    }

    public static void ApplyColour(GameObject obj, Color colour)
    {
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = colour;
    }

    private void PlaceShape(PrimitiveType type, Vector3 position, Vector3 scale, Color colour)
    {
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.transform.position = position;
        obj.transform.localScale = scale;
        ApplyColour(obj, colour);
    }

    
    public Color GetSegmentColour(int segment)
    {
        float low = _analyser.LowEnergyOverTime[segment];
        float mid = _analyser.MidEnergyOverTime[segment];
        float high = _analyser.HighEnergyOverTime[segment];

        float total = low + mid + high;
        if (total == 0) return Color.grey;

        //Normalise each band as proportion of this segment's total energy//
        float lowRatio = low / total;
        float midRatio = mid / total;
        float highRatio = high / total;

        //Map frequency balance directly to hue//
        //Low = warm (0-60 degrees)//
        //Mid = green (90-150 degrees)//
        //High = blue/violet (200-280 degrees)//
        float hue = (lowRatio * 30f + midRatio * 120f + highRatio * 240f) / 360f;

        //Saturation driven by how dominant the the winning band is if one band clealry dominates, colour is vivid. if all equal, more grey//
        float maxRatio = Mathf.Max(lowRatio, midRatio, highRatio);
        float saturation = Mathf.Lerp(0.2f, 1f, (maxRatio - 0.33f) / 0.67f);

        //Brightness driven by energy//
        float brightness = Mathf.Lerp(0.4f, 1f, _analyser.EnergyOverTime[segment] / _analyser.PeakEnergy);

        return Color.HSVToRGB(hue, saturation, brightness);
    }
    private PrimitiveType GetShapeFromEnergy(float normalizedEnergy)
    {
        if (normalizedEnergy < 0.4f) return PrimitiveType.Cube;
        if (normalizedEnergy < 0.7f) return PrimitiveType.Sphere;
        return PrimitiveType.Cylinder; //Unity doesnt have a pyramid primitive//
    }

    private bool IsInsideShape(int x, int z, int size)
    {
        //Normalise x and z to -1 to 1 range relative to grid centre//
        float nx = (x / (float)size) * 2f - 1f;
        float nz = (z / (float)size) * 2f - 1f;

        switch(landscapeShape)
        {
            case LandscapeShape.Square:
                //All positions valid//
                return true;
            case LandscapeShape.Circle:
                //Inside unit circle//
                return (nx * nx + nz * nz) <= 1f;

            case LandscapeShape.Diamond:
                //Inside diamond = absolute values sum to less than 1//
                return (Mathf.Abs(nx) + Mathf.Abs(nz)) <= 1f;

            case LandscapeShape.Cross:
                //Inside Cross = either x or z is within the centre third//
                return Mathf.Abs(nx) <= 0.33f || Mathf.Abs(nz) <= 0.33f;

            default:
                return true;
        }
    }

    public float GetMusicalDistance(int segA, int segB)
    {
        //How different are two segments musically//
        //Combines energy difference and frequency character difference//
        float energyDiff = Mathf.Abs(_analyser.EnergyOverTime[segA] - _analyser.EnergyOverTime[segB]);

        float lowDiff = Mathf.Abs(_analyser.LowEnergyOverTime[segA] - _analyser.LowEnergyOverTime[segB]);
        float midDiff = Mathf.Abs(_analyser.MidEnergyOverTime[segA] - _analyser.MidEnergyOverTime[segB]);
        float highDiff = Mathf.Abs(_analyser.HighEnergyOverTime[segA] - _analyser.HighEnergyOverTime[segB]);

        float freqDiff = lowDiff + midDiff + highDiff;

        //Weight energy difference more heavily than frequency difference//
        return (energyDiff * 2f) + freqDiff;
    }
}
