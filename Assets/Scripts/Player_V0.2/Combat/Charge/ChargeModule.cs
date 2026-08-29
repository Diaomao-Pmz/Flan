using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>蓄力模块的一次结算结果</summary>
    public enum ChargeReleaseResult
    {
        /// <summary>没在蓄力，什么都没发生</summary>
        NotCharging,

        /// <summary>没蓄满就松手 —— 什么都不放，连段清零</summary>
        Aborted,

        /// <summary>蓄满释放，应当打出目标等级的招式</summary>
        Fired,
    }

    /// <summary>
    /// 【蓄力模块】—— 一只手的充能进度，纯逻辑，不是状态。
    ///
    /// ==========================================================
    /// 【为什么要从状态里抽出来】
    ///
    /// 旧设计里蓄力是一个 State：玩家"待在蓄力房间里"。
    /// 于是任何换房间的动作 —— 跳跃、跑动、另一只手出招 ——
    /// 都会触发 ChargeState.Exit，蓄了半天的力直接清零。
    ///
    /// 比喻：改成"我左手一直攥着一个正在充能的东西"，
    ///       人走到哪儿都不影响左手继续攥着。
    ///
    /// 这和我们做滑铲动量时是同一个套路：
    /// 当时把"冲劲"从滑铲状态里抽出来跨状态传递，这次抽的是"充能进度"。
    ///
    /// 抽出来之后自然获得三件事：
    ///   ① 蓄力中可以跳跃、跑动（速度折损靠属性修饰器）
    ///   ② 两只手各持一个模块，协助手能正常打连招
    ///   ③ UI 可以同时显示两条蓄力条
    /// ==========================================================
    /// </summary>
    public class ChargeModule
    {
        /// <summary>这个模块属于哪只手</summary>
        public WeaponSlot Slot { get; private set; }

        /// <summary>对应的攻击指令</summary>
        public InputCmd Command { get; private set; }

        public bool IsCharging { get; private set; }

        /// <summary>本次蓄力的目标等级。开始时锁定，全程不变</summary>
        public int TargetLevel { get; private set; }

        /// <summary>是否已蓄满。蓄满后可以无限期举着</summary>
        public bool IsCharged { get; private set; }

        /// <summary>蓄满这一级需要多少秒（已套用加速倍率）</summary>
        public float RequiredTime { get; private set; }

        /// <summary>已经蓄了多久</summary>
        public float ChargedTime { get; private set; }

        /// <summary>进度 0~1</summary>
        public float Progress
            => RequiredTime <= 0f ? 0f : Mathf.Clamp01(ChargedTime / RequiredTime);

        /// <summary>正在蓄的武器</summary>
        public WeaponMoveSet Weapon { get; private set; }

        /// <summary>
        /// 本次蓄力是否有效。
        /// 没武器、或该手蓄力 CD 中时为 false —— 仍然会进入蓄力姿态
        /// （否则按住毫无反馈，玩家会以为卡键），但永远蓄不满。
        /// </summary>
        public bool IsValid { get; private set; }

        public ChargeModule(WeaponSlot slot)
        {
            Slot = slot;
            Command = WeaponMoveSet.SlotToCommand(slot);
        }

        /// <summary>
        /// 开始蓄力。
        /// </summary>
        /// <param name="weapon">这只手的武器，可为 null</param>
        /// <param name="targetLevel">目标等级，由连段深度决定</param>
        /// <param name="isAfterCombo">是否接在普攻后面（决定要不要吃加速倍率）</param>
        /// <param name="chargeReady">该手的蓄力 CD 好了吗</param>
        public void Begin(WeaponMoveSet weapon, int targetLevel, bool isAfterCombo, bool chargeReady)
        {
            Weapon = weapon;
            TargetLevel = Mathf.Clamp(targetLevel, 1, WeaponMoveSet.MaxChargeLevel);

            IsCharging = true;
            IsCharged = false;
            ChargedTime = 0f;

            IsValid = (weapon != null) && chargeReady;
            RequiredTime = IsValid ? CalcRequiredTime(isAfterCombo) : float.MaxValue;
        }

        /// <summary>
        /// 蓄满这一级需要多久。
        ///
        /// 基准 = 目标等级的累计阈值 - 起始等级的累计阈值，
        /// 也就是武器上那三个阈值之间的差值。
        ///
        /// 接在普攻后面的蓄力享受加速倍率（「普攻后的同手蓄力速度加快」），
        /// 原地起手则没有这个优待。
        /// </summary>
        private float CalcRequiredTime(bool isAfterCombo)
        {
            int start = Mathf.Max(0, TargetLevel - 1);

            float span = Weapon.GetTimeForLevel(TargetLevel) - Weapon.GetTimeForLevel(start);
            span = Mathf.Max(0.01f, span);

            if (isAfterCombo)
            {
                float mul = Mathf.Max(0.01f, Weapon.comboChargeSpeedMultiplier);
                span /= mul;
            }

            return span;
        }

        /// <summary>每帧推进。返回本帧是否【刚好】蓄满（用于广播升级事件）</summary>
        public bool Tick(float deltaTime)
        {
            if (!IsCharging || !IsValid || IsCharged) return false;

            ChargedTime += deltaTime;

            if (ChargedTime >= RequiredTime)
            {
                IsCharged = true;
                return true;
            }

            return false;
        }

        /// <summary>松手结算</summary>
        public ChargeReleaseResult Release()
        {
            if (!IsCharging) return ChargeReleaseResult.NotCharging;

            bool fired = IsValid && IsCharged;
            Stop();

            // 没蓄满就松手 → 什么都不放。
            // 【不会退而求其次打出低一级的招】—— 这是"蓄力是一场赌注"的实现处。
            return fired ? ChargeReleaseResult.Fired : ChargeReleaseResult.Aborted;
        }

        /// <summary>被打断（受击、死亡、换武器等）。与未蓄满松手后果相同</summary>
        public void Stop()
        {
            IsCharging = false;
            IsCharged = false;
            ChargedTime = 0f;
        }
    }
}