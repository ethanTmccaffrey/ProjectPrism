using UnityEngine;

//GeneratorID//
//Enum identifying the 18 generators, ordered by Klüver form-constant category//
//Count is last for array sizing, Order must match the weight array in TimbralProfile//
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

    //Category 6: Wavy Lines and Amorphous Forms//
    FluidTendril,
    BilateralDuplication,

    //Category 7: Small Circular forms//
    SpeckCluster,
    OrganicCluster,

    Count //Always last, gives the total (18) for array sizing//
}
