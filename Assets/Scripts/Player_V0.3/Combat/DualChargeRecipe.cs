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
    /// 两只手同时放蓄力时，主武器等级 + 副武器等级 = 等级总和 → 打出这里的强化招。
    /// </summary>
    [CreateAssetMenu(fileName = "NewDualChargeRecipe", menuName = "Flandre/Combat/Dual Charge Recipe")]
    public class DualChargeRecipe : ScriptableObject
    {
        [Header("组合")]
        [Tooltip(
            "两只手蓄力等级加起来是多少时打出这一招（2~6）。主副手各蓄几级不区分：1+2 和 2+1 都是 3。\n" +
            "武器配置（WeaponLoadout）的配方表里不能有两个总和相同的配方，否则这几个都不生效，并报错。")]
        [Range(2, 6)]
        public int levelSum = 2;

        [Header("结果")]
        [Tooltip(
            "打出的强化招。就是一个普通的 ComboNode（动画、判定、子弹、特效都照常配）。\n" +
            "它不走按键序列 / 连段深度匹配，查到配方就直接出。打出后连段归零（R7）。")]
        public ComboNode result;

        [Tooltip("强化招用哪只手的武器来出（决定子弹池 key、位移瞄准方式）")]
        public DualWeaponSource weaponSource = DualWeaponSource.Auto;

        // ---- 旧版的「类型 + 等级」组合：只用来第一次打开时换算成等级总和，面板上不再显示 ----
        [HideInInspector] public WeaponType typeA = WeaponType.Melee;
        [HideInInspector] public int levelA = 1;
        [HideInInspector] public WeaponType typeB = WeaponType.Ranged;
        [HideInInspector] public int levelB = 1;
        [HideInInspector, SerializeField] private bool migratedToSum;

        /// <summary>两只手的等级加起来是否命中本配方（顺序无关）</summary>
        public bool Matches(int level1, int level2)
        {
            if (result == null) return false;
            return level1 + level2 == levelSum;
        }

        private void OnEnable() => MigrateToSum();

        private void OnValidate() => MigrateToSum();

        /// <summary>旧配方第一次加载时：等级总和 = 旧的 Level A + Level B</summary>
        private void MigrateToSum()
        {
            if (migratedToSum) return;

            levelSum = Mathf.Clamp(levelA + levelB, 2, 6);
            migratedToSum = true;

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
}
