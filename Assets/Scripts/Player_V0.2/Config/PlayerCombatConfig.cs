using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【战斗出厂说明书】
    ///
    /// 同样是运行时只读。与 PlayerMovementConfig 分开，
    /// 是为了以后策划分工时（一个人调手感、一个人调数值）不会互相冲突。
    ///
    /// 所有默认值 = 2026-08 时 Inspector 上的实际值。
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerCombatConfig", menuName = "Flandre/Player/Combat Config")]
    public class PlayerCombatConfig : ScriptableObject
    {
        [Header("Hit Settings")]
        [Tooltip("受击后失去控制的硬直时间")]
        public float hitStunDuration = 0.8f;
        [Tooltip("受击击飞力度 (X水平, Y垂直)")]
        public Vector2 hitKnockbackForce = new Vector2(1.5f, 8f);
        [Tooltip("受击闪烁频率（多少秒闪一次）")]
        public float blinkInterval = 0.15f;

        [Header("Health Settings")]
        public int maxHP = 100;
        public int maxMP = 100;
        [Tooltip("受击后的无敌保护期总长")]
        public float invulnerableDuration = 0.35f;

        [Header("Shoot Settings")]
        public float projectileSpeed = 12f;

        // ==========================================================
        // 【设计备忘】当前 hitStunDuration(0.8) > invulnerableDuration(0.35)
        //
        // 意味着受击后半程（约 0.45 秒）玩家处于「不能动、但可以被打」的状态，
        // 会被敌人连锁击飞。
        //
        // 本次改动原样保留了这两个数值，未做任何调整。
        // 如果这是刻意的惩罚性设计，忽略本备忘；
        // 如果是疏漏，把 invulnerableDuration 提到 >= hitStunDuration 即可。
        // ==========================================================
    }
}
