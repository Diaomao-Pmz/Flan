using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Properties;
using UnityEditor.U2D.Sprites;
using UnityEditor.U2D.Tooling.Analyzer.UIElement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Tooling.Analyzer
{
    class SourceTextureWithCompressionIssue : AnalyzerIssueReportBase
    {
        const string k_Uxml = "Packages/com.unity.2d.tooling/Editor/Insider/SpriteAtlas/SpriteAtlasIssueReport/SourceTextureWithCompressionIssue/SourceTextureWithCompressionIssue.uxml";
        const string k_SaveFilePath = "Library/com.unity.2d.tooling/AnalyzerWindow/SourceTextureWithCompressionIssue.json";
        VisualElement m_IssueView;
        MultiColumnTreeView m_Table;
        List<TreeViewItemData<SourceTextureWithCompressionCellData>> m_TableData = new();
        Label m_NoDataLabel;

        public SourceTextureWithCompressionIssue(): base(new [] {typeof(SpriteAtlasDataSource)})
        {
            SetReportListItemName("Source Textures with compression in Sprite Atlas");
            SetReportListemCount("0");

            m_IssueView = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml).Instantiate();

            m_Table = m_IssueView.Q<MultiColumnTreeView>("Table");
            m_Table.AddManipulator(new ContextualMenuManipulator(OnContextualMenuManipulator));
            m_Table.selectionChanged += OnSelectionChanged;
            if(EditorGUIUtility.isProSkin)
                m_IssueView.AddToClassList("dark");
            else
                m_IssueView.AddToClassList("light");
            SetupTable();
            m_NoDataLabel = m_IssueView.Q<Label>("NoDataLabel");
            ShowTable(false, "Analyze has not been done yet.");
        }

        void ShowTable(bool show, string noShowReason)
        {
            m_Table.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            m_NoDataLabel.style.display = show ? DisplayStyle.None : DisplayStyle.Flex;
            if (!string.IsNullOrEmpty(noShowReason) && !show)
                m_NoDataLabel.text = noShowReason;
        }

        void OnSelectionChanged(IEnumerable<object> obj)
        {
            var item = m_Table.GetItemDataForIndex<SourceTextureWithCompressionCellData>(m_Table.selectedIndex);
            if(item != null)
                InspectObject(item.asset.GetAsset());
        }

        void OnContextualMenuManipulator(ContextualMenuPopulateEvent evt)
        {
            var menuStatus = m_TableData.Count > 0 && m_Table.selectedIndex >= 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
            evt.menu.AppendAction("Reanalyze Atlas", (a) => RecheckAtlas(),
                menuStatus);
        }

        void RecheckAtlas()
        {
            var item = m_Table.GetItemDataForIndex<SourceTextureWithCompressionCellData>(m_Table.selectedIndex);
            if (item != null)
            {
                RequestCapture(new [] {AssetDatabase.GetAssetPath(item.atlasInfo.GetObject())});
            }
        }

        public override VisualElement reportContent => m_IssueView;
        public override VisualElement settingsContent => null;
        public override string reportTitle => "Source Textures with Compression";

        protected override async void OnReportDataSourceChanged(IReportDataSource reportDataSource)
        {
            if (reportDataSource is SpriteAtlasDataSource dataSource)
            {
                await SetDataSourceProvider(dataSource);
            }
        }

        async Task SetDataSourceProvider(SpriteAtlasDataSource dataSource)
        {
            if (dataSource?.data != null)
            {
                await SetDataSource(dataSource);
            }
        }

        async Task SetDataSource(SpriteAtlasDataSource dataSource)
        {
            if (dataSource == null)
                return;
            ShowTable(false, "Filtering data in progress...");
            var saveFile = Utilities.LoadSaveDataFromFile<ReportSaveDataRoot<SourceTextureWithCompressionCellData>>(k_SaveFilePath);
            if (saveFile == null || dataSource.lastCaptureTime != saveFile.lastCaptureTime)
            {
                m_TableData = await FilterDataAsync(dataSource.data);
                Utilities.WriteSaveDataToFile(k_SaveFilePath, new ReportSaveDataRoot<SourceTextureWithCompressionCellData>()
                {
                    lastCaptureTime = dataSource.lastCaptureTime,
                    root = ReportSaveDataRoot<SourceTextureWithCompressionCellData>.CovertToSaveFormat(m_TableData)
                });
            }
            else
            {
                m_TableData =ReportSaveDataRoot<SourceTextureWithCompressionCellData>.ConvertToTreeViewItemData(saveFile.root);
            }

            m_Table.SetRootItems(m_TableData);
            m_Table.Rebuild();
            SetReportListemCount($"{m_TableData.Count}");
            ShowTable(m_TableData.Count > 0, "No Sprite Atlas where source texture has compression found.");
        }

        async Task<List<TreeViewItemData<SourceTextureWithCompressionCellData>>> FilterDataAsync(List<EditorAtlasInfo> dataSource)
        {
            isFilteringReport = true;
            SpriteDataProviderFactories spriteDataFactory = new();
            spriteDataFactory.Init();
            var result = new List<TreeViewItemData<SourceTextureWithCompressionCellData>>();
            var uniqueTexture = new HashSet<int>();
            int id = 0;
            for (int i = 0; i < dataSource?.Count; ++i)
            {
                var atlasInfo = dataSource[i];
                List <TreeViewItemData<SourceTextureWithCompressionCellData >> children = new ();
                for (int j = 0; j < atlasInfo?.spriteInfo.Count; ++j)
                {
                    await Task.Delay(10);
                    var si = atlasInfo.spriteInfo[j];
                    var texture = si.GetSpriteTexture(false);
                    if (texture)
                    {
                        if(uniqueTexture.Contains(texture.GetInstanceID()))
                            continue;
                        uniqueTexture.Add(texture.GetInstanceID());
                        if (GraphicsFormatUtility.IsCompressedFormat(texture.graphicsFormat))
                        {
                            children.Add(new (id++,
                                new SourceTextureWithCompressionCellData
                                {
                                    icon = "texture-icon",
                                    name = texture.name,
                                    textureFormat = texture.graphicsFormat.ToString(),
                                    asset = texture,
                                    atlasInfo = atlasInfo
                                }));
                        }
                    }
                }
                if(children.Count > 0)
                {
                    var atlasInfoData = new SourceTextureWithCompressionCellData
                    {
                        icon = "spriteatlas-icon",
                        name = atlasInfo.name,
                        textureFormat = atlasInfo.textureFormat.ToString(),
                        asset = atlasInfo.GetObject(),
                        atlasInfo = atlasInfo
                    };
                    result.Add(new TreeViewItemData<SourceTextureWithCompressionCellData>(id++, atlasInfoData, children));
                }
            }
            isFilteringReport = false;
            return result;
        }

        void SetNameColumnCellIcon(VisualElement ele, SourceTextureWithCompressionCellData data)
        {
            var icon = ele.Q("Icon");
            icon.RemoveFromClassList("texture-icon");
            icon.RemoveFromClassList("sprite-icon");
            icon.RemoveFromClassList("spriteatlas-icon");
            if(data.icon?.Length > 0)
                icon.AddToClassList(data.icon);
        }

        void SetupTable()
        {
            m_Table.SetRootItems(m_TableData);
            for(int i = 0; i < m_Table.columns.Count; ++i)
            {
                var column = m_Table.columns[i];

                if (column.name == "Name")
                {
                    column.bindCell = (element, i) => // item is template
                    {
                        var itemData = m_Table.GetItemDataForIndex<SourceTextureWithCompressionCellData>(i);
                        (element as CellLabelWithIcon).BindLabel(new DataBinding
                        {
                            dataSourcePath = new PropertyPath(column.bindingPath),
                        });
                        element.dataSource = itemData;
                        SetNameColumnCellIcon(element, itemData);
                    };
                }
                else
                {
                    column.bindCell = (element, i) => // item is template
                    {
                        var itemData = m_Table.GetItemDataForIndex<SourceTextureWithCompressionCellData>(i);
                        (element as CellLabelWithIcon).BindLabel(new DataBinding
                        {
                            dataSourcePath = new PropertyPath(column.bindingPath),
                        });
                        element.dataSource = itemData;
                    };
                }

                column.makeCell = () =>
                {
                    return new CellLabelWithIcon();
                };
            }
        }
    }
}
