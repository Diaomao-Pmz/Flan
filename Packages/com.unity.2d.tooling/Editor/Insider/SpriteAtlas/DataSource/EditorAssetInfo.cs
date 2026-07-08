using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D.Tooling.Analyzer
{
    [Serializable]
    class EditorAssetInfo<T> where T : Object
    {
        [SerializeField]
        LazyLoadReference<Object> m_Object;
        [SerializeField]
        string m_AssetPath;
        [SerializeField]
        string m_Name;

        public EditorAssetInfo(int instanceID, string assetPath)
        {
            m_Object = instanceID;
            m_AssetPath = assetPath;
            m_Name =m_Object.GetAsset()?.name ?? $"{m_Object.instanceID}_No_Name";
        }

        public virtual string assetPath => m_AssetPath;
        public virtual string name => m_Name;

        //public T unityObject => m_InstanceID.asset as T;

        public T GetObject()
        {
            return m_Object.GetAsset() as T;
        }

        public int instanceID => m_Object.instanceID;
        public string globalObjectIDString => m_Object.globalObjectIDString;
    }
}
