using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>蓄力模块的一次结算结果</summary>
    public enum ChargeReleaseResult
    {
        /// <summary>没在蓄力，什么都没发生</summary>
        NotCharging,

        /// <summary>还没蓄到 1 级就松手 —— 什么都不放，连段清零</summary>
        Aborted,

        /// <summary>至少蓄到了 1 级，应当打出当前达到的最高等级</summary>
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
    /// ==========================================================
    ///
    /// ==========================================================
    /// 【本次重写：逐级升，像加热旋钮】
    ///
    /// 删掉的：
    ///   ✗ 目标等级由连段深度决定（打几段普攻才能蓄几级、一次只升一级）
    ///   ✗ 连放惩罚（CD 内起手 → 弱化版 AA1 + 大僵直）
    ///
    /// 现在：按住越久等级越高，按武器上的三个累计阈值逐级升到 3 级封顶，
    /// 松手打出【当前达到的最高等级】。还没到 1 级就松手 → 什么都不放。
    ///
    /// 【普攻后接蓄力：起步更高，但有保险锁】
    /// 打完第 N 段普攻接蓄力，进度直接从 N 级的阈值起算（A2 后一起手就在 AA2 刻度上），
    /// 但必须按住满 minHoldTime（来自刚打完那一招的 ComboNode.chargeMinHoldAfter）才解锁；
    /// 锁住期间等级显示为 0，松手 = 什么都不放。锁住的这段时间照样计入升级进度。
    /// ==========================================================
    /// </summary>
    public class ChargeModule
    {
        /// <summary>这个模块属于哪只手</summary>
        public WeaponSlot Slot { get; private set; }

        /// <summary>对应的攻击指令</summary>
        public InputCmd Command { get; private set; }

        public bool IsCharging { get; private set; }

        /// <summary>
        /// 当前已经蓄到第几级（0 = 还没到 1 级）。
        /// 本值在 Stop() 里【刻意不清】：它是"刚结束那次蓄力蓄到了几级"，调用方要在 Release() 之后读。
        /// </summary>
        public int CurrentLevel { get; private set; }

        /// <summary>至少蓄到了 1 级（松手能打出东西）</summary>
        public bool IsCharged => CurrentLevel >= 1;

        /// <summary>已经蓄到封顶等级</summary>
        public bool IsMaxed => CurrentLevel >= WeaponMoveSet.MaxChargeLevel;

        /// <summary>已经蓄了多久（已套用加速倍率，可直接和武器阈值比较）</summary>
        public float ChargedTime { get; private set; }

        /// <summary>整体进度 0~1：从开始到蓄满最高级</summary>
        public float Progress
        {
            get
            {
                if (!IsValid) return 0f;
                float full = Weapon.GetTimeForLevel(WeaponMoveSet.MaxChargeLevel);
                return full <= 0f ? 1f : Mathf.Clamp01(ChargedTime / full);
            }
        }

        /// <summary>正在蓄的武器</summary>
        public WeaponMoveSet Weapon { get; private set; }

        /// <summary>
        /// 本次蓄力是否有效。唯一的无效情况：这只手没装武器 ——
        /// 没有招式表，蓄出来也没东西可放。
        /// 这种情况下仍然进入蓄力姿态（否则按住毫无反馈，玩家会以为卡键），但永远升不了级。
        /// </summary>
        public bool IsValid { get; private set; }

        /// <summary>
        /// 本次蓄力是不是【在空中起手】的 —— "要不要把角色钉在空中"的唯一判据。
        ///   空中起手 → 钉住（空中蓄力是一次高风险承诺，想挪位置只能花一次冲刺）
        ///   地面起手 → 不钉（跳跃只是普通的折损跳跃）
        /// 起手位置在整段蓄力里是不变的，所以不会出现"某一帧忽然变规则"。
        /// </summary>
        public bool BeganAirborne { get; private set; }

        // 普攻后蓄力的加速倍率（武器上的 comboChargeSpeedMultiplier），原地起手为 1
        private float speedMultiplier = 1f;

        // ---- 保险锁 ----
        private float heldTime;       // 本次蓄力实际按住了多久（不吃加速倍率）
        private float minHoldTime;    // 按住满这么久才解锁；原地起手为 0

        /// <summary>本次蓄力的起步等级（原地起手为 0）</summary>
        public int StartLevel { get; private set; }

        /// <summary>还在保险锁里（普攻后起手、按住还不满最短时长）</summary>
        public bool IsLocked => heldTime < minHoldTime;

        public ChargeModule(WeaponSlot slot)
        {
            Slot = slot;
            Command = WeaponMoveSet.SlotToCommand(slot);
        }

        /// <summary>开始蓄力</summary>
        /// <param name="weapon">这只手的武器，可为 null</param>
        /// <param name="startLevel">起步等级：原地起手 0，第 N 段普攻后为 N</param>
        /// <param name="minHold">起步等级 > 0 时，按住满多少秒才解锁（保险锁）</param>
        /// <param name="isAfterCombo">是否接在普攻后面（决定要不要吃加速倍率）</param>
        /// <param name="beganAirborne">起手那一刻角色在空中吗。全程不变，见 BeganAirborne</param>
        public void Begin(WeaponMoveSet weapon, int startLevel, float minHold,
                          bool isAfterCombo, bool beganAirborne)
        {
            Weapon = weapon;
            BeganAirborne = beganAirborne;
            IsValid = weapon != null;

            speedMultiplier = (IsValid && isAfterCombo)
                ? Mathf.Max(0.01f, weapon.comboChargeSpeedMultiplier)
                : 1f;

            StartLevel = IsValid ? Mathf.Clamp(startLevel, 0, WeaponMoveSet.MaxChargeLevel) : 0;

            // 进度直接从起步等级的阈值起算（A2 后 → 从 AA2 的刻度开始）
            ChargedTime = IsValid ? Weapon.GetTimeForLevel(StartLevel) : 0f;

            heldTime = 0f;
            minHoldTime = StartLevel > 0 ? Mathf.Max(0f, minHold) : 0f;

            IsCharging = true;
            CurrentLevel = 0;
        }

        /// <summary>每帧推进。返回本帧是否【升了级】（用于广播升级事件、换光效）</summary>
        public bool Tick(float deltaTime)
        {
            if (!IsCharging || !IsValid || IsMaxed) return false;

            heldTime += deltaTime;

            // 进度封顶在最高级阈值，没必要无限累加
            float full = Weapon.GetTimeForLevel(WeaponMoveSet.MaxChargeLevel);
            ChargedTime = Mathf.Min(ChargedTime + deltaTime * speedMultiplier, full);

            // 保险锁没开：进度照涨，但等级不对外生效
            if (IsLocked) return false;

            int level = Weapon.GetLevelForTime(ChargedTime);
            if (level <= CurrentLevel) return false;

            CurrentLevel = level;
            return true;
        }

        /// <summary>松手结算</summary>
        public ChargeReleaseResult Release()
        {
            if (!IsCharging) return ChargeReleaseResult.NotCharging;

            bool fired = IsValid && IsCharged;
            Stop();

            // 还没到 1 级就松手 → 什么都不放（"蓄力是一场赌注"的实现处）
            return fired ? ChargeReleaseResult.Fired : ChargeReleaseResult.Aborted;
        }

        /// <summary>被打断（受击、起飞、换武器等）。与未蓄到 1 级就松手后果相同</summary>
        public void Stop()
        {
            IsCharging = false;
            ChargedTime = 0f;
            // CurrentLevel 刻意不清，见其注释
        }
    }
}
