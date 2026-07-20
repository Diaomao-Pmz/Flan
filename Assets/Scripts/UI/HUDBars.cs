using UnityEngine;
using UnityEngine.UI;

public class HUDBars : MonoBehaviour
{
    // 关键修复：UI 需要的是状态数据，类型从 PlayerController 改为 PlayerState
    public PlayerState playerState;

    public Image hpFill;
    public Image mpFill;

    private void Start()
    {
        if (playerState != null)
        {
            // 订阅玩家健康系统里的广播
            playerState.health.OnStatChanged += UpdateBars;
            UpdateBars();
        }
    }

    private void OnDestroy()
    {
        if (playerState != null)
            playerState.health.OnStatChanged -= UpdateBars;
    }

    void UpdateBars()
    {
        if (playerState == null) return;

        // 根据 playerState.health 里的数据计算填充百分比
        if (hpFill != null) hpFill.fillAmount = (float)playerState.health.currentHP / playerState.health.maxHP;
        if (mpFill != null) mpFill.fillAmount = (float)playerState.health.currentMP / playerState.health.maxMP;
    }

    // DEBUG 测试按钮调用的方法
    public void DBG_AddHP() { playerState.health.AddHP(10); }
    public void DBG_SubHP() { playerState.health.TakeDamage(10, Vector2.zero, playerState); }
    public void DBG_AddMP() { playerState.health.AddMP(10); }
    public void DBG_SubMP() { playerState.health.AddMP(-10); }
}