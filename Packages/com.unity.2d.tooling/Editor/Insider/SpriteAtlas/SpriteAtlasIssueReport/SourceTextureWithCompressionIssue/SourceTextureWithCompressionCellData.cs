using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D.Tooling.Analyzer
{
    [Serializable]
    class SourceTextureWithCompressionCellData
    {
        public string name;
        public string textureFormat;
        public LazyLoadReference<Object> asset;
        public EditorAtlasInfo atlasInfo;
        public string icon;
    }
}
