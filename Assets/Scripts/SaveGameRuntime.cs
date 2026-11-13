using System.Collections;
using UnityEngine;

/// <summary>Completes a requested load after the game scene has finished starting.</summary>
public class SaveGameRuntime : MonoBehaviour
{
    void Start()
    {
        if (!SaveGameService.ConsumePendingLoad(out var data, out string error))
        {
            if (!string.IsNullOrEmpty(error)) Debug.LogWarning("[Save] " + error);
            return;
        }
        StartCoroutine(Restore(data));
    }

    IEnumerator Restore(SaveGameData data)
    {
        bool ok = false;
        string error = null;
        yield return SaveGameService.RestoreSnapshot(data, (success, reason) =>
        {
            ok = success;
            error = reason;
        });
        if (!ok) Debug.LogError("[Save] 读取存档失败：" + error);
        else Debug.Log("[Save] 存档读取完成。");
    }
}
