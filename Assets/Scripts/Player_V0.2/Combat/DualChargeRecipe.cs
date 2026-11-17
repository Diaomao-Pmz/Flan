using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>强化招用哪只手的武器来出（决定子弹池、瞄准方式）</summary>
    public enum DualWeaponSource
    {
        /// <summary>两把里有远程就用远程那把（子弹能正常射出），否则用后松手的那只手</summary>
        Auto,
        /// <summary>固定用主武器</summary>
        Main,
        /// <summary>固定用副武器</summary>
        Sub,
    }

    /// <summary>
    /// 【双蓄力配方】（实验：平a/蓄力重做 · R5）
    ///
    /// 两只手都是有效蓄力、在窗口内先后松手 → 按「武器类型（近/远）+ 等级」的组合查表，
    /// 查到就打出 result 这一招强化招。组合【无序】：
    /// 「近1 + 远2」与「远2 + 近1」是同一条配方，和哪只手先松、哪把武器在主手都无关。
    ///
    /// 用法：Create → Flandre/Combat/Dual Charge Recipe，
    /// 然后拖进玩家身上 WeaponLoadout 的 Dual Charge Recipes 列表。
    /// 查不到配方时，按两发单蓄力处理。
    /// </summary>
    [CreateAssetMenu(fileName = "NewDualChargeRecipe", menuName = "Flandre/Combat/Dual Charge Recipe")]
    public class DualChargeRecipe : ScriptableObject
    {
        [Header("组合（无序）")]
        public WeaponType typeA = WeaponType.Melee;
        [Range(1, 3)] public int levelA = 1;

        public WeaponType typeB = WeaponType.Ranged;
        [Range(1, 3)] public int levelB = 1;

        [Header("结果")]
        [Tooltip(
            "打出的强化招。就是一个普通的 ComboNode（动画、判定、子弹、特效都照常配）。\n" +
            "它不走按键序列 / 连段深度匹配，查到配方就直接出。打出后连段归零（R7）。")]
        public ComboNode result;

        [Tooltip("强化招用哪只手的武器来出（决定子弹池 key、位移瞄准方式）")]
        public DualWeaponSource weaponSource = DualWeaponSource.Auto;

        /// <summary>两只手的（武器类型, 等级）是否命中本配方。顺序无关</summary>
        public bool Matches(WeaponType t1, int l1, WeaponType t2, int l2)
        {
            if (result == null) return false;

            return (typeA == t1 && levelA == l1 && typeB == t2 && levelB == l2)
                || (typeA == t2 && levelA == l2 && typeB == t1 && levelB == l1);
        }
    }
}
