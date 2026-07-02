using UnityEngine;
using System.Collections.Generic;
using Unity.VisualScripting;

public class OrganicGenerator : MonoBehaviour
{
    public enum OrganicShape { DNA, SolarSystem, Waves, Murmuration, Aurora}

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

    //Solar System state//
    public enum OrbitGroups { Few = 4, Standard = 8, Many = 16 }
    [SerializeField] private OrbitGroups orbitGroups = OrbitGroups.Standard;

    private bool _SolarAnimating = false;
    private GameObject _sun;
    private float _sunBaseSize;
    private List<GameObject> _planets = new List<GameObject>();
    private List<float> _planetBaseSizes = new List<float>();
    private List<int> _planetSegmentIndex = new List<int>();
    private List<float> _planetOrbitRadius = new List<float>();
    private List<float> _planetOrbitSpeed = new List<float>();
    private List<float> _planetOrbitAngle = new List<float>();
    private List<int> _planetGroupIndex = new List<int>();
    private List<LineRenderer> _orbitLines = new List<LineRenderer>();
    private List<GameObject> _beltSpheres = new List<GameObject>();
    private float _beltBaseRadius;
    private int _peakSegment;
    private float[] _orbitPlaneAngle;
    private float[] _orbitPlaneAxis; //0=X, 1=Z, varied per group//
    private float[] _orbitPlaneSpeed;


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
            case OrganicShape.SolarSystem:
                GenerateSolarSystem();
                break;
            case OrganicShape.Waves:
                break;
            case OrganicShape.Murmuration:
                break;
            case OrganicShape.Aurora:
                break;
        }
    }

    private void Update()
    {
        if (shape == OrganicShape.DNA && _animating) UpdateDNA();
        else if (shape == OrganicShape.SolarSystem) updateSolarSystem();
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

    private void UpdateDNA()
    {
        if (!_animating || shape != OrganicShape.DNA) return;
        if (_audioSource == null || !_audioSource.isPlaying) return;


        //Sample real time spectrum//
        float[] spectrum = new float[256];
        _audioSource.GetSpectrumData(spectrum, 0, FFTWindow.BlackmanHarris);

        //Get overall real time energy from spectrum//
        float[] bandEnergy = new float[_totalSegments];
        int binsPerSegment = spectrum.Length / _totalSegments;
        for (int i = 0; i < _totalSegments; i++)
        {
            float sum = 0f;
            int start = i * binsPerSegment;
            int end = Mathf.Min(start + binsPerSegment, spectrum.Length);
            for (int b = start; b < end; b++) sum += spectrum[b];
            bandEnergy[i] = sum / binsPerSegment;
        }

        //Normalise band energy//
        float maxBand = 0f;
        for (int i = 0; i < _totalSegments; i++)
            if (bandEnergy[i] > maxBand) maxBand = bandEnergy[i];
        if (maxBand <= 0f) return;

        //Rotate entire helix - BPM drives speed//
        float rotateSpeed = Mathf.Lerp(10f, 40f, Mathf.InverseLerp(60f, 180f, _bpm));
        _rotationY += rotateSpeed * Time.deltaTime;
        Quaternion rotation = Quaternion.Euler(0, _rotationY, 0);

        //Update each segment//
        Vector3[] currentA = new Vector3[_totalSegments];
        Vector3[] currentB = new Vector3[_totalSegments];

        float overallEnergy = 0f;
        for (int i = 0; i < spectrum.Length; i++) overallEnergy += spectrum[i];
        overallEnergy /= spectrum.Length;
        float normalizedOverall = Mathf.Clamp01(overallEnergy / (maxBand * 0.1f));

        for (int i = 0; i < _totalSegments; i++)
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
            if (_nodesB[i] != null)
            {
                _nodesB[i].transform.position = rotatedB;
                _nodesB[i].transform.localScale = Vector3.one * size;
            }
        }

        //Update backbone connectors//
        for (int i = 0; i < _totalSegments - 1; i++)
        {
            if (_backboneA[i] != null) UpdateConnector(_backboneA[i], currentA[i], currentA[i + 1]);
            if (_backboneB[i] != null) UpdateConnector(_backboneB[i], currentB[i], currentB[i + 1]);
        }

        //Update cross connectors//
        for (int i = 0; i < _totalSegments; i++)
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

    private void GenerateSolarSystem()
    {
        _bpm = _analyser.EstimatedTempo;
        _totalSegments = _analyser.EnergyOverTime.Length;

        float[] energyMap = _analyser.EnergyOverTime;
        float peakEnergy = _analyser.PeakEnergy;
        float avgEnergy = _analyser.AverageEnergy;
        int totalSegments = energyMap.Length;
        int groupCount = (int)orbitGroups;

        //Clear Lists//
        _planets.Clear();
        _planetBaseSizes.Clear();
        _planetSegmentIndex.Clear();
        _planetOrbitRadius.Clear();
        _planetOrbitSpeed.Clear();
        _planetOrbitAngle.Clear();
        _planetGroupIndex.Clear();
        _orbitLines.Clear();
        _beltSpheres.Clear();

        //Find peak energy segment for Saturn belt//
        _peakSegment = 0;
        float peakVal = 0f;
        for(int i = 0; i < totalSegments; i++)
        {
            if (energyMap[i] > peakVal)
            {
                peakVal = energyMap[i];
                _peakSegment = i;
            }
        }

        //Step 1: Spawn Sun//
        float sunSize = Mathf.Lerp(3f, 6f, avgEnergy / peakEnergy);
        _sun = SpatialGenerator.CreatePrimitiveChild(PrimitiveType.Sphere, _environment);
        _sun.transform.position = Vector3.zero;
        _sun.transform.localScale = Vector3.one * sunSize;
        SpatialGenerator.ApplyColour(_sun, new Color(1f, 0.9f, 0.3f));
        _sunBaseSize = sunSize;

        //Step 2: Group segments by musical similarity//
        //Use k-means style grouping, assign each segment to nearest group centre//
        int[] segmentGroups = new int[totalSegments];
        float[] groupRadii = new float[groupCount];

        //Initialis group centres evenly spaced in energy//
        float[] groupCentreEnergy = new float[groupCount];
        for (int g = 0; g < groupCount; g++) groupCentreEnergy[g] = peakEnergy * ((g + 1f) / (groupCount + 1f));

        //Assign segments to nearest group energy//
        for(int i = 0; i < totalSegments; i++)
        {
            int nearest = 0;
            float nearestDist = float.MaxValue;
            for(int g = 0; g < groupCount; g++)
            {
                float dist = Mathf.Abs(energyMap[i] - groupCentreEnergy[g]);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = g;
                }
            }
            segmentGroups[i] = nearest;
        }

        //Step 3: Calculate orbital radii for each group//
        //Inner groups = low energy, outer groups = high energy//
        float minRadius = 10f;
        float maxRadius = 55f;
        for(int g = 0; g < groupCount; g++)
        {
            float t = g / (float)(groupCount - 1);
            groupRadii[g] = Mathf.Lerp(minRadius, maxRadius, t);
        }

        //Step 4: Spawn orbital path LineRenderers//
        for(int g = 0; g < groupCount; g++)
        {
            //Calculate average colour for this group//
            Color groupColour = Color.black;
            int groupSize = 0;
            for(int i = 0; i < totalSegments; i++)
            {
                if(segmentGroups[i] == g)
                {
                    groupColour += _spatial.GetSegmentColour(i);
                    groupSize++;
                }
            }
            if (groupSize > 0) groupColour /= groupSize;
            groupColour.a = 0.4f;

            //Create Orbit Ring as LineRenderer//
            GameObject orbitObj = new GameObject("Orbit_" + g);
            orbitObj.transform.SetParent(_environment.transform);
            LineRenderer lr = orbitObj.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;

            //Tilt orbit based on group index, variety of orbital planes//
            float tilt = Mathf.Lerp(-15f, 15f, g / (float)(groupCount - 1));
            orbitObj.transform.rotation = Quaternion.Euler(tilt, 0f, 0f);

            int orbitPoints = 64;
            lr.positionCount = orbitPoints;
            float radius = groupRadii[g];
            for(int p = 0; p < orbitPoints; p++)
            {
                float angle = (p / (float)orbitPoints) * Mathf.PI * 2f;
                lr.SetPosition(p, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            lr.startWidth = 0.15f;
            lr.endWidth = 0.15f;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = groupColour;
            lr.endColor = groupColour;

            _orbitLines.Add( lr );
        }

        //Step 5: Spawn Planets//
        //Track angle offset per group so planets spread around the ring//
        float[] groupAngleOffset = new float[groupCount];
        int[] groupPlanetCount = new int[groupCount];
        for (int i = 0; i < totalSegments; i++) groupPlanetCount[segmentGroups[i]]++;

        int[] groupCurrentCount = new int[groupCount];

        for(int i = 0; i < totalSegments;i++)
        {
            int group = segmentGroups[i];
            float normalizedEnergy = energyMap[i] / peakEnergy;
            Color colour = _spatial.GetSegmentColour(i);

            //Spread planets evenly around their orbit//
            float startAngle = (groupCurrentCount[group] / (float)groupPlanetCount[group] * Mathf.PI * 2f);
            groupCurrentCount[group]++;

            float radius = groupRadii[group];
            float tilt = Mathf.Lerp(-15f, 15f, group / (float)(groupCount - 1));
            Quaternion orbitRotation = Quaternion.Euler(tilt, 0f, 0f);

            Vector3 orbitPos = orbitRotation * new Vector3(Mathf.Cos(startAngle) * radius, 0f, Mathf.Sin(startAngle) * radius);

            float planetSize = Mathf.Lerp(0.3f, 3.5f, Mathf.Pow(normalizedEnergy, 1.5f));

            GameObject planet = SpatialGenerator.CreatePrimitiveChild(PrimitiveType.Sphere, _environment);
            planet.transform.position = orbitPos;
            planet.transform.localScale = Vector3.one * planetSize;
            SpatialGenerator.ApplyColour(planet, colour);

            _planets.Add(planet);
            _planetBaseSizes.Add(planetSize);
            _planetSegmentIndex.Add(i);
            _planetOrbitRadius.Add(radius);
            _planetOrbitSpeed.Add(Mathf.Lerp(20f, 60f, Mathf.InverseLerp(60f, 180f, _bpm)) / radius);
            _planetOrbitAngle.Add(startAngle);
            _planetGroupIndex.Add(group);
        }

        //Step 6: Spawn Saturn belt on Peak energy planet//
        int peakPlanetIndex = -1;
        for (int i = 0; i < _planetSegmentIndex.Count; i++)
        {
            if (_planetSegmentIndex[i] == _peakSegment)
            {
                peakPlanetIndex = i;
                break;
            }
        }

        if(peakPlanetIndex >=0)
        {
            float peakPlanetSize = _planetBaseSizes[peakPlanetIndex];
            _beltBaseRadius = peakPlanetSize * 2f;
            int beltSphereCount = 24;
            Color beltColour = _spatial.GetSegmentColour(_peakSegment);
            beltColour = Color.Lerp(beltColour, Color.white, 0.3f);

            for(int b = 0; b < beltSphereCount; b++)
            {
                float beltAngle = (b / (float)beltSphereCount) * Mathf.PI * 2f;
                float beltX = Mathf.Cos(beltAngle) * _beltBaseRadius;
                float beltZ = Mathf.Sin(beltAngle) * _beltBaseRadius;

                GameObject beltSphere = SpatialGenerator.CreatePrimitiveChild(PrimitiveType.Sphere, _environment);
                beltSphere.transform.localScale = Vector3.one * (peakPlanetSize * 0.2f);
                SpatialGenerator.ApplyColour(beltSphere, beltColour);

                _beltSpheres.Add(beltSphere);
            }
        }

        //Step 7: orbital plane rotation//
        _orbitPlaneAngle = new float[groupCount];
        _orbitPlaneAxis = new float[groupCount];
        _orbitPlaneSpeed = new float[groupCount];

        for(int g = 0; g < groupCount; g++)
        {
            _orbitPlaneAngle[g] = 0f;
            _orbitPlaneAxis[g] = Random.Range(0f, 1f); //Blend between X and Z axis rotation//

            //Outer orbites rotate slower than inner ones//
            float baseSpeed= Mathf.Lerp(8f, 2f, g / (float)(groupCount - 1));
            _orbitPlaneSpeed[g] = baseSpeed;
        }

        _SolarAnimating = true;
    }

    private void updateSolarSystem()
    {
        if (!_SolarAnimating) return;
        if (_audioSource == null || !_audioSource.isPlaying) return;

        float[] spectrum = new float[256];
        _audioSource.GetSpectrumData(spectrum, 0, FFTWindow.BlackmanHarris);

        //Overall energy for sun pulse//
        float overallEnergy = 0f;
        for(int i = 0; i < spectrum.Length; i++) overallEnergy += spectrum[i];
        overallEnergy /= spectrum.Length;
        float normalizedOverall = Mathf.Clamp01(overallEnergy * 200f);

        //Band energy per segment for planet pulse//
        float[] bandEnergy = new float[_totalSegments];
        int binsPerSegments  = spectrum.Length / _totalSegments;
        for(int i = 0; i < _totalSegments; i++)
        {
            float sum = 0f;
            int start = i * binsPerSegments;
            int end = Mathf.Min(start + binsPerSegments, spectrum.Length);
            for(int b = start; b < end; b++) sum += spectrum[b];
            bandEnergy[i] = sum / binsPerSegments;
        }

        float maxBand = 0f;
        for(int i = 0; i < _totalSegments; i++) 
            if(bandEnergy[i] > maxBand) maxBand = bandEnergy[i];

        //Pulse Sun//
        if(_sun != null)
        {
            float sunPulse = 1f + normalizedOverall * 0.4f;
            _sun.transform.localScale = Vector3.one * _sunBaseSize * sunPulse;
            _sun.transform.Rotate(Vector3.up, 5f * Time.deltaTime);
        }

        //Orbital speed surge on beat//
        float speedMultiplier = 1f + normalizedOverall * 1.5f;

        int groupCount = (int)orbitGroups;

        //update Oribital plane rotations//
        for(int g = 0; g < groupCount; g++)
        {
            float planeSpeedMultiplier = 1f + normalizedOverall * 6f;

            _orbitPlaneAngle[g] += _orbitPlaneSpeed[g] * planeSpeedMultiplier * Time.deltaTime;

            float baseTilt = Mathf.Lerp(-15f, 15f, g / (float)(groupCount - 1));
            Quaternion xRot = Quaternion.Euler(_orbitPlaneAngle[g] + baseTilt, 0f, 0f);
            Quaternion zRot = Quaternion.Euler(0f, 0f, _orbitPlaneAngle[g] + baseTilt);
            Quaternion orbitRotation = Quaternion.Lerp(xRot, zRot, _orbitPlaneAxis[g]);

            //Update orbit line to match rotating plane//
            if(g < _orbitLines.Count && _orbitLines[g] != null)
            {
                _orbitLines[g].transform.rotation = orbitRotation;
            }
        }

        //Update planets//
        for (int i = 0; i < _planets.Count; i++)
        {
            if (_planets[i] == null) continue;

            int seg = _planetSegmentIndex[i];
            int group = _planetGroupIndex[i];

            //Get this group's current orbital rotation//
            float baseTilt = Mathf.Lerp(-15f, 15f, group / (float)(groupCount - 1));
            Quaternion xRotation = Quaternion.Euler(_orbitPlaneAngle[group] + baseTilt, 0f, 0f);
            Quaternion zRotaiton = Quaternion.Euler(0f, 0f, _orbitPlaneAngle[group] + baseTilt);
            Quaternion orbitRotation = Quaternion.Lerp(xRotation, zRotaiton, _orbitPlaneAxis[group]);

            //Advance Orbit angle//
            _planetOrbitAngle[i] += _planetOrbitSpeed[i] * speedMultiplier * Time.deltaTime;

            float angle = _planetOrbitAngle[i];
            float radius = _planetOrbitRadius[i];

            Vector3 newPos = orbitRotation * new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

            _planets[i].transform.position = newPos;

            //Planet pulse//
            float bandPulse = maxBand > 0 ? (bandEnergy[seg] / maxBand) * 0.5f : 0f;
            float pulse = 1f + bandPulse + normalizedOverall * 0.3f;
            _planets[i].transform.localScale = Vector3.one * _planetBaseSizes[i] * pulse;

            //Update satrun belt to follow peak planet//
            if (seg == _peakSegment && _beltSpheres.Count > 0)
            {
                float beltPulse = 1f + normalizedOverall * 0.3f;
                float currentBeltRadius = _beltBaseRadius * beltPulse;

                for (int b = 0; b < _beltSpheres.Count; b++)
                {
                    if (_beltSpheres[b] == null) continue;
                    float beltAngle = (b / (float)_beltSpheres.Count) * Mathf.PI * 2f;
                    // Belt sits in XZ plane around planet, then tilted with orbit
                    Quaternion beltRot = Quaternion.Euler(orbitRotation.eulerAngles.x, 0f, orbitRotation.eulerAngles.z);
                    Vector3 beltOffset = beltRot * new Vector3(
                        Mathf.Cos(beltAngle) * currentBeltRadius,
                        0f,
                        Mathf.Sin(beltAngle) * currentBeltRadius);
                    _beltSpheres[b].transform.position = newPos + beltOffset;
                }
            }
        }
    }
}
