using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VRPortalToolkit.Rendering;
using VRPortalToolkit.Rendering.Universal;

namespace VRPortalToolkit.Presence
{
    public static class PresenceBuild
    {
        private const string Toolkit = "Assets/VRPortalToolkit";
        private const string Folder = Toolkit + "/PresenceStudy";
        private const string Scene = Folder + "/SimultaneousPresence.unity";

        [MenuItem("VR Portal/Create Simultaneous Presence Study")]
        public static void CreateStudy()
        {
            int rendererIndex = ConfigureRenderer();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.65f, 0.67f, 0.73f);
            RenderSettings.ambientIntensity = 1f;

            var sun = new GameObject("Study lighting").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.23f;
            sun.color = new Color(1f, 0.95f, 0.875f);
            sun.transform.rotation = Quaternion.Euler(52f, -38f, 0f);
            sun.shadows = LightShadows.None;

            var observer = new GameObject("Primary observer", typeof(Camera), typeof(AudioListener));
            observer.tag = "MainCamera";
            observer.transform.SetPositionAndRotation(new Vector3(0f, 1.62f, 0f), Quaternion.Euler(15f, 0f, 0f));
            var eye = observer.GetComponent<Camera>();
            eye.nearClipPlane = 0.035f;
            eye.farClipPlane = 42f;
            eye.fieldOfView = 61f;
            eye.clearFlags = CameraClearFlags.SolidColor;
            eye.backgroundColor = new Color(0.115f, 0.145f, 0.19f);
            eye.allowMSAA = false;
            var cameraData = eye.GetUniversalAdditionalCameraData();
            cameraData.SetRenderer(rendererIndex);
            cameraData.renderShadows = false;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Toolkit + "/Prefabs/BasicPortalPair.prefab");
            var pair = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            pair.SetActive(true);
            Transform a = pair.transform.Find("PortalA"), b = pair.transform.Find("PortalB");
            a.localScale = b.localScale = Vector3.one;
            a.position = new Vector3(0f, 1.41f, 0.57f);
            a.rotation = Quaternion.Euler(0f, 180f, 0f);
            b.position = new Vector3(100f, 1.41f, 0.57f);
            foreach (Collider collider in pair.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (PortalCameraTransition transition in pair.GetComponentsInChildren<PortalCameraTransition>(true))
                transition.enabled = false;
            b.GetComponentInChildren<PortalMeshRenderer>().enabled = false;

            var study = new GameObject("Simultaneous Presence study").AddComponent<PresenceStudy>();
            study.view = eye;
            study.entrance = a.GetComponentInChildren<PortalMeshRenderer>();
            study.exit = b.GetComponentInChildren<Portal>();
            study.surface = AssetMaterial("StudySurface", Shader.Find("Universal Render Pipeline/Lit"));
            study.labelMaterial = AssetMaterial("StudyLabels", Shader.Find("VRPortalToolkit/Presence/Label"));
            study.pointerMaterial = AssetMaterial("StudyMarking", Shader.Find("Universal Render Pipeline/Unlit"));
            study.composite = AssetMaterial("StudyOverlay", Shader.Find("VRPortalToolkit/Presence/Overlay"));

            EditorSceneManager.SaveScene(scene, Scene);
            AssetDatabase.SaveAssets();
        }

        private static Material AssetMaterial(string name, Shader shader)
        {
            if (!shader) throw new InvalidOperationException("Shader missing for " + name);
            string path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            return material;
        }

        private static int ConfigureRenderer()
        {
            string path = Folder + "/StudyRenderer.asset";
            if (!AssetDatabase.LoadAssetAtPath<ForwardRendererData>(path))
                AssetDatabase.CopyAsset(Toolkit + "/UniversalRenderPipelineAsset_Renderer.asset", path);
            var studyRenderer = AssetDatabase.LoadAssetAtPath<ForwardRendererData>(path);
            var feature = studyRenderer.rendererFeatures.OfType<PortalRenderFeature>().Single();
            feature.renderMode = PortalRenderFeature.RenderMode.RenderTexture;
            feature.maxDepth = feature.editorMaxDepth = 1;
            feature.maxRenders = feature.editorMaxRenders = 4;
            feature.bufferResolution = feature.editorBufferResolution = 0f;
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(studyRenderer);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                Toolkit + "/UniversalRenderPipelineAsset.asset");
            var serialized = new SerializedObject(pipeline);
            var list = serialized.FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; ++i)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == studyRenderer)
                    return i;

            int index = list.arraySize;
            list.arraySize = index + 1;
            list.GetArrayElementAtIndex(index).objectReferenceValue = studyRenderer;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
            return index;
        }

        public static void Linux()
        {
            string output = Environment.GetEnvironmentVariable("VRPORTAL_PRESENCE_BUILD");
            if (string.IsNullOrEmpty(output)) output = Path.GetFullPath("Builds/PresenceLinux");
            Directory.CreateDirectory(output);

            PlayerSettings.companyName = "VRPortalToolkit";
            PlayerSettings.productName = "Simultaneous Presence";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);

            string[] scenes = new[] { Scene, DesktopDemoBuild.ScenePath }.Concat(
                Directory.GetFiles(Toolkit + "/Examples/Scenes", "*.unity")).ToArray();
            EditorBuildSettings.scenes = scenes.Select(s => new EditorBuildSettingsScene(s, true)).ToArray();
            AssetDatabase.SaveAssets();
            BuildReport report = BuildPipeline.BuildPlayer(scenes,
                Path.Combine(output, "VRPortalPresence.x86_64"), BuildTarget.StandaloneLinux64, BuildOptions.None);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Presence build failed: " + report.summary.result);
            Debug.Log("PRESENCE_BUILD_SUCCEEDED " + output);
        }
    }
}
