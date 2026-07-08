using UnityEngine;

// 纯数据类保持不变，由 PlayerState 统一管理
[System.Serializable]
public class PlayerStats
{
    public float cooldownReduction = 0f;
}

[System.Serializable]
public class PlayerCombat
{
    public float comboWindowTolerance = 0f;

    public void SetSlideCooldown(float time)
    {
        Debug.Log($"[躯干总线] 滑行进入冷却: {time}秒");
    }
}

[System.Serializable]
public class PlayerHealth
{
    [Header("Health & Mana Settings")]
    public int maxHP = 100;
    public int currentHP = 100;
    public int maxMP = 100;
    public int currentMP = 100;
    public bool isUntargetable = false;

    public event System.Action OnStatChanged;

    public void Init()
    {
        currentHP = maxHP;
        currentMP = maxMP;
        OnStatChanged?.Invoke();
    }

    public void TakeDamage(int damage)
    {
        if (isUntargetable) return;

        currentHP -= damage;
        if (currentHP < 0) currentHP = 0;

        OnStatChanged?.Invoke(); // 通知 UI

        Debug.Log($"[健康总线] 芙兰受到了 {damage} 点伤害，当前血量: {currentHP}");
    }

    public void SetHP(int value)
    {
        currentHP = Mathf.Clamp(value, 0, maxHP);
        OnStatChanged?.Invoke();
    }

    public void SetMP(int value)
    {
        currentMP = Mathf.Clamp(value, 0, maxMP);
        OnStatChanged?.Invoke();
    }

    public void AddHP(int value)
    {
        SetHP(currentHP + value);
    }

    public void AddMP(int value)
    {
        SetMP(currentMP + value);
    }

    public void SetUntargetable(bool state)
    {
        isUntargetable = state;
        Debug.Log($"[躯干总线] 芙兰的无敌状态改变为: {state}");
    }
}

// 【这是你需要挂载到玩家身上的组件】
public class PlayerState : MonoBehaviour
{
    [Header("核心数据总线")]
    public PlayerStats stats = new PlayerStats();
    public PlayerHealth health = new PlayerHealth();
    public PlayerCombat combat = new PlayerCombat();

    void Awake()
    {
        // 统一在此初始化运行时数据
        health.Init();
    }
}