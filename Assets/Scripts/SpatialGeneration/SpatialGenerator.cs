using UnityEngine;

public class SpatialGenerator : MonoBehaviour
{
    //Mode//
    public enum GenerationMode { Landscape, Abstract }
    public enum LandscapeShape { Square, Circle, Diamond, Cross}
    public enum GridSize { Small = 64, Medium = 128, Large = 256 }

    [Header("Generation Settings")]
    [SerializeField] private GenerationMode mode = GenerationMode.Landscape;
    [SerializeField] private GridSize gridSize = GridSize.Small;
    [SerializeField] private LandscapeShape landscapeShape = LandscapeShape.Square;

    //Internal//
    private AudioAnalyser _analyser;
    private GameObject _generatedEnvironment;

    //Primitive prefab references//
    private const float BASE_UNIT = 1f;

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

    private PrimitiveType GetShapeFromEnergy(float normalizedEnergy)
    {
        if (normalizedEnergy < 0.4f) return PrimitiveType.Cube;
        if (normalizedEnergy < 0.7f) return PrimitiveType.Sphere;
        return PrimitiveType.Cylinder; //Unity doesnt have a pyramid primitive//
    }

    private void PlaceShape(PrimitiveType type, Vector3 position, Vector3 scale, Color colour)
    {
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.transform.SetParent(_generatedEnvironment.transform);
        obj.transform.position = position;
        obj.transform.localScale = scale;

        //Apply colour via material//
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = colour;
        }

        //Remove collider for now//
        Collider col = obj.GetComponent<Collider>();
        if (col != null) Destroy(col);
    }

    private Color GetSegmentColour(int segment)
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

        //Low -> warm red/organge//
        //Mid -> green/yellow//
        //High -> blue/Cyan//
        //These blend together based on which bands are dominant//
        Color lowColour = new Color(0.9f, 0.15f, 0.05f); //Warm Red//
        Color midColour = new Color(0.2f, 0.8f, 0.2f); //Green//
        Color highColour = new Color(0.05f, 0.4f, 0.95f); //Bright blue//

        //Weighted blend of all three colours by their ratios//
        Color blended = lowColour * lowRatio + midColour * midRatio + highColour * highRatio;

        //Normalise birghtness so no channel dominance makes it too dark//
        float brightness = Mathf.Lerp(0.5f, 1.0f, _analyser.EnergyOverTime[segment] / _analyser.PeakEnergy);
        return blended * brightness;
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
}
