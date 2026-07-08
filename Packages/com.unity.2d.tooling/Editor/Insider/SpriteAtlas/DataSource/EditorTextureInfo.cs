using System;
using UnityEditor.U2D.Common;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D.Tooling.Analyzer
{
    [Serializable]
    class EditorTextureInfo : EditorResourceUsageInfo<Texture2D>
    {
        [SerializeField]
        TextureFormat m_TextureFormat;


        public EditorTextureInfo(int instanceID, string assetPath)
            : base(instanceID, assetPath) { }

        public EditorTextureInfo(int instanceID, string assetPath, int width, int height, TextureFormat textureFormat)
            : base(instanceID, assetPath)
        {
            m_TextureFormat = textureFormat;
            this.width = width;
            this.height = height;
            if(width == 0 || height == 0)
            {
                memorySize = 0;
                return;
            }
            var t = new Texture2D(width, height, textureFormat, false) { hideFlags = HideFlags.HideAndDontSave };
            memorySize = (ulong)InternalEditorBridge.GetTextureStorageMemorySizeLong(t);
            Object.DestroyImmediate(t);
        }

        public void CollectInfo(Texture2D texture)
        {
            memorySize = (ulong)InternalEditorBridge.GetTextureStorageMemorySizeLong(texture);
            m_TextureFormat = texture.format;
            width = texture.width;
            height = texture.height;
        }

        public TextureFormat textureFormat => m_TextureFormat;

        public ulong textureMemorySize => memorySize;
    }
}
