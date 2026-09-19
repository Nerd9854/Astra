using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using Unity.Mathematics;
using UnityEngine;

public class PhysicsManager : MonoBehaviour
{
    //physics = fast
    //bugs = off


    public EngineManager EngineManager;
    public SceneManager SceneManager;
    public physicsObject[] fullObjData; // in the thing of [[posx, pos, y, posz, accx, accy, accz]]
    private int numbOfObjects;
    public int spacecraftID = -1;

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

        public bool isThrusting = false;
        public double3 burnStartVel;
        public double3 burnTargetVel;
        public double burnStartTime;
        public double burnDuration;
    }

    public void physicsInit(int lNumOfObjects)
    {
        numbOfObjects = lNumOfObjects + 1;
        fullObjData = new physicsObject[numbOfObjects];
        accBuffer = new double3[numbOfObjects];

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

    public physicsObject addSpacecraft(double3 pos, double3 vel, double gm = 0.0)
    {
        int newID = fullObjData.Length;
        System.Array.Resize(ref fullObjData, fullObjData.Length + 1);

        physicsObject craft = new physicsObject();
        craft.ID = newID;
        craft.pos = pos;
        craft.vel = vel;
        craft.acc = new double3(0, 0, 0);
        craft.a0 = new double3(0, 0, 0);
        craft.mass = gm;
        for (int j = 0; j < 7; j++) craft.poly[j] = new double3(0, 0, 0);

        fullObjData[newID] = craft;
        numbOfObjects = fullObjData.Length;
        System.Array.Resize(ref accBuffer, fullObjData.Length);
        spacecraftID = newID;
        return craft;
    }

    public void commandBurn(physicsObject target, double3 targetVel, double duration)
    {
        target.burnStartVel = target.vel;
        target.burnTargetVel = targetVel;
        target.burnStartTime = t;
        target.burnDuration = math.max(duration, 1e-6); // guard against 0/negative duration
        target.isThrusting = true;
    }

    private double[] ratios = { 0.0562625605369221, 0.1802406917368924, 0.3526247171131696, 0.5471536263305554, 0.7342101772154105, 0.8853209468390957, 0.9775206135612875 };
    public double t = 0;
    public double h = 0.1;
    public double targetAcc = 1e-9;
    private double thresh = 0.000001;
    public double convergenceTol = 1e-12;
    private double[,] invertedVandRaw;
    private double3[] accBuffer;
    private double[] coScratchX = new double[7];
    private double[] coScratchY = new double[7];
    private double[] coScratchZ = new double[7];
    private int iterations;
    public int maxIterations = 100;
    public double maxHChange;
    public double minHChange;
    private double safetyF = 0.9;
    public double minH = 1e-8;
    public double maxH = 100;

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
        invertedVandRaw = vandermondeMatrix.Inverse().ToArray();
    }

    public void physicsUpdate()
    {
        converged = false;
        iterations = 0;

        computeAllAccFromPos(t);

        while (!converged && iterations < maxIterations)
        {
            iterations++;
            maxChange = 0;

            foreach (physicsObject obj in fullObjData)
            {
                for (int i = 0; i < 7; i++)
                {
                    double tau = ratios[i] * h;
                    obj.predictedPos[i] = predictPos(obj, tau);
                }
            }

            for (int i = 0; i < 7; i++)
            {
                computeAllSampledAcc(i, t + ratios[i] * h);
            }

            foreach (physicsObject obj in fullObjData)
            {
                double3 oldB6 = obj.poly[6];
                solveCo(obj.sampledAcc, obj.a0, obj.poly);
                double3 newB6 = obj.poly[6];
                double diff = math.cmax(math.abs(newB6 - oldB6));
                maxChange = math.max(maxChange, diff);
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
        foreach (physicsObject obj in fullObjData)
        {
            double b6mag = math.cmax(math.abs(obj.poly[6]));
            double amag = math.max(math.cmax(math.abs(obj.a0)), 1e-300);
            maxRError = math.max(maxRError, b6mag / amag);
        }
        /*double maxB6 = 0.0;
        double maxTAcc = 0.0;
        foreach (physicsObject obj in fullObjData)
        {
            maxB6 = math.max(maxB6, math.cmax(math.abs(obj.poly[6])));
            maxTAcc = math.max(maxTAcc, math.cmax(math.abs(obj.a0)));
        }
        maxTAcc = math.max(maxTAcc, 1e-300);
        double maxRError = maxB6 / maxTAcc;*/

        double newHProp;
        if (maxRError <= 0.0) newHProp = h * maxHChange;
        else newHProp = h * safetyF * math.pow(targetAcc / maxRError, 1.0 / 7.0);

        if (double.IsNaN(newHProp) || double.IsInfinity(newHProp) || newHProp <= 0)
        {
            Debug.LogError($"Yo h is weird {newHProp}");
            newHProp = h * 0.5;
        }

        foreach (var obj in fullObjData)
            if (double.IsNaN(obj.a0.x) || double.IsInfinity(obj.a0.x))
                Debug.LogError($"Body {obj.ID} has bad acceleration: {obj.a0}");

        double newT = t + h;

        foreach (physicsObject obj in fullObjData)
        {
            obj.pos = predictPos(obj, h);
            obj.vel = predictVel(obj, h);
        }

        computeAllAccFromPos(newT);

        foreach (physicsObject obj in fullObjData)
        {
            obj.acc = obj.a0;
            if (obj.isThrusting && newT >= obj.burnStartTime + obj.burnDuration)
            {
                obj.isThrusting = false;
            }
        }

        t = newT;
        h = math.clamp(newHProp, h * minHChange, h * maxHChange);
        h = math.max(h, minH);
        h = math.min(h, maxH);
    }

    public double temp = 1e4;
    private void computeAllAccFromPos(double sampleTime)
    {
        for (int k = 0; k < fullObjData.Length; k++) accBuffer[k] = new double3(0, 0, 0);

        for (int i = 0; i < fullObjData.Length; i++)
        {
            physicsObject bi = fullObjData[i];
            for (int j = i + 1; j < fullObjData.Length; j++)
            {
                physicsObject bj = fullObjData[j];
                double3 diff = bj.pos - bi.pos;
                double dist2 = math.lengthsq(diff);
                double dist3 = math.max(dist2 * math.sqrt(dist2), 1e-300);
                double3 diffOverDist3 = diff / dist3;
                if (dist2 < temp) Debug.LogWarning($"{bi.ID} and {bj.ID} are closee   {dist2}");
                accBuffer[i] += bj.mass * diffOverDist3;
                accBuffer[j] -= bi.mass * diffOverDist3;
            }
        }

        for (int i = 0; i < fullObjData.Length; i++)
        {
            fullObjData[i].a0 = accBuffer[i] + getThrustAcc(fullObjData[i], sampleTime);
        }

        for (int i = 0; i < fullObjData.Length; i++)
        {
            if (IsBad(fullObjData[i].a0))
                Debug.LogError($"Body {fullObjData[i].ID} has invalid acceleration: {fullObjData[i].a0}");
        }
    }

    private void computeAllSampledAcc(int stage, double sampleTime)
    {
        for (int k = 0; k < fullObjData.Length; k++) accBuffer[k] = new double3(0, 0, 0);

        for (int i = 0; i < fullObjData.Length; i++)
        {
            physicsObject bi = fullObjData[i];
            double3 posI = bi.predictedPos[stage];
            for (int j = i + 1; j < fullObjData.Length; j++)
            {
                physicsObject bj = fullObjData[j];
                double3 diff = bj.predictedPos[stage] - posI;
                double dist2 = math.lengthsq(diff);
                double dist3 = math.max(dist2 * math.sqrt(dist2), 1e-300);
                double3 diffOverDist3 = diff / dist3;
                accBuffer[i] += bj.mass * diffOverDist3;
                accBuffer[j] -= bi.mass * diffOverDist3;
            }
        }

        for (int i = 0; i < fullObjData.Length; i++)
        {
            fullObjData[i].sampledAcc[stage] = accBuffer[i] + getThrustAcc(fullObjData[i], sampleTime);
        }
    }

    private double3 getThrustAcc(physicsObject obj, double sampleTime)
    {
        if (!obj.isThrusting) return new double3(0, 0, 0);

        double s = (sampleTime - obj.burnStartTime) / obj.burnDuration;
        if (s <= 0.0 || s >= 1.0) return new double3(0, 0, 0);

        double dProfile_ds = 30.0 * s * s * (1.0 - s) * (1.0 - s);
        double3 deltaV = obj.burnTargetVel - obj.burnStartVel;
        return deltaV * (dProfile_ds / obj.burnDuration);
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

    private void solveCo(double3[] sampledAcc, double3 a0, double3[] outPoly)
    {
        for (int i = 0; i < 7; i++)
        {
            double3 delta = sampledAcc[i] - a0;

            coScratchX[i] = delta.x;
            coScratchY[i] = delta.y;
            coScratchZ[i] = delta.z;
        }

        for (int i = 0; i < 7; i++)
        {
            outPoly[i].x = invertedVandRaw[i, 0] * coScratchX[0] + invertedVandRaw[i, 1] * coScratchX[1] + invertedVandRaw[i, 2] * coScratchX[2] + invertedVandRaw[i, 3] * coScratchX[3] + invertedVandRaw[i, 4] * coScratchX[4] + invertedVandRaw[i, 5] * coScratchX[5] + invertedVandRaw[i, 6] * coScratchX[6];
            outPoly[i].y = invertedVandRaw[i, 0] * coScratchY[0] + invertedVandRaw[i, 1] * coScratchY[1] + invertedVandRaw[i, 2] * coScratchY[2] + invertedVandRaw[i, 3] * coScratchY[3] + invertedVandRaw[i, 4] * coScratchY[4] + invertedVandRaw[i, 5] * coScratchY[5] + invertedVandRaw[i, 6] * coScratchY[6];
            outPoly[i].z = invertedVandRaw[i, 0] * coScratchZ[0] + invertedVandRaw[i, 1] * coScratchZ[1] + invertedVandRaw[i, 2] * coScratchZ[2] + invertedVandRaw[i, 3] * coScratchZ[3] + invertedVandRaw[i, 4] * coScratchZ[4] + invertedVandRaw[i, 5] * coScratchZ[5] + invertedVandRaw[i, 6] * coScratchZ[6];
        }
    }

    private bool IsBad(double3 v)
    {
        return double.IsNaN(v.x) || double.IsNaN(v.y) || double.IsNaN(v.z) || double.IsInfinity(v.x) || double.IsInfinity(v.y) || double.IsInfinity(v.z);
    }
}
