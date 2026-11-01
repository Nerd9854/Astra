using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class spacecraftPathGen : MonoBehaviour
{
    public OrbitViewer orbitViewer;
    public showWarning showWarning;
    public EngineManager engineManager;
    public PhysicsManager physicsManager;
    public speedViewer speedViewer;
    PhysicsManager.physicsObject sunObj;

    public void genStart()
    {
        sunObj = physicsManager.fullObjData[engineManager.bodylookup["SUN"]];
    }

    public static double Dot(double3 v1, double3 v2)
    {
        return v1.x * v2.x + v1.y * v2.y + v1.z * v2.z;
    }

    public static double3 Cross(double3 v1, double3 v2)
    {
        return new double3(
            v1.y * v2.z - v1.z * v2.y,
            v1.z * v2.x - v1.x * v2.z,
            v1.x * v2.y - v1.y * v2.x
        );
    }

    private readonly double3 impossibleAnswer =
        new double3(math.NAN, math.NAN, math.NAN);

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool IsValidVector(double3 v)
    {
        return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
    }


    // ------------------------------------------------------------
    // STUMPFF FUNCTIONS
    // ------------------------------------------------------------

    public static double StumpffC(double z)
    {
        if (!IsFinite(z))
            return double.NaN;

        if (Math.Abs(z) < 1e-3)
        {
            return 0.5 - z / 24.0
                + (z * z) / 720.0
                - (z * z * z) / 40320.0
                + Math.Pow(z, 4) / 3628800.0;
        }

        if (z > 0.0)
        {
            double sqrtZ = Math.Sqrt(z);
            double sinHalf = Math.Sin(sqrtZ * 0.5);

            return 2.0 * sinHalf * sinHalf / z;
        }

        double sqrtNz = Math.Sqrt(-z);

        if (sqrtNz > 700.0)
            return double.PositiveInfinity;

        return (Math.Cosh(sqrtNz) - 1.0) / (-z);
    }


    public static double StumpffS(double z)
    {
        if (!IsFinite(z))
            return double.NaN;

        if (Math.Abs(z) < 1e-3)
        {
            return 1.0 / 6.0 - z / 120.0
                + (z * z) / 5040.0
                - (z * z * z) / 362880.0
                + Math.Pow(z, 4) / 39916800.0;
        }

        if (z > 0.0)
        {
            double sqrtZ = Math.Sqrt(z);

            return (sqrtZ - Math.Sin(sqrtZ))
                / (sqrtZ * sqrtZ * sqrtZ);
        }

        double sqrtNz = Math.Sqrt(-z);

        if (sqrtNz > 700.0)
            return double.PositiveInfinity;

        return (Math.Sinh(sqrtNz) - sqrtNz)
            / (sqrtNz * sqrtNz * sqrtNz);
    }


    // ------------------------------------------------------------
    // TIME OF FLIGHT CALCULATION
    // ------------------------------------------------------------

    // Returns false if psi produces an invalid solution.

    private static bool TryCalculateTime(
        double psi,
        double r1,
        double r2,
        double A,
        double mu,
        out double y,
        out double calculatedTime)
    {
        y = double.NaN;
        calculatedTime = double.NaN;

        double c2 = StumpffC(psi);
        double c3 = StumpffS(psi);

        if (!IsFinite(c2) || !IsFinite(c3) || c2 <= 0.0)
            return false;

        y = r1 + r2
            + A * (psi * c3 - 1.0) / Math.Sqrt(c2);

        if (!IsFinite(y) || y < 0.0)
            return false;

        double x = Math.Sqrt(y / c2);

        calculatedTime =
            (x * x * x * c3 + A * Math.Sqrt(y))
            / Math.Sqrt(mu);

        return IsFinite(calculatedTime) && calculatedTime >= 0.0;
    }


    // ------------------------------------------------------------
    // BRACKETING
    // ------------------------------------------------------------

    // Walks psi from lo to hi in `samples` steps looking for two neighbouring samples whose time of flight
    // lies either side of deltaT. Time of flight rises with psi wherever the solution is valid, so a sign
    // change between two valid neighbours means the root is between them.
    //
    // Used to scan 8192 samples, which made every Lambert call cost thousands of Stumpff evaluations and,
    // because the optimizer calls it tens of thousands of times (twice per evaluation on a one-flyby
    // mission), froze multi-leg planning. A coarse scan is enough, the only thing it can step over is the
    // sliver between the edge of the valid region (where y goes negative and the time of flight falls to 0)
    // and the first valid sample, so when the scan crosses that edge it is bisected out explicitly.
    private static bool ScanForBracket(
        double lo, double hi, int samples,
        double r1, double r2, double A, double mu, double deltaT, double tolerance,
        out double psiLow, out double psiUp)
    {
        psiLow = 0.0;
        psiUp = 0.0;

        double previousPsi = 0.0;
        double previousTime = 0.0;
        double lastInvalidPsi = lo;
        bool previousValid = false;

        for (int i = 0; i <= samples; i++)
        {
            double psi = lo + ((double)i / samples) * (hi - lo);

            if (!TryCalculateTime(psi, r1, r2, A, mu, out _, out double time))
            {
                previousValid = false;
                lastInvalidPsi = psi;
                continue;
            }

            double difference = time - deltaT;

            if (Math.Abs(difference) <= tolerance)
            {
                psiLow = psi;
                psiUp = psi;
                return true;
            }

            if (!previousValid && i > 0)
            {
                // the previous sample was invalid and this one is valid, so the edge of the valid region is
                // between them. Find it and use it as the "previous" sample, its time of flight is ~0.
                double bad = lastInvalidPsi;
                double good = psi;
                for (int k = 0; k < 50; k++)
                {
                    double mid = 0.5 * (bad + good);
                    if (TryCalculateTime(mid, r1, r2, A, mu, out _, out _)) good = mid;
                    else bad = mid;
                }

                if (TryCalculateTime(good, r1, r2, A, mu, out _, out double edgeTime))
                {
                    previousPsi = good;
                    previousTime = edgeTime;
                    previousValid = true;
                }
            }

            if (previousValid)
            {
                double previousDifference = previousTime - deltaT;

                if ((previousDifference < 0.0 && difference > 0.0) ||
                    (previousDifference > 0.0 && difference < 0.0))
                {
                    psiLow = previousPsi;
                    psiUp = psi;
                    return true;
                }
            }

            previousPsi = psi;
            previousTime = time;
            previousValid = true;
        }

        return false;
    }


    // ------------------------------------------------------------
    // LAMBERT SOLVER
    // ------------------------------------------------------------

    // Same as SolveLambert but never shows a UI warning, it reports the reason in `error` instead.
    // This is the one the optimizer has to use: a search throws thousands of unsolvable candidates at the
    // solver (e.g. a time of flight that is too short for the distance), and popping a notification for
    // every one of them floods the UI and stalls the game.
    // Returns true with v1 / v2 filled in, or false with both set to NaN and `error` describing why.
    public bool TrySolveLambert(
        double3 r1Vec,
        double3 r2Vec,
        double deltaT,
        double mu,
        string direction,
        out double3 v1Vec,
        out double3 v2Vec,
        out string error)
    {
        v1Vec = impossibleAnswer;
        v2Vec = impossibleAnswer;
        error = null;

        // --------------------------------------------------------
        // 1. INPUT VALIDATION
        // --------------------------------------------------------

        if (!IsValidVector(r1Vec) ||
            !IsValidVector(r2Vec) ||
            !IsFinite(deltaT) ||
            !IsFinite(mu) ||
            deltaT <= 0.0 ||
            mu <= 0.0)
        {
            error = "Invalid Lambert solver input.";
            return false;
        }

        if (direction == null ||
            (!direction.Equals("prograde", StringComparison.OrdinalIgnoreCase) &&
             !direction.Equals("retrograde", StringComparison.OrdinalIgnoreCase)))
        {
            error = "Invalid Lambert direction. Use prograde or retrograde.";
            return false;
        }

        double r1 = math.length(r1Vec);
        double r2 = math.length(r2Vec);

        if (!IsFinite(r1) || !IsFinite(r2) ||
            r1 <= 1e-12 || r2 <= 1e-12)
        {
            error = "Invalid position vectors supplied to Lambert solver.";
            return false;
        }


        // --------------------------------------------------------
        // 2. TRANSFER ANGLE
        // --------------------------------------------------------

        double cosDeltaTheta = Dot(r1Vec, r2Vec) / (r1 * r2);

        cosDeltaTheta = Math.Clamp(
            cosDeltaTheta, -1.0, 1.0
        );

        double3 crossProd = Cross(r1Vec, r2Vec);

        double crossProdZ = crossProd.z;

        double shortAngle = Math.Acos(cosDeltaTheta);

        double deltaTheta;

        if (direction.Equals(
            "prograde", StringComparison.OrdinalIgnoreCase))
        {
            deltaTheta = crossProdZ >= 0.0
                ? shortAngle
                : 2.0 * Math.PI - shortAngle;
        }
        else
        {
            deltaTheta = crossProdZ < 0.0
                ? shortAngle
                : 2.0 * Math.PI - shortAngle;
        }


        // --------------------------------------------------------
        // 3. CHECK DEGENERATE GEOMETRY
        // --------------------------------------------------------

        double sinDeltaTheta = Math.Sin(deltaTheta);

        if (Math.Abs(sinDeltaTheta) < 1e-10)
        {
            error = "Error solving Lambert's problem:\n" +
                    "Transfer geometry is degenerate or approximately 180 degrees.";
            return false;
        }

        double denominator = 1.0 - cosDeltaTheta;

        if (denominator <= 1e-12)
        {
            error = "Error solving Lambert's problem:\n" +
                    "Initial and final position vectors are too similar.";
            return false;
        }

        double A = sinDeltaTheta *
            Math.Sqrt((r1 * r2) / denominator);

        if (!IsFinite(A) || Math.Abs(A) < 1e-12)
        {
            error = "Invalid Lambert transfer geometry.";
            return false;
        }


        // --------------------------------------------------------
        // 4. FIND A VALID PSI BRACKET
        // --------------------------------------------------------

        const int maxBracketExpansions = 5;
        const int samplesPerSegment = 256;

        double psiLow = 0.0;
        double psiUp = 0.0;

        bool bracketFound = false;

        double tolerance = Math.Max(1e-6, deltaT * 1e-10);

        // The upper limit stays below 4*pi^2.
        // This is the zero-revolution branch.

        double upperLimit =
            4.0 * Math.PI * Math.PI - 1e-7;

        double segmentLow = -4.0 * Math.PI * Math.PI;
        double segmentHigh = upperLimit;

        // Time of flight rises with psi, so if the root isn't in a segment it is further down (more
        // hyperbolic). Each expansion only scans the newly added stretch below the previous one.
        for (int expansion = 0;
             expansion < maxBracketExpansions;
             expansion++)
        {
            if (ScanForBracket(
                segmentLow, segmentHigh, samplesPerSegment,
                r1, r2, A, mu, deltaT, tolerance,
                out psiLow, out psiUp))
            {
                bracketFound = true;
                break;
            }

            // Expand the hyperbolic (negative psi) search range.

            segmentHigh = segmentLow;
            segmentLow *= 2.0;
        }

        if (!bracketFound)
        {
            error = "Error solving Lambert's problem:\n" +
                    "Could not find a valid time-of-flight bracket.";
            return false;
        }


        // --------------------------------------------------------
        // 5. SAFEGUARDED BISECTION
        // --------------------------------------------------------

        double psi = 0.5 * (psiLow + psiUp);

        double yFinal = 0.0;

        bool converged = false;

        const int maxIterations = 300;

        for (int i = 0; i < maxIterations; i++)
        {
            psi = 0.5 * (psiLow + psiUp);

            if (!TryCalculateTime(
                psi, r1, r2, A, mu,
                out double y,
                out double calculatedTime))
            {
                // Invalid y occurs at the boundary of the
                // physically valid zero-revolution branch.

                if (A > 0.0)
                    psiLow = psi;
                else
                    psiUp = psi;

                continue;
            }

            yFinal = y;

            double timeError = calculatedTime - deltaT;

            if (Math.Abs(timeError) <= tolerance)
            {
                converged = true;
                break;
            }

            if (timeError < 0.0)
                psiLow = psi;
            else
                psiUp = psi;

            if (Math.Abs(psiUp - psiLow) < 1e-13)
                break;
        }

        if (!converged)
        {
            error = "Error solving Lambert's problem:\n" +
                    "Solver failed to converge.";
            return false;
        }


        // --------------------------------------------------------
        // 6. RECONSTRUCT DEPARTURE AND ARRIVAL VELOCITIES
        // --------------------------------------------------------

        double f = 1.0 - yFinal / r1;

        double g = A * Math.Sqrt(yFinal / mu);

        double gDot = 1.0 - yFinal / r2;

        if (!IsFinite(g) || Math.Abs(g) < 1e-12)
        {
            error = "Error solving Lambert's problem:\n" +
                    "Lagrange g coefficient is approximately zero.";
            return false;
        }

        double3 v1Candidate =
            (r2Vec - f * r1Vec) / g;

        double3 v2Candidate =
            (gDot * r2Vec - r1Vec) / g;

        if (!IsValidVector(v1Candidate) || !IsValidVector(v2Candidate))
        {
            error = "Error solving Lambert's problem:\n" +
                    "Calculated velocities are invalid.";
            return false;
        }

        v1Vec = v1Candidate;
        v2Vec = v2Candidate;
        return true;
    }

    // Original entry point, same behaviour as before: shows a UI warning and returns NaN vectors on failure.
    // Use TrySolveLambert from anything that calls the solver in bulk.
    public (double3 v1, double3 v2) SolveLambert(
        double3 r1Vec,
        double3 r2Vec,
        double deltaT,
        double mu,
        string direction = "prograde")
    {
        if (TrySolveLambert(r1Vec, r2Vec, deltaT, mu, direction,
                out double3 v1, out double3 v2, out string error))
        {
            return (v1, v2);
        }

        showWarning.dispalyNotif(0, error);
        return (impossibleAnswer, impossibleAnswer);
    }
}