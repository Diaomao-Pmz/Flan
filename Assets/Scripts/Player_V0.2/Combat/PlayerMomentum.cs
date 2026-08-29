using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【动量池】—— "你刚才攒了多少劲"。
    ///
    /// ==========================================================
    /// 【为什么不直接读当前移动速度】
    ///
    /// 蓄力一段的位移距离要"受玩家动量影响"。
    /// 但直接读 rb.velocity 会失效：
    ///   冲刺结束 → 速度立刻衰减到蓄力微移值（0.2 倍）
    ///   而蓄力一段的时长又长于这个衰减时长
    ///   → 释放那一刻速度必然已经见底，距离永远等于低保，机制形同虚设
    ///
    /// 比喻：移动速度是"你现在跑多快"，动量池是"你刚才攒了多少劲"。
    ///       前者可以 0.3 秒就见底（视觉上不拖沓），
    ///       后者留 1.5 秒（够你蓄完一段）。
    ///       两个衰减各调各的，互不牵制 —— 这正是把它独立出来的价值。
    /// ==========================================================
    ///
    /// 存入时机：冲刺结束、滑铲结束。
    /// 取用时机：蓄力一段的位移距离计算。
    /// </summary>
    public class PlayerMomentum : MonoBehaviour
    {
        [Header("衰减")]
        [Tooltip(
            "每秒衰减多少。\n" +
            "值越小留存越久 —— 这个数决定了「冲刺后多久之内还能吃到动量加成」。\n" +
            "建议让 储量/衰减 略大于蓄力一段的时长，否则蓄完就没了。")]
        public float decayPerSecond = 8f;

        [Tooltip("动量上限。防止某些高速手段刷出离谱的位移距离")]
        public float maxStored = 20f;

        [Header("蓄力时的速度继承")]
        [Tooltip(
        "蓄力期间，动量会充当一个【逐渐衰减的移动速度下限】。\n\n" +
        "冲刺后立刻进蓄力 → 起步速度接近冲刺速度 → 随动量衰减自然滑落到蓄力折损速度。\n" +
        "表现就是「带着冲劲进蓄力，还能滑行一小段」，而不是一进蓄力就急刹车。\n\n" +
        "本系数把动量值换算成速度。填 0 = 关闭这个效果。")]
        public float chargeSpeedScale = 0.5f;

        /// <summary>
        /// 蓄力期间的移动速度下限。
        ///
        /// 注意是【下限】不是【加成】—— 取 max 而不是相加。
        /// 语义是"你还带着多少冲劲"，冲劲耗尽后自然回落到正常的蓄力折损速度。
        /// </summary>
        public float GetChargeSpeedFloor() => Value * chargeSpeedScale;

        [Header("Debug")]
        public bool verboseLog = false;

        /// <summary>当前动量储量</summary>
        public float Value { get; private set; }

        /// <summary>归一化储量 0~1，供 UI 显示</summary>
        public float Normalized => maxStored <= 0f ? 0f : Mathf.Clamp01(Value / maxStored);

        private void Update()
        {
            if (Value <= 0f) return;

            Value = Mathf.Max(0f, Value - decayPerSecond * Time.deltaTime);
        }

        /// <summary>
        /// 存入一笔动量。
        ///
        /// 【取较大值而非累加】—— 连续冲刺不该把动量叠到天上去。
        /// 语义是"你刚才最快跑到多快"，不是"你一共跑了多少"。
        /// </summary>
        public void Deposit(float amount)
        {
            if (amount <= 0f) return;

            float newValue = Mathf.Min(maxStored, Mathf.Max(Value, amount));

            if (verboseLog && newValue > Value)
                Debug.Log($"[动量] 存入 {amount:F1}，当前 {newValue:F1}");

            Value = newValue;
        }

        /// <summary>清空。受击、死亡、切场景时用</summary>
        public void Clear() => Value = 0f;
    }
}