using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// 【[SerializeReference] 列表去共用】
/// Unity 的坑：列表里的元素是 [SerializeReference]（例如弹幕段的图案、演出步骤）时，
/// 在 Inspector 点「+」新加的元素不是复印一份参数，而是配了同一个抽屉的第二把钥匙 ——
/// 两个元素指向同一份参数，改一个另一个跟着变。
/// 用法：在资产的 OnValidate 里把列表过一遍，第一次见到的原样保留，再次见到的换成一份独立的复制品。
/// </summary>
public static class SerializeReferenceDedup
{
    /// <summary>按"是不是同一个对象"判断，不看内容是否相等</summary>
    private sealed class RefComparer : IEqualityComparer<object>
    {
        public static readonly RefComparer Instance = new RefComparer();
        public new bool Equals(object a, object b) => ReferenceEquals(a, b);
        public int GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
    }

    public static HashSet<object> NewSet() => new HashSet<object>(RefComparer.Instance);

    /// <summary>obj 之前出现过（被共用）就返回一份内容相同的独立复制品，否则原样返回并记下它</summary>
    public static T Unshare<T>(T obj, HashSet<object> seen) where T : class
    {
        if (obj == null) return null;
        if (seen.Add(obj)) return obj;

        T copy = (T)Activator.CreateInstance(obj.GetType());
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(obj), copy);
        seen.Add(copy);
        return copy;
    }
}
