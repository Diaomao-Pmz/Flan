using UnityEngine;

public class PlayerUIManager : MonoBehaviour
{
    [Header("UI 面板引用")]
    public GameObject skillTreePanel;

    [Header("玩家引用")]
    public PlayerState playerState; // 用于监听血量等数据变化以更新UI

    void Start()
    {
        // 订阅血量变化事件（如果之后需要更新血条）
        if (playerState != null)
        {
            playerState.health.OnStatChanged += RefreshHealthUI;
        }
    }

    void Update()
    {
        // UI 的唤出和关闭独立于玩家动作 Controller 之外
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (skillTreePanel != null)
            {
                skillTreePanel.SetActive(!skillTreePanel.activeSelf);
            }
        }
    }

    private void RefreshHealthUI()
    {
        // 在这里更新你的血条 Image 或 Slider
        // float healthPercent = (float)playerState.health.currentHP / playerState.health.maxHP;
    }

    void OnDestroy()
    {
        // 别忘了取消订阅，防止内存泄漏
        if (playerState != null)
        {
            playerState.health.OnStatChanged -= RefreshHealthUI;
        }
    }
}