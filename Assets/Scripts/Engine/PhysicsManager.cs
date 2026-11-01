using MathNet.Numerics.LinearAlgebra.Double;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// ---------------------------------------------------------------------------------------------
//  IAS15 integrator, Burst edition.
//
//  Layout:
//    * BurnState        - blittable per-body burn data (replaces the burn* fields on the old class)
//    * IAS15Job         - the whole integrator step, compiled by Burst. Runs on flat NativeArrays.
//    * PhysicsManager   - MonoBehaviour that owns the NativeArrays and calls the job.
//                         physicsObject is now a thin handle that reads/writes those arrays, so
//                         code like  fullObjData[i].pos  still works.
//
//  IMPORTANT: FloatMode must stay Strict. The position update uses compensated summation
//  (posLo = y - (newPos - pos)) and FloatMode.Fast would let the compiler "simplify" it away.
// ---------------------------------------------------------------------------------------------

public struct BurnState
{
    public double3 startVel;
    public double3 targetVel;
    public double startTime;
    public double duration;
    public int active;
}

[BurstCompile(FloatPrecision.High, FloatMode.Strict, CompileSynchronously = true)]
public struct IAS15Job : IJob
{
    const double Tiny = 1e-300;

    // closer than this to a burn's end counts as finished, has to be above the float spacing of t
    // (about 1e-7 s at t ~ 1e9 s) or a step that lands "exactly" on the end could miss it
    const double BurnEndTolerance = 1e-6;

    // indices into `stats`
    public const int StatIter = 0;      // iterations used by the last step
    public const int StatBad = 1;       // index of first body with NaN/Inf acceleration, or -1
    public const int StatCloseI = 2;    // closest pair seen this call
    public const int StatCloseJ = 3;
    public const int StatSteps = 4;     // steps actually completed this call
    public const int StatWeirdH = 5;    // number of times the step-size proposal was NaN/Inf/<=0

    public int n;
    public int steps;
    public int maxIterations;
    public int refreshAcc;
    public double targetAcc, convergenceTol, safetyF, minH, maxH, minHChange, maxHChange;

    // sim time to land on exactly, no step is taken past it, double.MaxValue = no stop
    public double stopTime;

    [ReadOnly] public NativeArray<double> ratios;   // 7 Gauss-Radau nodes
    [ReadOnly] public NativeArray<double> invV;     // inverse Vandermonde, 7x7 row-major
    [ReadOnly] public NativeArray<double> binom;    // Pascal triangle, 8x8 row-major
    [ReadOnly] public NativeArray<double> mass;     // GM

    public NativeArray<double3> pos, posLo, vel, a0;
    public NativeArray<double3> poly;       // [i*7 + k]   per-body polynomial coefficients
    public NativeArray<double3> stageOff;   // [k*n + i]   posLo + predicted displacement at node k
    public NativeArray<double3> sampled;    // [k*n + i]   acceleration sampled at node k
    public NativeArray<double3> accBuf;     // [i]
    public NativeArray<double3> polyTmp;    // [7]
    public NativeArray<BurnState> burn;
    public NativeArray<double> clock;       // [0]=t  [1]=h  [2]=closest pair dist^2
    public NativeArray<int> stats;

    public void Execute()
    {
        double t = clock[0];
        double h = clock[1];
        clock[2] = double.MaxValue;
        stats[StatBad] = -1;
        stats[StatCloseI] = -1;
        stats[StatCloseJ] = -1;
        stats[StatSteps] = 0;
        stats[StatWeirdH] = 0;

        // a0 is already valid from the end of the previous step, only recompute if something
        // outside changed the state (init, addSpacecraft, commandBurn, external pos/vel writes).
        if (refreshAcc != 0) ComputeAccFromPos(t);

        // stops once stopTime is reached so a multi-step call can't run past it
        for (int s = 0; s < steps && stats[StatBad] < 0 && t < stopTime - 1e-3; s++)
        {
            Step(ref t, ref h);
            stats[StatSteps] = s + 1;
        }

        clock[0] = t;
        clock[1] = h;
    }

    void Step(ref double t, ref double h)
    {
        // The thrust is only sampled at the 7 nodes inside a step, so a step much longer than a burn can
        // miss it completely (every node lands after the burn ends). Keep h small enough to resolve any
        // active burn, never let a step cross a burn's end, and land exactly on stopTime.
        double hCap = h;
        for (int i = 0; i < n; i++)
        {
            BurnState b = burn[i];
            if (b.active == 0) continue;

            hCap = math.min(hCap, b.duration / 20.0);
            double remaining = b.startTime + b.duration - t;
            if (remaining > BurnEndTolerance) hCap = math.min(hCap, remaining);
        }
        hCap = math.min(hCap, stopTime - t);

        if (hCap < h)
        {
            double q0 = hCap / h;
            for (int i = 0; i < n; i++) RescalePoly(i, q0);
            h = hCap;
        }

        // a0 doesn't change during the iterations, so this only needs doing once per step
        double maxAcc = Tiny;
        for (int i = 0; i < n; i++) maxAcc = math.max(maxAcc, math.cmax(math.abs(a0[i])));

        bool converged = false;
        int iterations = 0;
        while (!converged && iterations < maxIterations)
        {
            iterations++;

            // predicted displacement at each node (posLo folded in, see ComputeSampledAcc)
            for (int k = 0; k < 7; k++)
            {
                double s = ratios[k];
                double tau = s * h;
                int o = k * n;
                for (int i = 0; i < n; i++)
                    stageOff[o + i] = posLo[i] + PredictDisp(i, s, tau, h);
            }

            for (int k = 0; k < 7; k++) ComputeSampledAcc(k, t + ratios[k] * h);

            double maxChange = 0.0;
            for (int i = 0; i < n; i++)
            {
                double3 oldB6 = poly[i * 7 + 6];
                SolveCo(i);
                maxChange = math.max(maxChange, math.cmax(math.abs(poly[i * 7 + 6] - oldB6)));
            }

            if (math.isnan(maxChange)) break;
            if (maxChange / maxAcc < convergenceTol) converged = true;
        }
        stats[StatIter] = iterations;

        // error estimate -> proposed next step
        double maxRError = 0.0;
        for (int i = 0; i < n; i++)
        {
            double b6 = math.cmax(math.abs(poly[i * 7 + 6]));
            double am = math.max(math.cmax(math.abs(a0[i])), Tiny);
            maxRError = math.max(maxRError, b6 / am);
        }

        double newHProp;
        if (maxRError <= 0.0) newHProp = h * maxHChange;
        else newHProp = h * safetyF * math.pow(targetAcc / maxRError, 1.0 / 7.0);

        if (!math.isfinite(newHProp) || newHProp <= 0.0)
        {
            stats[StatWeirdH] = stats[StatWeirdH] + 1;
            newHProp = h * 0.5;
        }

        double newT = t + h;

        // advance state with compensated summation
        for (int i = 0; i < n; i++)
        {
            double3 y = PredictDisp(i, 1.0, h, h) + posLo[i];
            double3 p = pos[i];
            double3 newPos = p + y;
            posLo[i] = y - (newPos - p);
            pos[i] = newPos;
            vel[i] = PredictVel(i, 1.0, h, h);
        }

        ComputeAccFromPos(newT);

        for (int i = 0; i < n; i++)
        {
            BurnState b = burn[i];
            if (b.active != 0 && newT >= b.startTime + b.duration - BurnEndTolerance)
            {
                b.active = 0;
                burn[i] = b;
            }
        }

        double hOld = h;
        double hNew = math.clamp(newHProp, hOld * minHChange, hOld * maxHChange);
        hNew = math.min(math.max(hNew, minH), maxH);
        double q = hNew / hOld;
        for (int i = 0; i < n; i++) RescalePoly(i, q);

        t = newT;
        h = hNew;
    }

    // ---- accelerations ------------------------------------------------------------------

    void ComputeAccFromPos(double time)
    {
        for (int k = 0; k < n; k++) accBuf[k] = new double3(0, 0, 0);

        double minD2 = clock[2];
        int ci = stats[StatCloseI];
        int cj = stats[StatCloseJ];

        for (int i = 0; i < n; i++)
        {
            double3 pi = pos[i];
            double3 li = posLo[i];
            double mi = mass[i];
            double3 ai = accBuf[i];
            for (int j = i + 1; j < n; j++)
            {
                double3 diff = (pos[j] - pi) + (posLo[j] - li);
                double dist2 = math.lengthsq(diff);
                if (dist2 < minD2) { minD2 = dist2; ci = i; cj = j; }
                double dist3 = math.max(dist2 * math.sqrt(dist2), Tiny);
                double3 f = diff / dist3;
                ai += mass[j] * f;
                accBuf[j] -= mi * f;
            }
            accBuf[i] = ai;
        }

        clock[2] = minD2;
        stats[StatCloseI] = ci;
        stats[StatCloseJ] = cj;

        for (int i = 0; i < n; i++) a0[i] = accBuf[i] + ThrustAcc(i, time);

        for (int i = 0; i < n; i++)
        {
            if (IsBad(a0[i]) && stats[StatBad] < 0) stats[StatBad] = i;
        }
    }

    // The hot loop: called 7 x iterations x steps. Reads only flat arrays, no pointer chasing.
    // stageOff already contains posLo + predictedDisp so large positions are subtracted first
    // (exactly) and the small corrections are added after, same idea as the original.
    void ComputeSampledAcc(int stage, double time)
    {
        int o = stage * n;
        for (int k = 0; k < n; k++) accBuf[k] = new double3(0, 0, 0);

        for (int i = 0; i < n; i++)
        {
            double3 pi = pos[i];
            double3 oi = stageOff[o + i];
            double mi = mass[i];
            double3 ai = accBuf[i];
            for (int j = i + 1; j < n; j++)
            {
                double3 diff = (pos[j] - pi) + (stageOff[o + j] - oi);
                double dist2 = math.lengthsq(diff);
                double dist3 = math.max(dist2 * math.sqrt(dist2), Tiny);
                double3 f = diff / dist3;
                ai += mass[j] * f;
                accBuf[j] -= mi * f;
            }
            accBuf[i] = ai;
        }

        for (int i = 0; i < n; i++) sampled[o + i] = accBuf[i] + ThrustAcc(i, time);
    }

    double3 ThrustAcc(int i, double time)
    {
        BurnState b = burn[i];
        if (b.active == 0) return new double3(0, 0, 0);

        double s = (time - b.startTime) / b.duration;
        if (s <= 0.0 || s >= 1.0) return new double3(0, 0, 0);

        double q = 1.0 - s;
        double dProfile = 30.0 * s * s * q * q;
        return (b.targetVel - b.startVel) * (dProfile / b.duration);
    }

    // ---- polynomial maths ---------------------------------------------------------------

    double3 PredictDisp(int i, double s, double tau, double h)
    {
        int o = i * 7;
        double3 acc = s * s * s * (poly[o] / 6.0 +
            s * (poly[o + 1] / 12.0 +
            s * (poly[o + 2] / 20.0 +
            s * (poly[o + 3] / 30.0 +
            s * (poly[o + 4] / 42.0 +
            s * (poly[o + 5] / 56.0 +
            s * (poly[o + 6] / 72.0)))))));
        return vel[i] * tau + 0.5 * a0[i] * (tau * tau) + h * h * acc;
    }

    double3 PredictVel(int i, double s, double tau, double h)
    {
        int o = i * 7;
        double3 pv = h * (s * s * (poly[o] / 2.0 +
            s * (poly[o + 1] / 3.0 +
            s * (poly[o + 2] / 4.0 +
            s * (poly[o + 3] / 5.0 +
            s * (poly[o + 4] / 6.0 +
            s * (poly[o + 5] / 7.0 +
            s * (poly[o + 6] / 8.0))))))));
        return vel[i] + a0[i] * tau + pv;
    }

    void SolveCo(int i)
    {
        double3 a = a0[i];
        double3 d0 = sampled[i] - a;
        double3 d1 = sampled[n + i] - a;
        double3 d2 = sampled[2 * n + i] - a;
        double3 d3 = sampled[3 * n + i] - a;
        double3 d4 = sampled[4 * n + i] - a;
        double3 d5 = sampled[5 * n + i] - a;
        double3 d6 = sampled[6 * n + i] - a;

        int o = i * 7;
        for (int r = 0; r < 7; r++)
        {
            int ro = r * 7;
            poly[o + r] = invV[ro] * d0 + invV[ro + 1] * d1 + invV[ro + 2] * d2 + invV[ro + 3] * d3
                        + invV[ro + 4] * d4 + invV[ro + 5] * d5 + invV[ro + 6] * d6;
        }
    }

    void RescalePoly(int i, double q)
    {
        int o = i * 7;
        double qm = 1.0;
        for (int m = 1; m <= 7; m++)
        {
            qm *= q;
            double3 c = new double3(0, 0, 0);
            for (int k = m - 1; k < 7; k++) c += binom[(k + 1) * 8 + m] * poly[o + k];
            polyTmp[m - 1] = c * qm;
        }
        for (int m = 0; m < 7; m++) poly[o + m] = polyTmp[m];
    }

    static bool IsBad(double3 v)
    {
        return !math.all(math.isfinite(v));
    }
}

public class PhysicsManager : MonoBehaviour
{
    public EngineManager EngineManager;
    public SceneManager SceneManager;
    public physicsObject[] fullObjData;
    private int numbOfObjects;
    public int spacecraftID = -1;

    // ---- native storage (flat, one array per field) -------------------------------------
    NativeArray<double3> aPos, aPosLo, aVel, aA0, aPoly, aStageOff, aSampled, aAccBuf, aPolyTmp;
    NativeArray<double> aMass, aRatios, aInvV, aBinom, aClock;
    NativeArray<int> aStats;
    NativeArray<BurnState> aBurn;

    bool accDirty = true;
    bool closeWarnActive;

    /// <summary>
    /// Thin handle so existing code (fullObjData[i].pos etc.) keeps working.
    /// The data lives in the manager's NativeArrays. Only read these between physicsUpdate calls.
    /// </summary>
    public class physicsObject
    {
        public readonly int ID;
        readonly PhysicsManager m;

        public physicsObject(PhysicsManager manager, int id) { m = manager; ID = id; }

        public double3 pos { get { return m.aPos[ID]; } set { m.aPos[ID] = value; m.accDirty = true; } }
        public double3 vel { get { return m.aVel[ID]; } set { m.aVel[ID] = value; m.accDirty = true; } }
        public double3 acc { get { return m.aA0[ID]; } }
        public double3 a0 { get { return m.aA0[ID]; } }
        public double mass { get { return m.aMass[ID]; } set { m.aMass[ID] = value; m.accDirty = true; } }

        public bool isThrusting { get { return m.aBurn[ID].active != 0; } }
        public double3 burnStartVel { get { return m.aBurn[ID].startVel; } }
        public double3 burnTargetVel { get { return m.aBurn[ID].targetVel; } }
        public double burnStartTime { get { return m.aBurn[ID].startTime; } }
        public double burnDuration { get { return m.aBurn[ID].duration; } }
    }

    // ---- tunables -----------------------------------------------------------------------
    public double t = 0;
    public double h = 0.1;
    public double targetAcc = 1e-9;
    public double convergenceTol = 1e-12;
    public int maxIterations = 100;
    public double maxHChange;
    public double minHChange;
    private double safetyF = 0.9;
    public double minH = 1e-8;
    public double maxH = 100;

    [Tooltip("Integrator lands exactly on this sim time and never steps past it. double.MaxValue = no stop.")]
    public double stopTime = double.MaxValue;

    [Tooltip("Integrator steps per physicsUpdate() call, all done inside one Burst call.")]
    public int stepsPerUpdate = 1;

    [Tooltip("Log a warning (once) when two bodies get closer than sqrt(this).")]
    public double temp = 1e4;

    // diagnostics
    public int lastIterations;
    public int lastStepsDone;

    private double[] ratios = { 0.0562625605369221, 0.1802406917368924, 0.3526247171131696, 0.5471536263305554, 0.7342101772154105, 0.8853209468390957, 0.9775206135612875 };

    public bool paused = false;
    public void pauseOrPlay()
    {
        paused = paused != true;
    }

    // ---- setup --------------------------------------------------------------------------

    public void resetBody(physicsObject o, double3 newPos, double3 newVel)
    {
        int id = o.ID;
        aPos[id] = newPos;
        aVel[id] = newVel;
        aPosLo[id] = double3.zero;
        for (int k = 0; k < 7; k++) aPoly[id * 7 + k] = double3.zero;
        aBurn[id] = default;   // cancels any burn in progress
        accDirty = true;
    }

    public void physicsInit(int lNumOfObjects)
    {
        FreeAll();

        numbOfObjects = lNumOfObjects + 1;
        int n = numbOfObjects;

        aPos = Alloc<double3>(n);
        aPosLo = Alloc<double3>(n);
        aVel = Alloc<double3>(n);
        aA0 = Alloc<double3>(n);
        aMass = Alloc<double>(n);
        aBurn = Alloc<BurnState>(n);
        aPoly = Alloc<double3>(n * 7);
        AllocScratch(n);

        aPolyTmp = Alloc<double3>(7);
        aRatios = Alloc<double>(7);
        aInvV = Alloc<double>(49);
        aBinom = Alloc<double>(64);
        aClock = Alloc<double>(4);
        aStats = Alloc<int>(8);

        fullObjData = new physicsObject[n];
        for (int i = 0; i < n; i++)
        {
            fullObjData[i] = new physicsObject(this, i);
            aPos[i] = EngineManager.initalPos[i];
            aVel[i] = EngineManager.initalVel[i];
            aMass[i] = SceneManager.allBodys[i].GetComponent<Body>().mass;
        }

        setupMat();
        accDirty = true;
    }

    public physicsObject addSpacecraft(double3 pos, double3 vel, double gm = 0.0)
    {
        int newID = fullObjData.Length;
        int newN = newID + 1;

        Grow(ref aPos, newN);
        Grow(ref aPosLo, newN);
        Grow(ref aVel, newN);
        Grow(ref aA0, newN);
        Grow(ref aMass, newN);
        Grow(ref aBurn, newN);
        Grow(ref aPoly, newN * 7);
        FreeScratch();
        AllocScratch(newN);

        aPos[newID] = pos;
        aVel[newID] = vel;
        aMass[newID] = gm;

        System.Array.Resize(ref fullObjData, newN);
        physicsObject craft = new physicsObject(this, newID);
        fullObjData[newID] = craft;

        numbOfObjects = newN;
        spacecraftID = newID;
        accDirty = true;
        return craft;
    }

    public void commandBurn(physicsObject target, double3 targetVel, double duration)
    {
        int id = target.ID;
        aBurn[id] = new BurnState
        {
            startVel = aVel[id],
            targetVel = targetVel,
            startTime = t,
            duration = math.max(duration, 1e-6), // guard against 0/negative duration
            active = 1
        };
        accDirty = true;
    }

    private void setupMat()
    {
        double[,] vandermondeDat = new double[7, 7];
        for (int i = 0; i < 7; i++)
        {
            vandermondeDat[i, 0] = ratios[i];
            for (int j = 1; j < 7; j++)
            {
                vandermondeDat[i, j] = vandermondeDat[i, j - 1] * ratios[i];
            }
        }
        double[,] inv = DenseMatrix.OfArray(vandermondeDat).Inverse().ToArray();

        for (int r = 0; r < 7; r++)
        {
            aRatios[r] = ratios[r];
            for (int c = 0; c < 7; c++) aInvV[r * 7 + c] = inv[r, c];
        }

        for (int n = 0; n < 8; n++)
        {
            aBinom[n * 8] = 1;
            for (int m = 1; m <= n; m++)
                aBinom[n * 8 + m] = aBinom[(n - 1) * 8 + m - 1] + aBinom[(n - 1) * 8 + m];
        }
    }

    // ---- the update ---------------------------------------------------------------------

    public void physicsUpdate()
    {
        if (paused || !aPos.IsCreated) return;

        aClock[0] = t;
        aClock[1] = h;

        var job = new IAS15Job
        {
            n = numbOfObjects,
            steps = math.max(1, stepsPerUpdate),
            maxIterations = maxIterations,
            refreshAcc = accDirty ? 1 : 0,
            targetAcc = targetAcc,
            convergenceTol = convergenceTol,
            safetyF = safetyF,
            minH = minH,
            maxH = maxH,
            minHChange = minHChange,
            maxHChange = maxHChange,
            stopTime = stopTime,

            ratios = aRatios,
            invV = aInvV,
            binom = aBinom,
            mass = aMass,

            pos = aPos,
            posLo = aPosLo,
            vel = aVel,
            a0 = aA0,
            poly = aPoly,
            stageOff = aStageOff,
            sampled = aSampled,
            accBuf = aAccBuf,
            polyTmp = aPolyTmp,
            burn = aBurn,
            clock = aClock,
            stats = aStats
        };

        job.Run(); // synchronous, but compiled by Burst

        accDirty = false;
        t = aClock[0];
        h = aClock[1];

        lastIterations = aStats[IAS15Job.StatIter];
        lastStepsDone = aStats[IAS15Job.StatSteps];

        int bad = aStats[IAS15Job.StatBad];
        if (bad >= 0) Debug.LogError($"Body {bad} has invalid acceleration: {aA0[bad]}");

        if (aStats[IAS15Job.StatWeirdH] > 0) Debug.LogError($"Yo h is weird (proposal was NaN/Inf/<=0), halved it. h = {h}");

        double minD2 = aClock[2];
        if (minD2 < temp)
        {
            if (!closeWarnActive)
            {
                closeWarnActive = true;
                Debug.LogWarning($"{aStats[IAS15Job.StatCloseI]} and {aStats[IAS15Job.StatCloseJ]} are closee   {minD2}");
            }
        }
        else closeWarnActive = false;
    }

    // ---- native memory helpers ----------------------------------------------------------

    static NativeArray<T> Alloc<T>(int len) where T : struct
    {
        return new NativeArray<T>(len, Allocator.Persistent, NativeArrayOptions.ClearMemory);
    }

    static void Grow<T>(ref NativeArray<T> a, int newLen) where T : struct
    {
        var neu = Alloc<T>(newLen);
        if (a.IsCreated)
        {
            NativeArray<T>.Copy(a, neu, a.Length);
            a.Dispose();
        }
        a = neu;
    }

    static void Free<T>(ref NativeArray<T> a) where T : struct
    {
        if (a.IsCreated) a.Dispose();
    }

    void AllocScratch(int n)
    {
        aStageOff = Alloc<double3>(n * 7);
        aSampled = Alloc<double3>(n * 7);
        aAccBuf = Alloc<double3>(n);
    }

    void FreeScratch()
    {
        Free(ref aStageOff);
        Free(ref aSampled);
        Free(ref aAccBuf);
    }

    void FreeAll()
    {
        Free(ref aPos); Free(ref aPosLo); Free(ref aVel); Free(ref aA0);
        Free(ref aPoly); Free(ref aPolyTmp);
        Free(ref aMass); Free(ref aRatios); Free(ref aInvV); Free(ref aBinom); Free(ref aClock);
        Free(ref aStats); Free(ref aBurn);
        FreeScratch();
    }

    void OnDestroy()
    {
        FreeAll();
    }
}