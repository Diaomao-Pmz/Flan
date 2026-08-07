using System.Collections.Generic;
using UnityEngine;

public class GMPanel : MonoBehaviour
{
    public static GMPanel Instance { get; private set; }

    [Header("Toggle")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F1;
    [SerializeField] private bool visible = false;

    [Header("Preset Commands")]
    [SerializeField] private List<GMCommandDefinition> buttons = new();

    [Header("Layout")]
    [SerializeField] private int maxLogLines = 30;

    private string input = "";
    private Vector2 logScroll;
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
        Log("[GM] panel ready. Press F1 to toggle.");
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            visible = !visible;
    }

    private void OnGUI()
    {
        if (!visible) return;

        float margin = 12f;
        float width = Mathf.Min(900f, Screen.width - margin * 2f);
        float height = Mathf.Min(700f, Screen.height - margin * 2f);

        GUILayout.BeginArea(new Rect(margin, margin, width, height), GUI.skin.window);
        GUILayout.Label("GM Panel");

        GUILayout.BeginHorizontal();
        GUILayout.Label("Command", GUILayout.Width(70));
        GUI.SetNextControlName("GMInput");
        input = GUILayout.TextField(input, GUILayout.ExpandWidth(true));
        if (GUILayout.Button("Run", GUILayout.Width(80)))
            ExecuteInput();
        GUILayout.EndHorizontal();

        GUILayout.Space(8);

        GUILayout.Label("Preset Commands");
        var buttonScroll = GUILayout.BeginScrollView(Vector2.zero, GUILayout.Height(220));
        for (int i = 0; i < buttons.Count; i++)
        {
            var btn = buttons[i];
            GUILayout.BeginHorizontal();

            if (GUILayout.Button(string.IsNullOrWhiteSpace(btn.label) ? $"Button {i}" : btn.label, GUILayout.Width(180)))
            {
                RunCommand(btn.command);
            }

            GUILayout.Label(btn.command, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();

        GUILayout.Space(8);
        GUILayout.Label("Log");

        logScroll = GUILayout.BeginScrollView(logScroll, GUILayout.Height(280));
        foreach (var line in logs)
            GUILayout.Label(line);
        GUILayout.EndScrollView();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Clear Log", GUILayout.Height(28)))
            logs.Clear();

        if (GUILayout.Button("Close", GUILayout.Height(28)))
            visible = false;
        GUILayout.EndHorizontal();

        GUILayout.EndArea();

        if (visible && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
        {
            ExecuteInput();
            Event.current.Use();
        }

        GUI.FocusControl("GMInput");
    }

    private void ExecuteInput()
    {
        if (string.IsNullOrWhiteSpace(input)) return;

        RunCommand(input);
        input = "";
    }

    private void RunCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        Log($"> {command}");

        if (GMRegistry.Execute(command, out var message))
            Log(message);
        else
            Log($"ERR: {message}");
    }

    public void Log(string text)
    {
        logs.Add(text);
        while (logs.Count > maxLogLines)
            logs.RemoveAt(0);
    }
}