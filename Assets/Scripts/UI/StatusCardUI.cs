using UnityEngine;
using TMPro;

public class StatusCardUI : MonoBehaviour
{
    // 将变量名改为 playerState 更清晰，防止与总枢纽 Player.cs 混淆
    public PlayerState playerState;

    public TextMeshProUGUI hpText;
    public TextMeshProUGUI mpText;

    private void Start()
    {
        if (playerState != null)
        {
            // 事件现在在 playerState 的 health 模块中
            playerState.health.OnStatChanged += UpdateUI;
            UpdateUI();
        }
    }

    private void OnDestroy()
    {
        if (playerState != null)
            playerState.health.OnStatChanged -= UpdateUI;
    }

    void UpdateUI()
    {
        // 数据现在都在 playerState.health 中
        hpText.text = $"{playerState.health.currentHP} / {playerState.health.maxHP}";
        mpText.text = $"{playerState.health.currentMP} / {playerState.health.maxMP}";
    }
}