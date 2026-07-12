using UnityEngine;

//GeneratorId — stable identifier for each of the 20 generators//
//Order matches the weight declarations in TimbralProfile and the TimbralProfile.Weights array, so a GeneratorId can index directly into that array//
//Keep the two in lockstep: if a generator is added or reordered, update both here and the array fill//
//Used by the prominence coordinator (PRISMGenerator) to rank generators against each other and by generators to identify themselves when asking for their prominence//
public enum GeneratorID
{
    //Ctegory 1: Tunnels and Funnels//
    ConcentricRings = 0,
    TunnelDepth,

    //Category 2: Spirals//
    SpiralGrowth,
    RotationField,
    Drift,

    //Category 3: Lattices and Honeycombs//
    Honeycomb,
    GridGrating,
    Filigree,
    Reduplication,

    //Category 4: Cobwebs and Radial Forms//
    RadiationBurst,
    Fracture,
    CobwebSpline,

    //Category 5: Parallel figures//
    ZigzagParallel,
    WavyParallel,
    Hatching,

    //Category 6: Wavy Lines and Amorphous Forms//
    FluidTendril,
    AmorphousSpeck,
    BilateralDuplication,

    //Category 7: Small Circular forms//
    SpeckCluster,
    OrganicCluster,

    Count //Always last, gives the total (20) for array sizing//
}
