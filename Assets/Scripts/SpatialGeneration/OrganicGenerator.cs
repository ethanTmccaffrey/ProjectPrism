using UnityEngine;

public class OrganicGenerator : MonoBehaviour
{
    public enum OrganicShape { DNA, Reef, Roots, Web}

    [Header("Organic Settings")]
    [SerializeField] private OrganicShape shape = OrganicShape.DNA;

    private AudioAnalyser _analyser;
    private GameObject _environment;
    private SpatialGenerator _spatial;

    public void Generate(AudioAnalyser analyser, GameObject environment)
    {
        _analyser = analyser;
        _environment = environment;
        _spatial = GetComponent<SpatialGenerator>();

        switch (shape)
        {
            case OrganicShape.DNA:
                GenerateDNA();
                break;
            case OrganicShape.Reef:
                break;
            case OrganicShape.Roots:
                break;
            case OrganicShape.Web:
                break;
        }
    }

    private void GenerateDNA()
    {
        float[] energyMap = _analyser.EnergyOverTime;
        float peakEnergy = _analyser.PeakEnergy;
        float avgEnergy = _analyser.AverageEnergy;
        int totalSegments = energyMap.Length;
        float bpm = _analyser.EstimatedTempo;

        //BPM drives twist, fast tracks coil tight slow tracks loop wide//
        float twists = Mathf.Lerp(1.5f, 6f, Mathf.InverseLerp(60f, 180f, bpm));
        //Helix parameters//
        float totalHeight = 60f;
        float baseRadius = 5f; //Base width of the helix//

        Vector3[] strandA = new Vector3[totalSegments];
        Vector3[] strandB = new Vector3[totalSegments];

        //Pre calculate cumulative height so loud segments get more vertical space//
        float[] segmentHeights = new float[totalSegments];
        float heightAccumulator = 0f;
        float totalWeight = 0f;

        for (int i = 0; i < totalSegments; i++) totalWeight += Mathf.Lerp(0.5f, 2f, energyMap[i] / peakEnergy);

        for (int i = 0; i < totalSegments; i++)
        {
            float weight = Mathf.Lerp(0.5f, 2f, energyMap[i] / peakEnergy);
            heightAccumulator += (weight / totalWeight) * totalHeight;
            segmentHeights[i] = heightAccumulator;
        }

        //Step 1: Calculate node positions//
        for(int i = 0; i < totalSegments; i++)
        {
            float t = i / (float)(totalSegments - 1); //0-1//
            float normalizedEnergy = energyMap[i] / peakEnergy;

            float angle = t * twists * Mathf.PI * 2f;
            float heightY = t * totalHeight;

            //Energy makes the helix bulge outward at loud moments//
            float radius = Mathf.Lerp(baseRadius * 0.6f, baseRadius * 1.6f, normalizedEnergy);

            strandA[i] = new Vector3(Mathf.Cos(angle) * radius, heightY, Mathf.Sin(angle) * radius);
            strandB[i] = new Vector3(Mathf.Cos(angle + Mathf.PI) * radius, heightY, Mathf.Sin(angle + Mathf.PI) * radius);
        }

        //Step 2: Spawn nodes//
        for(int i = 0; i < totalSegments;i++)
        {
            float normalizedEnergy = energyMap[i] / peakEnergy;
            Color colour = _spatial.GetSegmentColour(i);
            float nodeSize = Mathf.Lerp(0.3f, 1.8f, normalizedEnergy);

            SpawnNode(strandA[i], nodeSize, colour);
            SpawnNode(strandB[i], nodeSize, colour);
        }

        //Step 3: spawn backbone connectors along each strand//
        for(int i = 0; i < totalSegments - 1; i++)
        {
            float normalizedEnergy = energyMap[i] / peakEnergy;
            Color colour = _spatial.GetSegmentColour(i);
            float width = Mathf.Lerp(0.08f, 0.25f, normalizedEnergy);

            SpawnConnector(strandA[i], strandA[i + 1], width, colour);
            SpawnConnector(strandB[i], strandB[i + 1], width, colour);
        }

        //Step 4: Spawn cross connectors between strands//
        
        for (int i = 0; i < totalSegments; i++)
        {

            float normalizedEnergy = energyMap[i] / peakEnergy;
            Color colourA = _spatial.GetSegmentColour(i);

            //Blend the two node colours for the rung colour//
            Color colourB = _spatial.GetSegmentColour(Mathf.Min(i + 1, totalSegments - 1));
            Color rungColour = Color.Lerp(colourA, colourB, 0.5f);

            float width = Mathf.Lerp(0.15f, 0.45f, normalizedEnergy);

            SpawnConnector(strandA[i], strandB[i], width, rungColour);
            
        }
    }

    private void SpawnNode(Vector3 position, float size, Color colour)
    {
        GameObject node = SpatialGenerator.CreatePrimitiveChild(PrimitiveType.Sphere, _environment);
        node.transform.position = position;
        node.transform.localScale = Vector3.one * size;
        SpatialGenerator.ApplyColour(node, colour);
    }

    private void SpawnConnector(Vector3 from, Vector3 to, float width, Color colour)
    {
        Vector3 midPoint = (from + to) / 2f;
        float length = Vector3.Distance(from, to);

        GameObject connector = SpatialGenerator.CreatePrimitiveChild(PrimitiveType.Cylinder, _environment);
        connector.transform.position = midPoint;
        connector.transform.localScale = new Vector3(width, length / 2f, width);
        connector.transform.rotation = Quaternion.FromToRotation(Vector3.up, (to - from).normalized);
        SpatialGenerator.ApplyColour(connector, colour);
    }
}
