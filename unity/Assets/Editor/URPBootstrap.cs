// Editor-only helper: on first open of a freshly cloned project, creates a
// Universal Render Pipeline asset in Assets/Settings/ and assigns it in
// Graphics + Quality settings so the project renders with URP out of the box.
// Safe to keep: it does nothing once a render pipeline is assigned.
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EcoBotBuilders.EditorTools
{
    [InitializeOnLoad]
    public static class URPBootstrap
    {
        private const string SettingsFolder = "Assets/Settings";
        private const string RendererPath = SettingsFolder + "/EcoBot_URP_Renderer.asset";
        private const string PipelinePath = SettingsFolder + "/EcoBot_URP.asset";

        static URPBootstrap()
        {
            EditorApplication.delayCall += EnsureUrpAssigned;
        }

        [MenuItem("Eco-Bot Builders/Setup URP Pipeline")]
        public static void EnsureUrpAssigned()
        {
            // Import packages first. The offline scene uses ScreenSpaceOverlay sprites;
            // it does not rely on this helper or any legacy gameplay assembly.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += EnsureUrpAssigned;
                return;
            }
            if (GraphicsSettings.defaultRenderPipeline != null) return;

            if (!AssetDatabase.IsValidFolder(SettingsFolder))
                AssetDatabase.CreateFolder("Assets", "Settings");

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
                if (renderer == null)
                {
                    renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                    ResourceReloader.TryReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
                    AssetDatabase.CreateAsset(renderer, RendererPath);
                }

                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);

            AssetDatabase.SaveAssets();
            Debug.Log("[Eco-Bot Builders] URP pipeline asset created and assigned: " + PipelinePath);
        }
    }
}
#endif
