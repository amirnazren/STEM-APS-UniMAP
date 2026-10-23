#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using EcoBotBuilders.Level1.View;

namespace EcoBotBuilders.Level1.EditorTools
{
    /// <summary>Optional recovery menu. The real scene is already shipped and needs no manual assembly.</summary>
    public static class Level1SceneSetup
    {
        public const string ScenePath = "Assets/Scenes/MainGameScene.unity";
        [MenuItem("Eco-Bot Builders/Level 1/Open Main Game Scene")]
        public static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }
        [MenuItem("Eco-Bot Builders/Level 1/Rebuild Scene (recovery)")]
        public static void Rebuild()
        {
            if (!EditorUtility.DisplayDialog("Rebuild Level 1 scene?", "This replaces MainGameScene with the offline auto-wired scene. Existing unsaved scenes can be saved first.", "Rebuild", "Cancel")) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)); camera.tag="MainCamera";
            camera.transform.position=new Vector3(0,0,-10);
            camera.GetComponent<Camera>().orthographic=true;
            camera.GetComponent<Camera>().clearFlags=CameraClearFlags.SolidColor;
            camera.GetComponent<Camera>().backgroundColor=IslandUI.Ocean;
            new GameObject("Tideleaf Island — Offline Level 1", typeof(Level1Game));
            EditorSceneManager.SaveScene(scene,ScenePath);
            var scenes=new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int index=scenes.FindIndex(s=>s.path==ScenePath);
            if(index>=0)scenes.RemoveAt(index);
            scenes.Insert(0,new EditorBuildSettingsScene(ScenePath,true));
            EditorBuildSettings.scenes=scenes.ToArray();
            Debug.Log("[Tideleaf] Scene rebuilt and set as the first build scene. Press Play; all UI, artwork, input and sound wire themselves.");
        }
    }
}
#endif
