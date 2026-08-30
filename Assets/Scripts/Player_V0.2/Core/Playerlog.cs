using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 【日志封装】—— 正式包里整段消失。
    ///
    /// ==========================================================
    /// 【为什么不能直接留 Debug.Log】
    ///
    /// 很多人以为"正式包里 Debug.Log 会自动被剥掉"，其实不会 ——
    /// 默认情况下它照常执行、照常写日志文件。
    ///
    /// 更要命的是【参数会先被求值】。这一行：
    ///     Debug.Log($"[连招] 第{depth}段：{node.nodeName}");
    /// 就算日志本身被忽略，那个内插字符串也已经拼好了 ——
    /// 每次攻击都在堆上产生一个新 string，全是白给 GC 的垃圾。
    ///
    /// [Conditional("UNITY_EDITOR")] 的特殊之处在于：
    /// 它让编译器【连调用语句带参数一起删掉】，字符串根本不会被拼。
    ///
    /// 比喻：普通的 if(debug) 是"把信寄出去再让收件人扔掉"；
    ///       Conditional 是"信压根没写"。
    /// ==========================================================
    ///
    /// 用法：把 Debug.Log(...) 换成 PlayerLog.Info(...) 即可。
    ///       警告和报错【不要】换 —— 那些在正式包里也该保留。
    /// </summary>
    public static class PlayerLog
    {
        /// <summary>普通调试信息。正式包里整句消失</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Info(string message)
        {
            Debug.Log(message);
        }

        /// <summary>带来源物体的调试信息。点击日志会在 Hierarchy 里高亮它</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Info(string message, Object context)
        {
            Debug.Log(message, context);
        }

        /// <summary>
        /// 分类调试信息，输出形如 [连招] xxx。
        /// 建议统一用几个固定分类：连招 / 路由器 / 宝石 / 状态机 / 判定
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Info(string category, string message)
        {
            Debug.Log($"[{category}] {message}");
        }

        // ==========================================================
        // 警告与报错【不】做条件编译 —— 正式包里出问题时需要它们
        // ==========================================================

        public static void Warn(string message, Object context = null)
        {
            Debug.LogWarning(message, context);
        }

        public static void Error(string message, Object context = null)
        {
            Debug.LogError(message, context);
        }
    }
}