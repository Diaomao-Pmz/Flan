namespace Flandre.CombatSystem
{
    /// <summary>
    /// 修饰器类型。结算顺序固定为：Flat → PercentAdd → PercentMult
    /// </summary>
    public enum StatModifierType
    {
        /// <summary>加法。例：+2 点移速。多条直接累加</summary>
        Flat = 100,

        /// <summary>百分比【相加】。例：+15% 和 +10% 合并为 +25%（不是 1.15*1.10）</summary>
        PercentAdd = 200,

        /// <summary>百分比【相乘】。例：+15% 和 +10% 结算为 1.15*1.10 = 1.265。用于稀有的「独立乘区」</summary>
        PercentMult = 300,
    }

    /// <summary>
    /// 【一张便利贴】
    ///
    /// 贴在 ModifiableStat 上，用来临时改变它的最终值。
    ///
    /// 最关键的字段是 source（签名）——
    /// 有了它，宝石卸下时只需说「把我签名的全撕了」，
    /// 而不需要程序员手写「把 15% 扣回来」。
    ///
    /// 这就是「只装不卸」这个 bug 在结构上不可能发生的原因：
    /// 根本没有「直接改基础值」这条路可走。
    ///
    /// 设计成 struct 是为了避免高频结算时产生 GC。
    /// </summary>
    public readonly struct StatModifier
    {
        public readonly float value;
        public readonly StatModifierType type;

        /// <summary>
        /// 签名：是谁贴的这张便利贴。
        /// 通常传宝石实例本身(this)。卸载时用 RemoveAllFromSource(this) 精确撕除。
        /// </summary>
        public readonly object source;

        public StatModifier(float value, StatModifierType type, object source)
        {
            this.value = value;
            this.type = type;
            this.source = source;
        }

        // ------- 语法糖，让调用处读起来像人话 -------

        /// <summary>加法修饰：Flat(+2, this)</summary>
        public static StatModifier Flat(float value, object source)
            => new StatModifier(value, StatModifierType.Flat, source);

        /// <summary>百分比相加：Percent(0.15f, this) 表示 +15%</summary>
        public static StatModifier Percent(float percent01, object source)
            => new StatModifier(percent01, StatModifierType.PercentAdd, source);

        /// <summary>独立乘区：Multiplicative(0.15f, this) 表示 ×1.15</summary>
        public static StatModifier Multiplicative(float percent01, object source)
            => new StatModifier(percent01, StatModifierType.PercentMult, source);
    }
}
