using UnityEngine;
using System;
using Flandre.CombatSystem;

// 这是所有敌人的老祖宗类，包含最基础的生命周期和数值
public abstract class EntityBase : MonoBehaviour, IDamageable
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

    /// <summary>
    /// 【统一受伤接口】敌我双方共用的唯一核心，子类重写这一个即可。
    /// 原先的 TakeDamage(int, DamageType) 已降级为转发用的非虚方法。
    /// </summary>
    public virtual void TakeDamage(in DamageInfo info)
    {
        if (isDead) return;

        currentHP = Mathf.Clamp(currentHP - info.amount, 0, maxHP);
        OnStatChanged?.Invoke(); // 通知血条 UI 更新

        if (currentHP <= 0)
        {
            isDead = true;
            Die();
        }
    }

    /// <summary>
    /// 【过渡用】旧签名。缺少来源坐标，只能用自身位置顶替，
    /// 因此拿不到正确的击退方向。请改用 DamageInfo 版本。
    /// 注意：这里是非虚方法 —— 子类若还写着 override 会编译报错（CS0506），
    /// 这是刻意的，避免重写了旧签名却在新调用路径下静默失效。
    /// </summary>
    [Obsolete("请改用 TakeDamage(in DamageInfo)，以便携带伤害来源坐标。")]
    public void TakeDamage(int damage, DamageType type = DamageType.Melee)
    {
        TakeDamage(new DamageInfo(damage, type, transform.position, null));
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
