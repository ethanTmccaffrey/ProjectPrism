using System.Collections.Generic;
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

    [Header("Containment Ellipsoid")]
    //THE containment shape. Instead of testing every mark against the skull's irregular, un-
    //predictable wall (which caused bespoke breach-fixing in every generator), all containment
    //is against a smooth ellipsoid inscribed INSIDE the cavity with margin, so nothing that
    //respects it can ever touch bone. The skull is just the visible frame around it - the
    //ellipsoid is invisible in normal use.
    //
    //The mind lives in the CRANIUM - the upper rounded vault - NOT the whole skull hollow. The
    //raw cavity probe includes jaw, nasal cavity and throat, so the ellipsoid must be lifted up
    //and squashed vertically to sit in the cranial vault, clear of the face and throat. Tune
    //fitFactor (overall size), centreOffset (lift it up), and the per-axis fit below with the
    //ellipsoid VISIBLE until it fills the cranium.
    [SerializeField, Range(0.3f, 1f)] private float fitFactor = 0.72f;
    //Per-axis multipliers on the fitted radii, so the vertical can be squashed independently to
    //keep the ellipsoid out of the jaw/throat while width and depth still fill the vault.
    [SerializeField] private Vector3 axisFit = new Vector3(1f, 0.7f, 1f);
    //World-space offset of the ellipsoid centre from the cavity centre - mainly a vertical LIFT
    //(positive Y) to move the shape up into the cranium.
    [SerializeField] private Vector3 centreOffset = new Vector3(0f, 40f, 0f);
    //Show the ellipsoid as a solid shape for testing, so its position is directly visible while
    //tuning. Turn off for normal use.
    [SerializeField] private bool showEllipsoid = false;
    [SerializeField] private Color ellipsoidColour = new Color(1f, 0f, 1f, 0.5f);   //magenta//

    public bool Ready { get; private set; }
    public Vector3 CavityCentre { get; private set; }
    //Half-extent of the cavity bounding box in world units (x,y,z).//
    public Vector3 CavityExtent { get; private set; }

    //The containment ellipsoid: centre + three radii. This is the ACTUAL boundary every
    //generator works within - simple analytic math, no field sampling.
    public Vector3 EllipsoidCentre { get; private set; }
    public Vector3 EllipsoidRadii { get; private set; }
    private GameObject _ellipsoidViz;

    //Occupancy: regions already claimed by placed marks, so new seeds push away from them and
    //generators spread across the cavity rather than piling in the middle. Each claim is a
    //centre and a radius of influence; the seeder repels candidates that fall within a claim.
    private struct Claim { public Vector3 centre; public float radius; }
    private readonly System.Collections.Generic.List<Claim> _claims = new System.Collections.Generic.List<Claim>();

    //Record that a mark has occupied a region, so later seeds avoid it. Generators call this
    //after seeding, passing their own footprint as the radius (a big vortex claims a big radius,
    //a small speck a small one).
    public void ClaimPoint(Vector3 centre, float radius)
    {
        _claims.Add(new Claim { centre = centre, radius = Mathf.Max(1f, radius) });
    }

    //Clears all claims - call when the canvas resets for a new track.//
    public void ClearClaims() => _claims.Clear();

    private void Awake()
    {
        if (field == null) field = GetComponent<HeadField>();
    }

    private void Start()
    {
        //HeadField loads and builds in its own Start; ordering between two objects' Start is
        //not guaranteed, so ensure it is loaded before probing rather than assuming.
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

        //Build the containment ellipsoid, positioned in the CRANIAL VAULT: centre lifted up by
        //centreOffset (out of the jaw/throat), radii = extent * fitFactor * per-axis squash.
        EllipsoidCentre = CavityCentre + centreOffset;
        EllipsoidRadii = Vector3.Scale(CavityExtent * fitFactor, axisFit);

        Ready = true;

        Debug.Log($"PRISM HeadPlacement: cavity probed - centre {CavityCentre}, " +
                  $"extent {CavityExtent}, {hits} hits at res {n}.");
        Debug.Log($"PRISM HeadPlacement: containment ellipsoid centre {EllipsoidCentre}, " +
                  $"radii {EllipsoidRadii} (fitFactor {fitFactor}).");

        if (showEllipsoid) BuildEllipsoidViz();
    }

    // ===================================================================================
    //  ELLIPSOID CONTAINMENT - the simple, predictable boundary all generators work within.
    //  Everything is analytic: transform a world point into "unit-sphere space" by subtracting
    //  the centre and dividing by the radii, and the ellipsoid becomes a unit sphere where every
    //  test is trivial. No field sampling, no ray marching, no shell gap.
    // ===================================================================================

    //True if a world point is inside the ellipsoid.//
    public bool InEllipsoid(Vector3 world)
    {
        Vector3 u = ToUnit(world);
        return u.sqrMagnitude <= 1f;
    }

    //A uniformly-distributed random point inside the ellipsoid.//
    public Vector3 RandomInEllipsoid()
    {
        //Uniform in the unit sphere, then scale back out to the ellipsoid.//
        Vector3 u = Random.insideUnitSphere;
        return EllipsoidCentre + Vector3.Scale(u, EllipsoidRadii);
    }

    //A random point inside the ellipsoid at a given fill fraction (0..1) of its radii - lets a
    //generator seed nearer the centre (small fill) or use the whole volume (fill 1).
    public Vector3 RandomInEllipsoid(float fill)
    {
        Vector3 u = Random.insideUnitSphere * Mathf.Clamp01(fill);
        return EllipsoidCentre + Vector3.Scale(u, EllipsoidRadii);
    }

    //Clamps a world point to lie inside (or on) the ellipsoid. If already inside, returned
    //unchanged; if outside, projected back onto the surface along the centre direction. This is
    //THE containment operation - a growing mark calls it each step and can never leave.
    public Vector3 ClampToEllipsoid(Vector3 world)
    {
        Vector3 u = ToUnit(world);
        float m = u.magnitude;
        if (m <= 1f) return world;                 //already inside//
        u /= m;                                     //project to unit sphere surface//
        return EllipsoidCentre + Vector3.Scale(u, EllipsoidRadii);
    }

    //A point ON the ellipsoid surface in a given world-space direction from the centre - the
    //analytic replacement for ray-marching to the wall (used by the web's anchors). Direction
    //need not be normalised.
    public Vector3 SurfacePointInDirection(Vector3 dir)
    {
        //Find where the ray centre+t*dir hits the unit sphere in unit space, then scale out.//
        Vector3 dU = new Vector3(dir.x / EllipsoidRadii.x, dir.y / EllipsoidRadii.y, dir.z / EllipsoidRadii.z);
        float len = dU.magnitude;
        if (len < 1e-6f) return EllipsoidCentre;
        Vector3 uHit = dU / len;                    //unit-sphere surface point//
        return EllipsoidCentre + Vector3.Scale(uHit, EllipsoidRadii);
    }

    //Outward surface normal of the ellipsoid at a world point (for marks that curve off the
    //wall). Gradient of (x/rx)^2+(y/ry)^2+(z/rz)^2 is (2x/rx^2, ...), normalised.
    public Vector3 EllipsoidNormal(Vector3 world)
    {
        Vector3 d = world - EllipsoidCentre;
        Vector3 g = new Vector3(
            d.x / (EllipsoidRadii.x * EllipsoidRadii.x),
            d.y / (EllipsoidRadii.y * EllipsoidRadii.y),
            d.z / (EllipsoidRadii.z * EllipsoidRadii.z));
        return g.sqrMagnitude < 1e-12f ? Vector3.up : g.normalized;
    }

    //How far inside the ellipsoid a point is, as a 0..1 fraction (1 = centre, 0 = surface,
    //<0 = outside). Cheap proxy for "depth" without field sampling.
    public float EllipsoidDepth(Vector3 world)
    {
        return 1f - ToUnit(world).magnitude;
    }

    //World point -> unit-sphere space (centre subtracted, divided by radii).//
    private Vector3 ToUnit(Vector3 world)
    {
        Vector3 d = world - EllipsoidCentre;
        return new Vector3(d.x / EllipsoidRadii.x, d.y / EllipsoidRadii.y, d.z / EllipsoidRadii.z);
    }

    //Builds a wireframe sphere scaled into the ellipsoid, for visual testing.//
    private void BuildEllipsoidViz()
    {
        if (_ellipsoidViz != null) Destroy(_ellipsoidViz);
        _ellipsoidViz = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _ellipsoidViz.name = "ContainmentEllipsoid_VIZ";
        var col = _ellipsoidViz.GetComponent<Collider>();
        if (col != null) Destroy(col);

        _ellipsoidViz.transform.SetParent(transform, false);
        _ellipsoidViz.transform.position = EllipsoidCentre;
        //Unity sphere primitive is radius 0.5, so diameter = 1; scale = 2*radii to match.//
        _ellipsoidViz.transform.localScale = EllipsoidRadii * 2f;

        var r = _ellipsoidViz.GetComponent<Renderer>();
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        var mat = new Material(sh);
        mat.color = ellipsoidColour;
        r.material = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    //Toggle the ellipsoid viz at runtime.//
    public void SetEllipsoidVisible(bool visible)
    {
        showEllipsoid = visible;
        if (visible && _ellipsoidViz == null && Ready) BuildEllipsoidViz();
        else if (_ellipsoidViz != null) _ellipsoidViz.SetActive(visible);
    }

#if UNITY_EDITOR
    //Live-tune in the editor: when ellipsoid values change during play, rebuild it so you can
    //drag fitFactor/axisFit/centreOffset and watch the magenta shape move in real time. Does
    //nothing in a build.
    private void OnValidate()
    {
        if (!Application.isPlaying || !Ready) return;
        EllipsoidCentre = CavityCentre + centreOffset;
        EllipsoidRadii = Vector3.Scale(CavityExtent * fitFactor, axisFit);
        if (showEllipsoid) BuildEllipsoidViz();
        else if (_ellipsoidViz != null) { Destroy(_ellipsoidViz); _ellipsoidViz = null; }
    }
#endif

    //A seed point inside the cranial volume, placed by the caller's prominence.
    //  centrality 0-1 : 1 sits at the cavity centre, 0 is pushed toward the wall.
    //  prominence 0-1 : how wide a spread around that anchor the point may fall in.
    //  minDepth       : optional minimum field-depth (SampleWorld value) the point must have,
    //                   so it sits CLEAR of the wall, not just inside the cavity. Scatterers
    //                   leave this 0 (anywhere inside is fine); generators that GROW outward
    //                   from the seed (spiral, burst) pass a positive value so the centre has
    //                   room before it reaches the wall, and does not land in the shell gap.
    //                   Field units - shellThickness is 0.18, so ~0.6-0.9 is a solid clearance.
    //Rejection-sampled against the real cavity, so a returned point is always inside. Falls
    //back to the cavity centre if the field is somehow unavailable, so a generator never NaNs.
    public Vector3 RandomPointInCavity(float prominence, float centrality, float minDepth = 0f)
    {
        if (!Ready) return CavityCentre;


        //Anchor: centrality slides from centre out toward the wall along a random direction.
        //A dominant mark (high centrality) anchors near centre, a minor one sits out to a side.
        //The offset is FLOORED (so fully-central marks keep some spread and don't all stack on
        //the centre) and CAPPED well below a full extent - the cavity is egg-shaped inside its
        //bounding box, so an offset near 1.0 lands in bone, every sample fails, and the point
        //collapses to the centre fallback (which is exactly what made low-centrality generators
        //all seed on the identical centre). 0.55 keeps the anchor inside the real cavity.
        float offMag = Mathf.Clamp(1f - Mathf.Clamp01(centrality), 0.2f, 0.55f);

        //Vertical spread fix: the raw offset leans toward the cavity's vertical middle-low, so
        //scenes trend low with the crown empty. Bias the vertical component of the anchor
        //direction upward-inclusive by using a full-range random Y, so seeds reach the top of
        //the cavity as readily as the bottom. Centred on the true vertical mid of the extent.
        Vector3 dir = Random.onUnitSphere;
        dir.y = Random.Range(-1f, 1f);   //even vertical coverage, not sphere-area-weighted//
        dir = dir.normalized;
        Vector3 anchor = CavityCentre + Vector3.Scale(dir * offMag, CavityExtent);

        float spread = Mathf.Lerp(0.15f, 0.9f, Mathf.Clamp01(prominence));

        //Track the best candidate seen (furthest from claimed regions) so a crowded cavity
        //still places SOMETHING rather than failing - soft repulsion, not hard rejection.
        Vector3 best = CavityCentre;
        float bestScore = float.NegativeInfinity;
        bool haveBest = false;

        for (int attempt = 0; attempt < 32; attempt++)
        {
            float s = spread * (1f - attempt / 48f);
            Vector3 candidate = anchor + Vector3.Scale(Random.insideUnitSphere * s, CavityExtent);

            //Must be inside, and clear of the wall by minDepth if asked.//
            bool inside = minDepth <= 0f ? field.IsInCavity(candidate)
                                         : field.SampleWorld(candidate) >= minDepth;
            if (!inside) continue;

            //Score by distance to the nearest claim (bigger = further from occupied regions).
            //A candidate outside all claims scores its raw distance; one inside a claim scores
            //negative by how deep it intrudes, so it's only chosen if nothing better turns up.
            float score = NearestClaimClearance(candidate);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
                haveBest = true;
                //Clear of every claim - good enough, take it immediately.//
                if (score >= 0f) return candidate;
            }
        }

        //Return the least-crowded valid candidate found. If NONE were inside (anchor was in a
        //bad spot), fall back near the cavity centre - but jittered and claim-aware, so repeated
        //fallbacks don't all pile on the exact same centre point (which caused the stacking).
        if (haveBest) return best;

        for (int j = 0; j < 16; j++)
        {
            Vector3 jittered = CavityCentre + Vector3.Scale(Random.insideUnitSphere * 0.35f, CavityExtent);
            bool inside = minDepth <= 0f ? field.IsInCavity(jittered)
                                         : field.SampleWorld(jittered) >= minDepth;
            if (inside && NearestClaimClearance(jittered) >= 0f) return jittered;
        }
        return CavityCentre;
    }

    //Signed clearance from a point to the nearest claim: positive = outside all claims (value is
    //distance to the nearest claim surface), negative = inside a claim (how far past its edge).
    private float NearestClaimClearance(Vector3 p)
    {
        if (_claims.Count == 0) return 1f;   //nothing claimed - always clear//
        float worst = float.PositiveInfinity;
        for (int i = 0; i < _claims.Count; i++)
        {
            float d = Vector3.Distance(p, _claims[i].centre) - _claims[i].radius;
            if (d < worst) worst = d;
        }
        return worst;
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

    //A point guaranteed inside the cavity, for starting the anchor marches. CavityCentre (the
    //bounding-box centre) is tried first; if it happens to sit in bone - possible for an
    //irregular cavity - sample the probe grid for any hit and return the one nearest the
    //bounding-box centre, so marches still start from roughly the middle.
    private Vector3 InteriorPoint()
    {
        if (field.IsInCavity(CavityCentre)) return CavityCentre;

        Bounds b = field.FieldBounds;
        Vector3 min = b.min, size = b.size;
        int n = 20;
        Vector3 best = CavityCentre;
        float bestDist = float.MaxValue;

        for (int ix = 0; ix < n; ix++)
            for (int iy = 0; iy < n; iy++)
                for (int iz = 0; iz < n; iz++)
                {
                    Vector3 p = min + new Vector3(
                        size.x * ix / (n - 1),
                        size.y * iy / (n - 1),
                        size.z * iz / (n - 1));
                    if (!field.IsInCavity(p)) continue;
                    float dd = (p - CavityCentre).sqrMagnitude;
                    if (dd < bestDist) { bestDist = dd; best = p; }
                }

        return best;
    }

    //Points ON the inner wall, spread evenly over the cavity surface - anchors for a web,
    //or later, seeds for the shell generators. Fires rays from the cavity centre in evenly
    //distributed DIRECTIONS (a Fibonacci sphere), and for each, walks outward to find where
    //the cavity ends and the wall begins. Because directions are even, anchors reach the
    //crown as reliably as the base - it sidesteps the volume seeder's centre-bias entirely,
    //since a wall point only needs the wall to exist in that direction, not open volume.
    //
    //  count       - how many anchors to return (may return fewer if some rays miss).
    //  jitter 0-1  - random angular wobble per direction, so repeated calls/webs differ and
    //                the anchors don't sit on a perfectly regular lattice.
    //Returns points a hair INSIDE the wall (last cavity point before the shell), so threads
    //anchored to them sit in the void, not buried in bone.
    public List<Vector3> WallAnchors(int count, float jitter = 0.15f, float inwardPull = 14f, float verticalStretch = 0.5f)
    {
        var anchors = new List<Vector3>(count);
        if (!Ready) return anchors;

        //March step: a fraction of the smallest extent, fine enough to land close to the wall
        //without excessive samples. The cavity is large, so this is tens of steps per ray.
        float step = Mathf.Min(CavityExtent.x, Mathf.Min(CavityExtent.y, CavityExtent.z)) / 40f;
        step = Mathf.Max(step, field.VoxelWorldSize);
        float maxReach = CavityExtent.magnitude * 1.2f;   //ray length ceiling//

        //Golden-angle increment for an even spread of directions over the sphere.//
        float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));

        //Start the marches from a point KNOWN to be inside the cavity. CavityCentre is the
        //bounding-box centre, which for an irregular (non-convex) cavity is not guaranteed to
        //be inside - if it sits in bone, a ray starting there marches straight across and
        //anchors on the far wall or outside the head. Find a genuine interior start first.
        Vector3 start = InteriorPoint();

        for (int i = 0; i < count; i++)
        {
            //Fibonacci-sphere direction. y is spread evenly across -1..1, but a sphere's area
            //concentrates near the equator, so with few rays the poles (roof/floor of the
            //cavity) get too few anchors. verticalStretch warps y toward the poles to push more
            //rays up and down: 0 = plain sphere (middle-heavy), 1 = strongly pole-biased. Sign-
            //preserving power curve pulls mid values toward the extremes.
            float t = (i + 0.5f) / count;
            float y = 1f - 2f * t;                       //-1..1//
            if (verticalStretch > 0f)
            {
                float p = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(verticalStretch));  //exponent < 1 pushes toward +/-1//
                y = Mathf.Sign(y) * Mathf.Pow(Mathf.Abs(y), p);
            }
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float phi = i * golden;
            Vector3 dir = new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);

            //Jitter the direction a little so anchors aren't a perfect lattice.//
            if (jitter > 0f)
                dir = (dir + Random.insideUnitSphere * jitter).normalized;

            //Walk the WHOLE ray and record the outermost inside->outside transition - the true
            //outer wall in this direction. Taking the FIRST transition would stop at an internal
            //concavity; taking the last inside point overall could sit just past a re-entry.
            //The outermost transition is the point where we leave the cavity and never return.
            Vector3 wall = start;
            bool haveWall = false;
            bool prevInside = field.IsInCavity(start);

            for (float d = step; d <= maxReach; d += step)
            {
                Vector3 p = start + dir * d;
                bool inside = field.IsInCavity(p);

                //Transition from inside to outside: p-step was the last inside point. Record it
                //as the current best wall, but keep marching in case the ray re-enters (a
                //concavity) and exits again further out - we want the LAST such exit.
                if (prevInside && !inside)
                {
                    wall = start + dir * (d - step);
                    haveWall = true;
                }
                prevInside = inside;
            }

            //Pull the anchor a fixed WORLD distance inward off the wall, along the ray it came
            //in on. This binds to the INNER field, but the visible skull is the OUTER shell
            //(inner scale 90, outer 100 - roughly 10 world units of gap). An anchor right at the
            //inner wall renders inside that gap, poking through the bone. Stepping inward by
            //inwardPull world units clears the gap and seats the anchor just inside the true
            //void - WITHOUT dragging it toward the cavity centre. The old field-unit depth
            //target pulled anchors a third of the way to centre because a small field-depth is a
            //large world distance in this shallow field; a plain world-space step is predictable.
            if (haveWall)
            {
                wall = wall - dir * inwardPull;
                //Accept only if the pulled point is comfortably inside - not just inside the
                //inner cavity but clear of the wall by a margin, so an anchor never lands in the
                //shell gap or (on an awkward ray) just outside. SampleWorld rising well above
                //shellThickness confirms real interior depth; a stray that pulled through a thin
                //slice fails this and is dropped.
                if (field.IsInCavity(wall) && field.SampleWorld(wall) > field.ShellThickness * 1.5f)
                    anchors.Add(wall);
            }
        }

        return anchors;
    }

}