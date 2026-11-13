using System.Collections.Generic;
using UnityEngine;

/// <summary>A manually clearable decorative bush. It is not a resource node and gives
/// no resources; it only provides a target for a worker's clear order.</summary>
public class ClearableBush : MonoBehaviour
{
    public static readonly List<ClearableBush> All = new List<ClearableBush>();

    [Tooltip("工人清除这丛草需要多少秒。")]
    public float clearSeconds = 1f;
    public SpriteRenderer visual;

    /// <summary>The prefab root, since this component lives on the Sprite child.</summary>
    public GameObject Root => transform.parent != null ? transform.parent.gameObject : gameObject;

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        if (visual == null) visual = GetComponentInChildren<SpriteRenderer>();
    }

    void OnDisable()
    {
        All.Remove(this);
        GarnishRules.Invalidate();
    }
}
