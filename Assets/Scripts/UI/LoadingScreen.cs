using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Global loading screen. One static entry — <see cref="Load"/> — covers every scene
/// switch in the game (menu → mode select → match, pause menu → main menu, save reload,
/// restart), so the player never watches a frozen frame while the next scene loads.
///
/// The visual is a prefab (Assets/Resources/LoadingScreen.prefab, built by
/// Assets/Editor/BuildLoadingScreen.cs) so the layout can be tuned in the editor.
/// If the prefab is missing we fall back to a plain synchronous load rather than
/// failing the transition.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    const string PrefabResource = "LoadingScreen";

    static LoadingScreen current;

    [Header("接线（由预制体填充）")]
    [Tooltip("Image.type = Filled, Horizontal, Origin Left。")]
    public Image progressFill;
    public Text tipText;

    [Tooltip("最短展示秒数：本地场景秒切时防止加载界面一闪而过。")]
    public float minSeconds = 1.2f;

    [Tooltip("进度条未就绪时展示的上限；之后等最短时长。")]
    public float asyncProgressCap = 0.9f;

    /// <summary>True while a transition is running, so double-clicks cannot stack two loads.</summary>
    public static bool IsLoading => current != null;

    /// <summary>Show the loading screen and switch to the named scene. Returns false when
    /// a load is already in flight — the caller must NOT lock its own state (buttons,
    /// pause-menu guards) on that path, because nothing is going to happen.</summary>
    public static bool Load(string sceneName)
    {
        if (current != null)
        {
            Debug.LogWarning("[LoadingScreen] 正在加载中，忽略重复请求：" + sceneName);
            return false;
        }
        var prefab = Resources.Load<LoadingScreen>(PrefabResource);
        if (prefab == null)
        {
            Debug.LogWarning("[LoadingScreen] 缺少 Resources/LoadingScreen.prefab，退化为直接切场景。");
            SceneManager.LoadScene(sceneName);
            return true;
        }
        current = Instantiate(prefab);
        DontDestroyOnLoad(current.gameObject);
        current.StartCoroutine(current.LoadRoutine(sceneName));
        return true;
    }

    IEnumerator LoadRoutine(string sceneName)
    {
        AsyncOperation op;
        try
        {
            op = SceneManager.LoadSceneAsync(sceneName);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[LoadingScreen] 无法加载场景 " + sceneName + "：" + e.Message);
            current = null;
            Destroy(gameObject);
            yield break;
        }
        op.allowSceneActivation = false;

        float elapsed = 0f;
        while (op.progress < asyncProgressCap || elapsed < minSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            SetFill(Mathf.Min(op.progress / asyncProgressCap, elapsed / Mathf.Max(0.01f, minSeconds)));
            yield return null;
        }

        SetFill(1f);
        op.allowSceneActivation = true;
        while (!op.isDone) yield return null;

        current = null;
        Destroy(gameObject);
    }

    void SetFill(float v)
    {
        if (progressFill != null) progressFill.fillAmount = Mathf.Clamp01(v);
    }
}
