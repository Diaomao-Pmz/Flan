using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthBar : MonoBehaviour
{
    [Header("绑定目标 (二选一即可)")]
    public EntityBase targetEntity; // 小怪拖给这个
    public BossState bossState;     // Boss拖给这个 (你需要在Inspector里把Boss拖进来)

    [Header("UI 组件")]
    public Image fillImage;
    public GameObject rootObject;

    private void OnEnable()
    {
        // 智能订阅：有 Boss 就听 Boss 的，否则听小怪的
        if (bossState != null) bossState.health.OnStatChanged += Refresh;
        else if (targetEntity != null) targetEntity.OnStatChanged += Refresh;
    }

    private void OnDisable()
    {
        if (bossState != null) bossState.health.OnStatChanged -= Refresh;
        else if (targetEntity != null) targetEntity.OnStatChanged -= Refresh;
    }

    private void Start()
    {
        Refresh();
    }

    public void Refresh()
    {
        if (fillImage == null) return;

        // 智能读取：读取真正发生变化的血量数据
        if (bossState != null)
        {
            fillImage.fillAmount = (float)bossState.health.currentHP / bossState.health.maxHP;
            if (rootObject != null) rootObject.SetActive(bossState.health.currentHP < bossState.health.maxHP);
        }
        else if (targetEntity != null)
        {
            fillImage.fillAmount = (float)targetEntity.currentHP / targetEntity.maxHP;
            if (rootObject != null) rootObject.SetActive(targetEntity.currentHP < targetEntity.maxHP);
        }
    }
}