using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【动作类别】—— 用于打断判定的对位关系。
    ///
    /// ==========================================================
    /// 【为什么是掩码而不是等级】
    ///
    /// 一开始的设想是 poiseLevel 整数比大小：breakLevel >= poiseLevel 就能打断。
    /// 但实际设计是：
    ///     AA1 只破 melee
    ///     AA2 只破 bullet   ← 打不断 melee
    ///     AA3 破 melee + bullet + 所有 special
    ///
    /// AA2 打不断 melee —— 这是石头剪刀布式的【对位】关系，不是包含关系。
    /// 用整数比大小的话，breakLevel=2 >= poiseLevel=1 会误判成"能打断近战"。
    ///
    /// 比喻：这不是"几级权限能开几级门"，而是"这把钥匙能开哪几扇门"。
    ///       钥匙上刻的是门牌号清单，不是权限等级。
    ///
    /// 改成掩码之后，以后想调整对位关系（让 AA1 也能破 special melee 之类），
    /// 改的是资产上的勾选框，不需要动代码。
    /// ==========================================================
    /// </summary>
    [System.Flags]
    public enum ActionCategory
    {
        None = 0,

        /// <summary>普通近战挥击 (MeleeNode)</summary>
        Melee = 1 << 0,

        /// <summary>普通弹幕 (BulletNode)</summary>
        Bullet = 1 << 1,

        /// <summary>特殊近战，如突进斩 (DashAttackNode)</summary>
        SpecialMelee = 1 << 2,

        /// <summary>特殊远程，如激光 (LaserNode)</summary>
        SpecialBullet = 1 << 3,

        /// <summary>传送。按设计【永远不可被打断】，所以不该出现在任何 breakMask 里</summary>
        Teleport = 1 << 4,

        // ---- 常用组合，方便在 Inspector 里一键选中 ----

        /// <summary>所有近战</summary>
        AllMelee = Melee | SpecialMelee,

        /// <summary>所有远程</summary>
        AllRanged = Bullet | SpecialBullet,

        /// <summary>除传送外的一切 —— AA3 用这个</summary>
        AllAttacks = Melee | Bullet | SpecialMelee | SpecialBullet,
    }

    /// <summary>
    /// 统一的伤害载荷。敌我双方共用同一份契约。
    ///
    /// 设计要点：**这里没有击退力度字段，这是有意为之。**
    /// 击退的「力度」归受击方所有（玩家在 PlayerCombatConfig.hitKnockbackForce 里配置，
    /// 由 HitState 读取），攻击方只负责提供 sourcePosition —— 也就是「我从哪来」。
    /// 受击方拿到坐标后自行推算方向，这样霸体、击退抗性、方向翻转等逻辑
    /// 全部集中在受击方一处，不必去改每一种子弹。
    ///
    /// 【新增字段 breakMask】遵循同一条原则：
    /// 攻击方只声明"我能破哪几类动作"，
    /// 至于"当前这一招算不算那一类""破了之后怎么表现"，全由受击方裁决。
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

        /// <summary>
        /// 本次攻击能打断哪几类动作。None 表示不具备打断能力（普通平A）。
        /// 受击方用 (breakMask &amp; 当前动作类别) != 0 来判断。
        /// </summary>
        public readonly ActionCategory breakMask;

        public DamageInfo(int amount, DamageType type, Vector2 sourcePosition,
                          GameObject instigator = null,
                          ActionCategory breakMask = ActionCategory.None)
        {
            this.amount = amount;
            this.type = type;
            this.sourcePosition = sourcePosition;
            this.instigator = instigator;
            this.breakMask = breakMask;
        }

        /// <summary>本次攻击能否打断指定类别的动作</summary>
        public bool CanBreak(ActionCategory category)
            => breakMask != ActionCategory.None && (breakMask & category) != 0;
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
