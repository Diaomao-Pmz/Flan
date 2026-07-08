using UnityEngine;
using System;

// 这是所有敌人的老祖宗类，包含最基础的生命周期和数值
public abstract class EntityBase : MonoBehaviour
{
    [Header("Base Stats")]
    public int maxHP = 100;
    public int currentHP { get; protected set; } // 保护写权限，只能通过方法修改
    public bool isDead { get; protected set; }

    // UI 监听的事件（保留了你组员的优秀设计）
    public event Action OnStatChanged;

    // 留给子类在 Awake 时调用的基础初始化逻辑
    protected virtual void Awake()
    {
        currentHP = maxHP;
        isDead = false;
    }

    // 统一的受伤接口
    public virtual void TakeDamage(int damage)
    {
        if (isDead) return;

        currentHP = Mathf.Clamp(currentHP - damage, 0, maxHP);
        OnStatChanged?.Invoke(); // 通知血条 UI 更新

        if (currentHP <= 0)
        {
            isDead = true;
            Die();
        }
    }

    public virtual void Heal(int amount)
    {
        if (isDead) return;
        currentHP = Mathf.Clamp(currentHP + amount, 0, maxHP);
        OnStatChanged?.Invoke();
    }

    // 抽象/虚方法：具体的死亡表现交给子类自己决定
    protected virtual void Die()
    {
        // 默认行为：销毁物体
        Destroy(gameObject);
    }
}