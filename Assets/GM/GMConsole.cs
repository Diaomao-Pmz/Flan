using System.Collections.Generic;
using UnityEngine;

public class GMConsole : MonoBehaviour
{
    public static GMConsole Instance { get; private set; }

    [SerializeField] private bool visible;
    [SerializeField] private int maxLogLines = 20;

    private string input = "";
    private Vector2 scroll;
    private readonly List<string> logs = new();

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        GMRegistry.Init();
        Log("[GM] console ready. Press F1 to toggle.");
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1))
        {
            visible = !visible;
            if (visible)
                Log("[GM] opened");
        }
    }

    private void OnGUI()
    {
        if (!visible) return;

        const float margin = 12f;
        float width = Mathf.Min(900f, Screen.width - margin * 2f);
        float height = Mathf.Min(600f, Screen.height - margin * 2f);

        GUILayout.BeginArea(new Rect(margin, margin, width, height), GUI.skin.window);
        GUILayout.Label("GM Console");

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(height - 110f));
        for (int i = 0; i < logs.Count; i++)
            GUILayout.Label(logs[i]);
        GUILayout.EndScrollView();

        GUI.SetNextControlName("GMInput");
        input = GUILayout.TextField(input, GUILayout.Height(28f));
        GUI.FocusControl("GMInput");

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Execute", GUILayout.Height(30f)))
            RunInput();

        if (GUILayout.Button("Clear", GUILayout.Height(30f)))
            logs.Clear();

        if (GUILayout.Button("Close", GUILayout.Height(30f)))
            visible = false;

        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        if (visible && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
        {
            RunInput();
            Event.current.Use();
        }
    }

    private void RunInput()
    {
        if (string.IsNullOrWhiteSpace(input))
            return;

        Log($"> {input}");

        if (GMRegistry.Execute(input, out var message))
            Log(message);
        else
            Log($"ERR: {message}");

        input = "";
    }

    public void Log(string text)
    {
        logs.Add(text);
        while (logs.Count > maxLogLines)
            logs.RemoveAt(0);
    }
}