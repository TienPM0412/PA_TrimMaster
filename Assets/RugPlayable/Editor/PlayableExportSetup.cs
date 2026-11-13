using System;
using System.Linq;
using RugMaster;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
public static class PlayableExportSetup
{
    [MenuItem("Rug Playable/Bake Assets Into Current Scene")]
    public static void BakeAssets()
    {
        var game = UnityEngine.Object.FindObjectOfType<GameManager>();
        if (game == null) throw new Exception("Open the gameplay scene first");
        var library = game.GetComponent<RugPlayableAssets>();
        if (library == null) library = game.gameObject.AddComponent<RugPlayableAssets>();
        var entries = new System.Collections.Generic.List<RugPlayableAssetEntry>();
        const string resourceRoot = "Assets/RugPlayable/Resources/";
        foreach (var guid in AssetDatabase.FindAssets("", new[] { resourceRoot.TrimEnd('/') }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(path)) continue;
            string key = path.Substring(resourceRoot.Length);
            key = key.Substring(0, key.LastIndexOf('.'));
            var entry = new RugPlayableAssetEntry { path = key };
            entry.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            entry.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            entry.font = AssetDatabase.LoadAssetAtPath<Font>(path);
            entry.material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (text != null) entry.json = text.text;
            if (entry.texture != null || entry.sprite != null || entry.font != null || entry.material != null || text != null)
                entries.Add(entry);
        }
        library.entries = entries.ToArray();
        library.level = JsonUtility.FromJson<LevelDefinition>(Resources.Load<TextAsset>(Constants.LevelResource(1)).text);
        library.originalManifest = JsonUtility.FromJson<OriginalManifest>(Resources.Load<TextAsset>("RugMaster/OriginalUI/manifest").text);
        library.originalDom = JsonUtility.FromJson<OriginalDom>(Resources.Load<TextAsset>("RugMaster/OriginalUI/dom-layout").text);
        library.presentationManifest = JsonUtility.FromJson<RugPresentationManifest>(Resources.Load<TextAsset>("RugMaster/Presentation/manifest").text);
        // JSON is parsed only by the editor. Playworks receives concrete serialized fields.
        foreach (var entry in library.entries) entry.json = null;
        library.Bind();
        game.assets = library;
        if (game.GetComponent<ControllerManager>() == null) game.gameObject.AddComponent<ControllerManager>();
        if (game.GetComponent<BoosterManager>() == null) game.gameObject.AddComponent<BoosterManager>();
        if (game.GetComponent<LevelManager>() == null) game.gameObject.AddComponent<LevelManager>();
        if (game.GetComponent<RugTrimEffects>() == null) game.gameObject.AddComponent<RugTrimEffects>();
        if (game.GetComponent<RugPresentation>() == null) game.gameObject.AddComponent<RugPresentation>();
        var view = game.GetComponent<RugOriginalView>();
        if (view != null && view.canvas != null)
            view.Bind(new RugSimulation(library.level), 1);
        EditorUtility.SetDirty(library);
        EditorUtility.SetDirty(game);
        EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
        EditorSceneManager.SaveScene(game.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("PLAYABLE_ASSETS_BAKED: " + entries.Count + " entries; JSON embedded; managers serialized.");
    }
    public static void BakeScene()
    {
        EditorSceneManager.OpenScene("Assets/RugPlayable/Scenes/GamePlay.unity");
        BakeAssets();
    }
    public static void VerifyRuntime()
    {
        EditorSceneManager.OpenScene("Assets/RugPlayable/Scenes/GamePlay.unity");
        foreach (var image in UnityEngine.Object.FindObjectsOfType<Image>(true))
        {
            if (image.sprite != null && !AssetDatabase.Contains(image.sprite))
                throw new Exception("Nonpersistent sprite on " + image.name);
        }
        Debug.Log("PLAYABLE_SPRITE_ASSETS_OK: scene UI uses persistent imported sprites.");
        SessionState.SetBool("RugPlayable.Verify", true);
        EditorApplication.isPlaying = true;
    }
    [InitializeOnLoadMethod]
    private static void InstallRuntimeCheck()
    {
        if (!SessionState.GetBool("RugPlayable.Verify", false)) return;
        double started = EditorApplication.timeSinceStartup;
        bool failed = false;
        Application.logMessageReceived += (message, stack, type) =>
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failed = true; };
        EditorApplication.CallbackFunction check = null;
        check = () =>
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - started < 6) return;
            EditorApplication.update -= check;
            SessionState.SetBool("RugPlayable.Verify", false);
            try
            {
                if (failed) throw new Exception("Runtime logged an error");
                var game = GameManager.Ins;
                var view = game.View;
                var panel = view.selector.transform.Find("Pixeland selector/Panel").GetComponent<Image>();
                if (panel.sprite == null || panel.type != Image.Type.Sliced || panel.color != Color.white)
                    throw new Exception("Trim selector frame missing");
                if (view.PresentationBlocksInput) throw new Exception("Intro did not finish");
                foreach (int width in new[] {1, 4, 2})
                {
                    view.FindUI("Head hit target " + width).GetComponent<Button>().onClick.Invoke();
                    if (ControllerManager.Ins.HeadWidth != width) throw new Exception("Head UI failed");
                }
                var sim = LevelManager.Ins.Simulation;
                var color = sim.Cells.GroupBy(c => c.Color).OrderByDescending(g => g.Count()).First().Key;
                foreach (var cell in sim.Cells.Where(c => c.Color == color).Take(5)) sim.TryTrim(cell.Index);
                if (sim.TrimmedCount != 5) throw new Exception("Runtime trim failed");
                view.boosters[1].GetComponent<Button>().onClick.Invoke();
                if (BoosterManager.Ins.Sweeps != 1) throw new Exception("Sweep UI failed");
                for (int i = 0; i < 100; i++) sim.Tick(.02f);
                sim.ValidateEconomy();
                if (view.FindUI("Settings button") != null) throw new Exception("Settings still present");
                if (failed) throw new Exception("Interaction logged an error");
                Debug.Log("PLAYABLE_RUNTIME_OK: trim selector frame, intro, three head buttons, trim, sweep button, economy, no settings.");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        };
        EditorApplication.update += check;
    }
    [MenuItem("Rug Playable/Rebuild Gameplay Scene")]
    public static void CreateScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("Rug Playable");
        root.AddComponent<GameManager>();
        // Persist the empty scene before baking the asset library used by hierarchy generation.
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/RugPlayable/Scenes/GamePlay.unity");
        BakeAssets();
        var view = root.AddComponent<RugOriginalView>();
        view.BuildHierarchy();
        var text = Resources.Load<TextAsset>(Constants.LevelResource(1));
        view.Bind(new RugSimulation(JsonUtility.FromJson<LevelDefinition>(text.text)), 1);
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        var camera = new GameObject("Main Camera", typeof(Camera));
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0, 0, -10);
        camera.GetComponent<Camera>().orthographic = true;
        const string path = "Assets/RugPlayable/Scenes/GamePlay.unity";
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), path);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
        AssetDatabase.SaveAssets();
        ValidateSimulation();
        Debug.Log("PLAYABLE_EXPORT_READY: scene saved; simulation, trim, boosters verified.");
    }
    private static void ValidateSimulation()
    {
        var text = Resources.Load<TextAsset>(Constants.LevelResource(1));
        var sim = new RugSimulation(JsonUtility.FromJson<LevelDefinition>(text.text));
        sim.ValidateEconomy();
        if (sim.Cells.Length == 0) throw new Exception("Empty rug");
        var color = sim.Cells.GroupBy(c => c.Color).OrderByDescending(g => g.Count()).First().Key;
        foreach (var cell in sim.Cells.Where(c => c.Color == color).Take(5))
            if (!sim.TryTrim(cell.Index)) throw new Exception("Trim failed");
        if (sim.TrimmedCount != 5) throw new Exception("Trim count mismatch");
        sim.Sweep(); sim.Rearrange();
        for (int i = 0; i < 100; i++) sim.Tick(.02f);
        sim.ValidateEconomy();
    }
}
