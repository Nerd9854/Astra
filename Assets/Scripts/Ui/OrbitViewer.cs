using System;
using Unity.Mathematics;
using UnityEngine;
using static PhysicsManager;

public class OrbitViewer : MonoBehaviour
{
    public EngineManager EngineManager;
    public BodyLabeler BodyLabeler;
    public SceneManager SceneManager;
    public PhysicsManager PhysicsManager;
    public GameObject orbitPrefab;
    public GameObject parent;
    public Camera cam;
    public string[] objectsWithTrails = new string[] { "MERCURY", "VENUS", "EARTH", "MARS", "JUPITER", "SATURN", "URANUS", "NEPTUNE", "PLUTO" };
    public KeplerianElements[] keplarinElements;
    public LineRenderer[] trails;
    public int trailLength = 100;
    public int[] trailIds;
    public Transform[] bodies;
    public double scale;
    public float scaleDiff;

    private int numOfTrails;
    public double sunMass;
    public double sunPos;

    public void orbitInit()
    {
        scaleDiff = (float)(scale / SceneManager.scale);
        numOfTrails = objectsWithTrails.Length;
        trailIds = new int[numOfTrails];
        trails = new LineRenderer[numOfTrails];
        bodies = new Transform[numOfTrails];
        sunMass = PhysicsManager.fullObjData[EngineManager.bodylookup["SUN"]].mass;
        keplarinElements = new KeplerianElements[numOfTrails];

        int id = 0;
        int currIt = 0;
        Color color;
        foreach (string obj in objectsWithTrails)
        {
            id = EngineManager.bodylookup[obj];

            GameObject objToGetTrail = SceneManager.allBodys[id];
            GameObject objTrail = Instantiate(orbitPrefab, parent.transform);

            objTrail.name = $"{obj} trail";

            trails[currIt] = objTrail.GetComponent<LineRenderer>();
            trails[currIt].positionCount = trailLength;

            try { color = BodyLabeler.labelColors[currIt + 1]; }
            catch (Exception e) { color = Color.white; }
            trails[currIt].startColor = color;
            trails[currIt].endColor = color;

            bodies[currIt] = SceneManager.allBodys[id].transform;
            trailIds[currIt] = id;

            currIt++;
        }

        calcAllKep();
        for (int i = 0; i < numOfTrails; i++)
        {
            trails[i].SetPositions(calcAngle(keplarinElements[i], 100));
        }
    }

    public void updatePos(Vector3 pos) { parent.transform.position = pos; }

    public void calcAllKep()
    {
        physicsObject objData;
        int iter = 0;
        for (int i = 0; i < numOfTrails; i++)
        {
            objData = PhysicsManager.fullObjData[trailIds[i]];

            keplarinElements[i] = calcKep(objData.pos, objData.vel, objData.mass, sunMass);
        }
    }

    public class KeplerianElements
    {
        public double semiMajorA;
        public double eccentricity;
        public double inclination;
        public double longOfAsendingNode;
        public double argOfPeri;
        public double meanAnom;
        public double trueAno;
        public double eccAnom;
        public double gravParam;
    }

    public KeplerianElements calcKep(double3 pos, double3 vel, double mass, double sunMass)
    {
        double gravParam = sunMass + mass;

        double posMag = sqrt(pos);
        double velMag = sqrt(vel);

        double3 angMomVec = math.cross(pos, vel);
        double angularMom = sqrt(angMomVec);

        double3 k = new double3(0, 0, 1);
        double3 nodeVec = math.cross(k, angMomVec);
        double node = sqrt(nodeVec);

        double posVelDot = math.dot(pos, vel);
        double3 eccentricityVec = (1.0 / gravParam) * (((velMag * velMag) - (gravParam / posMag)) * pos - (posVelDot * vel));
        double eccentricity = sqrt(eccentricityVec);

        double semiMajorA = -(gravParam / (2.0 * (((velMag * velMag) / 2.0) - (gravParam / posMag))));

        double inclination = math.acos(angMomVec.z / angularMom);

        double longOfAsendingNode = math.atan2(nodeVec.y, nodeVec.x);
        if (longOfAsendingNode < 0) longOfAsendingNode += 2.0 * math.PI;

        double3 hCrossN = math.cross(angMomVec, nodeVec);
        double first = math.dot(eccentricityVec, hCrossN) / angularMom;
        double second = math.dot(nodeVec, eccentricityVec);
        double argOfPeri = math.atan2(first, second);
        if (argOfPeri < 0) argOfPeri += 2.0 * math.PI;

        double muR = gravParam * posMag;
        first = (angularMom * posVelDot) / muR;
        second = ((angularMom * angularMom) / muR) - 1.0;
        double trueAno = math.atan2(first, second);
        if (trueAno < 0) trueAno += 2.0 * math.PI;

        double eccAnom = 2.0 * math.atan2(math.sqrt(1 - eccentricity) * math.sin(trueAno / 2.0), math.sqrt(1 + eccentricity) * math.cos(trueAno / 2.0));
        double meanAmo = eccAnom - eccentricity * math.sin(eccAnom);

        return new KeplerianElements
        {
            semiMajorA = semiMajorA,
            eccentricity = eccentricity,
            inclination = inclination,
            longOfAsendingNode = longOfAsendingNode,
            argOfPeri = argOfPeri,
            meanAnom = meanAmo,
            trueAno = trueAno,
            eccAnom = eccAnom,
            gravParam = gravParam
        };
    }

    private double sqrt(double3 d3)
    {
        return math.sqrt(d3.x * d3.x + d3.y * d3.y + d3.z * d3.z);
    }

    public Vector3[] calcAngle(KeplerianElements kepElem, int amountOfSteps)
    {
        double3[] positions = new double3[amountOfSteps];
        Vector3[] scaledPos = new Vector3[amountOfSteps];
        double top = kepElem.semiMajorA * (1 - (kepElem.eccentricity * kepElem.eccentricity));

        double cosLong = math.cos(kepElem.longOfAsendingNode);
        double sinLong = math.sin(kepElem.longOfAsendingNode);
        double cosInc = math.cos(kepElem.inclination);
        double sinInc = math.sin(kepElem.inclination);

        for (int i = 0; i < amountOfSteps; i++)
        {
            double newAngle = (double)i / (double)amountOfSteps * (2.0 * math.PI);

            double newTrueAnom = kepElem.trueAno + newAngle;

            double r = top / (1 + (kepElem.eccentricity * math.cos(newTrueAnom)));


            double add = kepElem.argOfPeri + newTrueAnom;
            double sinAdd = math.sin(add);
            double cosAdd = math.cos(add);

            double x = r * ((cosLong * cosAdd) - (sinLong * sinAdd * cosInc));
            double y = r * ((sinLong * cosAdd) + (cosLong * sinAdd * cosInc));
            double z = r * (sinAdd * sinInc);

            positions[i] = new double3(x, y, z);
            scaledPos[i] = new Vector3((float)(x / scale), (float)(y / scale), (float)(z / scale));
        }
        return scaledPos;
    }



    public double3 calcStep(KeplerianElements kepElem, double deltaTime, double amountofLoops)
    {
        double meanMotion = math.sqrt(kepElem.gravParam / (kepElem.semiMajorA * kepElem.semiMajorA * kepElem.semiMajorA));

        double newMeanAnom = kepElem.meanAnom + meanMotion * deltaTime;

        double currEcc = kepElem.eccentricity;
        double newEcc = newMeanAnom;
        double deltaEcc = 0;
        for (int i = 0; i < amountofLoops; i++)
        {
            deltaEcc = (newMeanAnom - (newEcc - currEcc * math.sin(newEcc))) / (1 - currEcc * math.cos(newEcc));
            newEcc += deltaEcc;
        }

        double xObital = kepElem.semiMajorA * (math.cos(newEcc) - currEcc);
        double yOrbital = kepElem.semiMajorA * math.sqrt(1 - (currEcc * currEcc)) * math.sin(newEcc);

        double cosU = math.cos(kepElem.argOfPeri) * xObital - math.sin(kepElem.argOfPeri) * yOrbital;
        double sinU = math.sin(kepElem.argOfPeri) * xObital + math.cos(kepElem.argOfPeri) * yOrbital;

        double x = math.cos(kepElem.longOfAsendingNode) * cosU - math.sin(kepElem.longOfAsendingNode) * sinU * math.cos(kepElem.inclination);
        double y = math.sin(kepElem.longOfAsendingNode) * cosU + math.cos(kepElem.longOfAsendingNode) * sinU * math.cos(kepElem.inclination);
        double z = sinU * math.sin(kepElem.inclination);

        return new double3(x, y, z);
    }


    public float relativeWidth;
    public float cutttoff;
    public float cutttofff;

    public void orbitUpdate()
    {
        calcAllKep();

        for (int i = 0; i < numOfTrails; i++)
        {
            float dist = Vector3.Distance(cam.transform.position / scaleDiff, bodies[i].position / scaleDiff);
            float normDist = (dist * relativeWidth);
            if (normDist < cutttoff) normDist = cutttofff;
            trails[i].startWidth = normDist;

            trails[i].SetPositions(calcAngle(keplarinElements[i], 100));
        }
    }
}
