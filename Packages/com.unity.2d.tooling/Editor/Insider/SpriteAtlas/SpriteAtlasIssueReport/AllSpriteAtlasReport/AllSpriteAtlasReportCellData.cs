using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D.Tooling.Analyzer
{
    [Serializable]
    class AllSpriteAtlasReportCellData
    {
        public string name;
        public string sprites;
        public int spritesValue;
        public string usage;
        [SerializeField]
        float usageValue;
        public string totalMemory;
        [SerializeField]
        float totalMemoryValue;
        public string unusedMemory;
        [SerializeField]
        float unusedMemoryValue;
        public string totalArea;
        [SerializeField]
        float totalAreaValue;
        public string usedArea;
        [SerializeField]
        float usedAreaValue;
        public string width;
        [SerializeField]
        float widthValue;
        public string height;
        [SerializeField]
        float heightValue;
        public string packingMode;
        public string textureFormat;
        public string icon;
        public LazyLoadReference<Object> asset;
        public LazyLoadReference<Object> atlasAsset;
        public AllSpriteAtlasReportCellData(EditorAtlasInfo atlasInfo, EditorSpriteInfo info)
        {
            name = info.name;
            sprites = "";
            spritesValue = 0;
            // calculate sprite usage in atlas
            usageValue = (info.usedArea / atlasInfo.totalArea);
            usage = usageValue.ToString("0.0 %");
            totalMemoryValue = usageValue * atlasInfo.memorySize;
            totalMemory = EditorUtility.FormatBytes((long)totalMemoryValue);
            unusedMemoryValue = 0;
            unusedMemory = "";
            totalAreaValue = 0;
            totalArea = "";
            usedAreaValue = Mathf.Max(0, info.usedArea);
            usedArea = info.usedArea > 0 ? info.usedArea.ToString("#,##0") : "";
            width = $"{Mathf.Ceil(info.width)}";
            height = $"{Mathf.Ceil(info.height)}";
            packingMode = $"{info.spritePackingMode}";
            textureFormat = $"{info.textureFormat}";
            asset = info.GetObject();
            widthValue = info.width;
            heightValue = info.height;
            atlasAsset = atlasInfo.GetObject();
        }

        public AllSpriteAtlasReportCellData(EditorAtlasInfo info)
        {
            name = info.name;
            sprites = $"{info.spriteInfo?.Count}";
            spritesValue = info.spriteInfo?.Count ?? 0;
            usageValue = info.usedRatio;
            usage = info.usedRatio.ToString("0.0 %");
            totalMemoryValue = info.memorySize;
            totalMemory = EditorUtility.FormatBytes((long)info.memorySize);
            unusedMemoryValue = info.unusedMemory;
            unusedMemory = EditorUtility.FormatBytes((long)info.unusedMemory);
            totalAreaValue = Mathf.Max(0, info.totalArea);
            totalArea = info.totalArea > 0 ? info.totalArea.ToString("#,##0") : "";
            usedAreaValue = Mathf.Max(0, info.usedArea);
            usedArea = info.usedArea > 0 ? info.usedArea.ToString("#,##0") : "";
            width = $"{Mathf.Ceil(info.width)}";
            height = $"{Mathf.Ceil(info.height)}";
            packingMode = "";
            textureFormat = $"{info.textureFormat}";
            asset = info.GetObject();
            widthValue = info.width;
            heightValue = info.height;
            atlasAsset = asset;
        }

        public static int Compare(AllSpriteAtlasReportCellData a, AllSpriteAtlasReportCellData b, string propertyToCompare)
        {
            switch (propertyToCompare)
            {
                case "name":
                case null:
                    return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
                case "sprites":
                    return a.spritesValue.CompareTo(b.spritesValue);
                case "usage":
                    return a.usageValue.CompareTo(b.usageValue);
                case "totalMemory":
                    return a.totalMemoryValue.CompareTo(b.totalMemoryValue);
                case "unusedMemory":
                    return a.unusedMemoryValue.CompareTo(b.unusedMemoryValue);
                case "totalArea":
                    return a.totalAreaValue.CompareTo(b.totalAreaValue);
                case "usedArea":
                    return a.usedAreaValue.CompareTo(b.usedAreaValue);
                case "width":
                    return a.widthValue.CompareTo(b.widthValue);
                case "height":
                    return a.heightValue.CompareTo(b.heightValue);
                case "packingMode":
                    return string.Compare(a.packingMode, b.packingMode, StringComparison.Ordinal);
                case "textureFormat":
                    return string.Compare(a.textureFormat, b.textureFormat, StringComparison.Ordinal);
                default:
                    return 0;
            }
        }
    }
}
