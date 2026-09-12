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

    public string startBody;
    public string endBody;
    public double tof = 3600;


    public void Update()
    {
        int startBodyId = engineManager.bodylookup[startBody];
        int endBodyId = engineManager.bodylookup[endBody];
        PhysicsManager.physicsObject endBodyObj = physicsManager.fullObjData[endBodyId];
        PhysicsManager.physicsObject sunObj = physicsManager.fullObjData[engineManager.bodylookup["SUN"]];

        double muEarth = sunObj.mass;
        double3 initPos = physicsManager.fullObjData[startBodyId].pos - sunObj.pos;
        Vector3D r1 = new Vector3D(initPos.x, initPos.y, initPos.z);

        OrbitViewer.KeplerianElements endBodyKep = orbitViewer.calcKep(endBodyObj.pos, endBodyObj.vel, endBodyObj.mass, sunObj.mass);
        double3 endPos = orbitViewer.getPositionAtTime(endBodyKep, tof) - sunObj.pos;
        Vector3D r2 = new Vector3D(endPos.x, endPos.y, endPos.z);

        /*muEarth = 3.986004418e5;
        r1 = new Vector3D(5000, 10000, 2100);
        r2 = new Vector3D(-14600, 2500, 7000);*/

        Debug.Log($"r1: {r1} (5000, 10000, 2100), r2: {r2} (-14600, 2500, 7000), mu: {muEarth} (3.986004418e5)");

        try
        {
            var (v1, v2) = SolveLambert(r1, r2, tof, muEarth, "prograde");
            Debug.Log($"Initial Velocity Vector (V1): {v1} km/s");
            Debug.Log($"Final Velocity Vector (V2): {v2} km/s");
        }
        catch (Exception ex)
        {
            Debug.Log($"Error solving Lambert's problem: {ex.Message}");
        }
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