using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using static TreeEditor.TreeEditorHelper;

// ---------------------------------------------------------------------
// Generalizes spacecraftPathGen's single-leg SolveSingleLeg into an N-node
// route: pass a list of bodies + node types, get back the DE-optimized
// departure date, per-leg TOFs, total dV, and a feasibility verdict.
//
//   var result = missionPlanner.PlanMission(
//       new[] { "EARTH", "VENUS", "JUPITER" },
//       new[] { NodeType.Start, NodeType.Flyby, NodeType.End },
//       config);
//
// Wire the three fields below in the inspector (same objects your
// spacecraftPathGen/SpacecraftManager already reference).
// ---------------------------------------------------------------------

public class MissionPlanner : MonoBehaviour
{
    public OrbitViewer orbitViewer;
    public PhysicsManager physicsManager;
    public EngineManager engineManager;
    public SceneManager sceneManager;

    private const double G0 = 9.80665; // standard gravity (m/s^2) - a fixed constant, not any body's local gravity.
                                       // NOTE: this assumes SI units (Isp in seconds, positions/velocities in
                                       // meters & m/s). Your existing Lambert test comment ("expect roughly 2-4,
                                       // in whatever unit your mu/positions use") suggests your sim might run on
                                       // a non-SI unit system - if so, either convert Isp/G0 to match, or feed
                                       // DryMass/FuelMass/PayloadMass in units consistent with that system.

    private PhysicsManager.physicsObject sunObj;
    private Dictionary<string, OrbitViewer.KeplerianElements> kepCache;
    private double[] _epochScratch; // reused across DE evaluations within one PlanMission call - avoids
                                    // reallocating a small array on every one of the ~thousands of cost evals

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
    public MissionResult PlanMission(
        string[] bodies,
        NodeType[] types,
        SpacecraftConfig config,
        double tofMinDays = 30,
        double tofMaxDaysPerLeg = 1500,
        int pop = 40,
        int gens = 200)
    {
        if (bodies.Length != types.Length)
            throw new ArgumentException("bodies and types must be the same length");
        if (bodies.Length < 2)
            throw new ArgumentException("need at least a Start and an End node");
        if (types[0] != NodeType.Start || types[types.Length - 1] != NodeType.End)
            throw new ArgumentException("first node must be Start, last node must be End");

        if (sunObj == null) Init();

        var nodes = new List<MissionNode>(bodies.Length);
        for (int i = 0; i < bodies.Length; i++)
            nodes.Add(new MissionNode(bodies[i], types[i]));

        BuildKepCache(bodies);
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

    private void BuildKepCache(string[] bodies)
    {
        kepCache = new Dictionary<string, OrbitViewer.KeplerianElements>();
        foreach (string b in bodies)
        {
            if (kepCache.ContainsKey(b)) continue;
            var obj = physicsManager.fullObjData[engineManager.bodylookup[b]];
            kepCache[b] = orbitViewer.calcKep(obj.pos, obj.vel, obj.mass, sunObj.mass);
            // NOTE: you mentioned elements for all ~100 bodies are already precomputed relative to the
            // Sun - if OrbitViewer exposes a direct lookup for that, swap it in here instead of
            // recalculating from the live physicsObject state (matches what spacecraftPathGen.getVel
            // already does today, so this is at parity, not a regression - just a possible later win).
        }
    }

    private double3 GetPos(string bodyName, double epoch) =>
        orbitViewer.getPositionAtTime(kepCache[bodyName], epoch) - sunObj.pos;

    private double3 GetVel(string bodyName, double epoch) =>
        orbitViewer.getVelocityAtTime(kepCache[bodyName], epoch);

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
}