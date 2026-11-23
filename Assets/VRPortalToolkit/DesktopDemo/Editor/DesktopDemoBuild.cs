using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using VRPortalToolkit;
using VRPortalToolkit.Portables;

public static class DesktopDemoBuild
{
    private const string Folder = "Assets/VRPortalToolkit/DesktopDemo";
    public const string ScenePath = Folder + "/DesktopDemo.unity";

    public static void Build()
    {
        CreateScene();
        var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
        if (xr != null)
        {
            xr.InitManagerOnStart = false;
            EditorUtility.SetDirty(xr);
            if (xr.Manager != null)
            {
                foreach (var loader in xr.Manager.activeLoaders.ToArray())
                    xr.Manager.TryRemoveLoader(loader);
                EditorUtility.SetDirty(xr.Manager);
            }
        }
        PlayerSettings.companyName = "VRPortalToolkit";
        PlayerSettings.productName = "VR Portal Desktop Demo";
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 800;
        PlayerSettings.defaultIsNativeResolution = false;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        string output = Environment.GetEnvironmentVariable("VRPORTAL_BUILD_DIR");
        if (string.IsNullOrEmpty(output)) output = Path.GetFullPath("../build/linux");
        Directory.CreateDirectory(output);
        string[] scenes = new[] { ScenePath }.Concat(Directory.GetFiles("Assets/VRPortalToolkit/Examples/Scenes", "*.unity")).ToArray();
        EditorBuildSettings.scenes = scenes.Select(s => new EditorBuildSettingsScene(s, true)).ToArray();
        AssetDatabase.SaveAssets();
        BuildReport report = BuildPipeline.BuildPlayer(scenes, Path.Combine(output, "VRPortalDemo.x86_64"), BuildTarget.StandaloneLinux64, BuildOptions.None);
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("Linux build failed: " + report.summary.result);
        Debug.Log("VRPORTAL_BUILD_SUCCEEDED " + output);
    }

    [MenuItem("VR Portal/Create Desktop Demo")]
    public static void CreateScene()
    {
        Directory.CreateDirectory(Folder + "/Materials");
        AssetDatabase.Refresh();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/VRPortalToolkit/UniversalRenderPipelineAsset.asset");
        GraphicsSettings.renderPipelineAsset = pipeline;
        QualitySettings.renderPipeline = pipeline;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.6f, 0.7f);
        Material blue = Material("Blue", new Color(0.12f, 0.33f, 0.65f));
        Material orange = Material("Orange", new Color(0.75f, 0.3f, 0.08f));
        Material dark = Material("Floor", new Color(0.16f, 0.19f, 0.24f));
        Material white = Material("White", new Color(0.82f, 0.88f, 0.94f));
        Room(0, blue, dark);
        Room(14, orange, dark);
        for (int i = 0; i < 4; i++)
        {
            Cube("Blue landmark " + i, new Vector3(-3.4f + i * 2.2f, 0.45f + 0.15f * i, -2), new Vector3(0.7f, 0.9f + 0.3f * i, 0.7f), blue);
            Cube("Orange landmark " + i, new Vector3(10.6f + i * 2.2f, 0.5f + 0.2f * i, -2), new Vector3(0.75f, 1 + 0.4f * i, 0.75f), orange);
        }
        var pairPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VRPortalToolkit/Prefabs/BasicPortalPair.prefab");
        var pair = (GameObject)PrefabUtility.InstantiatePrefab(pairPrefab);
        pair.SetActive(true);
        Transform a = pair.transform.Find("PortalA");
        Transform b = pair.transform.Find("PortalB");
        a.SetPositionAndRotation(new Vector3(0, 1.5f, 6), Quaternion.Euler(0, 180, 0));
        b.SetPositionAndRotation(new Vector3(14, 1.5f, 6), Quaternion.Euler(0, 180, 0));
        a.localScale = b.localScale = new Vector3(2.2f, 3, 1);
        Frame(0, blue);
        Frame(14, orange);
        var light = new GameObject("Sun").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.transform.rotation = Quaternion.Euler(55, -30, 0);
        var player = new GameObject("Desktop Camera", typeof(Camera), typeof(AudioListener));
        player.tag = "MainCamera";
        player.transform.position = new Vector3(0, 1.6f, 0);
        var camera = player.GetComponent<Camera>();
        camera.nearClipPlane = 0.03f;
        camera.farClipPlane = 100;
        camera.fieldOfView = 65;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.035f, 0.045f, 0.07f);
        camera.GetUniversalAdditionalCameraData().renderShadows = true;
        player.AddComponent<Portable>().origin = player.transform;
        player.AddComponent<PortableCamera>().source = player.transform;
        player.AddComponent<ForcePortableCheck>();
        DesktopPortalDemo demo = player.AddComponent<DesktopPortalDemo>();
        demo.entrance = a.GetComponentInChildren<Portal>();
        demo.exit = b.GetComponentInChildren<Portal>();
        demo.ballMaterial = white;
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("VRPORTAL_DESKTOP_SCENE_READY");
    }

    private static Material Material(string name, Color color)
    {
        string path = Folder + "/Materials/" + name + ".mat";
        Material result = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (result == null)
        {
            result = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(result, path);
        }
        result.color = color;
        return result;
    }

    private static void Cube(string name, Vector3 position, Vector3 size, Material material)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.position = position;
        cube.transform.localScale = size;
        cube.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static void Room(float x, Material wall, Material floor)
    {
        Cube("Floor", new Vector3(x, -0.15f, 0), new Vector3(12, 0.3f, 16), floor);
        Cube("Back wall", new Vector3(x, 2, -7.9f), new Vector3(12, 4, 0.2f), wall);
        Cube("Left wall", new Vector3(x - 6, 2, 0), new Vector3(0.2f, 4, 16), wall);
        Cube("Right wall", new Vector3(x + 6, 2, 0), new Vector3(0.2f, 4, 16), wall);
        Cube("Portal wall left", new Vector3(x - 3.6f, 2, 6.15f), new Vector3(4.8f, 4, 0.2f), wall);
        Cube("Portal wall right", new Vector3(x + 3.6f, 2, 6.15f), new Vector3(4.8f, 4, 0.2f), wall);
        Cube("Portal wall top", new Vector3(x, 3.55f, 6.15f), new Vector3(2.4f, 0.9f, 0.2f), wall);
    }

    private static void Frame(float x, Material material)
    {
        Cube("Portal frame left", new Vector3(x - 1.2f, 1.5f, 5.95f), new Vector3(0.12f, 3.2f, 0.12f), material);
        Cube("Portal frame right", new Vector3(x + 1.2f, 1.5f, 5.95f), new Vector3(0.12f, 3.2f, 0.12f), material);
        Cube("Portal frame top", new Vector3(x, 3.05f, 5.95f), new Vector3(2.5f, 0.12f, 0.12f), material);
    }
}

