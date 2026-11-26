using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CoOpHorror.Editor
{
    // PS1-style low-quality look for the PC quality level: low render scale upscaled with point filtering,
    // no MSAA, local light shadows (the flashlight), and a full-screen colour-depth reduction with ordered
    // dithering and vignette (CoOp/PS1Screen). Re-runnable; called by the prototype scene builder.
    public static class ElecMoleLookSetup
    {
        public const float RenderScale = 0.65f;
        private const string PipelinePath = "Assets/Settings/PC_RPAsset.asset";
        private const string RendererPath = "Assets/Settings/PC_Renderer.asset";
        private const string MaterialPath = "Assets/_Game/Materials/Prototype/PS1Screen.mat";
        private const string FeatureName = "PS1Screen";
        private const string CctvRendererPath = "Assets/Settings/PC_Renderer_Cctv.asset";
        public const int CctvRendererIndex = 1; // CCTV feed: same renderer without the PS1 pass (it crushes dark rooms)

        [MenuItem("CoOp/Setup/Apply PS1 look (PC quality)")]
        public static void Apply()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline != null)
            {
                pipeline.renderScale = RenderScale;
                pipeline.upscalingFilter = UpscalingFilterSelection.Point;
                pipeline.msaaSampleCount = 1;
                pipeline.supportsCameraDepthTexture = true;
                var so = new SerializedObject(pipeline);
                var shadows = so.FindProperty("m_AdditionalLightShadowsSupported");
                if (shadows != null) shadows.boolValue = true;
                var resolution = so.FindProperty("m_AdditionalLightsShadowmapResolution");
                if (resolution != null) resolution.intValue = 1024;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(pipeline);
            }

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null) return;
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
                material = new Material(Shader.Find("CoOp/PS1Screen"));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.SetFloat("_Levels", 22f);
            material.SetFloat("_Dither", 0.6f);
            material.SetFloat("_Vignette", 1.1f);
            material.SetFloat("_Lift", 0.75f);
            material.SetFloat("_Outline", 0.55f);
            material.SetFloat("_OutlineThreshold", 0.12f);
            material.SetFloat("_OutlineFade", 22f);
            material.SetFloat("_Aberration", 0.004f);
            material.SetFloat("_Grain", 0.06f);
            EditorUtility.SetDirty(material);

            FullScreenPassRendererFeature feature = null;
            foreach (var f in renderer.rendererFeatures)
                if (f is FullScreenPassRendererFeature fs && f.name == FeatureName) feature = fs;
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                feature.name = FeatureName;
                AssetDatabase.AddObjectToAsset(feature, renderer);
                renderer.rendererFeatures.Add(feature);
                // Keep the serialized feature map in step with the list (what the renderer inspector does).
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                var so = new SerializedObject(renderer);
                var map = so.FindProperty("m_RendererFeatureMap");
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            feature.passMaterial = material;
            // Apply to the entire world, including transparent particles, before the final colour/bloom pass.
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
            feature.fetchColorBuffer = true;
            feature.requirements = ScriptableRenderPassInput.Depth; // depth-edge outlines
            feature.SetActive(true);
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
            AddCctvRenderer(pipeline);
            PixelFonts();
        }

        // Fusion Pixel must render as hard pixels (no anti-aliasing) to stay crisp at 12 px multiples.
        private static void PixelFonts()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Font", new[] { "Assets/_Game/Resources/Fonts/FusionPixel" }))
            {
                var importer = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as TrueTypeFontImporter;
                if (importer == null || importer.fontRenderingMode == FontRenderingMode.HintedRaster) continue;
                importer.fontRenderingMode = FontRenderingMode.HintedRaster;
                importer.fontTextureCase = FontTextureCase.Dynamic;
                importer.SaveAndReimport();
            }
        }

        // Second renderer for the CCTV feed camera: a copy of the PC renderer with the PS1 pass switched off.
        private static void AddCctvRenderer(UniversalRenderPipelineAsset pipeline)
        {
            if (pipeline == null) return;
            AssetDatabase.DeleteAsset(CctvRendererPath);
            if (!AssetDatabase.CopyAsset(RendererPath, CctvRendererPath)) return;
            var cctv = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(CctvRendererPath);
            foreach (var f in cctv.rendererFeatures)
                if (f != null && f.name == FeatureName) { f.SetActive(false); EditorUtility.SetDirty(f); }
            EditorUtility.SetDirty(cctv);
            var so = new SerializedObject(pipeline);
            var list = so.FindProperty("m_RendererDataList");
            while (list.arraySize <= CctvRendererIndex) list.arraySize++;
            list.GetArrayElementAtIndex(CctvRendererIndex).objectReferenceValue = cctv;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();
        }
    }
}
