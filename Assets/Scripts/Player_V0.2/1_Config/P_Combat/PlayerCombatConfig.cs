using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【战斗出厂说明书】
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
        [Tooltip("无敌期间角色 sprite 的透明度（1 = 不透明）。滑铲、飞行下坠、宝石、受击等所有无敌来源都生效")]
        [Range(0f, 1f)]
        public float invulnerableAlpha = 0.8f;

        [Header("Health Settings")]
        // 最大 HP / MP 已搬到 PlayerStateConfig（资源类数值统一放那里）
        [Tooltip("受击后的无敌保护期总长")]
        public float invulnerableDuration = 0.35f;

        [Header("Boss Hit Detection（敌人判定方式）")]
        [Tooltip("勾选 = Boss 的近战（普通近战、突进后的挥击）判定的是玩家根节点上的【身体碰撞箱】，\n" +
                 "蹲下 / 滑铲时碰撞箱被压扁，打得高的挥击可以躲开。\n" +
                 "不勾 = 近战和远程一样，按图层打 Hurtbox_Core。\n" +
                 "远程（子弹、激光）始终打 Hurtbox_Core，不受此项影响")]
        public bool meleeHitsBodyCollider = true;

        [Header("Shoot Settings")]
        public float projectileSpeed = 12f;

        // ==========================================================
        // 【设计备忘】当前 hitStunDuration(0.8) > invulnerableDuration(0.35)
        // ==========================================================
    }
}
