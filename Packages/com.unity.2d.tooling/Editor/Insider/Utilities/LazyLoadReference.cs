using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D.Tooling.Analyzer
{
    [Serializable]
    struct LazyLoadReference<T> where T : Object
    {
        GlobalObjectId m_GlobalObjectID;
        [SerializeField]
        SerializableGuid m_GUID;
        [SerializeField]
        long m_LocalFileID;
        [SerializeField]
        bool m_ValidReference;
        [SerializeField]
        string m_GlobalObjectIDString;
        EntityId m_InstanceID;

        public LazyLoadReference(T obj)
        {
            m_GlobalObjectID = GlobalObjectId.GetGlobalObjectIdSlow(obj);
            m_GlobalObjectIDString = m_GlobalObjectID.ToString();
            if (obj)
            {
                m_ValidReference = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out m_LocalFileID);
                m_GUID = new SerializableGuid(guid);
                m_InstanceID = obj.GetInstanceID();
            }
            else
            {
                m_ValidReference = false;
                m_InstanceID = EntityId.None;
                m_LocalFileID = 0;
                m_GUID = new SerializableGuid(new GUID());
            }
        }

        public LazyLoadReference(EntityId instanceID)
        {
            m_GlobalObjectID = GlobalObjectId.GetGlobalObjectIdSlow(instanceID);
            m_GlobalObjectIDString = m_GlobalObjectID.ToString();
            m_InstanceID = instanceID;
            m_ValidReference = false;
            var obj = EditorUtility.EntityIdToObject(m_InstanceID);
            if (obj != null)
            {
                m_ValidReference = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out m_LocalFileID);
                m_GUID = new SerializableGuid(guid);
            }
            else
            {
                m_ValidReference = false;
                m_InstanceID = EntityId.None;
                m_LocalFileID = 0;
                m_GUID = new SerializableGuid(new GUID());
            }
        }

        public static implicit operator LazyLoadReference<T>(T asset)
        {
            return new LazyLoadReference<T>(asset);
        }

        public static implicit operator LazyLoadReference<T>(int instanceID)
        {
            return new LazyLoadReference<T>(instanceID);
        }

        public T GetAsset()
        {
            if (m_ValidReference)
            {
                if (m_InstanceID == EntityId.None)
                {
                    if (m_GlobalObjectID.assetGUID == new GUID())
                    {
                        GlobalObjectId.TryParse(m_GlobalObjectIDString, out m_GlobalObjectID);
                    }
                    m_InstanceID = GlobalObjectId.GlobalObjectIdentifierToInstanceIDSlow(m_GlobalObjectID);

                }
                return EditorUtility.EntityIdToObject(m_InstanceID) as T;
            }

            return null;
        }

        public int instanceID => m_InstanceID;
        public string globalObjectIDString => m_GlobalObjectIDString;
    }
}
