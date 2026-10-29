/*using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class MissionPlanner : MonoBehaviour
{
    public OrbitViewer orbitViewer;
    public PhysicsManager physicsManager;
    public EngineManager engineManager;
    public SceneManager sceneManager;
    public SpacecraftManager spacecraftManager;
    public spacecraftPathGen spacecraftPathGen;
    public showWarning showWarning;

    private const double G0 = 9.80665;

    private PhysicsManager.physicsObject sunObj;
    private Dictionary<string, OrbitViewer.KeplerianElements> kepvalues;
    private double[] _epochScratch;

    public void Init()
    {
        sunObj = physicsManager.fullObjData[engineManager.bodylookup["SUN"]];
    }

    // ==================================================================
    // Public entry point
    // ==================================================================

    /// <summary>
    /// bodies/types are parallel arrays describing the route, e.g.
    ///   bodies = ["EARTH", "VENUS", "JUPITER"]
    ///   types  = [Start,    Flyby,   End]
    /// First entry must be Start, last must be End; anything between is a Flyby.
    /// tofMinDays/tofMaxDaysPerLeg bound each leg's time-of-flight search (same
    /// bound applied to every leg for now - widen tofMaxDaysPerLeg for outer-planet
    /// routes, e.g. ~1500 days comfortably covers an Earth->Jupiter leg).
    /// </summary>
    public MissionResult PlanMission(string[] bodies, NodeType[] types, SpacecraftConfig config, double tofMinDays = 30, double tofMaxDaysPerLeg = 1500, int pop = 40, int gens = 200)
    {
        if (bodies.Length != types.Length)
        {
            showWarning.dispalyNotif(0, "Bodies and types must be the same length");
            return null;
        }
        if (bodies.Length < 2)
        {
            showWarning.dispalyNotif(0, "Need at least a Start and an End node");
            return null;
        }
        if (types[0] != NodeType.Start || types[types.Length - 1] != NodeType.End)
        {
            showWarning.dispalyNotif(0, "First node must be Start, last node must be End");
            return null;
        }

        var nodes = new List<MissionNode>(bodies.Length);
        for (int i = 0; i < bodies.Length; i++)
            nodes.Add(new MissionNode(bodies[i], types[i]));

        storeKepValues(bodies);
        _epochScratch = new double[nodes.Count];

        int numLegs = nodes.Count - 1;
        int dims = 1 + numLegs; // [t0, TOF_1, ..., TOF_n]

        double[] lower = new double[dims];
        double[] upper = new double[dims];
        lower[0] = config.SearchWindowStart;
        upper[0] = config.SearchWindowEnd;

        double tofMinSec = tofMinDays * 86400.0;
        double tofMaxSec = tofMaxDaysPerLeg * 86400.0;
        for (int i = 1; i < dims; i++)
        {
            lower[i] = tofMinSec;
            upper[i] = tofMaxSec;
        }

        double expectedMaxDeltaV = EstimateExpectedMaxDeltaV(config);

        Func<double[], double> costFn = (x) => CostFunction(x, nodes, config, expectedMaxDeltaV, out _, out _);
        var (bestX, _) = DifferentialEvolution(lower, upper, costFn, pop, gens);

        // one final evaluation at the winning X to get the actual legs + totalDeltaV back
        double finalCost = CostFunction(bestX, nodes, config, expectedMaxDeltaV, out double totalDeltaV, out var legs);

        // write the winning epochs back onto the node objects we hand back to the caller
        nodes[0].Epoch = bestX[0];
        for (int i = 1; i < nodes.Count; i++)
            nodes[i].Epoch = nodes[i - 1].Epoch + bestX[i];

        // _epochScratch still holds bestX's epochs from the CostFunction call directly above
        double[] finalNodeCosts = NodeCostsArray(nodes, _epochScratch, legs, config);
        bool feasible = FeasibilityFromCosts(finalNodeCosts, config, out double margin);

        double totalTOF = 0;
        for (int i = 1; i < bestX.Length; i++) totalTOF += bestX[i];
        bool durationOk = config.MaxMissionDuration <= 0 || totalTOF <= config.MaxMissionDuration;
        bool periapsisOk = PeriapsisPenalty(nodes, _epochScratch, legs, config) <= 0;

        return new MissionResult
        {
            Feasible = feasible && durationOk && periapsisOk,
            TotalDeltaV = totalDeltaV,
            TotalTOF = totalTOF,
            MassMargin = margin,
            BestCost = finalCost,
            Nodes = nodes,
            Legs = legs
        };
    }

    // ==================================================================
    // Ephemeris helpers
    // ==================================================================

    private void storeKepValues(string[] bodies)
    {
        kepvalues = new Dictionary<string, OrbitViewer.KeplerianElements>();
        foreach (string b in bodies)
        {
            if (kepvalues.ContainsKey(b)) continue;
            var obj = physicsManager.fullObjData[engineManager.bodylookup[b]];
            kepvalues[b] = orbitViewer.calcKep(obj.pos, obj.vel, obj.mass, sunObj.mass);
        }
    }

    private double3 GetPos(string bodyName, double epoch) =>
        orbitViewer.getPositionAtTime(kepvalues[bodyName], epoch) - sunObj.pos;

    private double3 GetVel(string bodyName, double epoch) =>
        orbitViewer.getVelocityAtTime(kepvalues[bodyName], epoch);

    private double GetBodyRadius(string bodyName)
    {
        // TODO: wire this to whatever your Body component exposes, e.g.
        //   SceneManager.allBodys[engineManager.bodylookup[bodyName]].GetComponent<Body>().radius
        // Returning 0 means the periapsis check is against raw altitude rather than altitude-above-
        // surface - a fine v1 sanity check, but tighten this once you point it at a real radius field.
        return sceneManager.allBodys[engineManager.bodylookup[bodyName]].GetComponent<Body>().radius;
    }

    // ==================================================================
    // Cost function (called by DE, thousands of times per solve)
    // ==================================================================

    private double CostFunction(
        double[] x,
        List<MissionNode> nodes,
        SpacecraftConfig config,
        double expectedMaxDeltaV,
        out double totalDeltaV,
        out List<MissionLeg> legs)
    {
        // Deliberately does NOT write into nodes[i].Epoch here - that field is only ever set once,
        // after DE finishes, in PlanMission. Keeping this function free of shared-state mutation
        // means the population loop below can be swapped for Parallel.For later without any of the
        // DE candidates stomping on each other's epochs.
        if (_epochScratch == null || _epochScratch.Length != nodes.Count)
            _epochScratch = new double[nodes.Count];
        double[] epochs = _epochScratch;

        epochs[0] = x[0];
        double totalTOF = 0;
        for (int i = 1; i < nodes.Count; i++)
        {
            totalTOF += x[i];
            epochs[i] = epochs[i - 1] + x[i];
        }

        legs = new List<MissionLeg>(nodes.Count - 1);
        for (int i = 1; i < nodes.Count; i++)
        {
            var leg = new MissionLeg { NodeA = nodes[i - 1], NodeB = nodes[i] };

            double3 r1 = GetPos(nodes[i - 1].BodyName, epochs[i - 1]);
            double3 r2 = GetPos(nodes[i].BodyName, epochs[i]);

            try
            {
                var (v1, v2) = spacecraftPathGen.SolveLambert(r1, r2, x[i], sunObj.mass, "prograde");
                leg.V_departure = v1;
                leg.V_arrival = v2;
            }
            catch
            {
                // degenerate/non-convergent Lambert case (e.g. ~180-degree transfer) - hand the DE a
                // heavy penalty rather than crashing, so it steers away from this region of the search
                legs.Add(leg);
                totalDeltaV = double.NaN;
                return 1e12;
            }

            legs.Add(leg);
        }

        double[] nodeCosts = NodeCostsArray(nodes, epochs, legs, config);
        totalDeltaV = 0;
        foreach (double c in nodeCosts) totalDeltaV += c;

        double penalty = 0;

        bool feasible = FeasibilityFromCosts(nodeCosts, config, out double margin);
        if (!feasible)
            penalty += 1e6 * (-margin);

        if (config.MaxMissionDuration > 0 && totalTOF > config.MaxMissionDuration)
            penalty += 1e6 * (totalTOF - config.MaxMissionDuration);

        penalty += PeriapsisPenalty(nodes, epochs, legs, config);

        // normalize before blending - dV and TOF are different scales/units
        double normalizedDeltaV = totalDeltaV / math.max(expectedMaxDeltaV, 1e-9);
        double normalizedTOF = totalTOF / math.max(config.SearchWindowEnd - config.SearchWindowStart, 1e-9);
        double blended = config.ObjectiveWeight * normalizedTOF + (1 - config.ObjectiveWeight) * normalizedDeltaV;

        return blended + penalty;
    }

    // ==================================================================
    // Node cost / feasibility / flyby safety
    // ==================================================================

    /// <summary>
    /// The "unified dV formula": at every node, cost = |departure velocity - arrival velocity| in the
    /// heliocentric frame. At the Start node, arrival velocity is just the departure body's own velocity
    /// (co-moving assumption). At the End node, departure velocity is either the same as arrival (Flyby -
    /// no burn) or the target body's own velocity (OrbitInsertion). Everywhere else it's whatever the two
    /// adjoining Lambert legs computed.
    /// NOTE: this treats every flyby's full velocity discontinuity as billed dV, even though a real
    /// unpowered gravity assist gets some of that turn for free from the body's gravity (see
    /// PeriapsisPenalty below, which checks whether the *turn* would be geometrically safe/achievable -
    /// it doesn't currently discount the cost for it). That's a known v1 simplification worth revisiting
    /// once the basic multi-leg solve is working end to end.
    /// </summary>
    private double[] NodeCostsArray(List<MissionNode> nodes, double[] epochs, List<MissionLeg> legs, SpacecraftConfig config)
    {
        double[] costs = new double[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            double3 vArrive = i == 0
                ? GetVel(nodes[0].BodyName, epochs[0])
                : (legs[i - 1].V_arrival);

            double3 vDepart;
            if (i == nodes.Count - 1)
            {
                vDepart = config.ArrivalMode == ArrivalMode.Flyby
                    ? vArrive
                    : GetVel(nodes[i].BodyName, epochs[i]);
            }
            else
            {
                vDepart = (legs[i].V_departure);
            }

            costs[i] = math.length(vDepart - vArrive);
        }
        return costs;
    }

    private bool FeasibilityFromCosts(double[] nodeCosts, SpacecraftConfig config, out double margin)
    {
        double ve = config.Isp * G0;
        double mass = config.WetMass;
        double minMass = config.DryMass + config.PayloadMass;

        foreach (double cost in nodeCosts)
        {
            double propUsed = mass * (1 - Math.Exp(-cost / math.max(ve, 1e-9)));
            mass -= propUsed;
            if (mass < minMass)
            {
                margin = minMass - mass; // shortfall - how much prop/Isp is missing
                return false;
            }
        }

        margin = mass - minMass;
        return true;
    }

    private double PeriapsisPenalty(List<MissionNode> nodes, double[] epochs, List<MissionLeg> legs, SpacecraftConfig config)
    {
        double penalty = 0;
        for (int i = 1; i < nodes.Count - 1; i++) // only interior nodes have both an incoming and outgoing leg
        {
            if (nodes[i].Type != NodeType.Flyby) continue;

            double3 vArrive = (legs[i - 1].V_arrival);
            double3 vDepart = (legs[i].V_departure);
            double3 vBody = GetVel(nodes[i].BodyName, epochs[i]);

            double3 vInfIn = vArrive - vBody;
            double3 vInfOut = vDepart - vBody;
            double vInfInMag = math.length(vInfIn);
            double vInfOutMag = math.length(vInfOut);
            double vInf = 0.5 * (vInfInMag + vInfOutMag); // equal for a purely unpowered flyby

            if (vInf < 1e-9) continue; // effectively no relative velocity - nothing meaningful to check

            double dotP = math.clamp(math.dot(vInfIn, vInfOut) / (vInfInMag * vInfOutMag), -1.0, 1.0);
            double turnAngle = Math.Acos(dotP);
            double sinHalf = Math.Sin(turnAngle / 2.0);
            if (sinHalf < 1e-6) continue; // ~straight-line pass, no real periapsis constraint

            double muBody = physicsManager.fullObjData[engineManager.bodylookup[nodes[i].BodyName]].mass; // GM, not kg
            double impliedPeriapsis = (muBody / (vInf * vInf)) * (1.0 / sinHalf - 1.0);

            double minSafe = config.GetMinFlybyAltitude(nodes[i].BodyName) + GetBodyRadius(nodes[i].BodyName);
            if (impliedPeriapsis < minSafe)
                penalty += 1e6 * (minSafe - impliedPeriapsis);
        }
        return penalty;
    }

    private double EstimateExpectedMaxDeltaV(SpacecraftConfig config)
    {
        // Used only to normalize the DE cost blend, so it needs to be in the same units the Lambert
        // solver's velocities come out in - rather than guessing a constant (e.g. "50 km/s"), this
        // derives it from the ship's own theoretical full-tank ceiling, which is dimensionally
        // consistent with everything else in the cost function no matter what unit system you're on.
        double ve = config.Isp * G0;
        double minMass = math.max(config.DryMass + config.PayloadMass, 1e-6);
        return ve * Math.Log(config.WetMass / minMass);
    }

    // ==================================================================
    // Differential evolution (generalized to N dimensions)
    // ==================================================================

    private (double[] bestX, double bestCost) DifferentialEvolution(
        double[] lower, double[] upper, Func<double[], double> costFn,
        int popSize, int generations, double F = 0.8, double CR = 0.9)
    {
        var rng = new System.Random();
        int dims = lower.Length;

        double[][] population = new double[popSize][];
        double[] costs = new double[popSize];
        for (int i = 0; i < popSize; i++)
        {
            population[i] = new double[dims];
            for (int d = 0; d < dims; d++)
                population[i][d] = lower[d] + rng.NextDouble() * (upper[d] - lower[d]);
            costs[i] = costFn(population[i]);
        }

        double[] trial = new double[dims]; // reused scratch buffer - not allocated per candidate/generation

        for (int gen = 0; gen < generations; gen++)
        {
            double currentF = F;
            if (gen > 0.8 * generations)
                currentF = Lerp(F, 0.3, (gen - 0.8 * generations) / (0.2 * generations));

            for (int i = 0; i < popSize; i++)
            {
                int a, b, c;
                do { a = rng.Next(popSize); } while (a == i);
                do { b = rng.Next(popSize); } while (b == i || b == a);
                do { c = rng.Next(popSize); } while (c == i || c == a || c == b);

                int forcedDim = rng.Next(dims);
                for (int d = 0; d < dims; d++)
                {
                    trial[d] = (rng.NextDouble() < CR || d == forcedDim)
                        ? population[a][d] + currentF * (population[b][d] - population[c][d])
                        : population[i][d];
                    trial[d] = math.clamp(trial[d], lower[d], upper[d]);
                }

                double trialCost = costFn(trial);
                if (trialCost < costs[i])
                {
                    Array.Copy(trial, population[i], dims);
                    costs[i] = trialCost;
                }
            }
        }

        int best = 0;
        for (int i = 1; i < popSize; i++) if (costs[i] < costs[best]) best = i;
        return (population[best], costs[best]);

        // Perf note (from the design doc): this whole loop is independent per population member, so if
        // pop*gens ever gets too slow for a many-node route, wrapping the `for (int i = 0; i < popSize...)`
        // body in Parallel.For is a two-line change - CostFunction just needs its own epoch/trial buffers
        // per thread instead of the shared _epochScratch field used here for the sequential version.
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * math.clamp(t, 0.0, 1.0);

    // ==================================================================
    // Launch handoff (extends the single-leg LaunchMission pattern to N nodes)
    // ==================================================================

    /// <summary>
    /// Spawns the craft at the Start node with the winning departure burn, then - if ArrivalMode is
    /// OrbitInsertion - waits for the final epoch and commands the capture burn. Intermediate Flyby
    /// nodes get NO commanded burn: the IAS15 N-body integrator provides the gravity assist naturally
    /// as the craft passes each body, which is why PeriapsisPenalty checks that pass is geometrically
    /// safe/achievable during the search.
    /// </summary>
    public IEnumerator LaunchMissionCoroutine(MissionResult result, SpacecraftConfig config)
    {
        if (!result.Feasible)
            Debug.LogWarning("MissionPlanner: launching a mission PlanMission flagged infeasible.");

        var nodes = result.Nodes;
        var legs = result.Legs;

        double3 spawnPos = GetPos(nodes[0].BodyName, nodes[0].Epoch) + sunObj.pos;
        double3 spawnVel = GetVel(nodes[0].BodyName, nodes[0].Epoch);

        while (physicsManager.t < nodes[0].Epoch)
            yield return null;

        var craft = physicsManager.addSpacecraft(spawnPos, spawnVel, 0.0);
        physicsManager.commandBurn(craft, (legs[0].V_departure), 60.0);

        if (config.ArrivalMode == ArrivalMode.OrbitInsertion)
        {
            double arrivalEpoch = nodes[nodes.Count - 1].Epoch;
            while (physicsManager.t < arrivalEpoch)
                yield return null;

            double3 targetVel = GetVel(nodes[nodes.Count - 1].BodyName, arrivalEpoch);
            physicsManager.commandBurn(craft, targetVel, 60.0);
        }
    }
}*/
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public enum NodeType
{
    Start,
    Flyby,
    End
}

public enum ArrivalMode
{
    Flyby,          // no capture burn at the final node, just pass by
    OrbitInsertion  // match the target body's velocity at the final node
}

public enum EnginePreset
{
    Chemical,
    Ion,
    Custom
}

[Serializable]
public class SpacecraftConfig
{
    // masses (kg)
    public double DryMass;
    public double FuelMass;
    public double PayloadMass;

    // derived so it can never drift out of sync with the three fields above
    public double WetMass => DryMass + FuelMass + PayloadMass;

    // engine
    public double Isp;    // seconds
    public double Thrust; // Newtons, not used by the solver (impulsive burns), kept for UI / burn duration checks
    public EnginePreset EnginePreset = EnginePreset.Chemical; // UI only

    // optimizer
    public double ObjectiveWeight = 0.5; // 0 = pure fuel optimal, 1 = pure time optimal
    public double SearchWindowStart;     // seconds, absolute sim time, bounds t0
    public double SearchWindowEnd;

    // mission shape
    public ArrivalMode ArrivalMode = ArrivalMode.OrbitInsertion;
    public double MaxMissionDuration = 0; // seconds, 0 or negative = unconstrained

    // flyby safety, altitude above the surface in meters, keyed by the same names as EngineManager.bodylookup
    public Dictionary<string, double> MinFlybyPeriapsisAltitude = new();
    public double DefaultMinFlybyPeriapsisAltitude = 200_000;

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
    public double Epoch; // seconds, filled in once solved

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
    public double3 V_departure; // heliocentric velocity at NodeA
    public double3 V_arrival;   // heliocentric velocity at NodeB
}

public class MissionResult
{
    public bool Feasible;
    public double TotalDeltaV;
    public double TotalTOF;   // seconds
    public double MassMargin; // kg above (DryMass + PayloadMass), negative = shortfall if infeasible
    public double BestCost;   // raw optimizer cost, debugging only
    public double[] RestartCosts; // best cost found by each restart, big spread = the search is multi-modal
    public List<MissionNode> Nodes = new();
    public List<MissionLeg> Legs = new();
}

public class MissionPlanner : MonoBehaviour
{
    public OrbitViewer orbitViewer;
    public PhysicsManager physicsManager;
    public EngineManager engineManager;
    public SceneManager sceneManager;
    public SpacecraftManager spacecraftManager;
    public spacecraftPathGen spacecraftPathGen;
    public showWarning showWarning;

    private const double G0 = 9.80665;

    // The blended dV/TOF cost sits at roughly 0..2, so any violated constraint (base 10) always loses
    // to a valid mission, while the scaled term lets a near-miss beat a far-off miss, which gives the
    // optimizer something to follow back into the valid region.
    private const double PenaltyBase = 10.0;
    private const double PenaltyScale = 100.0;
    private const double LambertFailureCost = 1e4;

    private PhysicsManager.physicsObject sunObj;
    private Dictionary<string, OrbitViewer.KeplerianElements> kepvalues;
    private Dictionary<string, double> bodyRadius;
    private Dictionary<string, double> bodyMu;

    // everything the cost function works out for one candidate, so the final result is built from
    // the exact same numbers the optimizer scored
    private class Evaluation
    {
        public double Cost;
        public bool LambertFailed;
        public bool Feasible;
        public double[] Epochs;
        public List<MissionLeg> Legs;
        public double[] NodeCosts;
        public double TotalDeltaV;
        public double TotalTOF;
        public double Margin;
    }

    public void Init()
    {
        sunObj = physicsManager.fullObjData[engineManager.bodylookup["SUN"]];
    }

    // bodies and types are parallel arrays, first must be Start, last must be End, anything between is a Flyby
    // e.g. bodies = ["EARTH", "VENUS", "JUPITER"], types = [Start, Flyby, End]
    // tofMaxDaysPerLeg applies to every leg, ~1500 covers an Earth -> Jupiter leg
    // pop = 0 picks a population size from the number of dimensions
    // the DE is run `restarts` times from different seeds and the best polished result is kept,
    // the same seed always gives the same answer, change it to explore a different set of searches
    public MissionResult PlanMission(
        string[] bodies, NodeType[] types, SpacecraftConfig config,
        double tofMinDays = 30, double tofMaxDaysPerLeg = 1500,
        int pop = 0, int gens = 300, int restarts = 8, int seed = 12345)
    {
        if (bodies.Length != types.Length)
        {
            showWarning.dispalyNotif(0, "Bodies and types must be the same length");
            return null;
        }
        if (bodies.Length < 2)
        {
            showWarning.dispalyNotif(0, "Need at least a Start and an End node");
            return null;
        }
        if (types[0] != NodeType.Start || types[types.Length - 1] != NodeType.End)
        {
            showWarning.dispalyNotif(0, "First node must be Start, last node must be End");
            return null;
        }

        Init(); // refresh the Sun in case the sim has moved on since startup

        var nodes = new List<MissionNode>(bodies.Length);
        for (int i = 0; i < bodies.Length; i++)
        {
            nodes.Add(new MissionNode(bodies[i], types[i]));
        }

        cacheBodyData(bodies);

        int numLegs = nodes.Count - 1;
        int dims = 1 + numLegs; // [t0, TOF_1, ..., TOF_n]

        double[] lower = new double[dims];
        double[] upper = new double[dims];
        lower[0] = config.SearchWindowStart;
        upper[0] = config.SearchWindowEnd;

        double tofMinSec = tofMinDays * 86400.0;
        double tofMaxSec = tofMaxDaysPerLeg * 86400.0;
        for (int i = 1; i < dims; i++)
        {
            lower[i] = tofMinSec;
            upper[i] = tofMaxSec;
        }

        // dV and TOF are different units, so each is scaled by the largest value it can reasonably take
        double dvScale = math.max(estimateExpectedMaxDeltaV(config), 1e-9);
        double tofScale = math.max(numLegs * tofMaxSec, 1e-9);

        Func<double[], double> costFn = p => evaluate(p, nodes, config, dvScale, tofScale).Cost;

        int popSize = pop > 0 ? pop : Math.Max(15 * dims, 40);
        restarts = Math.Max(1, restarts);

        double[] bestX = null;
        double bestCost = double.MaxValue;
        double[] restartCosts = new double[restarts];

        for (int r = 0; r < restarts; r++)
        {
            var rng = new System.Random(unchecked(seed + 7919 * r));
            var (rx, rc) = differentialEvolution(lower, upper, costFn, popSize, gens, rng);
            (rx, rc) = polish(rx, lower, upper, costFn, rc);

            restartCosts[r] = rc;
            if (rc < bestCost)
            {
                bestCost = rc;
                bestX = rx;
            }
        }

        Evaluation best = evaluate(bestX, nodes, config, dvScale, tofScale);
        if (best.LambertFailed)
        {
            showWarning.dispalyNotif(0, "No valid transfer found, try widening the search window or the time of flight");
            return null;
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            nodes[i].Epoch = best.Epochs[i];
        }

        return new MissionResult
        {
            Feasible = best.Feasible,
            TotalDeltaV = best.TotalDeltaV,
            TotalTOF = best.TotalTOF,
            MassMargin = best.Margin,
            BestCost = best.Cost,
            RestartCosts = restartCosts,
            Nodes = nodes,
            Legs = best.Legs
        };
    }

    // ephemeris

    private void cacheBodyData(string[] bodies)
    {
        kepvalues = new();
        bodyRadius = new();
        bodyMu = new();
        foreach (string b in bodies)
        {
            if (kepvalues.ContainsKey(b)) continue;

            int idx = engineManager.bodylookup[b];
            var obj = physicsManager.fullObjData[idx];
            kepvalues[b] = orbitViewer.calcKep(obj.pos, obj.vel, obj.mass, sunObj.mass);
            bodyMu[b] = obj.mass; // GM, not kg
            bodyRadius[b] = sceneManager.allBodys[idx].GetComponent<Body>().radius;
        }
    }

    private double3 getPos(string bodyName, double epoch)
    {
        return orbitViewer.getPositionAtTime(kepvalues[bodyName], epoch) - sunObj.pos;
    }

    private double3 getVel(string bodyName, double epoch)
    {
        return orbitViewer.getVelocityAtTime(kepvalues[bodyName], epoch);
    }

    // cost function, called by the optimizer tens of thousands of times per solve

    private Evaluation evaluate(double[] x, List<MissionNode> nodes, SpacecraftConfig config, double dvScale, double tofScale)
    {
        int n = nodes.Count;
        var ev = new Evaluation { Epochs = new double[n], Legs = new List<MissionLeg>(n - 1) };

        ev.Epochs[0] = x[0];
        for (int i = 1; i < n; i++)
        {
            ev.TotalTOF += x[i];
            ev.Epochs[i] = ev.Epochs[i - 1] + x[i];
        }

        for (int i = 1; i < n; i++)
        {
            double3 r1 = getPos(nodes[i - 1].BodyName, ev.Epochs[i - 1]);
            double3 r2 = getPos(nodes[i].BodyName, ev.Epochs[i]);
            double3 v1 = default, v2 = default;

            try
            {
                (v1, v2) = spacecraftPathGen.SolveLambert(r1, r2, x[i], sunObj.mass, "prograde");
            }
            catch
            {
                // degenerate / non-convergent Lambert case (e.g. ~180 degree transfer)
                ev.LambertFailed = true;
                ev.Cost = LambertFailureCost;
                return ev;
            }

            if (math.any(math.isnan(v1)) || math.any(math.isnan(v2)))
            {
                ev.LambertFailed = true;
                ev.Cost = LambertFailureCost;
                return ev;
            }

            ev.Legs.Add(new MissionLeg { NodeA = nodes[i - 1], NodeB = nodes[i], V_departure = v1, V_arrival = v2 });
        }

        ev.NodeCosts = nodeCostsArray(nodes, ev.Epochs, ev.Legs, config);
        foreach (double nc in ev.NodeCosts) ev.TotalDeltaV += nc;
        ev.Margin = massMargin(ev.NodeCosts, config);

        double penalty = 0;

        if (ev.Margin < 0)
        {
            penalty += PenaltyBase + PenaltyScale * (-ev.Margin) / math.max(config.WetMass, 1e-9);
        }

        if (config.MaxMissionDuration > 0 && ev.TotalTOF > config.MaxMissionDuration)
        {
            penalty += PenaltyBase + PenaltyScale * (ev.TotalTOF - config.MaxMissionDuration) / config.MaxMissionDuration;
        }

        penalty += periapsisPenalty(nodes, ev.Epochs, ev.Legs, config);

        ev.Feasible = penalty <= 0;

        double blended = config.ObjectiveWeight * (ev.TotalTOF / tofScale)
                       + (1 - config.ObjectiveWeight) * (ev.TotalDeltaV / dvScale);
        ev.Cost = blended + penalty;
        return ev;
    }

    // cost at every node = |departure velocity - arrival velocity| (heliocentric)
    // Start: arrival velocity is the departure body's own velocity
    // End: departure velocity is the arrival velocity (Flyby) or the target body's velocity (OrbitInsertion)
    // NOTE: flybys bill the full velocity change even though a real gravity assist gives some of the turn for free
    private double[] nodeCostsArray(List<MissionNode> nodes, double[] epochs, List<MissionLeg> legs, SpacecraftConfig config)
    {
        double[] costs = new double[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            double3 vArrive = i == 0
                ? getVel(nodes[0].BodyName, epochs[0])
                : legs[i - 1].V_arrival;

            double3 vDepart;
            if (i == nodes.Count - 1)
            {
                vDepart = config.ArrivalMode == ArrivalMode.Flyby
                    ? vArrive
                    : getVel(nodes[i].BodyName, epochs[i]);
            }
            else
            {
                vDepart = legs[i].V_departure;
            }

            costs[i] = math.length(vDepart - vArrive);
        }
        return costs;
    }

    // mass left over after every burn (rocket equation), minus the mass that has to stay (dry + payload)
    // positive = spare, negative = how many kg short the ship is
    private double massMargin(double[] nodeCosts, SpacecraftConfig config)
    {
        double ve = math.max(config.Isp * G0, 1e-9);
        double mass = config.WetMass;
        foreach (double dv in nodeCosts)
        {
            mass *= Math.Exp(-dv / ve);
        }
        return mass - (config.DryMass + config.PayloadMass);
    }

    private double periapsisPenalty(List<MissionNode> nodes, double[] epochs, List<MissionLeg> legs, SpacecraftConfig config)
    {
        double penalty = 0;
        for (int i = 1; i < nodes.Count - 1; i++) // only interior nodes have an incoming and outgoing leg
        {
            if (nodes[i].Type != NodeType.Flyby) continue;

            double3 vBody = getVel(nodes[i].BodyName, epochs[i]);
            double3 vInfIn = legs[i - 1].V_arrival - vBody;
            double3 vInfOut = legs[i].V_departure - vBody;
            double vInfInMag = math.length(vInfIn);
            double vInfOutMag = math.length(vInfOut);
            if (vInfInMag < 1e-9 || vInfOutMag < 1e-9) continue;

            double vInf = 0.5 * (vInfInMag + vInfOutMag); // equal for a purely unpowered flyby

            double dotP = math.clamp(math.dot(vInfIn, vInfOut) / (vInfInMag * vInfOutMag), -1.0, 1.0);
            double turnAngle = Math.Acos(dotP);
            double sinHalf = Math.Sin(turnAngle / 2.0);
            if (sinHalf < 1e-6) continue; // basically a straight line pass

            double impliedPeriapsis = (bodyMu[nodes[i].BodyName] / (vInf * vInf)) * (1.0 / sinHalf - 1.0);
            double minSafe = config.GetMinFlybyAltitude(nodes[i].BodyName) + bodyRadius[nodes[i].BodyName];

            if (impliedPeriapsis < minSafe)
            {
                penalty += PenaltyBase + PenaltyScale * (minSafe - impliedPeriapsis) / minSafe;
            }
        }
        return penalty;
    }

    // only used to scale the dV part of the cost, derived from the ship's full tank dV so the units always match
    private double estimateExpectedMaxDeltaV(SpacecraftConfig config)
    {
        double ve = config.Isp * G0;
        double minMass = math.max(config.DryMass + config.PayloadMass, 1e-6);
        return ve * Math.Log(config.WetMass / minMass);
    }

    // optimizers

    private (double[] bestX, double bestCost) differentialEvolution(
        double[] lower, double[] upper, Func<double[], double> costFn,
        int popSize, int generations, System.Random rng, double F = 0.8, double CR = 0.9)
    {
        popSize = Math.Max(popSize, 4);
        int dims = lower.Length;

        double[][] population = new double[popSize][];
        double[] costs = new double[popSize];
        for (int i = 0; i < popSize; i++)
        {
            population[i] = new double[dims];
            for (int d = 0; d < dims; d++)
            {
                population[i][d] = lower[d] + rng.NextDouble() * (upper[d] - lower[d]);
            }
            costs[i] = costFn(population[i]);
        }

        double[] trial = new double[dims];

        for (int gen = 0; gen < generations; gen++)
        {
            double currentF = F;
            if (gen > 0.8 * generations)
            {
                currentF = lerp(F, 0.3, (gen - 0.8 * generations) / (0.2 * generations));
            }

            for (int i = 0; i < popSize; i++)
            {
                int a, b, c;
                do { a = rng.Next(popSize); } while (a == i);
                do { b = rng.Next(popSize); } while (b == i || b == a);
                do { c = rng.Next(popSize); } while (c == i || c == a || c == b);

                int forcedDim = rng.Next(dims);
                for (int d = 0; d < dims; d++)
                {
                    trial[d] = (rng.NextDouble() < CR || d == forcedDim)
                        ? population[a][d] + currentF * (population[b][d] - population[c][d])
                        : population[i][d];
                    trial[d] = math.clamp(trial[d], lower[d], upper[d]);
                }

                double trialCost = costFn(trial);
                if (trialCost < costs[i])
                {
                    Array.Copy(trial, population[i], dims);
                    costs[i] = trialCost;
                }
            }
        }

        int best = 0;
        for (int i = 1; i < popSize; i++)
        {
            if (costs[i] < costs[best]) best = i;
        }
        return (population[best], costs[best]);
    }

    // shrinking-step coordinate search, squeezes the last bit out of a DE result so two restarts that
    // landed in the same basin end up on the same answer instead of "close but not equal"
    private (double[] x, double cost) polish(
        double[] start, double[] lower, double[] upper, Func<double[], double> costFn, double startCost)
    {
        double[] best = (double[])start.Clone();
        double[] trial = new double[best.Length];
        double cost = startCost;
        double scale = 0.01; // fraction of each dimension's range
        int guard = 0;

        while (scale > 1e-9 && guard++ < 5000)
        {
            bool improved = false;
            for (int d = 0; d < best.Length && !improved; d++)
            {
                for (int s = 0; s < 2; s++)
                {
                    double sign = s == 0 ? 1.0 : -1.0;
                    Array.Copy(best, trial, best.Length);
                    trial[d] = math.clamp(best[d] + sign * scale * (upper[d] - lower[d]), lower[d], upper[d]);

                    double c = costFn(trial);
                    if (c < cost)
                    {
                        Array.Copy(trial, best, best.Length);
                        cost = c;
                        improved = true;
                        break;
                    }
                }
            }

            if (!improved) scale *= 0.5;
        }

        return (best, cost);
    }

    private static double lerp(double a, double b, double t)
    {
        return a + (b - a) * math.clamp(t, 0.0, 1.0);
    }

    // spawns the craft at the Start node with the winning departure burn, then if ArrivalMode is
    // OrbitInsertion waits for the final epoch and commands the capture burn
    // flyby nodes get no burn, the IAS15 integrator handles the gravity assist as the craft passes each body
    public IEnumerator LaunchMissionCoroutine(MissionResult result, SpacecraftConfig config)
    {
        if (!result.Feasible)
        {
            Debug.LogWarning("MissionPlanner: launching a mission PlanMission flagged infeasible.");
        }

        MissionNode start = result.Nodes[0];
        MissionNode end = result.Nodes[result.Nodes.Count - 1];

        while (physicsManager.t < start.Epoch)
        {
            yield return null;
        }

        // taken after the wait so the craft spawns where the body actually is now, not where it was
        // at the planned epoch (the loop overshoots by up to a timestep, more under time warp)
        double now = physicsManager.t;
        double3 spawnPos = orbitViewer.getPositionAtTime(kepvalues[start.BodyName], now);
        double3 spawnVel = getVel(start.BodyName, now);

        var craft = physicsManager.addSpacecraft(spawnPos, spawnVel, config.WetMass * engineManager.g);
        physicsManager.commandBurn(craft, result.Legs[0].V_departure, 60.0);

        if (config.ArrivalMode == ArrivalMode.OrbitInsertion)
        {
            while (physicsManager.t < end.Epoch)
            {
                yield return null;
            }

            physicsManager.commandBurn(craft, getVel(end.BodyName, physicsManager.t), 60.0);
        }
    }
}