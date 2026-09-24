using System.Collections.Generic;

// ---------------------------------------------------------------------
// Data model for the multi-leg mission planner (companion to MissionPlanner.cs).
// Mirrors the SpacecraftConfig / Node / Leg shapes from the implementation guide.
// ---------------------------------------------------------------------

public enum NodeType
{
    Start,
    Flyby,
    End
}

public enum ArrivalMode
{
    Flyby,          // no capture burn at the final node - just pass by
    OrbitInsertion  // match the target body's velocity at the final node
}

public enum EnginePreset
{
    Chemical,
    Ion,
    Custom
}

[System.Serializable]
public class SpacecraftConfig
{
    // --- masses (kg) ---
    public double DryMass;
    public double FuelMass;
    public double PayloadMass;

    // Derived, never stored directly - keeps it from drifting out of sync
    // with the three fields above.
    public double WetMass => DryMass + FuelMass + PayloadMass;

    // --- engine ---
    public double Isp;    // seconds
    public double Thrust; // Newtons (not used by the trajectory/DE math - impulsive-burn
                          // assumption means thrust doesn't affect the trajectory shape.
                          // Kept here for a burn-duration sanity check / UI display later.)
    public EnginePreset EnginePreset = EnginePreset.Chemical; // UI-only, the solver never reads this

    // --- optimizer ---
    public double ObjectiveWeight = 0.5; // 0 = pure fuel-optimal, 1 = pure time-optimal
    public double SearchWindowStart;     // seconds, absolute sim time - bounds t0
    public double SearchWindowEnd;

    // --- mission shape ---
    public ArrivalMode ArrivalMode = ArrivalMode.OrbitInsertion;
    public double MaxMissionDuration = 0; // seconds, 0 or negative = unconstrained

    // --- flyby safety ---
    // Altitude above the body's surface, in meters. Keyed by the same body name
    // strings you pass into PlanMission (i.e. EngineManager.bodylookup keys).
    public Dictionary<string, double> MinFlybyPeriapsisAltitude = new Dictionary<string, double>();
    public double DefaultMinFlybyPeriapsisAltitude = 200_000; // 200 km fallback if a body isn't in the dict above

    public double GetMinFlybyAltitude(string bodyName)
    {
        return MinFlybyPeriapsisAltitude.TryGetValue(bodyName, out double alt)
            ? alt
            : DefaultMinFlybyPeriapsisAltitude;
    }
}

public class MissionNode
{
    public string BodyName;
    public NodeType Type;
    public double Epoch; // seconds - filled in by PlanMission once solved, meaningless before that

    public MissionNode(string bodyName, NodeType type)
    {
        BodyName = bodyName;
        Type = type;
    }
}

public class MissionLeg
{
    public MissionNode NodeA;
    public MissionNode NodeB;
    public double TOF => NodeB.Epoch - NodeA.Epoch;
    public spacecraftPathGen.Vector3D V_departure; // heliocentric velocity at NodeA
    public spacecraftPathGen.Vector3D V_arrival;   // heliocentric velocity at NodeB
}

public class MissionResult
{
    public bool Feasible;
    public double TotalDeltaV;  // same units as your Lambert solver's velocities
    public double TotalTOF;     // seconds
    public double MassMargin;   // kg left above (DryMass+PayloadMass) if feasible, or the shortfall (negative) if not
    public double BestCost;     // raw DE cost (blended objective + penalties) - for debugging, not user-facing
    public List<MissionNode> Nodes = new List<MissionNode>();
    public List<MissionLeg> Legs = new List<MissionLeg>();
}