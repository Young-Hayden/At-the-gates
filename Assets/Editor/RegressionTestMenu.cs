using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

/// <summary>Creates the isolated regression scene and runs the PlayMode suite.</summary>
[InitializeOnLoad]
public static class RegressionTestMenu
{
    const string SceneDirectory = "Assets/Tests/Scenes";
    const string ScenePath = SceneDirectory + "/RegressionTests.scene";
    const string TestAssembly = "AtTheGates.PlayModeTests";
    const string OwnedRunKey = "AtTheGates.RegressionTestMenu.OwnedRun";
    const string FilteredFullNameKey = "AtTheGates.RegressionTestMenu.FilteredFullName";
    const string LogRelativePath = "Logs/RegressionTests.txt";

    static TestRunnerApi runner;
    static TestRunnerApi callbackRelay;
    static RegressionCallbacks callbacks;

    static RegressionTestMenu()
    {
        EnsureCallbacks();
    }

    static bool IsMenuOwnedRun => SessionState.GetBool(OwnedRunKey, false);

    static string LogPath => Path.GetFullPath(Path.Combine(
        Path.Combine(Application.dataPath, ".."), LogRelativePath));

    // PlayMode normally reloads the editor domain, which drops TestRunnerApi
    // callbacks. Re-register once in each domain so the menu always reports results.
    static void EnsureCallbacks()
    {
        if (callbackRelay != null) return;
        callbackRelay = ScriptableObject.CreateInstance<TestRunnerApi>();
        callbacks = new RegressionCallbacks();
        callbackRelay.RegisterCallbacks(callbacks);
    }

    [MenuItem("Tools/Regression/Create Isolated Scene")]
    public static void CreateIsolatedScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Regression] Leave PlayMode before creating the isolated scene.");
            return;
        }

        Directory.CreateDirectory(SceneDirectory);
        AssetDatabase.Refresh();

        var previousActive = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        try
        {
            SceneManager.SetActiveScene(scene);

            var gridObject = new GameObject("Grid");
            gridObject.AddComponent<Grid>();

            var map = gridObject.AddComponent<MapGrid>();
            map.ground0 = CreateTilemap(gridObject.transform, "Ground 0");
            map.top1 = CreateTilemap(gridObject.transform, "Top 1");
            map.cliff1 = CreateTilemap(gridObject.transform, "Cliff 1");
            map.top2 = CreateTilemap(gridObject.transform, "Top 2");
            map.cliff2 = CreateTilemap(gridObject.transform, "Cliff 2");
            map.rampLayer = CreateTilemap(gridObject.transform, "Ramp Layer");

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 10f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;

            var managers = new GameObject("Regression Managers");
            managers.AddComponent<Economy>();
            managers.AddComponent<GameState>();
            managers.AddComponent<BuildingPlacer>();
            var selection = managers.AddComponent<SelectionManager>();
            selection.cam = camera;

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Could not save " + ScenePath);

            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[Regression] Created isolated scene: " + ScenePath);
        }
        finally
        {
            if (scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
            if (previousActive.IsValid() && previousActive.isLoaded)
                SceneManager.SetActiveScene(previousActive);
        }
    }

    static Tilemap CreateTilemap(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tilemap = go.AddComponent<Tilemap>();
        go.AddComponent<TilemapRenderer>();
        return tilemap;
    }

    [MenuItem("Tools/Regression/Run PlayMode Tests")]
    public static void RunPlayModeTests() { RunTests(null); }

    public static void RunPlayModeTest(string fullName)
    {
        if (string.IsNullOrEmpty(fullName)) throw new System.ArgumentException("A full test name is required", nameof(fullName));
        RunTests(new[] { fullName });
    }

    static void RunTests(string[] testNames)
    {
        // A run that ended outside RunFinished (editor crash / forced exit) must
        // not wedge the menu forever.
        if (IsMenuOwnedRun && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            SessionState.SetBool(OwnedRunKey, false);
            SessionState.SetString(FilteredFullNameKey, string.Empty);
        }
        if (runner != null || IsMenuOwnedRun)
        {
            Debug.LogWarning("[Regression] A PlayMode test run is already active.");
            return;
        }

        EnsureCallbacks();
        SessionState.SetBool(OwnedRunKey, true);
        SessionState.SetString(FilteredFullNameKey,
            testNames == null ? string.Empty : testNames[0]);
        runner = ScriptableObject.CreateInstance<TestRunnerApi>();
        try
        {
            runner.Execute(new ExecutionSettings
            {
                filters = new[]
                {
                    new Filter
                    {
                        testMode = TestMode.PlayMode,
                        assemblyNames = new[] { TestAssembly },
                        testNames = testNames
                    }
                }
            });
        }
        catch
        {
            SessionState.SetBool(OwnedRunKey, false);
            SessionState.SetString(FilteredFullNameKey, string.Empty);
            DestroyRunnerObjects(immediate: true);
            throw;
        }
    }

    sealed class RegressionCallbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun)
        {
            if (!IsMenuOwnedRun) return;
            string filteredFullName = SessionState.GetString(FilteredFullNameKey, string.Empty);
            int testCaseCount = string.IsNullOrEmpty(filteredFullName)
                ? testsToRun.TestCaseCount
                : CountMatchingTestCases(testsToRun, filteredFullName);
            string line = "[Regression] PlayMode run started. Test cases: " + testCaseCount;
            Debug.Log(line);
            WriteLog(line, overwrite: true);
        }

        static int CountMatchingTestCases(ITestAdaptor test, string fullName)
        {
            if (!test.IsSuite)
                return string.Equals(test.FullName, fullName, System.StringComparison.Ordinal) ? 1 : 0;

            int count = 0;
            foreach (var child in test.Children)
                count += CountMatchingTestCases(child, fullName);
            return count;
        }

        public void TestStarted(ITestAdaptor test)
        {
        }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (!IsMenuOwnedRun) return;
            if (result.Test != null && result.Test.IsSuite) return;

            var line = string.Format(
                "[Regression] {0}: {1} ({2:0.000}s)",
                result.ResultState, result.FullName, result.Duration);
            if (!string.IsNullOrEmpty(result.Message))
                line += "\n" + result.Message;
            if (!string.IsNullOrEmpty(result.StackTrace))
                line += "\n" + result.StackTrace;
            Debug.Log(line);
            WriteLog(line, overwrite: false);
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            if (!IsMenuOwnedRun) return;

            var line = string.Format(
                "[Regression] PlayMode run finished: {0}. Passed={1}, Failed={2}, Skipped={3}, Inconclusive={4}, Duration={5:0.000}s",
                result.ResultState, result.PassCount, result.FailCount,
                result.SkipCount, result.InconclusiveCount, result.Duration);
            Debug.Log(line);
            WriteLog(line, overwrite: false);

            SessionState.SetBool(OwnedRunKey, false);
            SessionState.SetString(FilteredFullNameKey, string.Empty);
            DestroyRunnerObjects();
        }
    }

    /// <summary>Writes the same lines that go to the console straight to
    /// Logs/RegressionTests.txt, so the artifact is produced by the run itself
    /// rather than reconstructed afterwards.</summary>
    static void WriteLog(string text, bool overwrite)
    {
        try
        {
            string path = LogPath;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var encoding = new UTF8Encoding(false);
            if (overwrite) File.WriteAllText(path, text + "\n", encoding);
            else File.AppendAllText(path, text + "\n", encoding);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Regression] Could not write " + LogRelativePath + ": " + e.Message);
        }
    }

    /// <summary>Releases the TestRunnerApi instances once the menu-owned run is
    /// over. Deferred destruction is used from inside the RunFinished callback;
    /// immediate destruction is safe from the Execute exception path.</summary>
    static void DestroyRunnerObjects(bool immediate = false)
    {
        var api = runner;
        var relay = callbackRelay;
        runner = null;
        callbackRelay = null;
        callbacks = null;

        if (immediate)
        {
            if (api != null) Object.DestroyImmediate(api);
            if (relay != null) Object.DestroyImmediate(relay);
        }
        else
        {
            if (api != null) Object.Destroy(api);
            if (relay != null) Object.Destroy(relay);
        }
    }
}
