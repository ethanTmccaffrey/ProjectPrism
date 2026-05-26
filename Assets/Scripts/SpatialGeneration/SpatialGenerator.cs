using System.Collections.Generic;
using UnityEngine;

public class SpatialGenerator : MonoBehaviour
{
    //Mode//
    public enum GenerationMode { Landscape, Abstract, Organic }
    public enum LandscapeShape { Square, Circle, Diamond, Cross}
    public enum GridSize { Small = 64, Medium = 128, Large = 256 }

    [Header("Generation Settings")]
    [SerializeField] private GenerationMode mode = GenerationMode.Landscape;
    [SerializeField] private GridSize gridSize = GridSize.Small;
    [SerializeField] private LandscapeShape landscapeShape = LandscapeShape.Square;

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

    private void PlaceShapeWithRotation(PrimitiveType type, Vector3 position, Vector3 scale, Color colour, Quaternion rotation)
    {
        GameObject obj = CreatePrimitiveChild(type, _generatedEnvironment);
        obj.transform.rotation = rotation;
        obj.transform.localScale = scale;
        obj.transform.position = position;
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

    private float GetMusicalDistance(int segA, int segB)
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
