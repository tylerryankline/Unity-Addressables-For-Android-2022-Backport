# com.unity.addressables.android

This is a stripped down version of the Unity Addressables for Android package that is avialable in unity 2023, but backported to work in unity 2022. This was done for a specific project and if you adopt this you may need to edit the package to work for you. Just stick this whole repo in your [ProjectRoot]/Packages folder and add it as an entry in the package manifest file. 

Some notes: 
- Doesn't support the texture compression packs like the 2023 version
- Removed the tests folder
- Check out PlayAssetDeliveryPostGenerateGradleAndroidProject.cs for the majority of the backport. Main thing that we cant use is the new gradle project API on unity 2023 so this is using the "older version"
