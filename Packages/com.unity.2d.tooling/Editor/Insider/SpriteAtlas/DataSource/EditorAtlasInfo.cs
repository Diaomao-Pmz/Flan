using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor.U2D.Common;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.U2D;

namespace UnityEditor.U2D.Tooling.Analyzer
{
    [Serializable]
    class EditorAtlasInfo : EditorResourceUsageInfo<SpriteAtlas>
    {
        [SerializeField]
        List<EditorTextureInfo> m_TextureInfo = new();
        [SerializeField]
        List<EditorSpriteInfo> m_SpriteInfo = new();
        [SerializeField]
        long m_FileModifiedTime;
        [SerializeField]
        long m_MetaFileModifiedTime;
        [SerializeField]
        Hash128 m_AssetHash;
        [SerializeField]
        TextureFormat m_TextureFormat;

        public EditorAtlasInfo(int instanceID, string assetPath)
            : base(instanceID, assetPath) { }

        public async Task CollectAtlasInfo()
        {
            Profiler.BeginSample("CollectAtlasInfo");
            var atlas = GetObject();
            var path = AssetDatabase.GetAssetPath(atlas);
            m_FileModifiedTime = File.GetLastWriteTimeUtc(path).ToFileTimeUtc();
            m_AssetHash = AssetDatabase.GetAssetDependencyHash(path);
            path = AssetDatabase.GetTextMetaFilePathFromAssetPath(path);
            m_MetaFileModifiedTime = File.GetLastWriteTimeUtc(path).ToFileTimeUtc();
            m_TextureFormat = SpriteAtlasBridge.GetSpriteAtlasTextureFormat(atlas, EditorUserBuildSettings.activeBuildTarget);
            memorySize = 0;
            usedArea = 0;

            var packables = atlas.GetPackables();
            for (int i = 0; i < packables.Length; ++i)
            {
                var packable = packables[i];
                if (!packable)
                    continue;
                if (packable is DefaultAsset folder)
                {
                    var folderPath = AssetDatabase.GetAssetPath(folder);
                    var spriteGuids = AssetDatabase.FindAssets("t:Sprite", new[] { folderPath });
                    for (int j = 0; j < spriteGuids.Length; ++j)
                    {
                        var spritePath = AssetDatabase.GUIDToAssetPath(spriteGuids[j]);
                        var assets = AssetDatabase.LoadAllAssetsAtPath(spritePath);
                        foreach (var asset in assets)
                        {
                            if (asset is Sprite sprite)
                            {
                                AddSpriteInfo(sprite);
                            }
                        }
                    }
                }
                else if (packable is Texture2D)
                {
                    var texturePath = AssetDatabase.GetAssetPath(packable);
                    var assets = AssetDatabase.LoadAllAssetsAtPath(texturePath);
                    foreach (var asset in assets)
                    {
                        if (asset is Sprite sprite)
                        {
                            AddSpriteInfo(sprite);
                        }
                    }
                }
                else if (packable is Sprite sprite)
                {
                    AddSpriteInfo(sprite);
                }
                else
                {
                    Debug.LogError("Packed is " + packable.GetType());
                }
            }
            var textures = SpriteAtlasBridge.GetSpriteAtlasTextures(atlas);
            Dictionary<int, Vector2Int> m_TextureSize = new();
            if (spriteInfo.Count > 0 && textures.Length == 0)
            {
                // atlas is not packed. trigger a pack
                var t = SpriteAtlasBridge.SpriteAtlasFitDataAsync(atlas, spriteInfo.Count);
                await t.WaitForJob();
                for(int i = 0; i < t.Count; ++i)
                {
                    var p = t.GetPage(i);
                    if (p < 0 || m_TextureSize.ContainsKey(p))
                        continue;
                    m_TextureSize[p] = t.GetPageSize(i);

                }
                t.Dispose();
                foreach (var k in m_TextureSize)
                {
                    var textureInfo = new EditorTextureInfo(EntityId.None, null,
                        k.Value.x, k.Value.y, textureFormat);
                    m_TextureInfo.Add(textureInfo);
                    width += k.Value.x;
                    height += k.Value.y;
                    memorySize += textureInfo.textureMemorySize;
                }
            }
            else
            {
                for(int i = 0; i < textures.Length; ++i)
                {
                    var texture = textures[i];
                    var texturePath = AssetDatabase.GetAssetPath(texture);
                    var textureInfo = new EditorTextureInfo(texture.GetInstanceID(), texturePath);
                    textureInfo.CollectInfo(texture);
                    width += texture.width;
                    height += texture.height;
                    m_TextureInfo.Add(textureInfo);
                    memorySize += textureInfo.textureMemorySize;
                }
            }
            Profiler.EndSample();
        }

        void AddSpriteInfo(Sprite sprite)
        {
            var spritePath = AssetDatabase.GetAssetPath(sprite);
            var spriteInfo = new EditorSpriteInfo(sprite.GetInstanceID(), spritePath);
            spriteInfo.CollectSpriteInfo();
            m_SpriteInfo.Add(spriteInfo);
            usedArea += spriteInfo.usedArea;
        }

        EditorTextureInfo FindEditorTextureInfo(int instanceID)
        {
            for (int i = 0; i < m_TextureInfo.Count; ++i)
            {
                if (m_TextureInfo[i].instanceID == instanceID)
                    return m_TextureInfo[i];
            }
            return null;
        }

        public virtual List<EditorSpriteInfo> spriteInfo => m_SpriteInfo;
        public virtual List<EditorTextureInfo> textureInfo => m_TextureInfo;
        public TextureFormat textureFormat => m_TextureFormat;
        public long metaFileModifiedTime => m_MetaFileModifiedTime;
        public Hash128 assetHash => m_AssetHash;

        public long fileModifiedTime => m_FileModifiedTime;

        public static bool HasAtlasChange(EditorAtlasInfo prevCaptureAtlasInfo, string atlasPath)
        {
            var hash = AssetDatabase.GetAssetDependencyHash(atlasPath);
            var fileTime = File.GetLastWriteTimeUtc(atlasPath).ToFileTimeUtc();
            var metaPath = AssetDatabase.GetTextMetaFilePathFromAssetPath(atlasPath);
            var metaTime = File.GetLastWriteTimeUtc(metaPath).ToFileTimeUtc();
            return prevCaptureAtlasInfo?.fileModifiedTime != fileTime || prevCaptureAtlasInfo?.metaFileModifiedTime != metaTime || prevCaptureAtlasInfo?.assetHash != hash;
        }
    }
}
