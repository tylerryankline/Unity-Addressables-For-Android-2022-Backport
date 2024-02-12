using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets.Android;
using UnityEditor.Android;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.Android;

public class PlayAssetDeliveryPostGenerateGradleAndroidProject : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 0;
    
    const string kAssetPacksMissing = "Asset packs entry (**PLAY_ASSET_PACKS**) is missing from Custom Launcher Gradle Template. Asset packs are not included to the AAB.";
    
    public void OnPostGenerateGradleAndroidProject(string path)
    {
        Debug.Log("doing PlayAssetDeliveryPostGenerateGradleAndroidProject");
        bool useAssetPacks = !(EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android ||
                               !TextureCompressionTargetingHelper.UseAssetPacks ||
                               PlayAssetDeliverySetup.PlayAssetDeliveryNotInitialized() ||
                               !File.Exists(CustomAssetPackUtility.CustomAssetPacksDataEditorPath) ||
                               (TextureCompressionTargetingHelper.EnabledTextureCompressionTargeting && !TextureCompressionTargetingHelper.IsCurrentTextureCompressionDefault));
        
        if (!useAssetPacks)
        {
            Debug.LogWarning("Not using asset packs!");
            return;
        }
        //We need to go up a level to the main gradle proj, not the unityLibrary one
        path = path.Replace("/unityLibrary", "");


        Debug.Log("CustomAssetPackUtility.CustomAssetPacksDataEditorPath: " + CustomAssetPackUtility.CustomAssetPacksDataEditorPath);
        var customPackData = JsonUtility.FromJson<CustomAssetPackData>(File.ReadAllText(CustomAssetPackUtility.CustomAssetPacksDataEditorPath));
        Debug.Log($"custom Asset pack data: {customPackData.ToString()}" );
        //Add Custom Asset Pack Data Context
        //Copy asset pack data to gradle project
        var customAssetPackTargetPath = Path.Combine(path, CreateAddressableAssetPackAssetsPath(), CustomAssetPackUtility.kCustomAssetPackDataFilename);
        var customAssetPackSourcePath = Path.Combine(CustomAssetPackUtility.BuildRootDirectory, $"{Addressables.StreamingAssetsSubFolder}", CustomAssetPackUtility.kCustomAssetPackDataFilename);
        Debug.Log("customAssetPackTargetPath: " + customAssetPackTargetPath);
        Debug.Log("customAssetPackSourcePath: " + customAssetPackSourcePath);

        //projectFilesContext.AddFileToCopy(sourcePath, targetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(customAssetPackTargetPath));
        File.Copy(customAssetPackSourcePath, Path.Combine(path, customAssetPackTargetPath), true);
        
        //Add bundles to files To gradle project
        var buildProcessorDataPath = Path.Combine(CustomAssetPackUtility.BuildRootDirectory, $"{Addressables.StreamingAssetsSubFolder}", CustomAssetPackUtility.kBuildProcessorDataFilename);
        Debug.Log("buildProcessorDataPath: " + buildProcessorDataPath);
        if (!File.Exists(buildProcessorDataPath))
        {
            return;
        }
        var contents = File.ReadAllText(buildProcessorDataPath);
        var data = JsonUtility.FromJson<BuildProcessorData>(contents);
        foreach (BuildProcessorDataEntry entry in data.Entries)
        {
            //Copy the bundles to the gradle project
            //projectFilesContext.AddFileToCopy(entry.BundleBuildPath, entry.AssetPackPath);
            var entryTargetPath = Path.Combine(path, entry.AssetPackPath);
            Debug.Log($"entry.BundleBuildPath: {entry.BundleBuildPath}");
            Debug.Log($"entryTargetPath: {entryTargetPath}");
            Directory.CreateDirectory(Path.GetDirectoryName(entryTargetPath));
            File.Copy(entry.BundleBuildPath, entryTargetPath, true);
        }
        
        //Add InstallTime Files To gradle project
        var targetPath = CreateAddressableAssetPackAssetsPath();
        var sourcePath = Addressables.BuildPath;
        foreach (var mask in CustomAssetPackUtility.InstallTimeFilesMasks)
        {
            //Copy everything at the default build path to the gradle project. It will be in the install time asset pack
            var files = Directory.EnumerateFiles(sourcePath, mask, SearchOption.AllDirectories).ToList();
            foreach (var f in files)
            {
                var dest = Path.Combine(targetPath, Path.GetRelativePath(sourcePath, f));
                //projectFilesContext.AddFileToCopy(f, dest);
                var fileTargetPath = Path.Combine(path, dest);
                Directory.CreateDirectory(Path.GetDirectoryName(fileTargetPath));
                File.Copy(f, fileTargetPath, true);
            }
        }

        
        //For each asset pack, make a build.gradle file and fill it out
        foreach (var entry in customPackData.Entries)
        {
            var assetPackGradlePath = Path.Combine(entry.AssetPackName, "build.gradle");
            Debug.Log($"Custom Asset Pack Gradle Path: {assetPackGradlePath}");
            using (FileStream fs = File.Create(Path.Combine(path, assetPackGradlePath)))
            {
                StreamWriter w = new StreamWriter(fs); 
                w.WriteLine("apply plugin: 'com.android.asset-pack'");
                w.WriteLine();
                w.WriteLine("assetPack {");
                w.WriteLine("\t{");
                w.WriteLine($"\t\tpackName = \"{entry.AssetPackName}\"");
                w.WriteLine("\t\tdynamicDelivery {");
                w.WriteLine($"\t\t\tdeliveryType = \"{CustomAssetPackUtility.DeliveryTypeToGradleString(entry.DeliveryType)}\"");
                w.WriteLine("\t\t}");
                w.WriteLine("\t}");
                w.WriteLine("}");
                w.Flush();
            }
        }
    
        string settingsPath = Path.Combine(path, "settings.gradle");

        var settingsAsset = File.ReadAllText(settingsPath);
        var includeAssetPacks = new StringBuilder();
        foreach (var entry in customPackData.Entries)
        {
            var assetPackEntry = $"\':{entry.AssetPackName}\'";
            if (!settingsAsset.Contains(assetPackEntry, StringComparison.InvariantCulture))
            {
                includeAssetPacks.Append("\ninclude ").Append(assetPackEntry);
            }
        }

        if (includeAssetPacks.Length > 0)
        {
            settingsAsset += includeAssetPacks.ToString();
            File.WriteAllText(settingsPath, settingsAsset);
        }
        
        string launcherGradlePath = Path.Combine(path, "launcher", "build.gradle");
        
        var launcherAsset = File.ReadAllLines(launcherGradlePath);

        for(int i = 0; i < launcherAsset.Length; i++)
        {
            if (launcherAsset[i].Contains("assetPacks =", StringComparison.InvariantCulture))
            {
                launcherAsset[i] = GenerateAssetPacksGradleContents(customPackData);
                Debug.Log($"asset pack line of launcher/build.gradle: {launcherAsset[i]}");
            }
        }
        File.Delete(launcherGradlePath);
        File.WriteAllLines(launcherGradlePath, launcherAsset);
    }
    
    static string GenerateAssetPacksGradleContents(CustomAssetPackData customPackData)
    {
        var assetPacks = "\":UnityDataAssetPack\", ";

        int count = 0;
        int lastEntry = customPackData.Entries.Count - 1;
        foreach (var entry in customPackData.Entries)
        {
            var thisPackString = $"\":{entry.AssetPackName}\"";
            Debug.Log($"[PAD] adding {thisPackString} to launcher/build.gradle");
            assetPacks += thisPackString;
            if (count != lastEntry)
            {
                assetPacks += ", ";
            }
            count++;
        }

        return "    assetPacks = [" + assetPacks + "]";
    }

    static string CreateAddressableAssetPackAssetsPath()
    {
        return Path.Combine(CustomAssetPackUtility.kAddressablesAssetPackName, $"{CustomAssetPackUtility.CustomAssetPacksAssetsPath}");
    }
}