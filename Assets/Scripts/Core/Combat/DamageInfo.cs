using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 统一的伤害载荷。敌我双方共用同一份契约。
    ///
    /// 设计要点：**这里没有击退力度字段，这是有意为之。**
    /// 击退的「力度」归受击方所有（玩家在 PlayerStateMachine.hitKnockbackForce 里配置，
    /// 由 HitState 读取），攻击方只负责提供 sourcePosition —— 也就是「我从哪来」。
    /// 受击方拿到坐标后自行推算方向，这样霸体、击退抗性、方向翻转等逻辑
    /// 全部集中在受击方一处，不必去改每一种子弹。
    ///
    /// 用 readonly struct + in 传递，避免堆分配与防御性拷贝。
    /// </summary>
    public readonly struct DamageInfo
    {
        /// <summary>原始伤害值。护盾减免等由受击方自行结算。</summary>
        public readonly int amount;

        /// <summary>近战 / 远程。Boss 的护盾对两者有不同的扣除规则。</summary>
        public readonly DamageType type;

        /// <summary>伤害来源的世界坐标，受击方据此计算击退方向。</summary>
        public readonly Vector2 sourcePosition;

        /// <summary>发起者（子弹、玩家本体等），可为 null。用于溯源、计分、防止自伤。</summary>
        public readonly GameObject instigator;

        public DamageInfo(int amount, DamageType type, Vector2 sourcePosition,
                          GameObject instigator = null)
        {
            this.amount = amount;
            this.type = type;
            this.sourcePosition = sourcePosition;
            this.instigator = instigator;
        }
    }

    /// <summary>
    /// 一切可受伤实体的统一入口。
    /// 目前的实现方：EntityBase（所有敌人）与 PlayerState（玩家）。
    ///
    /// 有了它，子弹不再需要知道自己打的是谁 —— 拿到 IDamageable 就能结算，
    /// 敌我双方的伤害通路从此对称。
    /// </summary>
    public interface IDamageable
    {
        void TakeDamage(in DamageInfo info);
    }
}
