using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【武器装备中枢】—— 管理主武器(左键)与副武器(右键)两个槽。
    ///
    /// 两个槽各自可以插近战或远程，于是有四种战斗形态：
    ///   近近 / 近远 / 远近 / 远远
    ///
    /// ==========================================================
    /// 【批次J 新增】每把武器独立的蓄力 CD
    ///
    /// ⚠️ 计时器【必须】放在这里，不能放 WeaponMoveSet ——
    /// 那是磁盘上的 ScriptableObject 资产，运行时往它字段上写值：
    ///   编辑器里会污染资产文件，退出 Play Mode 后脏数据残留
    ///   打包后所有实例共享同一份
    /// 这正是我们做宝石系统时处理过的同一个雷：
    /// 说明书是印刷品，不能在上面写字；每个人另配一本草稿本。
    ///
    /// 所以「谁在什么时候能蓄力」是本组件的运行时状态，与武器资产无关。
    /// 换武器时 CD 会一并重置 —— 新武器不该继承上一把的冷却。
    /// ==========================================================
    /// </summary>
    public class WeaponLoadout : MonoBehaviour
    {
        [Header("武器插槽")]
        [Tooltip("主武器 = 左键")]
        public WeaponMoveSet mainWeapon;

        [Tooltip("副武器 = 右键")]
        public WeaponMoveSet subWeapon;

        [Header("设置")]
        [Tooltip("是否允许主副装同一把武器资产")]
        public bool allowSameWeaponBothSlots = true;

        /// <summary>武器变更时广播。UI 与连招引擎可订阅</summary>
        public event System.Action<WeaponSlot, WeaponMoveSet> OnWeaponChanged;

        /// <summary>蓄力 CD 状态变化时广播 (是否就绪)。UI 订阅这个来点亮/熄灭蓄力图标</summary>
        public event System.Action<bool> OnChargeReadyChanged;

        // ---- 运行时：蓄力 CD 结束时刻 ----
        //
        // 【为什么是一份而不是每手一份】
        // 蓄力招打出后连段计数就清零了，而连段计数本来就是两只手共用的。
        // CD 只锁一只手的话，换手长按就能立刻绕过去 —— 规则形同虚设。
        // 所以 CD 跟着「连段」走，是全局的，不是跟着「手」走。
        private float chargeCooldownEndTime;
        private bool wasChargeReady = true;

        private ComboInputBuffer comboBuffer;

        private void Awake()
        {
            comboBuffer = GetComponent<ComboInputBuffer>();
        }

        private void Start()
        {
            if (mainWeapon == null && subWeapon == null)
            {
                Debug.LogWarning(
                    "[武器中枢] 主副武器都为空。连招引擎会回退到 ComboInputBuffer 上的 rootNodes 旧配置。", this);
            }
        }

        private void Update()
        {
            // CD 到点时广播一次，供 UI 把图标点亮
            bool ready = IsChargeReady;

            if (ready != wasChargeReady)
            {
                wasChargeReady = ready;
                OnChargeReadyChanged?.Invoke(ready);
            }
        }

        // ==========================================================
        // 查询
        // ==========================================================

        public WeaponMoveSet GetWeapon(WeaponSlot slot)
            => slot == WeaponSlot.Main ? mainWeapon : subWeapon;

        public WeaponMoveSet GetWeaponForCommand(InputCmd cmd)
        {
            if (!WeaponMoveSet.TryCommandToSlot(cmd, out WeaponSlot slot)) return null;
            return GetWeapon(slot);
        }

        /// <summary>当前战斗形态，如「近远」。供 UI 显示</summary>
        public string GetLoadoutLabel()
        {
            string m = mainWeapon != null ? (mainWeapon.IsRanged ? "远" : "近") : "空";
            string s = subWeapon != null ? (subWeapon.IsRanged ? "远" : "近") : "空";
            return m + s;
        }

        public bool HasAnyWeapon => mainWeapon != null || subWeapon != null;

        // ==========================================================
        // 蓄力 CD
        // ==========================================================

        /// <summary>
        /// 现在能不能打出【正常的】蓄力。
        /// 注意是全局的，不分左右手 —— 主手打出蓄力后，副手同样要等。
        /// </summary>
        public bool IsChargeReady => Time.time >= chargeCooldownEndTime;

        /// <summary>剩余 CD 秒数。供 UI 显示</summary>
        public float ChargeCooldownRemaining
            => Mathf.Max(0f, chargeCooldownEndTime - Time.time);

        /// <summary>
        /// 打出蓄力攻击后调用，开始蓄力冷却。
        /// </summary>
        /// <param name="slot">打出蓄力的那只手。只用来决定读哪把武器的 CD 配置</param>
        /// <param name="level">打出的蓄力等级。等级越高 CD 越短</param>
        /// <param name="delay">
        /// 延后多少秒才开始倒计时。传收招硬直的时长 ——
        /// CD 是【接在硬直后面】的一段，不是和硬直并行的。
        /// 两者并行的话，硬直越长 CD 的实际约束力越弱，调一个会挤压另一个。
        /// </param>
        public void StartChargeCooldown(WeaponSlot slot, int level, float delay = 0f)
        {
            WeaponMoveSet w = GetWeapon(slot);
            if (w == null) return;

            // 【取较晚的那个，不是直接覆盖】
            // 强行打出的 AA1 会起一个比当前剩余 CD 更长的新 CD，那是对的；
            // 但反过来不该发生 —— 否则「AA3 的 0.3s CD」会把一个还剩 0.8s 的
            // 长 CD 缩短，等于打一发弱化招就能洗掉惩罚。
            float end = Time.time + Mathf.Max(0f, delay) + w.GetChargeCooldown(level);
            chargeCooldownEndTime = Mathf.Max(chargeCooldownEndTime, end);

            wasChargeReady = false;
            OnChargeReadyChanged?.Invoke(false);
        }

        public void ClearChargeCooldown()
        {
            chargeCooldownEndTime = 0f;
            wasChargeReady = true;
            OnChargeReadyChanged?.Invoke(true);
        }

        // ==========================================================
        // 装备
        // ==========================================================

        /// <summary>
        /// 换武器。传 null 表示卸空该槽。
        ///
        /// 换武器时会重置当前连招与该槽的蓄力 CD ——
        /// 否则会出现「上一段是旧武器打的，接续却从新武器的表里找」这种不一致状态，
        /// 以及新武器莫名其妙继承了上一把冷却的怪现象。
        /// </summary>
        public void SetWeapon(WeaponSlot slot, WeaponMoveSet weapon)
        {
            if (weapon != null && !allowSameWeaponBothSlots)
            {
                WeaponSlot other = (slot == WeaponSlot.Main) ? WeaponSlot.Sub : WeaponSlot.Main;
                if (GetWeapon(other) == weapon)
                {
                    Debug.LogWarning(
                        $"[武器中枢] 「{weapon.displayName}」已装在 {other} 槽，不允许双槽同武器。", this);
                    return;
                }
            }

            if (slot == WeaponSlot.Main) mainWeapon = weapon;
            else subWeapon = weapon;

            comboBuffer?.ResetCombo();
            ClearChargeCooldown();

            OnWeaponChanged?.Invoke(slot, weapon);

            Debug.Log($"[武器中枢] {slot} 槽装备：{(weapon != null ? weapon.displayName : "空")}，当前形态 {GetLoadoutLabel()}");
        }

        /// <summary>主副互换</summary>
        public void SwapWeapons()
        {
            WeaponMoveSet temp = mainWeapon;
            mainWeapon = subWeapon;
            subWeapon = temp;

            comboBuffer?.ResetCombo();
            ClearChargeCooldown();

            OnWeaponChanged?.Invoke(WeaponSlot.Main, mainWeapon);
            OnWeaponChanged?.Invoke(WeaponSlot.Sub, subWeapon);
        }
    }
}
