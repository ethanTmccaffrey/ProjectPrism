using System.Collections.Generic;
using UnityEditor.Analytics;
using UnityEngine;

//FracureGenerator: Luver Category 4 (Cobwebs and Radial Forms), fracture subcategory//

//Produces a scattered filed of jagged, angular shards stabbed into the canvas volume//
//Where Fluidtendril flows and branches from a source, fracture cracks discrete shards into existence acrosss the whole space one per detected beat//
//Builds a dence angular field that is the visual oppoisite of tendrils smooth curves//

//Activation: very high spectural flatness and very high ZCR(noise + roughness)//
//Wegiht source: TimbralProfile.WeightFracture = F^2 * Z^2 (heavy metal, distorted)//

//Spawn trigger: onset detection on unsmoothed spectral flux (Dixon 2001)//
//A shard fires when current flux exceeds its recent running average by a margin, gated by a refeactory period so one beat does not spawn a burst across consecutive forms//

//Persistence: shards remain in the scene permanently//


public class FractureGenerator : MonoBehaviour
{
    private PRISMGenerator _prism;

    [Header("Activation")]
    //Fracture only spawns when its weight clears this. Below it, the timbral character is not fractured enought to justify marks//
    [SerializeField] private float activationThreshold = 0.15f;

    [Header("Shard Field")]
    //half-extent of the cubic volume shards scatter into, centred on this transform//
    [SerializeField] private float fieldRadius = 30f;
    //Vertical bias, canvas tends to build upwards like the mind filling//
    [SerializeField] private float verticalBias = 0.6f;
    //Seconds over which the field grows from tight core to full radius//
    [SerializeField] private float fieldGrowthSeconds = 120f;
    //Hollow core: fraction of the radius kept empty in the middle//
    //0 = solid ball, 0.6 = open core so earlier marks show through//
    [SerializeField, Range(0f, 0.9f)] private float shellInner = 0.5f;

    [Header("Shard Shape")]
    [SerializeField] private float minLength = 1.5f;
    [SerializeField] private float maxLength = 6f;
    //Shards are thin relative to length - angular, blade-like, not chunky//
    [SerializeField] private float thicknessRatio = 0.12f;

    [Header("Onset Detection (Dixon 2001)")]
    //How many recent frames of flux to avarge for the adaptive threshold//
    [SerializeField] private int fluxHistorySize = 43; //~0.7s at 60fps//
    //Current flux must exceed the running average by this multiple to count as an onset//
    [SerializeField] private float onsetSensitivity = 1.5f;
    //Minimum seconds between shars, stops one beat spawning many across frames//
    [SerializeField] private float refactorSeconds = 0.28f;

    [Header("Safety")]
    //Hard cap so a patholocial track cannot spawn unbounded geometry//
    [SerializeField] private int maxShards = 4000;

    //Internal state//
    private GameObject _root; //Parent for all spawned shards//
    private SpatialGenerator _spatial; //reusued for colour + primitive helpers//
    private readonly List<float> _fluxHistory = new List<float>();
    private float _timeSinceLastShard = 0f;
    private int _shardCount = 0;
    private bool _active = false;
    private float _debugTimer = 0f;
    private float _elapsed = 0f;

    //Shared material so every shard is one draw path rather than N materials//
    private Material _shardMaterialTemplate;

    public void SetPrism(PRISMGenerator prism)
    {
        _prism = prism;
    }
    public void Init(TimbralProfile profile)
    {
        _root = new GameObject("Fracture_Root");
        _spatial = GetComponent<SpatialGenerator>();

        _fluxHistory.Clear();
        _timeSinceLastShard = 0f;
        _shardCount = 0;
        _active = false;

        Debug.Log("PRSIM FractureGenerator: Initialised");
    }

    public void UpdateGenerator(TimbralProfile profile) 
    {
        float weight = profile.WeightFracture;
        _active = weight >= activationThreshold;

        _timeSinceLastShard += Time.deltaTime;
        _elapsed += Time.deltaTime;

        //Feed the onset detector ever frame regardless of activation, so the running average is warm the instant the tack becomes fractured mid-song//
        //Uses raw flux so sharp beat spikes survive for detection//
        float flux = profile.RealtimeFluxRaw;
        PushFlux(flux);

        //_debugTimer += Time.deltaTime;
        //if (_debugTimer >= 1f)
        //{
        //    _debugTimer = 0f;
        //    float avg = FluxAverage();
        //    bool onset = IsOnset(flux);
        //    Debug.Log(
        //        $"[FRACTURE] weight={weight:F3} (need≥{activationThreshold:F2}) " +
        //        $"active={_active} | " +
        //        $"F={profile.RealtimeFlatness:F3} Z={profile.RealtimeZCR:F3} " +
        //        $"fluxRaw={flux:F4} avg={avg:F4} onset={onset} " +
        //        $"(need flux>{avg * onsetSensitivity:F4}) | " +
        //        $"shards={_shardCount} histFill={_fluxHistory.Count}/{fluxHistorySize}"
        //    );
        //}

        if (!_active) return;
        if (_shardCount >= maxShards) return;
        if (_timeSinceLastShard < refactorSeconds) return;
        if (!IsOnset(flux)) return;

        //A beat landed while the music is fractured, crack a shard into the field//
        SpawnShard(profile, weight);
        _timeSinceLastShard = 0f;
    }
    public void Deactivate()
    {
        _active = false;
        //Shards persist, the canvas is never cleared. Deactivate only stops new spawns//
    }

    //Onset Detection//
    private void PushFlux(float flux)
    {
        _fluxHistory.Add(flux);
        if (_fluxHistory.Count > fluxHistorySize) _fluxHistory.RemoveAt(0);
    }

    private float FluxAverage()
    {
        if (_fluxHistory.Count == 0) return 0f;
        float sum = 0f;
        for (int i = 0; i < _fluxHistory.Count; i++) sum += _fluxHistory[i];
        return sum / _fluxHistory.Count;
    }

    private bool IsOnset(float flux)
    {
        //Need a full-ish window trusting the average//
        if (_fluxHistory.Count < fluxHistorySize / 2) return false;

        float sum = 0f;
        for (int i = 0; i < _fluxHistory.Count; i++) sum += _fluxHistory[i];
        float avg = sum / _fluxHistory.Count;

        //A tiny floor stops silience to quiet noise registering as beats//
        if(avg <1e-4f) return false;

        return flux > avg * onsetSensitivity;
    }

    //Shard Spawning//
    private void SpawnShard(TimbralProfile profile, float weight)
    {
        //Spherical falloff: sample a direction, then a radius biased toward the centre//
        //so density tapers outward with no hard box edge. Cube-root would give uniform//
        //density; using a higher power concentrates shards near the middle and lets the//
        //field fade into black at its limit.//
        Vector3 dir = Random.onUnitSphere;
        //Hollow shell: bias the radius toward the OUTER edge and enforce a minimum//
        //inner radius, so the core stays open and earlier marks (tendrils) show through.//
        //shellInner sets how empty the middle is; 0 = solid ball, 0.6 = clear core.//
        float rNorm = Mathf.Lerp(shellInner, 1f, Mathf.Pow(Random.value, 0.5f));

        //Field reaches full radius as the song reaches its end. Falls back to the//
        //serialized default if track length isn't available for any reason.//
        float trackLength = (_prism != null && _prism.TrackLength > 1f) ? _prism.TrackLength : fieldGrowthSeconds;
        float growth = Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(_elapsed / trackLength));
        float radius = rNorm * fieldRadius * growth;

        //Flatten vertically so the field is a touch wider than tall, not a perfect ball//
        Vector3 offset = new Vector3(dir.x, dir.y * verticalBias, dir.z) * radius;
        //Prominence drives the whole field, mirroring Honeycomb: dominant fracture//
        //spreads wide from the centre with big shards, a minor fracture stays compact//
        //and small further out. One value scales spread + position + size together.//
        Prominence pr = _prism != null ? _prism.GetProminence(GeneratorID.Fracture) : Prominence.Silent;

        //Field spread scales with prominence//
        float spread = fieldRadius * Mathf.Lerp(0.35f, 1f, pr.prominence);

        //Centre offset by (1 - centrality): dominant sits centred, minor pushed outward//
        Vector3 fieldCentre = transform.position + Random.onUnitSphere * (fieldRadius * (1f - pr.centrality));

        //Scattered position within the (now prominence-scaled) spherical field//
        Vector3 pos = fieldCentre + Random.onUnitSphere * (spread * Mathf.Pow(Random.value, 0.5f));

        //Build a procedural shard GameObject (irregular faceted mesh)//
        GameObject shard = new GameObject("Shard");
        shard.transform.SetParent(_root.transform);
        shard.transform.position = pos;
        shard.transform.rotation = Random.rotationUniform;

        //Overall size scales with how fractured the moment is//
        float length = Mathf.Lerp(minLength, maxLength, weight) * Mathf.Lerp(0.5f, 1f, pr.prominence);

        //Morph axis, biased by the timbral moment then spread randomly//
        //elongation: 1 = long spike, 0 = squat chunk. Roughness (Z) drives spikes//
        //flatness:   1 = flat sliver, 0 = full volume. Noise (F) drives slivers//
        float elongation = Mathf.Clamp01(profile.RealtimeZCR + Random.Range(-0.35f, 0.35f));
        float flatnessAxis = Mathf.Clamp01(profile.RealtimeFlatness + Random.Range(-0.35f, 0.35f));

        Mesh mesh = BuildShardMesh(length, elongation, flatnessAxis);
        shard.AddComponent<MeshFilter>().mesh = mesh;
        shard.AddComponent<MeshRenderer>();

        //Colour from the current trimral moment via the existing frequency band system//
        //Fracture lives at high flatness/ high centroid, so this trends bright/harsh which is correct for this character//
        //Falls back to a stark near white if the colour hook is unavailable so shards dont render invisible//
        Color c = ResolveColour(profile);
        SpatialGenerator.ApplyColour(shard, c);

        _shardCount++;
    }

    //Builds an irregular faceted shard mesh//
    //length     : overall scale of the shard//
    //elongation : 1 = long pointed spike, 0 = squat chunk//
    //flatnessAxis: 1 = flat glassy sliver, 0 = full crystalline volume//
    //Each shard is a small cluster of triangular facets tapering to a point at each end,//
    //with randomised vertices so no two shards are identical (per-moment uniqueness)//
    private Mesh BuildShardMesh(float length, float elongation, float flatnessAxis)
    {
        //Long axis grows with elongation, girth shrinks with it (spikes are thin)//
        float longAxis = length * Mathf.Lerp(0.5f, 1.4f, elongation);
        float girth = length * Mathf.Lerp(0.45f, 0.12f, elongation);
        //Flatness squashes one cross-section axis toward a plane//
        float depth = girth * Mathf.Lerp(1f, 0.18f, flatnessAxis);

        //Number of sides around the middle band (3-5 facets = angular, not round)//
        int sides = Random.Range(3, 6);

        //Two tip points along the long axis, slightly offset so it isn't symmetrical//
        Vector3 tipA = new Vector3(Random.Range(-0.15f, 0.15f) * girth, longAxis, Random.Range(-0.15f, 0.15f) * depth);
        Vector3 tipB = new Vector3(Random.Range(-0.15f, 0.15f) * girth, -longAxis, Random.Range(-0.15f, 0.15f) * depth);

        //Ring of vertices around the middle, irregular radius per vertex//
        Vector3[] ring = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float ang = (i / (float)sides) * Mathf.PI * 2f + Random.Range(-0.25f, 0.25f);
            float r = Random.Range(0.6f, 1f);
            ring[i] = new Vector3(Mathf.Cos(ang) * girth * r, Random.Range(-0.2f, 0.2f) * longAxis, Mathf.Sin(ang) * depth * r);
        }

        //Assemble vertices: [0..sides-1] ring, [sides] tipA, [sides+1] tipB//
        Vector3[] verts = new Vector3[sides + 2];
        for (int i = 0; i < sides; i++) verts[i] = ring[i];
        verts[sides] = tipA;
        verts[sides + 1] = tipB;

        //Triangles: fan each tip to the ring edges//
        System.Collections.Generic.List<int> tris = new System.Collections.Generic.List<int>();
        int tA = sides, tB = sides + 1;
        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            //top cap (tipA)//
            tris.Add(tA); tris.Add(i); tris.Add(next);
            //bottom cap (tipB), wound opposite so it faces outward//
            tris.Add(tB); tris.Add(next); tris.Add(i);
        }

        Mesh mesh = new Mesh();
        mesh.vertices = verts;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals(); //Flat facets since verts aren't shared smoothly//
        mesh.RecalculateBounds();
        return mesh;
    }
    private Color ResolveColour(TimbralProfile profile)
    {
        if (_prism != null) return _prism.RealtimeColour;
        return Color.HSVToRGB(0.6f, 0.5f, 0.9f);
    }
}
