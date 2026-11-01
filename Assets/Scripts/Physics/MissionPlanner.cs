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

    // what the tank can supply (rocket equation, full tank down to dry + payload), m/s
    public double AvailableDeltaV;
    // dV billed at each node, same order as Nodes, m/s
    public double[] NodeDeltaV;
    // empty when Feasible, otherwise says which constraint failed and by how much
    public string FailureReason = "";

    // snapshot of the body data this mission was planned with, so planning a second mission
    // before the first one launches can't change what the first one's launch uses
    internal Dictionary<string, OrbitViewer.KeplerianElements> Kepler;
    internal Dictionary<string, double> BodyRadius;
    internal Dictionary<string, double> BodyMu;
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

    [Tooltip("true: launch, flybys and capture are instant velocity changes, the same model the planner and Lambert solver use. " +
             "false: launch and capture are 60 s finite burns through PhysicsManager.commandBurn, which currently makes the IAS15 step size collapse to ~1e-10 s and freezes the sim.")]
    public bool impulsiveBurns = true;

    private const double G0 = 9.80665;

    // The blended dV/TOF cost sits at roughly 0..2, so any violated constraint (base 10) always loses
    // to a valid mission, while the scaled term lets a near-miss beat a far-off miss, which gives the
    // optimizer something to follow back into the valid region.
    private const double PenaltyBase = 10.0;
    private const double PenaltyScale = 100.0;
    private const double LambertFailureCost = 1e4;

    // the craft is spawned this far above the departure body's surface and burns straight out to the
    // escape-adjusted speed, so the planner bills the Start node the same way
    private const double LaunchAltitude = 300_000;   // meters, always real metres

    // The sim itself runs in km, km/s and km^3/s^2 (Earth reads GM 3.986e5, radius 6371, speed ~30).
    // Everything the planner takes from the sim (positions, velocities, GM, radii) is in those units, and
    // everything that is physical-world constants (Isp, G0, LaunchAltitude, flyby altitudes) is in metres.
    // This is how many metres one sim length unit is, change it if the sim units ever change.
    private const double SimUnitMeters = 1000.0;
    private const double DepartureBurnSeconds = 60.0;
    private const double CaptureBurnSeconds = 60.0;

    // used instead of the tank dV when the ship has no fuel (or something degenerate), m/s
    private const double FallbackDvScale = 30_000;

    // how close to an epoch counts as "there", the integrator lands on stopTime to within float error
    private const double TimeTolerance = 1e-3;

    private PhysicsManager.physicsObject sunObj;
    private Dictionary<string, OrbitViewer.KeplerianElements> kepvalues;
    private Dictionary<string, double> bodyRadius;
    private Dictionary<string, double> bodyMu;

    // first failure SolveLambert reported during the current PlanMission call, so "no valid transfer"
    // can say why instead of just failing silently
    private string lastLambertError;

    // everything the cost function works out for one candidate, so the final result is built from
    // the exact same numbers the optimizer scored
    private class Evaluation
    {
        public double Cost;
        public bool LambertFailed;
        public bool Feasible;
        public bool MarginViolated;
        public bool DurationViolated;
        public bool PeriapsisViolated;
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
    //
    // returns null (after showing a warning) when the inputs were bad or no transfer could be solved,
    // otherwise a result whose Feasible / FailureReason say whether the ship can actually fly it
    public MissionResult PlanMission(
        string[] bodies, NodeType[] types, SpacecraftConfig config,
        double tofMinDays = 30, double tofMaxDaysPerLeg = 1500,
        int pop = 0, int gens = 300, int restarts = 8, int seed = 12345)
    {
        if (bodies == null || types == null || config == null)
        {
            showWarning.dispalyNotif(0, "Mission needs bodies, node types and a spacecraft config");
            return null;
        }
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
        for (int i = 1; i < types.Length - 1; i++)
        {
            if (types[i] != NodeType.Flyby)
            {
                showWarning.dispalyNotif(0, "Only the first node can be Start and only the last can be End");
                return null;
            }
        }

        // a name that isn't in bodylookup used to throw a KeyNotFoundException from deep inside
        // cacheBodyData, which looks exactly like "the planner gave me nothing"
        foreach (string b in bodies)
        {
            if (string.IsNullOrEmpty(b) || !engineManager.bodylookup.ContainsKey(b))
            {
                Debug.LogError($"[Mission] Unknown body '{b}'. Names must match the EngineManager.bodylookup keys exactly (upper case, e.g. EARTH).");
                showWarning.dispalyNotif(0, $"Unknown body '{b}'");
                return null;
            }
        }

        Init(); // refresh the Sun in case the sim has moved on since startup
        lastLambertError = null;

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

        // a launch in the past can't happen, the sim only moves forward
        lower[0] = math.max(config.SearchWindowStart, physicsManager.t);
        upper[0] = config.SearchWindowEnd;
        if (upper[0] <= lower[0])
        {
            Debug.LogWarning($"[Mission] Search window is empty: start (clamped to now) = {lower[0] / 86400:F1}d, end = {upper[0] / 86400:F1}d");
            showWarning.dispalyNotif(0, "Search window must end after it starts (and after the current sim time)");
            return null;
        }

        double tofMinSec = tofMinDays * 86400.0;
        double tofMaxSec = tofMaxDaysPerLeg * 86400.0;
        for (int i = 1; i < dims; i++)
        {
            lower[i] = tofMinSec;
            upper[i] = tofMaxSec;
        }

        // dV and TOF are different units, so each is scaled by the largest value it can reasonably take
        double available = estimateExpectedMaxDeltaV(config);
        double dvScale = available;
        if (!(dvScale > 100.0)) dvScale = FallbackDvScale; // no fuel / bad Isp, also catches NaN
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
            Debug.LogWarning($"[Mission] Every candidate failed in the Lambert solver. First error seen: {lastLambertError ?? "none thrown, the solver returned NaN/Inf"}");
            showWarning.dispalyNotif(0, "No valid transfer found, try widening the search window or the time of flight");
            return null;
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            nodes[i].Epoch = best.Epochs[i];
        }

        string reason = best.Feasible ? "" : buildFailureReason(best, config, available);

        var nodeLines = new List<string>();
        for (int i = 0; i < nodes.Count; i++)
        {
            nodeLines.Add($"{nodes[i].BodyName}({nodes[i].Type}) t={best.Epochs[i] / 86400:F1}d dV={best.NodeCosts[i]:F0}m/s");
        }
        double worstRestart = double.MinValue;
        foreach (double c in restartCosts) worstRestart = Math.Max(worstRestart, c);
        Debug.Log($"[Mission] Plan done: feasible={best.Feasible}, totalDV={best.TotalDeltaV:F0} m/s, tankDV={available:F0} m/s, " +
                  $"TOF={best.TotalTOF / 86400:F1}d, margin={best.Margin:F1} kg, bestCost={best.Cost:F4} (worst restart {worstRestart:F4})\n" +
                  string.Join("\n", nodeLines) +
                  (best.Feasible ? "" : $"\nINFEASIBLE: {reason}"));

        return new MissionResult
        {
            Feasible = best.Feasible,
            TotalDeltaV = best.TotalDeltaV,
            TotalTOF = best.TotalTOF,
            MassMargin = best.Margin,
            BestCost = best.Cost,
            RestartCosts = restartCosts,
            AvailableDeltaV = available,
            NodeDeltaV = best.NodeCosts,
            FailureReason = reason,
            Nodes = nodes,
            Legs = best.Legs,
            Kepler = kepvalues,
            BodyRadius = bodyRadius,
            BodyMu = bodyMu
        };
    }

    private string buildFailureReason(Evaluation ev, SpacecraftConfig config, double available)
    {
        var parts = new List<string>();
        if (ev.MarginViolated)
        {
            parts.Add($"needs {ev.TotalDeltaV:F0} m/s of delta-v but the tank only holds {available:F0} m/s " +
                      $"(short by {-ev.Margin:F0} kg of fuel). Add fuel, raise Isp, or pick an easier route");
        }
        if (ev.DurationViolated)
        {
            parts.Add($"mission takes {ev.TotalTOF / 86400:F0} days, over the {config.MaxMissionDuration / 86400:F0} day limit");
        }
        if (ev.PeriapsisViolated)
        {
            parts.Add("a flyby would have to pass below the minimum safe altitude");
        }
        return string.Join("; ", parts);
    }

    // ephemeris

    // always builds fresh dictionaries (never clears the old ones) so a MissionResult that kept a
    // reference to the previous set is unaffected
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
            bodyRadius[b] = sceneManager.allBodys[idx].GetComponent<Body>().radius; // sim length units (km)

            // quick unit check, in km Earth should read GM ~ 3.986e5 and radius ~ 6371
            Debug.Log($"[Mission] {b}: GM = {bodyMu[b]:E3} (sim units), radius = {bodyRadius[b]:E3} (sim units)");
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
            double3 v1, v2;
            string lambertError;
            bool solved;

            // TrySolveLambert is the silent version of SolveLambert. The public SolveLambert pops a UI warning
            // on every failure, and the optimizer hits failures thousands of times per search (a leg whose
            // time of flight is too short for its distance is simply unsolvable), which flooded the UI and
            // froze the game on multi-leg missions.
            try
            {
                solved = spacecraftPathGen.TrySolveLambert(r1, r2, x[i], sunObj.mass, "prograde", out v1, out v2, out lambertError);
            }
            catch (Exception e)
            {
                solved = false;
                v1 = default;
                v2 = default;
                lambertError = e.GetType().Name + ": " + e.Message;
            }

            if (!solved || !math.all(math.isfinite(v1)) || !math.all(math.isfinite(v2)))
            {
                // degenerate / non-convergent Lambert case (e.g. ~180 degree transfer), remember the
                // first message so a total failure can be explained
                if (lastLambertError == null) lastLambertError = lambertError ?? "solver returned NaN/Inf";
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
            ev.MarginViolated = true;
            penalty += PenaltyBase + PenaltyScale * (-ev.Margin) / math.max(config.WetMass, 1e-9);
        }

        if (config.MaxMissionDuration > 0 && ev.TotalTOF > config.MaxMissionDuration)
        {
            ev.DurationViolated = true;
            penalty += PenaltyBase + PenaltyScale * (ev.TotalTOF - config.MaxMissionDuration) / config.MaxMissionDuration;
        }

        double periPenalty = periapsisPenalty(nodes, ev.Epochs, ev.Legs, config);
        if (periPenalty > 0) ev.PeriapsisViolated = true;
        penalty += periPenalty;

        ev.Feasible = penalty <= 0;

        double blended = config.ObjectiveWeight * (ev.TotalTOF / tofScale)
                       + (1 - config.ObjectiveWeight) * (ev.TotalDeltaV / dvScale);
        ev.Cost = blended + penalty;

        // a NaN cost never compares as "better" or "worse", so one NaN in the population would get stuck
        // there forever, treat it like any other failed candidate
        if (double.IsNaN(ev.Cost) || double.IsInfinity(ev.Cost))
        {
            ev.LambertFailed = true;
            ev.Cost = LambertFailureCost;
        }
        return ev;
    }

    // dV billed at every node
    // Start:  the burn the launch actually performs, from rest next to the body (LaunchAltitude above the
    //         surface) out to the escape-adjusted speed, sqrt(vInf^2 + 2mu/r), plus an estimate of the
    //         gravity loss over the DepartureBurnSeconds the burn takes (the launch itself solves this
    //         properly, see solveRadialBurnSpeed, this is the cheap version so the optimizer can use it)
    // Flyby:  |vInfOut| - |vInfIn|, a gravity assist rotates v-infinity for free but can't change its length,
    //         so only the magnitude mismatch costs fuel. How much turning is possible is limited by
    //         periapsisPenalty
    // End:    the heliocentric velocity change to the target body's velocity (OrbitInsertion),
    //         or nothing (Flyby arrival mode)
    private double[] nodeCostsArray(List<MissionNode> nodes, double[] epochs, List<MissionLeg> legs, SpacecraftConfig config)
    {
        int last = nodes.Count - 1;
        double[] costs = new double[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            string body = nodes[i].BodyName;

            if (i == 0)
            {
                double vInf = math.length(legs[0].V_departure - getVel(body, epochs[0]));
                double r = bodyRadius[body] + LaunchAltitude / SimUnitMeters; // sim units
                double mu = bodyMu[body];
                double ideal = Math.Sqrt(vInf * vInf + 2.0 * mu / r);
                // the craft spends the whole burn fighting gravity, about half of g * burn time is lost
                // (an impulsive launch has none)
                double gravityLoss = impulsiveBurns ? 0.0 : 0.5 * mu / (r * r) * DepartureBurnSeconds;
                costs[0] = ideal + gravityLoss; // still sim units, converted to m/s at the bottom
                continue;
            }

            if (i < last && nodes[i].Type == NodeType.Flyby)
            {
                double3 vBody = getVel(body, epochs[i]);
                double vInfIn = math.length(legs[i - 1].V_arrival - vBody);
                double vInfOut = math.length(legs[i].V_departure - vBody);
                costs[i] = Math.Abs(vInfOut - vInfIn);
                continue;
            }

            double3 vArrive = legs[i - 1].V_arrival;
            double3 vDepart;
            if (i == last)
            {
                vDepart = config.ArrivalMode == ArrivalMode.Flyby
                    ? vArrive
                    : getVel(body, epochs[i]);
            }
            else
            {
                vDepart = legs[i].V_departure;
            }

            costs[i] = math.length(vDepart - vArrive);
        }

        // everything above is in sim units (km/s), the rocket equation, Isp and the tank dV are in m/s
        for (int i = 0; i < costs.Length; i++) costs[i] *= SimUnitMeters;
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
            double minSafe = config.GetMinFlybyAltitude(nodes[i].BodyName) / SimUnitMeters + bodyRadius[nodes[i].BodyName]; // sim units

            if (impliedPeriapsis < minSafe)
            {
                penalty += PenaltyBase + PenaltyScale * (minSafe - impliedPeriapsis) / minSafe;
            }
        }
        return penalty;
    }

    // only used to scale the dV part of the cost (and to report what the tank holds),
    // derived from the ship's full tank dV so the units always match
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

    // launch burn maths

    // The burn isn't instant: for the whole DepartureBurnSeconds the body's gravity is pulling the craft
    // back, so commanding exactly sqrt(vInf^2 + 2mu/r) leaves it well short of the planned v-infinity
    // (an Earth departure planned at 3 km/s only left at about 1.7 km/s). This integrates the radial burn
    // with the same smootherstep thrust profile the IAS15 job uses and bisects on the commanded speed
    // until the craft leaves the burn with exactly the planned excess energy, vInf^2 / 2.
    private static double solveRadialBurnSpeed(double vInf, double mu, double r0, double duration)
    {
        double ideal = Math.Sqrt(vInf * vInf + 2.0 * mu / r0);
        double target = 0.5 * vInf * vInf;

        double lo = ideal; // always short, gravity only ever takes energy away
        double hi = ideal + 2.0 * (mu / (r0 * r0)) * duration + 100.0 / SimUnitMeters;

        if (radialBurnEnergy(lo, mu, r0, duration) >= target) return lo;
        if (radialBurnEnergy(hi, mu, r0, duration) < target) return hi;

        for (int i = 0; i < 60; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (radialBurnEnergy(mid, mu, r0, duration) < target) lo = mid;
            else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    // specific orbital energy (v^2/2 - mu/r) at the end of a radial burn of `cmd` m/s starting at rest at r0
    private static double radialBurnEnergy(double cmd, double mu, double r0, double duration)
    {
        const int steps = 240;
        double dt = duration / steps;
        double r = r0, v = 0.0, t = 0.0;

        for (int i = 0; i < steps; i++)
        {
            double k1r = v;
            double k1v = radialBurnAcc(cmd, mu, r, t, duration);
            double k2r = v + 0.5 * dt * k1v;
            double k2v = radialBurnAcc(cmd, mu, r + 0.5 * dt * k1r, t + 0.5 * dt, duration);
            double k3r = v + 0.5 * dt * k2v;
            double k3v = radialBurnAcc(cmd, mu, r + 0.5 * dt * k2r, t + 0.5 * dt, duration);
            double k4r = v + dt * k3v;
            double k4v = radialBurnAcc(cmd, mu, r + dt * k3r, t + dt, duration);

            r += dt / 6.0 * (k1r + 2.0 * k2r + 2.0 * k3r + k4r);
            v += dt / 6.0 * (k1v + 2.0 * k2v + 2.0 * k3v + k4v);
            t += dt;
        }
        return 0.5 * v * v - mu / r;
    }

    private static double radialBurnAcc(double cmd, double mu, double r, double t, double duration)
    {
        double s = Math.Min(Math.Max(t / duration, 0.0), 1.0);
        double q = 1.0 - s;
        double thrust = cmd * 30.0 * s * s * q * q / duration; // same profile as IAS15Job.ThrustAcc
        return thrust - mu / (r * r);
    }

    // debug: logs the craft's speed (sim units, km/s) straight from the physics arrays for a few frames after launch
    private IEnumerator logCraftSpeed(PhysicsManager.physicsObject craft, string bodyName, int frames)
    {
        var origin = physicsManager.fullObjData[engineManager.bodylookup[bodyName]];
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[Mission] speed log, {frames} frames after launch (stepsPerUpdate={physicsManager.stepsPerUpdate}, maxHChange={physicsManager.maxHChange}, maxH={physicsManager.maxH}):");
        double t0 = physicsManager.t;
        for (int i = 0; i < frames; i++)
        {
            yield return null;
            double3 relPos = craft.pos - origin.pos;
            double3 relVel = craft.vel - origin.vel;
            double dist = math.length(relPos);
            double radialSpeed = math.dot(relPos, relVel) / math.max(dist, 1e-9); // negative = falling toward the body
            sb.AppendLine($"f{i}: simT+{physicsManager.t - t0:F3}s h={physicsManager.h:E2} steps={physicsManager.lastStepsDone} iter={physicsManager.lastIterations} " +
                          $"thrusting={craft.isThrusting} dist={dist:F1} km radialSpeed={radialSpeed:F4} km/s relToBody={math.length(relVel):F4} km/s");
        }
        Debug.Log(sb.ToString()); // one message so it can be copied in one go
    }

    // puts the craft in the physics arrays, reusing the existing one if a mission already created it.
    // Uses resetBody so every bit of leftover integrator state (compensation term, polynomial coefficients,
    // any burn in progress) from wherever the craft was before is cleared, assigning craft.pos / craft.vel
    // directly leaves that stale state behind.
    private PhysicsManager.physicsObject placeCraft(double3 pos, double3 vel, double gm)
    {
        if (physicsManager.spacecraftID >= 0)
        {
            var craft = physicsManager.fullObjData[physicsManager.spacecraftID];
            craft.mass = gm;
            physicsManager.resetBody(craft, pos, vel);
            return craft;
        }
        return physicsManager.addSpacecraft(pos, vel, gm);
    }

    // After the craft is teleported next to a body the integrator's step size is whatever it happened to be
    // when the previous step landed on stopTime, which can be anywhere from ~1 ms up to maxH. The craft now
    // moves on a timescale of r / speed, so a big leftover step would skip straight through it with a
    // zeroed polynomial. Shrink h to a small fraction of that timescale, the integrator grows it back by itself.
    private void settleStepSize(double radius, double speed)
    {
        double timescale = radius / math.max(speed, 1e-9);
        double cap = math.max(timescale / 20.0, physicsManager.minH * 10.0);
        if (physicsManager.h > cap) physicsManager.h = cap;
    }

    // waits for the Start epoch, spawns the craft just above the departure body (on the integrator's own
    // copy of the body, not the Kepler one) and burns it radially out so it leaves with the planned
    // v-infinity. Every flyby node does the same thing again: the craft is moved next to the flyby body
    // and sent radially out along the planned outgoing v-infinity direction. If ArrivalMode is
    // OrbitInsertion it then waits for the final epoch and captures the craft into a low orbit.
    // physicsManager.stopTime makes the integrator land exactly on each epoch so no burn is ever stepped over
    public IEnumerator LaunchMissionCoroutine(MissionResult result, SpacecraftConfig config)
    {
        if (result == null || result.Nodes == null || result.Nodes.Count < 2 || result.Legs == null || result.Legs.Count == 0)
        {
            Debug.LogError("[Mission] LaunchMissionCoroutine got an empty mission (no nodes / legs), nothing to launch.");
            yield break;
        }

        if (!result.Feasible)
        {
            Debug.LogWarning("[Mission] Launching a mission PlanMission flagged infeasible: " + result.FailureReason);
        }

        MissionNode start = result.Nodes[0];
        MissionNode end = result.Nodes[result.Nodes.Count - 1];

        // the sim has to be running or t never reaches the epoch and this loop waits forever
        if (physicsManager.paused)
        {
            Debug.LogWarning("[Mission] Physics was paused, resuming it so the mission can run.");
            physicsManager.paused = false;
        }

        physicsManager.stopTime = start.Epoch;
        while (physicsManager.t < start.Epoch - TimeTolerance)
        {
            yield return null;
        }
        physicsManager.stopTime = double.MaxValue;

        double now = physicsManager.t;
        int startIdx = engineManager.bodylookup[start.BodyName];
        var body = physicsManager.fullObjData[startIdx];

        // the planned departure is relative to where the Kepler model says the body is moving
        double3 keplerVel = orbitViewer.getVelocityAtTime(result.Kepler[start.BodyName], now);
        double3 vInf = result.Legs[0].V_departure - keplerVel;
        double vInfMag = math.length(vInf);
        double3 dir = vInfMag > 1e-9 ? vInf / vInfMag : new double3(1, 0, 0);

        double r = result.BodyRadius[start.BodyName] + LaunchAltitude / SimUnitMeters; // sim units (km)
        double mu = result.BodyMu[start.BodyName];

        // a radial escape trajectory has its asymptote along dir, so no extra aiming is needed.
        // the commanded speed is bumped up to cover the gravity loss during the burn
        double3 spawnPos = body.pos + dir * r;
        double burnSpeed;
        PhysicsManager.physicsObject craft;
        double craftGM = config.WetMass * engineManager.g;

        if (impulsiveBurns)
        {
            // instant burn: the craft simply starts with the escape-adjusted velocity, no gravity loss to
            // make up because there is no burn time. Radial escape from r with this speed leaves with vInf.
            burnSpeed = Math.Sqrt(vInfMag * vInfMag + 2.0 * mu / r);
            craft = placeCraft(spawnPos, body.vel + dir * burnSpeed, craftGM);
            settleStepSize(r, burnSpeed);
        }
        else
        {
            burnSpeed = solveRadialBurnSpeed(vInfMag, mu, r, DepartureBurnSeconds);
            craft = placeCraft(spawnPos, body.vel + dir * burnSpeed, craftGM);
            physicsManager.commandBurn(craft, body.vel + dir * burnSpeed, DepartureBurnSeconds);
        }

        StartCoroutine(logCraftSpeed(craft, start.BodyName, 20));

        Debug.Log($"[Mission] Launched from {start.BodyName} at t={now / 86400:F2}d: planned vInf = {vInfMag * SimUnitMeters:F0} m/s, " +
                  $"burn dV = {burnSpeed * SimUnitMeters:F0} m/s over {DepartureBurnSeconds:F0}s, craft id = {craft.ID}, thrusting = {craft.isThrusting}");

        for (int i = 1; i < result.Nodes.Count; i++)
        {
            MissionNode node = result.Nodes[i];

            physicsManager.stopTime = node.Epoch;
            while (physicsManager.t < node.Epoch - TimeTolerance) yield return null;
            physicsManager.stopTime = double.MaxValue;

            var nodeBody = physicsManager.fullObjData[engineManager.bodylookup[node.BodyName]];
            Debug.Log($"[Mission] Reached {node.BodyName} ({node.Type}) at t={physicsManager.t / 86400:F2}d, " +
                      $"miss distance {math.length(craft.pos - nodeBody.pos):F0} km");

            if (node.Type == NodeType.Flyby)
            {
                // The planner treats a flyby as a patched-conic event at the body's centre: the craft
                // arrives with vInfIn and leaves with vInfOut, nothing else changes. The real N-body craft
                // never matches that. It has been pulled around by the body for days, so it is some distance
                // off the planned point, and the next leg (solved from the body's centre) would start from
                // the wrong place.
                //
                // Just rewriting the velocity to body.vel + vInfOut and leaving the craft where it is does not
                // work either: the craft is deep inside the body's gravity well (escape speed there is far
                // above a few km/s of v-infinity), so that velocity is *bound* and the craft ends up
                // orbiting the flyby body forever instead of continuing to the next node.
                //
                // So do exactly what the launch does: put the craft just above the body (at the minimum safe
                // flyby radius) and send it radially out along the planned outgoing v-infinity direction at
                // sqrt(vInfOut^2 + 2mu/r). Gravity takes the 2mu/r back on the way out, and it leaves
                // the sphere of influence with exactly vInfOut along dirOut.
                double flyMu = result.BodyMu[node.BodyName];
                double flyR = result.BodyRadius[node.BodyName] + config.GetMinFlybyAltitude(node.BodyName) / SimUnitMeters;

                double3 vInfOutVec = result.Legs[i].V_departure
                                   - orbitViewer.getVelocityAtTime(result.Kepler[node.BodyName], node.Epoch);
                double vInfOut = math.length(vInfOutVec);
                double3 dirOut = vInfOut > 1e-9 ? vInfOutVec / vInfOut : new double3(1, 0, 0);
                double flySpeed = Math.Sqrt(vInfOut * vInfOut + 2.0 * flyMu / flyR);

                physicsManager.resetBody(craft, nodeBody.pos + dirOut * flyR, nodeBody.vel + dirOut * flySpeed);
                settleStepSize(flyR, flySpeed);

                Debug.Log($"[Mission] Flyby at {node.BodyName}: leaving along planned direction with vInf = {vInfOut * SimUnitMeters:F0} m/s " +
                          $"(start speed {flySpeed * SimUnitMeters:F0} m/s at r = {flyR:F0} km)");
            }
            else if (config.ArrivalMode == ArrivalMode.OrbitInsertion)
            {
                physicsManager.stopTime = end.Epoch;
                while (physicsManager.t < end.Epoch - TimeTolerance)
                {
                    yield return null;
                }
                physicsManager.stopTime = double.MaxValue;

                // integrator's own velocity for the target, same frame the craft lives in
                var endBody = physicsManager.fullObjData[engineManager.bodylookup[end.BodyName]];
                if (impulsiveBurns)
                {
                    double muu = result.BodyMu[end.BodyName];
                    double rOrbit = result.BodyRadius[end.BodyName] + LaunchAltitude / SimUnitMeters;

                    double3 rel = craft.pos - endBody.pos;
                    double3 rHat = math.lengthsq(rel) > 1e-12 ? math.normalize(rel) : new double3(1, 0, 0);
                    double3 hVec = math.cross(rHat, craft.vel - endBody.vel);
                    double3 hHat = math.lengthsq(hVec) > 1e-18 ? math.normalize(hVec) : new double3(0, 0, 1);
                    double3 tHat = math.cross(hHat, rHat);
                    double vCirc = Math.Sqrt(muu / rOrbit);

                    physicsManager.resetBody(craft, endBody.pos + rHat * rOrbit, endBody.vel + tHat * vCirc);
                    settleStepSize(rOrbit, vCirc);
                }
                else physicsManager.commandBurn(craft, endBody.vel, CaptureBurnSeconds);
                Debug.Log($"[Mission] Arrived at {end.BodyName} at t={physicsManager.t / 86400:F2}d, capture burn commanded.");
            }
        }
    }
}