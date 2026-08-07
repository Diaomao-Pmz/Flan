using UnityEngine;

public class GameGM : MonoBehaviour
{
    [SerializeField] private bool createConsoleOnStart = true;

    private void Awake()
    {
        if (createConsoleOnStart && GMConsole.Instance == null)
        {
            var go = new GameObject("[GM Console]");
            go.AddComponent<GMConsole>();
        }
    }
}