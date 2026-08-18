using System;
using System.IO;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

public class DataExtractor : MonoBehaviour
{
    public EngineManager EngineManager;
    public string folderName;
    public string metaFileName;
    public string GMFileName;
    public string RadFileName;
    public string KepFileName;
    public string KepMetaFileName;
    public string tiltFileName;
    public string rotSpeedFileName;

    public void extractData()
    {
        string dataFolderPath = Path.Combine(Application.streamingAssetsPath, folderName);
        //convert the metadata file into a dictonary, i did not come up with the code on line 19, but i think i understnd the flow of data idk
        try { EngineManager.bodylookup = File.ReadLines((string)$"{dataFolderPath}\\{metaFileName}.txt").Select(line => line.Split(":")).ToDictionary(parts => parts[0], parts => int.Parse(parts[1])); }
        catch (Exception e1) { Debug.Log($"ERROR: file missing \nTrued to open {dataFolderPath}\\{metaFileName}.txt but got:\n{e1}"); }

        try { EngineManager.namelookup = File.ReadLines((string)$"{dataFolderPath}\\{metaFileName}.txt").Select(line => line.Split(":")).ToDictionary(parts => int.Parse(parts[1]), parts => parts[0]); }
        catch (Exception e1) { Debug.Log($"ERROR: file missing \nTrued to open {dataFolderPath}\\{metaFileName}.txt but got:\n{e1}"); }

        //open the closest file to the current date
        string paddedMonth = $"{DateTime.Now.Month}".PadLeft(2, '0');
        string closestDate = $"01-{paddedMonth}-{DateTime.Now.Year}"; //kinda janky

        string posfolderPath = $"{dataFolderPath}\\pos\\{closestDate}-pos.bin";
        string velfolderPath = $"{dataFolderPath}\\vel\\{closestDate}-vel.bin";
        try
        {
            EngineManager.initalPos = convertBinToVec3Array(posfolderPath);
            EngineManager.initalVel = convertBinToVec3Array(velfolderPath);
            EngineManager.GMValues = convertBinToArray($"{dataFolderPath}\\{GMFileName}.bin");
            EngineManager.RadValues = convertBinToVec3Array($"{dataFolderPath}\\{RadFileName}.bin");
            EngineManager.KepValues = convertBinToVec4Array($"{dataFolderPath}\\{KepFileName}.bin");
            EngineManager.KepValuesMeta = convertTxtToStrArray($"{dataFolderPath}\\{KepMetaFileName}.txt");
            EngineManager.tiltValues = convertBinToVec4Array($"{dataFolderPath}\\{tiltFileName}.bin");
            EngineManager.rotationSpeedValues = convertBinToArray($"{dataFolderPath}\\{rotSpeedFileName}.bin");
        }
        catch (Exception e2) { Debug.Log($"ERROR: file missing \nTrued to open {posfolderPath} but got:\n{e2}"); }
    }

    private double3[] convertBinToVec3Array(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        double3[] convertedArray = new double3[data.Length / 24];

        for (int i = 0; i <= (data.Length-24); i += 24) //doubles are stored as 8 bytes
        {
            double3 temp = new double3(0, 0, 0);
            for (int j = 0; j <=16; j += 8)
            {
                byte[] bytes = new byte[] { data[i + j], data[(i + 1) + j], data[(i + 2) + j], data[(i + 3) + j], data[(i + 4) + j], data[(i + 5) + j], data[(i + 6) + j], data[(i + 7) + j] };
                double convertedDouble = BitConverter.ToDouble(bytes, 0);
                temp[j/8] = convertedDouble;
            }
            convertedArray[i/24] = temp;
        }

        return convertedArray;
    }

    private double[] convertBinToArray(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        double[] convertedArray = new double[data.Length / 8];

        for (int i = 0; i <= (data.Length - 8); i += 8) //doubles are stored as 8 bytes
        {
            byte[] bytes = new byte[] { data[i + 0], data[i + 1], data[i + 2], data[i + 3], data[i + 4], data[i + 5], data[i + 6], data[i + 7] };
            double convertedDouble = BitConverter.ToDouble(bytes, 0);
            convertedArray[i / 8] = convertedDouble;
        }
        return convertedArray;
    }

    private double4[] convertBinToVec4Array(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        double4[] convertedArray = new double4[data.Length / 32];

        for (int i = 0; i <= (data.Length - 32); i += 32) //doubles are stored as 8 bytes
        {
            double4 temp = new double4(0, 0, 0, 0);
            for (int j = 0; j <= 24; j += 8)
            {
                byte[] bytes = new byte[] { data[i + j], data[(i + 1) + j], data[(i + 2) + j], data[(i + 3) + j], data[(i + 4) + j], data[(i + 5) + j], data[(i + 6) + j], data[(i + 7) + j] };
                double convertedDouble = BitConverter.ToDouble(bytes, 0);
                temp[j / 8] = convertedDouble;
            }
            convertedArray[i / 32] = temp;
        }

        return convertedArray;
    }

    private double2[] convertBinToVec2Array(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        double2[] convertedArray = new double2[data.Length / 16];

        for (int i = 0; i <= (data.Length - 16); i += 16) //doubles are stored as 8 bytes
        {
            double2 temp = new double2(0, 0);
            for (int j = 0; j <= 8; j += 8)
            {
                byte[] bytes = new byte[] { data[i + j], data[(i + 1) + j], data[(i + 2) + j], data[(i + 3) + j], data[(i + 4) + j], data[(i + 5) + j], data[(i + 6) + j], data[(i + 7) + j] };
                double convertedDouble = BitConverter.ToDouble(bytes, 0);
                temp[j / 8] = convertedDouble;
            }
            convertedArray[i / 16] = temp;
        }

        return convertedArray;
    }

    private string[] convertTxtToStrArray(string path) { return File.ReadAllLines(path); }
}
