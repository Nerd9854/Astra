using MathNet.Numerics;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.VisualScripting;
using Unity.VisualScripting.Antlr3.Runtime.Tree;
using UnityEngine;
using static UnityEditor.PlayerSettings;

public class spacecraftPathGen : MonoBehaviour
{
    public OrbitViewer orbitViewer;
    public EngineManager engineManager;
    public PhysicsManager physicsManager;
    PhysicsManager.physicsObject sunObj;


    public void genStart()
    {
        sunObj = physicsManager.fullObjData[engineManager.bodylookup["SUN"]];
    }

    public double getVel(string startBody, string endBody, double t0, double tof)
    {
        PhysicsManager.physicsObject endBodyObj = physicsManager.fullObjData[engineManager.bodylookup[endBody]];
        PhysicsManager.physicsObject startBodyObj = physicsManager.fullObjData[engineManager.bodylookup[startBody]];

        OrbitViewer.KeplerianElements startBodyKep = orbitViewer.calcKep(startBodyObj.pos, startBodyObj.vel, startBodyObj.mass, sunObj.mass);
        OrbitViewer.KeplerianElements endBodyKep = orbitViewer.calcKep(endBodyObj.pos, endBodyObj.vel, endBodyObj.mass, sunObj.mass);

        double mu = sunObj.mass;
        double3 initPos = orbitViewer.getPositionAtTime(startBodyKep, t0) - sunObj.pos;
        double3 endPos = orbitViewer.getPositionAtTime(endBodyKep, t0 + tof) - sunObj.pos;

        Vector3D r1 = new Vector3D(initPos.x, initPos.y, initPos.z);
        Vector3D r2 = new Vector3D(endPos.x, endPos.y, endPos.z);

        double3 vBody1 = orbitViewer.getVelocityAtTime(startBodyKep, t0);

        try
        {
            var (v1, v2) = SolveLambert(r1, r2, tof, mu, "prograde");
            double3 v1d = new double3(v1.X, v1.Y, v1.Z);
            return math.length(v1d - vBody1);
        }
        catch (Exception ex)
        {
            Debug.Log($"Error solving Lambert's problem: {ex.Message}");
        }
        return 0;
    }

    public (double bestT0, double bestTOF, double bestCost) SolveSingleLeg(string startBody, string endBody, double t0Min, double t0Max, double tofMin, double tofMax, int pop = 30, int gens = 100)
    {
        var rng = new System.Random();
        var t0s = new double[pop];
        var tofs = new double[pop];
        var costs = new double[pop];

        for (int i = 0; i < pop; i++)
        {
            t0s[i] = t0Min + rng.NextDouble() * (t0Max - t0Min);
            tofs[i] = tofMin + rng.NextDouble() * (tofMax - tofMin);
            costs[i] = getVel(startBody, endBody, t0s[i], tofs[i]);
        }

        for (int g = 0; g < gens; g++)
            for (int i = 0; i < pop; i++)
            {
                int a, b, c;
                do { a = rng.Next(pop); } while (a == i);
                do { b = rng.Next(pop); } while (b == i || b == a);
                do { c = rng.Next(pop); } while (c == i || c == a || c == b);

                double F = 0.8;
                double trialT0 = math.clamp(t0s[a] + F * (t0s[b] - t0s[c]), t0Min, t0Max);
                double trialTOF = math.clamp(tofs[a] + F * (tofs[b] - tofs[c]), tofMin, tofMax);
                double trialCost = getVel(startBody, endBody, trialT0, trialTOF);

                if (trialCost < costs[i])
                {
                    t0s[i] = trialT0; tofs[i] = trialTOF; costs[i] = trialCost;
                }
            }

        int best = 0;
        for (int i = 1; i < pop; i++) if (costs[i] < costs[best]) best = i;
        return (t0s[best], tofs[best], costs[best]);
    }


    public void LaunchMission(string startBody, string endBody, double t0, double tof)
    {
        var sunObj = physicsManager.fullObjData[engineManager.bodylookup["SUN"]];
        var startObj = physicsManager.fullObjData[engineManager.bodylookup[startBody]];
        var startKep = orbitViewer.calcKep(startObj.pos, startObj.vel, startObj.mass, sunObj.mass);

        double3 spawnPos = orbitViewer.getPositionAtTime(startKep, t0);
        double3 spawnVel = orbitViewer.getVelocityAtTime(startKep, t0);

        var craft = physicsManager.addSpacecraft(spawnPos, spawnVel, 0.0);

        double3 r1 = spawnPos - sunObj.pos;
        double3 r2 = orbitViewer.getPositionAtTime(orbitViewer.calcKep(
            physicsManager.fullObjData[engineManager.bodylookup[endBody]].pos,
            physicsManager.fullObjData[engineManager.bodylookup[endBody]].vel,
            physicsManager.fullObjData[engineManager.bodylookup[endBody]].mass,
            sunObj.mass), t0 + tof) - sunObj.pos;

        var (v1, _) = spacecraftPathGen.SolveLambert(
            new spacecraftPathGen.Vector3D(r1.x, r1.y, r1.z),
            new spacecraftPathGen.Vector3D(r2.x, r2.y, r2.z),
            tof, sunObj.mass, "prograde");

        physicsManager.commandBurn(craft, new double3(v1.X, v1.Y, v1.Z), 60.0);
    }


    public struct Vector3D
    {
        public double X, Y, Z;

        public Vector3D(double x, double y, double z)
        {
            X = x; Y = y; Z = z;
        }

        public double Length() { return Math.Sqrt(X * X + Y * Y + Z * Z); }

        public static double Dot(Vector3D v1, Vector3D v2) { return v1.X* v2.X + v1.Y * v2.Y + v1.Z * v2.Z; }

        public static Vector3D Cross(Vector3D v1, Vector3D v2)
        {
            return new Vector3D(
                v1.Y * v2.Z - v1.Z * v2.Y,
                v1.Z * v2.X - v1.X * v2.Z,
                v1.X * v2.Y - v1.Y * v2.X
            );
        }

        public static Vector3D operator -(Vector3D v1, Vector3D v2) => new Vector3D(v1.X - v2.X, v1.Y - v2.Y, v1.Z - v2.Z);
        public static Vector3D operator *(double scalar, Vector3D v) => new Vector3D(v.X * scalar, v.Y * scalar, v.Z * scalar);
        public static Vector3D operator /(Vector3D v, double scalar) => new Vector3D(v.X / scalar, v.Y / scalar, v.Z / scalar);

        public override string ToString() => $"[{X:F4}, {Y:F4}, {Z:F4}]";
    }

    public static double StumpffC(double z)
    {
        if (z > 1e-5)
        {
            return (1.0 - Math.Cos(Math.Sqrt(z))) / z;
        }
        else if (z < -1e-5)
        {
            return (Math.Cosh(Math.Sqrt(-z)) - 1.0) / (-z);
        }
        else
        {
            return 1.0 / 2.0 - z / 24.0 + (z * z) / 720.0;
        }
    }

    public static double StumpffS(double z)
    {
        if (z > 1e-5)
        {
            double sqrtZ = Math.Sqrt(z);
            return (sqrtZ - Math.Sin(sqrtZ)) / Math.Pow(sqrtZ, 3);
        }
        else if (z < -1e-5)
        {
            double sqrtNz = Math.Sqrt(-z);
            return (Math.Sinh(sqrtNz) - sqrtNz) / Math.Pow(sqrtNz, 3);
        }
        else
        {
            return 1.0 / 6.0 - z / 120.0 + (z * z) / 5040.0;
        }
    }

    public static (Vector3D v1, Vector3D v2) SolveLambert(Vector3D r1Vec, Vector3D r2Vec, double deltaT, double mu, string direction = "prograde")
    {
        double r1 = r1Vec.Length();
        double r2 = r2Vec.Length();

        double cosDeltaTheta = Vector3D.Dot(r1Vec, r2Vec) / (r1 * r2);
        cosDeltaTheta = Math.Clamp(cosDeltaTheta, -1.0, 1.0);

        Vector3D crossProd = Vector3D.Cross(r1Vec, r2Vec);
        double crossProdZ = crossProd.Z;

        double deltaTheta;
        if (direction.Equals("prograde", StringComparison.OrdinalIgnoreCase))
        {
            if (crossProdZ >= 0)
                deltaTheta = Math.Acos(cosDeltaTheta);
            else
                deltaTheta = 2.0 * Math.PI - Math.Acos(cosDeltaTheta);
        }
        else
        {
            if (crossProdZ < 0)
                deltaTheta = Math.Acos(cosDeltaTheta);
            else
                deltaTheta = 2.0 * Math.PI - Math.Acos(cosDeltaTheta);
        }

        double A = Math.Sin(deltaTheta) * Math.Sqrt((r1 * r2) / (1.0 - cosDeltaTheta));

        if (A == 0.0 && Math.Abs(deltaTheta - Math.PI) < 1e-8)
        {
            throw new ArgumentException("180-degree transfer cannot uniquely resolve orbital plane.");
        }

        double psi = 0.0;
        double psiUp = 4.0 * Math.PI * Math.PI;
        double psiLow = -4.0 * Math.PI;
        double tolerance = 1e-6;
        int maxIterations = 200;
        bool converged = false;
        double y = 0.0;

        for (int i = 0; i < maxIterations; i++)
        {
            double c2 = StumpffC(psi);
            double c3 = StumpffS(psi);

            if (c2 > 1e-5)
                y = r1 + r2 + A * (psi * c3 - 1.0) / Math.Sqrt(c2);
            else
                y = r1 + r2;

            if (A > 0.0 && y < 0.0)
            {
                psiLow = psi;
                psi = 0.5 * (psi + psiUp);
                continue;
            }

            double x = Math.Sqrt(y / c2);
            double tComputed = (Math.Pow(x, 3) * c3 + A * Math.Sqrt(y)) / Math.Sqrt(mu);

            if (Math.Abs(tComputed - deltaT) < tolerance)
            {
                converged = true;
                break;
            }

            double eps = 1e-5;
            double c2Eps = StumpffC(psi + eps);
            double c3Eps = StumpffS(psi + eps);
            double yEps = (c2Eps > 0) ? r1 + r2 + A * ((psi + eps) * c3Eps - 1.0) / Math.Sqrt(c2Eps) : r1 + r2;
            double xEps = (c2Eps > 0) ? Math.Sqrt(yEps / c2Eps) : 0;
            double tEps = (xEps * xEps * xEps * c3Eps + A * Math.Sqrt(yEps)) / Math.Sqrt(mu);
            double dtDpsi = (tEps - tComputed) / eps;

            if (tComputed <= deltaT)
                psiLow = psi;
            else
                psiUp = psi;

            if (Math.Abs(dtDpsi) > 1e-10)
                psi = psi - (tComputed - deltaT) / dtDpsi;
            else
                psi = 0.5 * (psiLow + psiUp);

            if (psi < psiLow || psi > psiUp)
                psi = 0.5 * (psiLow + psiUp);
        }

        if (!converged)
        {
            throw new InvalidOperationException("Lambert algorithm failed to converge.");
        }

        double f = 1.0 - y / r1;
        double g = A * Math.Sqrt(y / mu);
        double gDot = 1.0 - y / r2;

        Vector3D v1Vec = (r2Vec - f * r1Vec) / g;
        Vector3D v2Vec = (gDot * r2Vec - r1Vec) / g;

        return (v1Vec, v2Vec);
    }
}