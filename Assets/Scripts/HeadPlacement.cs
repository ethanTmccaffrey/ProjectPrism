using UnityEngine;

//HeadPlacement - the cavity as a coordinate space for the interior generators.
//
//The interior generators were built before the skull existed. Each scatters into its own
//arbitrary sphere anchored to its transform, with world-unit radii (30, 80) tuned against
//empty space. Inside a scale-96 head those numbers are meaningless - a generator's field
//can sit entirely outside the skull. This service re-bases them: it answers WHERE the
//cavity is and HOW BIG, so a generator asks for a point "in the cavity" instead of a point
//"30 units from my transform".
//
//It does NOT own each generator's DISTRIBUTION. A speck scatters uniformly, a tendril grows
//from a seed, a burst fires from a centre - those differences are the thesis, and they stay
//in the generators. This service only supplies the frame they distribute WITHIN.
//
//WHICH FIELD. Two HeadField shells exist - outer (scale 100, pale, visible skull) and inner
//(scale 96, near-black, the void). Marks live in the VOID, so this binds the INNER field.
//Assign HeadInLayer, not HeadOutLayer.
//
//WHAT IT PROVIDES.
//    RandomPointInCavity(prominence, centrality) - a seed inside the cranial volume, placed
//        by the generator's own prominence: centrality pulls toward the cavity centre,
//        prominence widens the spread. No privileged centre - a minor mark sits off to one
//        side, it does not pile into the middle.
//    IsInCavity(world) - forwards the field test, for per-step containment.
//    SampleNormal(world) - the outward wall direction at a point, for generators that curve
//        away from the wall as they grow. Gradient of the signed field.
//    CavityCentre / CavityExtent - the probed dimensions, if a generator wants them raw.

[RequireComponent(typeof(HeadField))]
public class HeadPlacement : MonoBehaviour
{
    [Header("Field")]
    //The INNER head (HeadInLayer, scale 96) - the void the marks live in. Falls back to a
    //HeadField on this same object if not assigned.
    [SerializeField] private HeadField field;

    [Header("Probe")]
    //Samples per axis when measuring the cavity at startup. 24 -> ~13k samples once, which
    //is a few ms and gives a centre and extent good to a voxel or two. Higher is pointless -
    //the field itself is only 63x82x97.
    [SerializeField] private int probeResolution = 24;

    public bool Ready { get; private set; }
    public Vector3 CavityCentre { get; private set; }
    //Half-extent of the cavity bounding box in world units (x,y,z).//
    public Vector3 CavityExtent { get; private set; }

    private void Awake()
    {
        if (field == null) field = GetComponent<HeadField>();
    }

    private void Start()
    {
        //HeadField loads and builds in its own Start; ordering between two objects' Start is
        //not guaranteed, so ensure it is loaded before probing rather than assuming.
        Debug.Log("PRISM HeadPlacement: Start running, field=" + (field != null));
        if (field != null && !field.Loaded) field.Load();
        Probe();
    }

    //Measures the cavity once by sampling IsInCavity across the field's world bounds and
    //recording the bounding box of the hits. Nothing about the cavity is hardcoded - if the
    //head, scale or shell thickness change, this tracks them on the next run.
    private void Probe()
    {
        if (field == null || !field.Loaded)
        {
            Debug.LogError("PRISM HeadPlacement: no loaded HeadField to probe.");
            return;
        }

        Bounds b = field.FieldBounds;
        Vector3 min = b.min, size = b.size;
        int n = Mathf.Max(2, probeResolution);

        Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        int hits = 0;

        for (int ix = 0; ix < n; ix++)
            for (int iy = 0; iy < n; iy++)
                for (int iz = 0; iz < n; iz++)
                {
                    Vector3 p = min + new Vector3(
                        size.x * ix / (n - 1),
                        size.y * iy / (n - 1),
                        size.z * iz / (n - 1));

                    if (!field.IsInCavity(p)) continue;

                    hits++;
                    lo = Vector3.Min(lo, p);
                    hi = Vector3.Max(hi, p);
                }

        if (hits == 0)
        {
            Debug.LogError("PRISM HeadPlacement: probe found no cavity. Is the inner HeadField " +
                           "assigned and its shell thickness sane?");
            return;
        }

        CavityCentre = (lo + hi) * 0.5f;
        CavityExtent = (hi - lo) * 0.5f;
        Ready = true;

        Debug.Log($"PRISM HeadPlacement: cavity probed - centre {CavityCentre}, " +
                  $"extent {CavityExtent}, {hits} hits at res {n}.");
    }

    //A seed point inside the cranial volume, placed by the caller's prominence.
    //  centrality 0-1 : 1 sits at the cavity centre, 0 is pushed toward the wall.
    //  prominence 0-1 : how wide a spread around that anchor the point may fall in.
    //Rejection-sampled against the real cavity, so a returned point is always inside. Falls
    //back to the centre if the field is somehow unavailable, so a generator never NaNs.
    public Vector3 RandomPointInCavity(float prominence, float centrality)
    {
        if (!Ready) return CavityCentre;

        //Anchor: centrality slides from centre out toward the wall along a random direction.
        //One random offset up to (1-centrality) of the extent - a dominant mark (centrality
        //high) anchors near centre, a minor one (centrality low) sits out to a side. No fixed
        //privileged point; the side is random each call.
        float offMag = (1f - Mathf.Clamp01(centrality));
        Vector3 anchor = CavityCentre + Vector3.Scale(Random.onUnitSphere * offMag, CavityExtent);

        //Spread around the anchor scales with prominence. Rejection-sample within the cavity;
        //shrink the spread on repeated misses so a tight pocket still resolves quickly.
        float spread = Mathf.Lerp(0.15f, 0.9f, Mathf.Clamp01(prominence));

        for (int attempt = 0; attempt < 24; attempt++)
        {
            float s = spread * (1f - attempt / 32f);
            Vector3 candidate = anchor + Vector3.Scale(Random.insideUnitSphere * s, CavityExtent);
            if (field.IsInCavity(candidate)) return candidate;
        }

        //Every candidate missed (anchor deep in bone near a thin wall). The anchor itself may
        //be solid; return the cavity centre, which the probe guarantees is inside.
        return CavityCentre;
    }

    public bool IsInCavity(Vector3 world) => field != null && field.IsInCavity(world);

    //Outward wall direction at a world point - the normalised gradient of the signed field.
    //Positive is inside, so the field DECREASES outward; the gradient points inward and we
    //negate it to get "out". Sampled a full scaled voxel apart, not an epsilon: SampleWorld
    //rounds to the nearest voxel (no interpolation), so a small step reads the same cell
    //three times and yields zero. One voxel straddles distinct cells and gives a stable
    //normal. Returns zero if degenerate (deep interior, far from any wall) - callers should
    //treat a zero normal as "no wall near, do not curve".
    public Vector3 SampleNormal(Vector3 world)
    {
        if (field == null || !field.Loaded) return Vector3.zero;

        float h = field.VoxelWorldSize;
        float dx = field.SampleWorld(world + Vector3.right * h) - field.SampleWorld(world - Vector3.right * h);
        float dy = field.SampleWorld(world + Vector3.up * h) - field.SampleWorld(world - Vector3.up * h);
        float dz = field.SampleWorld(world + Vector3.forward * h) - field.SampleWorld(world - Vector3.forward * h);

        Vector3 grad = new Vector3(dx, dy, dz);
        if (grad.sqrMagnitude < 1e-10f) return Vector3.zero;   //flat/degenerate - no wall//
        return (-grad).normalized;   //negate: field falls outward, we want "out"//
    }

    //Signed distance to the nearest surface at a point, positive inside. Lets a generator ask
    //"how close am I to the wall" to gate its curve, without a second field reference.
    public float DistanceInside(Vector3 world) => field != null ? field.SampleWorld(world) : -1f;
}
