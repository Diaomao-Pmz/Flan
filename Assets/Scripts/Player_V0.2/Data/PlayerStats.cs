using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【属性面板】
    ///
    /// 每一条属性都是 ModifiableStat（一面可以贴便利贴的墙）：
    ///   基础值来自 Config 说明书，宝石/buff/装备往上贴便利贴，读的时候拿最终值。
    ///
    /// 【批次B 新增】maxJumps
    ///   原先它是 PlayerStateMachine 上一个普通 int，
    ///   被 GemActionProcessor 每次跳跃都覆写一遍（Echo 写 2，Relay 写 1）。
    ///   换宝石时前一颗写的值不会还原 —— 典型的「只装不卸」。
    ///   现在改成便利贴，Echo 贴 +1，卸下自动撕掉。
    /// </summary>
    [System.Serializable]
    public class PlayerStats
    {
        [Header("位移属性")]
        [Tooltip("移动速度。基础值来自 PlayerMovementConfig.moveSpeed")]
        public ModifiableStat moveSpeed = new ModifiableStat(6f);

        [Tooltip("跳跃力度")]
        public ModifiableStat jumpForce = new ModifiableStat(13f);

        [Tooltip("冲刺速度")]
        public ModifiableStat dashSpeed = new ModifiableStat(12f);

        [Tooltip("最大跳跃次数。Echo 宝石往这里贴 +1")]
        public ModifiableStat maxJumps = new ModifiableStat(1f);

        [Header("战斗属性")]
        [Tooltip("冷却缩减，0.15 表示所有 CD 减少 15%")]
        public ModifiableStat cooldownReduction = new ModifiableStat(0f);

        [Tooltip("连招宽恕期额外宽容度（秒）")]
        public ModifiableStat comboWindowTolerance = new ModifiableStat(0f);

        [Tooltip("伤害倍率修正。1.0 为标准，预留给以后的宝石与装备")]
        public ModifiableStat damageMultiplier = new ModifiableStat(1f);

        private bool isInitialized = false;

        public void Init(PlayerMovementConfig moveConfig)
        {
            if (isInitialized) return;

            if (moveConfig != null)
            {
                moveSpeed.SetBaseValue(moveConfig.moveSpeed);
                jumpForce.SetBaseValue(moveConfig.jumpForce);
                dashSpeed.SetBaseValue(moveConfig.dashSpeed);
                maxJumps.SetBaseValue(moveConfig.maxJumps);
            }

            isInitialized = true;
        }

        /// <summary>
        /// 一次性撕掉某个来源在【所有】属性上贴的便利贴。
        /// 宝石 OnUnequip 时调用一次即可，不需要逐条属性去撕，也不可能漏。
        /// </summary>
        public int RemoveAllModifiersFrom(object source)
        {
            int n = 0;
            n += moveSpeed.RemoveAllFromSource(source);
            n += jumpForce.RemoveAllFromSource(source);
            n += dashSpeed.RemoveAllFromSource(source);
            n += maxJumps.RemoveAllFromSource(source);
            n += cooldownReduction.RemoveAllFromSource(source);
            n += comboWindowTolerance.RemoveAllFromSource(source);
            n += damageMultiplier.RemoveAllFromSource(source);
            return n;
        }

        /// <summary>
        /// 把冷却缩减套用到一个原始 CD 上，返回实际 CD。
        /// 内部 Clamp，防止某天叠出 120% 减CD 导致负数。
        /// </summary>
        public float ApplyCooldownReduction(float rawCooldown)
        {
            float cdr = Mathf.Clamp(cooldownReduction.Value, 0f, 0.9f);
            return rawCooldown * (1f - cdr);
        }
    }
}
