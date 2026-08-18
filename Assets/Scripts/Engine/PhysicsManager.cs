using MathNet.Numerics.Integration;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using System.Linq;
using Unity.Mathematics;
using UnityEditor.PackageManager;
using UnityEngine;


public class PhysicsManager : MonoBehaviour
{
    public EngineManager EngineManager;
    public SceneManager SceneManager;
    public physicsObject[] fullObjData; // in the thing of [[posx, pos, y, posz, accx, accy, accz]]
    private int numbOfObjects;

    public class physicsObject
    {
        public int ID; //this should be its index in the array, but if i wnat to delete elemnts this may be smart? idk it means less usefull info will be in cache [come bak to this]
        public double3 pos;
        public double3 vel;
        public double3 acc;
        public double3 a0;
        public double3[] predictedPos = new double3[7];
        public double3[] poly = new double3[7];
        public double3[] sampledAcc = new double3[7];
        public double mass; //GM not Kg
    }

    public void physicsInit(int lNumOfObjects)
    {
        numbOfObjects = lNumOfObjects+1;
        Debug.Log(numbOfObjects);
        fullObjData = new physicsObject[numbOfObjects];

        for (int i = 0; i < numbOfObjects; i++)
        {
            fullObjData[i] = new physicsObject();

            fullObjData[i].ID = i;
            fullObjData[i].pos = EngineManager.initalPos[i];
            fullObjData[i].vel = EngineManager.initalVel[i];
            fullObjData[i].acc = new double3(0, 0, 0);
            for (int j = 0; j < 7; j++) fullObjData[i].poly[j] = new double3(0, 0, 0);
            fullObjData[i].mass = SceneManager.allBodys[i].GetComponent<Body>().mass;
            
        }
        setupMat();
    }






    private double[] ratios = { 0.0562625605369221, 0.1802406917368924, 0.3526247171131696, 0.5471536263305554, 0.7342101772154105, 0.8853209468390957, 0.9775206135612875 };
    private double t = 0;
    public double h = 0.1;
    private double tol = 0.000000001;
    private double thresh = 0.000001;
    private double convergenceTol = 1e-15;
    Matrix<double> invertedVandMat;
    private int iterations;
    public int maxIterations = 100;
    public double maxHChange;
    public double minHChange;
    private double safetyF = 0.9;
    public double minH = 1e-12;


    private bool converged;
    private double maxChange;

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
        Matrix<double> vandermondeMatrix = DenseMatrix.OfArray(vandermondeDat);
        invertedVandMat = vandermondeMatrix.Inverse();
    }

    public void physicsUpdate()
    {
        converged = false;
        iterations = 0;

        while (!converged && iterations < maxIterations)
        {
            iterations++;
            maxChange = 0;

            foreach (physicsObject obj in fullObjData)
            {
                double3 pos = obj.pos;
                int id = obj.ID;
                obj.a0 = getAcc(id, pos, 0, true);

                for (int i = 0; i < 7; i++)
                {
                    double tau = ratios[i] * h;

                    obj.predictedPos[i] = predictPos(obj, tau);
                }
            }

            foreach (physicsObject self in fullObjData)
            {
                for (int i = 0; i < 7; i++)
                {
                    self.sampledAcc[i] = getAcc(self.ID, self.predictedPos[i], i, false);
                }
            }

            foreach (physicsObject obj in fullObjData)
            {
                double3[] newpoly = solveCo(obj.sampledAcc, obj.a0);
                double3 oldB6 = obj.poly[6];
                double3 newB6 = newpoly[6];
                double diff = math.cmax(math.abs(newB6 - oldB6));
                maxChange = math.max(maxChange, diff);// maxDiff(obj.poly, newpoly, maxChange);

                obj.poly = newpoly;
            }

            double maxAcc = 0.0;

            foreach (physicsObject obj in fullObjData)
            {
                maxAcc = math.max(maxAcc, math.cmax(math.abs(obj.a0)));
            }

            maxAcc = math.max(maxAcc, 1e-300);

            double rDiff = maxChange / maxAcc;

            if (rDiff < convergenceTol) converged = true;
        }

        double maxRError = 0.0;
        double maxB6 = 0.0;
        double maxTAcc = 0.0;
        foreach (physicsObject obj in fullObjData)
        {
            double acc = math.cmax(math.abs(obj.a0));

            maxB6 = math.max(maxB6, math.cmax(math.abs(obj.poly[6])));

            maxTAcc = math.max(maxTAcc, math.cmax(math.abs(obj.a0)));
            double b6 = math.cmax(math.abs(obj.poly[6]));

            acc = math.max(acc, 1e-300);

            double rError = b6 / acc;

            maxRError = math.max(maxRError, rError);
        }
        maxRError = maxB6 / maxTAcc;

        double newHProp;

        if (maxRError <= 0.0) newHProp = h * maxHChange;
        else newHProp = h * safetyF * math.pow(tol / maxRError, 1.0 / 7.0);

        if (double.IsNaN(newHProp) || double.IsInfinity(newHProp) || newHProp <= 0)
        {
            Debug.LogError($"Yo h is weird {newHProp}");
            newHProp = h * 0.5;
        }

        double[] newH = new double[numbOfObjects * 3];
        double hPow = h * h * h * h * h * h * h;
        int iter = 0;
        double maxa0 = 0;
        foreach (physicsObject obj in fullObjData) maxa0 = math.max(maxa0, math.cmax(math.abs(obj.a0)));
        maxa0 = math.max(maxa0, 1e-300);

        foreach (physicsObject obj in fullObjData)
        {
            obj.pos = predictPos(obj, h);
            obj.vel = predictVel(obj, h);
            /*
            double3 b7s = obj.poly[6];
            double errorEstx = math.max(math.abs(b7s.x) / maxa0, 1e-300);// * hPow, 1e-300);
            double errorEsty = math.max(math.abs(b7s.y) / maxa0, 1e-300);// * hPow, 1e-300);
            double errorEstz = math.max(math.abs(b7s.z) / maxa0, 1e-300);// * hPow, 1e-300);

            newH[iter]     = h * math.pow((tol / errorEstx), (1d / 7d));
            newH[iter + 1] = h * math.pow((tol / errorEsty), (1d / 7d));
            newH[iter + 2] = h * math.pow((tol / errorEstz), (1d / 7d));

            iter += 3;*/
        }

        foreach (physicsObject obj in fullObjData)
        {
            obj.a0 = getAcc(obj.ID, obj.pos, 0, true);
            obj.acc = obj.a0;
        }

        t += h;
        h = math.clamp(newHProp, h * minHChange, h * maxHChange);
        h = math.max(h, minH);
        /*
        double newHD = newH.Min();
        if(double.IsNaN(newHD) || double.IsInfinity(newHD) || newHD <= 0)
        {
            Debug.LogError($"Yo h is weird {newHD}");
            newHD = h * 0.5;
        }
        h = math.clamp(newHD, h * minHChange, h * maxHChange);*/
    }

    public double tempThresh;
    private double3 getAcc(int id, double3 curr, int i, bool usePos)
    {
        double3 net = new double3(0, 0, 0);

        foreach (physicsObject other in fullObjData)
        {
            if (id != other.ID)
            {
                double3 obj = usePos? other.pos : other.predictedPos[i];
                double3 diff = obj - curr;
                double dist2 = math.lengthsq(diff);
                if (math.sqrt(dist2) < tempThresh) Debug.Log($"{id}, {other.ID}");
                double dist3 = math.max(dist2 * math.sqrt(dist2), 1e-300);
                net += (other.mass * diff) / (dist3);
            }
        }

        return net;
    }

    private double3 predictPos(physicsObject obj, double tau)
    {
        double s = tau / h;
        double3 predAcc = h * h * distMovedByAcc(obj.poly, s);
        double tau2 = tau * tau;
        double pX = obj.pos.x + obj.vel.x * tau + 0.5 * obj.a0.x * tau2 + predAcc.x;
        double pY = obj.pos.y + obj.vel.y * tau + 0.5 * obj.a0.y * tau2 + predAcc.y;
        double pZ = obj.pos.z + obj.vel.z * tau + 0.5 * obj.a0.z * tau2 + predAcc.z;
        return new double3(pX, pY, pZ);
    }

    private double3 predictVel(physicsObject obj, double tau)
    {
        double s = tau / h;
        double3 predVel = h * distMovedByVel(obj.poly, s);
        double vX = obj.vel.x + obj.a0.x * tau + predVel.x;
        double vY = obj.vel.y + obj.a0.y * tau + predVel.y;
        double vZ = obj.vel.z + obj.a0.z * tau + predVel.z;
        return new double3(vX, vY, vZ);
    }

    private double3 distMovedByAcc(double3[] poly, double tau)
    {
        double3 predicted = new double3(0, 0, 0);

        for (int i = 0; i < 3; i++)
        {
            predicted[i] = tau * tau * tau * (poly[0][i] / 6.0 +
                tau * (poly[1][i] / 12.0 +
                tau * (poly[2][i] / 20.0 +
                tau * (poly[3][i] / 30.0 +
                tau * (poly[4][i] / 42.0 +
                tau * (poly[5][i] / 56.0 +
                tau * (poly[6][i] / 72.0)))))));
        }
        return predicted;
    }

    private double3 distMovedByVel(double3[] poly, double tau)
    {
        double3 predicted = new double3(0, 0, 0);

        for (int i = 0; i < 3; i++)
        {
            predicted[i] = tau * tau * (poly[0][i] / 2.0 +
                tau * (poly[1][i] / 3.0 +
                tau * (poly[2][i] / 4.0 +
                tau * (poly[3][i] / 5.0 +
                tau * (poly[4][i] / 6.0 +
                tau * (poly[5][i] / 7.0 +
                tau * (poly[6][i] / 8.0)))))));

        }
        return predicted;
    }

    private double3[] solveCo(double3[] sampledAcc, double3 a0)
    {
        double[] x = new double[7];
        double[] y = new double[7];
        double[] z = new double[7];
        for (int i = 0; i < 7; i++)
        {
            sampledAcc[i] = sampledAcc[i] - a0;
            x[i] = sampledAcc[i].x;
            y[i] = sampledAcc[i].y;
            z[i] = sampledAcc[i].z;
        }

        double3[] coef = new double3[7];

        for (int i = 0; i < 7; i++)
        {
            coef[i].x = invertedVandMat[i, 0] * x[0] +
                invertedVandMat[i, 1] * x[1] +
                invertedVandMat[i, 2] * x[2] +
                invertedVandMat[i, 3] * x[3] +
                invertedVandMat[i, 4] * x[4] +
                invertedVandMat[i, 5] * x[5] +
                invertedVandMat[i, 6] * x[6];

            coef[i].y = invertedVandMat[i, 0] * y[0] +
                invertedVandMat[i, 1] * y[1] +
                invertedVandMat[i, 2] * y[2] +
                invertedVandMat[i, 3] * y[3] +
                invertedVandMat[i, 4] * y[4] +
                invertedVandMat[i, 5] * y[5] +
                invertedVandMat[i, 6] * y[6];

            coef[i].z = invertedVandMat[i, 0] * z[0] +
                invertedVandMat[i, 1] * z[1] +
                invertedVandMat[i, 2] * z[2] +
                invertedVandMat[i, 3] * z[3] +
                invertedVandMat[i, 4] * z[4] +
                invertedVandMat[i, 5] * z[5] +
                invertedVandMat[i, 6] * z[6];
        }

        return coef;
    }

    private double maxDiff(double3[] init, double3[] calc, double maxChange)
    {
        double maxVal = maxChange;
        for (int i = 0; i < init.Length; i++) maxVal = math.max(maxVal, math.cmax(math.abs(init[i] - calc[i])));
        return maxVal;
    }
}
  