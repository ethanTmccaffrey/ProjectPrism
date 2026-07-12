using UnityEngine;
//Prominence — what the coordination layer hands back to a generator each frame//
//Describes how much presence this generator should command RIGHT NOW, relative to every other active generator, so marks can be placed with spatial hierarchy rather than all competing for the centre//
//Values are consumed at spawn time and then locked into the mark (persistent-canvas principle): a mark records the song's character at the moment it was born//
public struct Prominence
{
    //True only for the single highest-weighted active generator this frame//
    //That generator getss a guaranteed claim on the centre (the hybrid rule)//
    public bool isDominant;

    //0-1 How close to the centre this generator should place marks//
    //-1 = centre of the canvas, -0 = outer periphary//
    //Drives spawn-position radius (low centrality = spawn further out)//
    public float centrality;

    //0-1 How large and dense this generators marks should be//
    //A minor generator makes smaller, sparser marks so it reads as a trace not a competitor//
    //Drives mark scale and spawn rate//
    public float prominence;

    //Convenience: a silent generator (weight below the active floor) return this//
    public static Prominence Silent => new Prominence {isDominant = false, centrality = 0f, prominence = 0f};
}
