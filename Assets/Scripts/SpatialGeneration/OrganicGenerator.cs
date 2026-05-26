using UnityEngine;

public class OrganicGenerator : MonoBehaviour
{
    public enum OrganicShape { DNA, Reef, Roots, Web}

    [Header("Organic Settings")]
    [SerializeField] private OrganicShape shape = OrganicShape.DNA;

    private AudioAnalyser _analyser;
    private GameObject _environment;
    private SpatialGenerator _spatial;
    private AudioSource _audioSource;

    //DNA State for animation//
    private bool _animating = false;
    private int _totalSegments;
    private float _totalHeight;
    private float _baseRadius;
    private float _twists;
    private float[] _energyMap;
    private float _peakEnergy;
    private float[] _segmentHeights;
    private Vector3[] _basePositionsA;
    private Vector3[] _basePositionsB;

    //Stored references for animation//
    private GameObject[] _nodesA;
    private GameObject[] _nodesB;
    private GameObject[] _backboneA;
    private GameObject[] _backboneB;
    private GameObject[] _crossConnectors;
    private float[] _nodeSizes;

    //Rotation//
    private float _rotationY = 0f;
    private float _bpm;

    public void Init(AudioSource source)
    {
        _audioSource = source;
    }

    public void Generate(AudioAnalyser analyser, GameObject environment)
    {
        _analyser = analyser;
        _environment = environment;
        _spatial = GetComponent<SpatialGenerator>();
        _animating = false;

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
        _energyMap = _analyser.EnergyOverTime;
        _peakEnergy = _analyser.PeakEnergy;
        _totalSegments = _energyMap.Length;
        _bpm = _analyser.EstimatedTempo;

        //BPM drives twist, fast tracks coil tight slow tracks loop wide//
        _twists = Mathf.Lerp(1.5f, 6f, Mathf.InverseLerp(60f, 180f, _bpm));
        //Helix parameters//
        _totalHeight = 60f;
        _baseRadius = 5f; //Base width of the helix//

        Vector3[] strandA = new Vector3[_totalSegments];
        Vector3[] strandB = new Vector3[_totalSegments];

        //Initialise arrays//
        _basePositionsA = new Vector3[_totalSegments];
        _basePositionsB = new Vector3[_totalSegments];
        _nodesA = new GameObject[_totalSegments];
        _nodesB = new GameObject[_totalSegments];
        _backboneA = new GameObject[_totalSegments - 1];
        _backboneB = new GameObject[_totalSegments - 1];
        _crossConnectors = new GameObject[_totalSegments];
        _nodeSizes = new float[_totalSegments];
        _segmentHeights = new float[_totalSegments];


        //Pre calculate cumulative height so loud segments get more vertical space//
        float heightAccumulator = 0f;
        float totalWeight = 0f;

        for (int i = 0; i < _totalSegments; i++) totalWeight += Mathf.Lerp(0.5f, 2f, _energyMap[i] / _peakEnergy);

        for (int i = 0; i < _totalSegments; i++)
        {
            float weight = Mathf.Lerp(0.5f, 2f, _energyMap[i] / _peakEnergy);
            heightAccumulator += (weight / totalWeight) * _totalHeight;
            _segmentHeights[i] = heightAccumulator;
        }

        //Step 1: Calculate node positions//
        for(int i = 0; i < _totalSegments; i++)
        {
            float t = i / (float)(_totalSegments - 1); //0-1//
            float normalizedEnergy = _energyMap[i] / _peakEnergy;

            float angle = t * _twists * Mathf.PI * 2f;
            float heightY = t * _totalHeight;

            //Energy makes the helix bulge outward at loud moments//
            float radius = Mathf.Lerp(_baseRadius * 0.6f, _baseRadius * 1.6f, normalizedEnergy);

            strandA[i] = new Vector3(Mathf.Cos(angle) * radius, heightY, Mathf.Sin(angle) * radius);
            strandB[i] = new Vector3(Mathf.Cos(angle + Mathf.PI) * radius, heightY, Mathf.Sin(angle + Mathf.PI) * radius);

            _basePositionsA[i] = strandA[i];
            _basePositionsB[i] = strandB[i];
        }

        //Step 2: Spawn nodes//
        for(int i = 0; i < _totalSegments;i++)
        {
            float normalizedEnergy = _energyMap[i] / _peakEnergy;
            Color colour = _spatial.GetSegmentColour(i);
            float nodeSize = Mathf.Lerp(0.3f, 1.8f, normalizedEnergy);

            _nodeSizes[i] = nodeSize;
            _nodesA[i] = SpawnNode(strandA[i], nodeSize, colour);
            _nodesB[i] = SpawnNode(strandB[i], nodeSize, colour);
        }

        //Step 3: spawn backbone connectors along each strand//
        for(int i = 0; i < _totalSegments - 1; i++)
        {
            float normalizedEnergy = _energyMap[i] / _peakEnergy;
            Color colour = _spatial.GetSegmentColour(i);
            float width = Mathf.Lerp(0.08f, 0.25f, normalizedEnergy);

            _backboneA[i] = SpawnConnector(strandA[i], strandA[i + 1], width, colour);
            _backboneB[i] = SpawnConnector(strandB[i], strandB[i + 1], width, colour);
        }

        //Step 4: Spawn cross connectors between strands//
        
        for (int i = 0; i < _totalSegments; i++)
        {

            float normalizedEnergy = _energyMap[i] / _peakEnergy;
            Color colourA = _spatial.GetSegmentColour(i);

            //Blend the two node colours for the rung colour//
            Color colourB = _spatial.GetSegmentColour(Mathf.Min(i + 1, _totalSegments - 1));
            Color rungColour = Color.Lerp(colourA, colourB, 0.5f);

            float width = Mathf.Lerp(0.15f, 0.45f, normalizedEnergy);

            _crossConnectors[i] = SpawnConnector(strandA[i], strandB[i], width, rungColour);

        }

        _animating = true;
    }

    private void Update()
    {
        if (!_animating || shape != OrganicShape.DNA) return;
        if (_audioSource == null || !_audioSource.isPlaying) return;


        //Sample real time spectrum//
        float[] spectrum = new float[256];
        _audioSource.GetSpectrumData(spectrum, 0, FFTWindow.BlackmanHarris);

        //Get overall real time energy from spectrum//
        float[] bandEnergy = new float[_totalSegments];
        int binsPerSegment = spectrum.Length / _totalSegments;
        for(int i = 0; i < _totalSegments; i++)
        {
            float sum = 0f;
            int start = i * binsPerSegment;
            int end = Mathf.Min(start +  binsPerSegment, spectrum.Length);
            for (int b = start; b < end; b++) sum += spectrum[b];
            bandEnergy[i] = sum / binsPerSegment;
        }

        //Normalise band energy//
        float maxBand = 0f;
        for(int i = 0; i < _totalSegments; i++)
            if(bandEnergy[i] > maxBand ) maxBand = bandEnergy[i];
        if (maxBand <= 0f) return;

        //Rotate entire helix - BPM drives speed//
        float rotateSpeed = Mathf.Lerp(10f, 40f, Mathf.InverseLerp(60f, 180f, _bpm));
        _rotationY += rotateSpeed * Time.deltaTime;
        Quaternion rotation = Quaternion.Euler(0, _rotationY, 0);

        //Update each segment//
        Vector3[] currentA = new Vector3[_totalSegments];
        Vector3[] currentB = new Vector3[_totalSegments];

        float overallEnergy = 0f;
        for(int i = 0; i < spectrum.Length; i++) overallEnergy += spectrum[i];
        overallEnergy /= spectrum.Length;
        float normalizedOverall = Mathf.Clamp01(overallEnergy / (maxBand * 0.1f));

        for(int i = 0; i < _totalSegments; i++)
        {
            float bandPulse = (bandEnergy[i] / maxBand) * 0.4f;
            float overallPulse = normalizedOverall * 0.4f;
            float pulse = 1f + bandPulse + overallPulse;

            //Rotate base positions//
            Vector3 rotatedA = rotation * _basePositionsA[i];
            Vector3 rotatedB = rotation * _basePositionsB[i];

            currentA[i] = rotatedA;
            currentB[i] = rotatedB;

            //Scale nodes with pulse//
            float size = _nodeSizes[i] * pulse;
            if (_nodesA[i] != null)
            {
                _nodesA[i].transform.position = rotatedA;
                _nodesA[i].transform.localScale = Vector3.one * size;
            }
            if(_nodesB[i] != null)
            {
                _nodesB[i].transform.position = rotatedB;
                _nodesB[i].transform.localScale = Vector3.one * size;
            }
        }

        //Update backbone connectors//
        for(int i = 0; i <_totalSegments - 1; i++)
        {
            if (_backboneA[i] != null) UpdateConnector(_backboneA[i], currentA[i], currentA[i + 1]);
            if (_backboneB[i] != null) UpdateConnector(_backboneB[i], currentB[i], currentB[i + 1]);
        }

        //Update cross connectors//
        for(int i = 0; i< _totalSegments; i++)
        {
            if (_crossConnectors[i] != null) UpdateConnector(_crossConnectors[i], currentA[i], currentB[i]);
        }
    }

    private void UpdateConnector(GameObject connector, Vector3 from, Vector3 to)
    {
        Vector3 midPoint = (from + to) / 2f;
        float length = Vector3.Distance(from, to);
        connector.transform.position = midPoint;
        connector.transform.localScale = new Vector3(connector.transform.localScale.x, length / 2f, connector.transform.localScale.z);
        connector.transform.rotation = Quaternion.FromToRotation(Vector3.up, (to - from).normalized);
    }

    private GameObject SpawnNode(Vector3 position, float size, Color colour)
    {
        GameObject node = SpatialGenerator.CreatePrimitiveChild(PrimitiveType.Sphere, _environment);
        node.transform.position = position;
        node.transform.localScale = Vector3.one * size;
        SpatialGenerator.ApplyColour(node, colour);
        return node;
    }

    private GameObject SpawnConnector(Vector3 from, Vector3 to, float width, Color colour)
    {
        Vector3 midPoint = (from + to) / 2f;
        float length = Vector3.Distance(from, to);

        GameObject connector = SpatialGenerator.CreatePrimitiveChild(PrimitiveType.Cylinder, _environment);
        connector.transform.position = midPoint;
        connector.transform.localScale = new Vector3(width, length / 2f, width);
        connector.transform.rotation = Quaternion.FromToRotation(Vector3.up, (to - from).normalized);
        SpatialGenerator.ApplyColour(connector, colour);
        return connector;
    }
}
