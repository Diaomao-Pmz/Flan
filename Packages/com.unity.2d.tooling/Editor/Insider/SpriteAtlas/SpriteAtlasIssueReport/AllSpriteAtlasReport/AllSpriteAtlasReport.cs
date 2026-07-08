using System;
using System.Collections.Generic;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D.Tooling.Analyzer
{
    class AllSpriteAtlasReport : IAnalyzerReport
    {
        const string k_Uxml = "Packages/com.unity.2d.tooling/Editor/Insider/SpriteAtlas/SpriteAtlasIssueReport/AllSpriteAtlasReport/SpriteAtlasRawData.uxml";
        const string k_SaveFilePath = "Library/com.unity.2d.tooling/AnalyzerWindow/AllSpriteAtlasReport.json";
        IDataSourceProvider m_DataSourceProvider;
        SpriteAtlasDataSource m_SpriteAtlasDataSource;
        VisualElement m_ReportView;
        MultiColumnTreeView m_Table;
        Label m_NoDataLabel;
        List<TreeViewItemData<AllSpriteAtlasReportCellData>> m_Data = new();
        ReportListItem m_ReportListItem;
        event Action<IAnalyzerReport, Object> m_OnInspectObject;

        public AllSpriteAtlasReport()
        {
            m_ReportListItem = new();
            m_ReportListItem.SetName("All Sprite Atlases");
            m_ReportListItem.SetCount("0");
            m_ReportView = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml).Instantiate();
            m_Table = m_ReportView.Q<MultiColumnTreeView>("Table");
            m_Table.AddManipulator(new ContextualMenuManipulator(OnContextualMenuManipulator));
            m_Table.selectionChanged += OnSelectionChanged;
            if(EditorGUIUtility.isProSkin)
                m_Table.AddToClassList("dark");
            else
                m_Table.AddToClassList("light");
            SetupTable();
            m_NoDataLabel = m_ReportView.Q<Label>("NoDataLabel");
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
            var cellData = m_Table.GetItemDataForIndex<AllSpriteAtlasReportCellData>(m_Table.selectedIndex);
            if(cellData != null)
                m_OnInspectObject?.Invoke(this, cellData.asset.GetAsset());
        }


        void OnContextualMenuManipulator(ContextualMenuPopulateEvent obj)
        {
            var menuStatus =  m_Data.Count > 0 && m_Table.selectedIndex >= 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
            obj.menu.AppendAction("Reanalyze Atlas", a => RecheckAtlas(), menuStatus);
        }

        public VisualElement reportContent => m_ReportView;
        public VisualElement settingsContent => null;

        void RecheckAtlas()
        {
            var item = m_Table.GetItemDataForIndex<AllSpriteAtlasReportCellData>(m_Table.selectedIndex);
            if (item != null)
            {
                m_DataSourceProvider.GetDataSource<SpriteAtlasDataSource>().Capture(new string[]
                {
                    AssetDatabase.GetAssetPath(item.atlasAsset.GetAsset())
                });
            }
        }

        void SetNameColumnCellIcon(VisualElement ele, AllSpriteAtlasReportCellData data)
        {
            var icon = ele.Q("Icon");
            icon.RemoveFromClassList("texture-icon");
            icon.RemoveFromClassList("sprite-icon");
            icon.RemoveFromClassList("spriteatlas-icon");
            if(data.icon?.Length > 0)
                icon.AddToClassList(data.icon);
        }

        void BindLabelToDataSource(Label label, string path, AllSpriteAtlasReportCellData cellData)
        {
            label.SetBinding("text", new DataBinding
            {
                dataSourcePath = new PropertyPath(path),
                bindingMode = BindingMode.ToTarget,
                dataSource = cellData
            });
        }

        void SetupTable()
        {
            m_Table.sortingMode = ColumnSortingMode.Custom;
            m_Table.columnSortingChanged += OnColumnSortingChanged;
            for(int i = 0; i < m_Table.columns.Count; ++i)
            {
                var column = m_Table.columns[i];

                if (column.name == "Name")
                {
                    column.bindCell = (element, i) => // item is template
                    {
                        var label = element.Q<Label>();
                        var itemData = m_Table.GetItemDataForIndex<AllSpriteAtlasReportCellData>(i);
                        BindLabelToDataSource(label, column.bindingPath, itemData);
                        SetNameColumnCellIcon(element, itemData);
                    };
                }
                else
                {
                    column.bindCell = (element, i) => // item is template
                    {
                        var itemData = m_Table.GetItemDataForIndex<AllSpriteAtlasReportCellData>(i);
                        var label = element.Q<Label>();
                        BindLabelToDataSource(label, column.bindingPath, itemData);
                    };
                }

                column.unbindCell = (element, _) => // item is template
                {
                    var label = element.Q<Label>();
                    label.SetBinding("text", null);
                };

                column.makeCell = () =>
                {
                    var ve =  column.cellTemplate.Instantiate();
                    return ve;
                };
                column.comparison = (a,b) =>
                {
                    var aData = m_Table.GetItemDataForIndex<AllSpriteAtlasReportCellData>(a);
                    var bData = m_Table.GetItemDataForIndex<AllSpriteAtlasReportCellData>(b);
                    return AllSpriteAtlasReportCellData.Compare(aData, bData, column.bindingPath);
                };
            }
        }

        void OnColumnSortingChanged()
        {
            UpdateTableView(true, true);
        }

        public VisualElement listItem => m_ReportListItem;
        public string reportTitle => "All Sprite Atlases";

        public void SetDataSourceProvider(IDataSourceProvider dataSourceProvider)
        {
            m_DataSourceProvider = dataSourceProvider;
            m_DataSourceProvider.onDataSourceChanged += InitDataSource;
            InitDataSource();
        }

        void InitDataSource()
        {
            if(m_SpriteAtlasDataSource != null)
                m_SpriteAtlasDataSource.onDataSourceChanged -= OnSpriteAtlasDataSourceChanged;
            OnSpriteAtlasDataSourceChanged(m_DataSourceProvider.GetDataSource<SpriteAtlasDataSource>());
            if(m_SpriteAtlasDataSource != null)
                m_SpriteAtlasDataSource.onDataSourceChanged += OnSpriteAtlasDataSourceChanged;
        }

        void OnSpriteAtlasDataSourceChanged(IReportDataSource dataSource)
        {
            if (dataSource is SpriteAtlasDataSource source)
            {
                if (m_SpriteAtlasDataSource != null)
                {
                    m_SpriteAtlasDataSource.onDataSourceChanged -= OnSpriteAtlasDataSourceChanged;
                }
                m_SpriteAtlasDataSource = source;
                m_SpriteAtlasDataSource.onDataSourceChanged += OnSpriteAtlasDataSourceChanged;
                UpdateTableView(false);
            }
        }

        void UpdateTableView(bool keepExpand, bool sortUpdate = false)
        {
            BuildAltasInfoDataTree(sortUpdate);
            m_Table.Clear();
            m_ReportListItem.SetCount($"{m_Data.Count}");
            List<int> expandedIds = new();
            if (keepExpand)
            {
                foreach (var d in m_Data)
                {
                    if(m_Table.IsExpanded(d.id))
                        expandedIds.Add(d.id);
                }
            }
            m_Table.SetRootItems(m_Data);
            m_Table.Rebuild();
            foreach(var expand in expandedIds)
                m_Table.ExpandItem(expand);
            ShowTable(m_Data.Count > 0, "No Sprite Atlas found.");
        }

        void BuildAltasInfoDataTree(bool sortUpdate)
        {
            var saveFile = Utilities.LoadSaveDataFromFile<ReportSaveDataRoot<AllSpriteAtlasReportCellData>>(k_SaveFilePath);
            m_Data.Clear();

            if(sortUpdate || saveFile == null || m_SpriteAtlasDataSource.lastCaptureTime != saveFile.lastCaptureTime)
            {
                // if the last capture time is different, we need to rebuild the data
                var atlasData = m_SpriteAtlasDataSource.data;
                if (atlasData != null)
                {
                    int id = 0;
                    for (int i = 0; i < atlasData.Count; ++i, ++id)
                    {
                        var atlasCellData = new AllSpriteAtlasReportCellData(atlasData[i]) { icon = "spriteatlas-icon" };
                        m_Data.Add(new TreeViewItemData<AllSpriteAtlasReportCellData>(id, atlasCellData,
                            BuildSpriteInfoDataTree(ref id, atlasData[i])));
                    }
                    Utilities.WriteSaveDataToFile(k_SaveFilePath, new ReportSaveDataRoot<AllSpriteAtlasReportCellData>()
                    {
                        lastCaptureTime = m_SpriteAtlasDataSource.lastCaptureTime,
                        root = ReportSaveDataRoot<AllSpriteAtlasReportCellData>.CovertToSaveFormat(m_Data)
                    });
                }
            }
            else
            {
                m_Data.AddRange(ReportSaveDataRoot<AllSpriteAtlasReportCellData>.ConvertToTreeViewItemData(saveFile.root));
            }
            if(m_Table.sortedColumns != null)
            {
                m_Data.Sort(SortData);
            }
        }

        int SortData(TreeViewItemData<AllSpriteAtlasReportCellData> a, TreeViewItemData<AllSpriteAtlasReportCellData> b)
        {
            using (var enumerator = m_Table.sortedColumns.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    int result = AllSpriteAtlasReportCellData.Compare(a.data, b.data, enumerator.Current.column.bindingPath);
                    if (result != 0)
                        return result * (enumerator.Current.direction == SortDirection.Ascending ? 1 : -1);
                }
            }

            return AllSpriteAtlasReportCellData.Compare(a.data, b.data, null);
        }

        List<TreeViewItemData<AllSpriteAtlasReportCellData>> BuildSpriteInfoDataTree(ref int id, EditorAtlasInfo atlasInfo)
        {
            List<TreeViewItemData<AllSpriteAtlasReportCellData>> data = new List<TreeViewItemData<AllSpriteAtlasReportCellData>>();
            var spriteInfo = atlasInfo.spriteInfo;
            if (spriteInfo != null)
            {
                ++id;
                for (int i = 0; i < spriteInfo.Count; ++i, ++id)
                {
                    data.Add(new TreeViewItemData<AllSpriteAtlasReportCellData>(id, new AllSpriteAtlasReportCellData(atlasInfo, spriteInfo[i])
                    {
                        icon = "sprite-icon"
                    }));
                }
            }
            if(m_Table.sortedColumns != null)
            {
                data.Sort(SortData);
            }

            return data;
        }

        public void Dispose()
        {
            if(m_SpriteAtlasDataSource != null)
                m_SpriteAtlasDataSource.onDataSourceChanged -= OnSpriteAtlasDataSourceChanged;
            if(m_DataSourceProvider != null)
                m_DataSourceProvider.onDataSourceChanged -= InitDataSource;
        }

        public event Action<IAnalyzerReport, Object> onInspectObject
        {
            add => m_OnInspectObject += value;
            remove => m_OnInspectObject -= value;
        }

        public IAnalyzerReport GetReportForType(Type type)
        {
            if (type == GetType())
                return this;

            return null;
        }
    }
}
