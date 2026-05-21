using UnityEngine;

public class SpatialGenerator : MonoBehaviour
{
    //Mode//
    public enum GenerationMode { Landscape, Abstract }
    public enum GridSize { Small = 64, Medium = 128, Large = 256 }

    [Header("Generation Settings")]
    [SerializeField] private GenerationMode mode = GenerationMode.Landscape;
    [SerializeField] private GridSize gridSize = GridSize.Small;

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

        //Colour Palette from frequency balance//
        Color shapeColour = GetTrackColour();

        for (int segment = 0; segment < energyMap.Length; segment++)
        {
            float normalizedEnergy = energyMap[segment] / peakEnergy;

            //Height driven by energy//
            float terrainHeight = Mathf.Lerp(2f, 12f, normalizedEnergy);

            //Shape type driven by energy level//
            //Low energy = cube, mid = sphere, high = pyramid//
            PrimitiveType shapeType = GetShapeFromEnergy(normalizedEnergy);

            for (int col = 0; col < columnsPerSegment; col++)
            {
                int x = segment * columnsPerSegment + col;

                for (int z = 0; z < size; z += 2)
                {
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

    private Color GetTrackColour()
    {
        float pitch = _analyser.PitchRegister; //0 = low/warm, 1 = high/cool//
        float energy = _analyser.AverageEnergy / _analyser.PeakEnergy;

        float remappedPitch = Mathf.InverseLerp(0.08f, 0.45f, pitch);

        //Define anchor colours for the gradient//
        Color warmColour = new Color(0.8f, 0.1f, 0.05f);
        Color midColour = new Color(0.5f, 0.1f, 0.7f);
        Color coolColour = new Color(0.05f, 0.5f, 0.9f);

        Color baseColour;
        if (remappedPitch < 0.5f)
        {
            baseColour = Color.Lerp(warmColour, midColour, pitch * 2f);
        }
        else
        {
            baseColour = Color.Lerp(midColour, coolColour, (pitch - 0.05f) * 2f);
        }

        //Energy drives brightness - louder tracks are more vivid//
        float brightness = Mathf.Lerp(0.6f, 1.0f, energy);
        float r = baseColour.r * brightness;
        float g = baseColour.g * brightness;
        float b = baseColour.b * brightness;

        Debug.Log($"Pitch: {pitch} | Remapped: {remappedPitch} | R:{r:F3} G:{g:F3} B:{b:F3}");

        return new Color(r, g, b);
    }
}
