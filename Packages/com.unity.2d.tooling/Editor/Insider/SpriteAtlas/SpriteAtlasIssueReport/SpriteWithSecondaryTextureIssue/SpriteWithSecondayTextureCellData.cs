using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D.Tooling.Analyzer
{
    [Serializable]
    class SpriteWithSecondayTextureCellData
    {
        public string name;
        public int count;
        public LazyLoadReference<Object> instanceId;
        public string icon;
    }
}
