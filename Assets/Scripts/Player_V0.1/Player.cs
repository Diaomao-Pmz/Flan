using UnityEngine;
using Flandre.CombatSystem;

[RequireComponent(typeof(PlayerState), typeof(PlayerController))]
public class Player : MonoBehaviour
{
    public PlayerState state { get; private set; }
    public PlayerController controller { get; private set; }

    void Awake()
    {
        state = GetComponent<PlayerState>();
        controller = GetComponent<PlayerController>();
    }

    // 敌人命中玩家时，统一调用这个接口
    public void TakeDamage(int damage)
    {
        // 调用状态机的数据扣血
        ((IDamageable)state).TakeDamage(new DamageInfo(damage, DamageType.Melee, transform.position, null));

        // 检查生命周期逻辑（例如是否死亡）
        if (state.health.currentHP <= 0)
        {
            Die();
        }
    }

    public void Die()
    {
        Debug.Log("玩家死亡！");
        // 可以在这里触发死亡动画，或者通知 GameManager
        // controller.anim.SetTrigger("Die");
    }
}

