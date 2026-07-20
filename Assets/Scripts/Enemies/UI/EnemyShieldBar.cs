using UnityEngine;
using UnityEngine.UI;

// 1. 确保这里的名字和你的文件名 EnemyShieldBar.cs 一字不差！
public class EnemyShieldBar : MonoBehaviour
{
    public BossState bossState;
    public Image shieldFillImage;

    // 2. 抛弃 Start，改用 OnEnable。只要这个 UI 亮着，它就一定能监听到！
    void OnEnable()
    {
        if (bossState != null)
        {
            // 订阅护盾专属的刷新广播
            bossState.bossMechanic.OnShieldStatChanged += RefreshShieldUI;
            RefreshShieldUI();
        }
    }

    // 3. 养成好习惯：UI 隐藏或销毁时，立刻取消监听，防止报错
    void OnDisable()
    {
        if (bossState != null)
        {
            bossState.bossMechanic.OnShieldStatChanged -= RefreshShieldUI;
        }
    }

    private void RefreshShieldUI()
    {
        // 防呆设计：如果图片丢了，直接 return，不让游戏崩溃
        if (bossState == null || shieldFillImage == null) return;

        // 计算百分比并更新进度条 (你的这句代码写得非常标准！)
        float percent = (float)bossState.bossMechanic.shieldCurrentHP / bossState.bossMechanic.shieldMaxHP;
        shieldFillImage.fillAmount = percent;
    }
}