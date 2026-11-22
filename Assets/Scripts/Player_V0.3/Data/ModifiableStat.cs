using System.Collections.Generic;
using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【一面可以贴便利贴的墙】
    ///
    /// 内部结构：
    ///   基础值(baseValue，来自 Config 说明书) + 一叠便利贴(modifiers) → 最终值(Value)
    ///
    /// 对外只暴露 Value。任何人都拿不到「直接改基础值」的权限。
    ///
    /// 结算顺序：(base + 所有Flat) * (1 + 所有PercentAdd之和) * 每个PercentMult连乘
    ///
    /// 性能：结果会缓存，只有便利贴增删时才重算（脏标记）。
    ///       每帧读 Value 是零开销的。
    /// </summary>
    [System.Serializable]
    public class ModifiableStat
    {
        [SerializeField]
        [Tooltip("基础值。由 Config 在初始化时灌入，运行时可在 Inspector 观察")]
        private float baseValue;

        // 便利贴列表。用 List 而非数组，因为宝石装卸是低频操作
        private readonly List<StatModifier> modifiers = new List<StatModifier>();

        private float cachedValue;
        private bool isDirty = true;

        /// <summary>最终值变化时广播。UI 可以订阅它来刷新面板</summary>
        public event System.Action<float> OnValueChanged;

        public ModifiableStat() { }

        public ModifiableStat(float baseValue)
        {
            this.baseValue = baseValue;
        }

        // ==========================================
        // 读取
        // ==========================================

        /// <summary>【唯一对外出口】最终生效值</summary>
        public float Value
        {
            get
            {
                if (isDirty) Recalculate();
                return cachedValue;
            }
        }

        /// <summary>最终值的整数形式（用于跳跃次数、充能层数这类离散量）</summary>
        public int IntValue => Mathf.RoundToInt(Value);

        /// <summary>基础值。只读，仅供调试与 UI 显示「原始 6 → 现在 7.5」这类对比</summary>
        public float BaseValue => baseValue;

        /// <summary>当前贴了多少张便利贴</summary>
        public int ModifierCount => modifiers.Count;

        // ==========================================
        // 写入
        // ==========================================

        /// <summary>
        /// 设置基础值。【只应该由 PlayerStats.Init() 从 Config 灌入】
        /// 业务代码不要调用这个方法，请用 AddModifier。
        /// </summary>
        public void SetBaseValue(float value)
        {
            if (Mathf.Approximately(baseValue, value)) return;
            baseValue = value;
            MarkDirty();
        }

        /// <summary>贴一张便利贴</summary>
        public void AddModifier(in StatModifier modifier)
        {
            modifiers.Add(modifier);
            MarkDirty();
        }

        /// <summary>
        /// 【核心】撕掉某个来源贴的所有便利贴。
        /// 宝石 OnUnequip 时调用 RemoveAllFromSource(this) 即可完美还原。
        /// </summary>
        /// <returns>撕掉了几张</returns>
        public int RemoveAllFromSource(object source)
        {
            if (source == null) return 0;

            int removed = 0;
            // 倒序遍历，边遍历边删不会出错；且不产生闭包 GC（不用 List.RemoveAll）
            for (int i = modifiers.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(modifiers[i].source, source))
                {
                    modifiers.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0) MarkDirty();
            return removed;
        }

        /// <summary>清空所有便利贴（换角色、重开局时用）</summary>
        public void ClearModifiers()
        {
            if (modifiers.Count == 0) return;
            modifiers.Clear();
            MarkDirty();
        }

        // ==========================================
        // 内部结算
        // ==========================================

        private void MarkDirty()
        {
            isDirty = true;
            // 立刻算一次，好把新值播出去
            float newValue = Value;
            OnValueChanged?.Invoke(newValue);
        }

        private void Recalculate()
        {
            float flatSum = 0f;
            float percentAddSum = 0f;
            float percentMultProduct = 1f;

            for (int i = 0; i < modifiers.Count; i++)
            {
                var m = modifiers[i];
                switch (m.type)
                {
                    case StatModifierType.Flat:
                        flatSum += m.value;
                        break;
                    case StatModifierType.PercentAdd:
                        percentAddSum += m.value;
                        break;
                    case StatModifierType.PercentMult:
                        percentMultProduct *= (1f + m.value);
                        break;
                }
            }

            cachedValue = (baseValue + flatSum) * (1f + percentAddSum) * percentMultProduct;
            isDirty = false;
        }

        public override string ToString()
            => $"{Value:F2} (base {baseValue:F2}, {modifiers.Count} mods)";
    }
}
