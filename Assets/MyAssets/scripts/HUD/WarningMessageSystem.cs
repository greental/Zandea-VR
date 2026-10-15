using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Priority-based warning system: only the highest-priority active warning is shown.
/// Add to ship or HUD root; WarningBannerDisplay reads from here.
/// </summary>
public class WarningMessageSystem : MonoBehaviour
{
    public static WarningMessageSystem Instance { get; private set; }

    [Header("Debug")]
    [Tooltip("When true, show a test warning so you can see the banner.")]
    public bool testWarning;

    readonly List<Entry> _active = new List<Entry>();

    void Awake()
    {
        if (Instance != null && Instance != this)
            Destroy(Instance);
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (testWarning && _active.Count == 0)
            Add("TEST WARNING", 0);
    }

    /// <summary>Add a warning. Higher priority = shown in preference. Same id replaces.</summary>
    public void Add(string message, int priority, string id = null)
    {
        string useId = id ?? message;
        int i = _active.FindIndex(e => e.Id == useId);
        if (i >= 0)
        {
            _active[i] = new Entry(useId, message, priority);
            return;
        }
        _active.Add(new Entry(useId, message, priority));
    }

    public void Remove(string id)
    {
        _active.RemoveAll(e => e.Id == id);
    }

    public void Clear() => _active.Clear();

    /// <summary>Current message to show (highest priority), or null if none.</summary>
    public string CurrentMessage
    {
        get
        {
            if (_active.Count == 0) return null;
            Entry best = _active[0];
            for (int i = 1; i < _active.Count; i++)
                if (_active[i].Priority > best.Priority)
                    best = _active[i];
            return best.Message;
        }
    }

    struct Entry
    {
        public string Id;
        public string Message;
        public int Priority;
        public Entry(string id, string message, int priority) { Id = id; Message = message; Priority = priority; }
    }
}
