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

        // 【已删除】蓄力 CD（OnChargeReadyChanged / IsChargeReady / StartChargeCooldown …）
        // 连放惩罚移除后蓄力不再有冷却，打完一发马上能再蓄。

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
        // 装备
        // ==========================================================

        /// <summary>
        /// 换武器。传 null 表示卸空该槽。
        ///
        /// 换武器时会重置当前连招 ——
        /// 否则会出现「上一段是旧武器打的，接续却从新武器的表里找」这种不一致状态。
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

            OnWeaponChanged?.Invoke(WeaponSlot.Main, mainWeapon);
            OnWeaponChanged?.Invoke(WeaponSlot.Sub, subWeapon);
        }
    }
}
