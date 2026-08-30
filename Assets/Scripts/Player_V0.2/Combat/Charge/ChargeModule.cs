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
        ///
        /// 【只剩一种无效】这只手根本没装武器 —— 没有招式表，蓄出来也没东西可放。
        /// 这种情况下仍然进入蓄力姿态（否则按住毫无反馈，玩家会以为卡键），但永远蓄不满。
        ///
        /// 【只剩一种无效】这只手没装武器。CD 中不算无效 —— 见 IsWeakened。
        /// </summary>
        public bool IsValid { get; private set; }

        /// <summary>
        /// 【强行打出】本次蓄力【开始的那一刻】，上一发蓄力的 CD 还没走完。
        ///
        /// ==========================================================
        /// 【锚点为什么必须是起手，不是松手】
        ///
        /// 曾经试过在松手时判定，看起来更宽容 —— 举久一点就能把 CD 熬过去。
        /// 但那让「等待」和「蓄力」变成了同一件事：玩家举着武器本来就是他要做的事，
        /// 顺带就把 CD 熬完了，等待因此没有任何成本，CD 退化成纯节奏器，惩罚消失。
        ///
        /// 锚在起手就堵死了这条路：那一刻的状态把这一发定了性，
        /// 之后蓄多久、什么时候松手都洗不白。想要正常版，
        /// 就必须真的空出一段时间【不去蓄力】—— 那才是代价。
        ///
        /// 配合「CD 不被普攻重置」，"打一发普攻垫一下再蓄"也绕不过去。
        /// ==========================================================
        ///
        /// 它【不影响蓄力流程】：照常计时、照常蓄满、照常打出，伤害与位移都正常。
        /// 两个代价：等级压到 1 级；后摇换成 chargeCooldownLv1。
        ///
        /// 本标记在 Stop() 里【刻意不清】—— 和 TargetLevel 一样，
        /// 它是"刚刚结束的那次蓄力的属性"，调用方要在 Release() 之后读它。
        /// </summary>
        public bool IsWeakened { get; private set; }

        /// <summary>
        /// 本次蓄力是不是【在空中起手】的。
        ///
        /// 这是"要不要把角色钉在空中"的唯一判据：
        ///   空中起手 → 钉住。说明书里「空中蓄力是一次高风险承诺」指的就是这个，
        ///              想调整位置只能花一次冲刺。
        ///   地面起手 → 不钉。此时跳跃只是普通的折损跳跃，
        ///              该有完整的起跳-到顶-落下弧线。
        ///
        /// 【为什么不能用"当前 y 速度"代替】那个判据区分的是上升还是下落，
        /// 于是地面起手跳到顶点就被钉住了 —— 表现成"跳起来卡在空中"，
        /// 而玩家的本意只是跳一下。起手位置在整段蓄力里是不变的，
        /// y 速度每一帧都在变，用后者去表达一个"这次蓄力的性质"本来就错位。
        /// </summary>
        public bool BeganAirborne { get; private set; }

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
        /// <param name="chargeReady">
        /// 蓄力 CD 走完了吗（全局，不分手）。没走完【不阻止蓄力】，
        /// 只是把本次定性为「强行打出」并把等级压到 1 级。
        /// </param>
        /// <param name="beganAirborne">起手那一刻角色在空中吗。全程不变，见 BeganAirborne</param>
        public void Begin(
            WeaponMoveSet weapon, int targetLevel, bool isAfterCombo,
            bool chargeReady, bool beganAirborne)
        {
            Weapon = weapon;
            BeganAirborne = beganAirborne;

            // 两件事分开：能不能蓄（有没有武器） vs 蓄出来是不是缩水版（CD 走没走完）
            IsValid = weapon != null;
            IsWeakened = !chargeReady;

            // 【强行打出一律降到 1 级】
            //
            // 通常这自动成立 —— 蓄力打完连段就清零，紧接着再蓄目标本来就是 AA1。
            // 但连段计数是两只手共用的：打出蓄力后用另一只手飞快补三段普攻，
            // 深度会被顶回 3，此时若在 CD 内长按就能白拿一发 AA3。
            // 与其依赖"连段清零"这个间接结果，不如在这里显式钳死。
            int wanted = IsWeakened ? 1 : targetLevel;
            TargetLevel = Mathf.Clamp(wanted, 1, WeaponMoveSet.MaxChargeLevel);

            IsCharging = true;
            IsCharged = false;
            ChargedTime = 0f;

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